//! 测试用的假 sidecar（只有 `cargo test` 会构建它，不会进入应用包）。
//!
//! 实现协议的子集，行为由请求参数里的字符串（路径、附件 Id 等）里的关键字控制：
//! - `crash`：不回复，进程立刻以退出码 101 退出
//! - `hang`：永远不回复
//! - `slow-<毫秒>`：延迟这么久再回复（用来制造乱序响应）
//! - `garbage`：回复之前先输出几行不是合法响应的内容
//! - `exit-after`：回复之后立刻退出
//! - `missing`：返回 `unreadable`；`corrupt`：返回 `corrupted`
//!
//! 环境变量控制整个进程：`FAKE_CRASH_ON_START`、`FAKE_NO_PING`、`FAKE_PROTOCOL_VERSION`、`FAKE_IGNORE_EOF`。
//! `FAKE_POISON_FILE` 指向一个文本文件：打开路径中包含该文件里某一行的邮件时，进程崩溃。
//! 文件可以在 sidecar 运行期间创建，用来模拟“文件第一次能打开，重新打开时让 sidecar 崩溃”。
//! 每个请求在自己的线程里处理，所以响应顺序和请求顺序可以不同。

use std::collections::HashMap;
use std::io::{BufRead, Write};
use std::process::exit;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;

use serde_json::{Value, json};

struct State {
    pid: u32,
    counter: AtomicU64,
    /// docId -> 来源描述（顶层是文件路径，嵌套是 “父来源>附件Id”）
    docs: Mutex<HashMap<String, String>>,
    out: Mutex<std::io::Stdout>,
}

impl State {
    fn write_line(&self, line: &str) {
        let mut out = self.out.lock().unwrap();
        let _ = writeln!(out, "{line}");
        let _ = out.flush();
    }

    fn reply(&self, id: &Value, body: Result<Value, (&str, String)>) {
        let message = match body {
            Ok(result) => json!({ "id": id, "result": result }),
            Err((code, message)) => {
                json!({ "id": id, "error": { "code": code, "message": message } })
            }
        };
        self.write_line(&message.to_string());
    }

    fn new_doc(&self, source: String) -> String {
        let doc_id = format!(
            "p{}-d{}",
            self.pid,
            self.counter.fetch_add(1, Ordering::SeqCst) + 1
        );
        self.docs.lock().unwrap().insert(doc_id.clone(), source);
        doc_id
    }

    fn source_of(&self, params: &Value) -> Result<String, (&'static str, String)> {
        let doc_id = params["docId"].as_str().unwrap_or_default();
        self.docs
            .lock()
            .unwrap()
            .get(doc_id)
            .cloned()
            .ok_or(("document_not_found", format!("no document {doc_id}")))
    }
}

fn env_flag(name: &str) -> bool {
    std::env::var_os(name).is_some()
}

fn is_poisoned(path: &str) -> bool {
    let Some(file) = std::env::var_os("FAKE_POISON_FILE") else {
        return false;
    };
    std::fs::read_to_string(file).is_ok_and(|text| {
        text.lines()
            .any(|line| !line.is_empty() && path.contains(line))
    })
}

fn strings(params: &Value, out: &mut Vec<String>) {
    match params {
        Value::String(s) => out.push(s.clone()),
        Value::Object(map) => map.values().for_each(|v| strings(v, out)),
        Value::Array(items) => items.iter().for_each(|v| strings(v, out)),
        _ => {}
    }
}

fn delay_ms(all: &str) -> u64 {
    all.split("slow-")
        .nth(1)
        .map(|rest| {
            rest.chars()
                .take_while(char::is_ascii_digit)
                .collect::<String>()
        })
        .and_then(|digits| digits.parse().ok())
        .unwrap_or(0)
}

fn handle(state: &State, request: Value) {
    let id = request["id"].clone();
    let method = request["method"].as_str().unwrap_or_default().to_owned();
    let params = request["params"].clone();
    eprintln!("fake-sidecar: handling {method}");

    let mut texts = Vec::new();
    strings(&params, &mut texts);
    let all = texts.join("\n");

    if all.contains("crash") {
        eprintln!("fake-sidecar: simulating a crash");
        exit(101);
    }
    if all.contains("hang") || (method == "system.ping" && env_flag("FAKE_NO_PING")) {
        loop {
            thread::sleep(Duration::from_secs(3600));
        }
    }
    let delay = delay_ms(&all);
    if delay > 0 {
        thread::sleep(Duration::from_millis(delay));
    }
    if all.contains("garbage") {
        state.write_line("this line is not JSON");
        state.write_line("[1, 2, 3]");
        state.write_line(r#"{"id": 999999, "result": {"late": true}}"#);
        state.write_line(r#"{"id": null, "error": {"code": "invalid_request", "message": "pretend parse failure"}}"#);
    }

    let result: Result<Value, (&str, String)> = match method.as_str() {
        "system.ping" => {
            let version = std::env::var("FAKE_PROTOCOL_VERSION").ok().and_then(|v| v.parse::<u64>().ok()).unwrap_or(1);
            Ok(json!({ "protocolVersion": version, "pid": state.pid }))
        }
        "email.open" => {
            let path = params["path"].as_str().unwrap_or_default().to_owned();
            if is_poisoned(&path) {
                eprintln!("fake-sidecar: {path} is poisoned, crashing");
                exit(101);
            }
            if path.contains("missing") {
                Err(("unreadable", format!("cannot read {path}")))
            } else if path.contains("corrupt") {
                Err(("corrupted", format!("{path} is damaged")))
            } else {
                let doc_id = state.new_doc(path.clone());
                Ok(json!({ "docId": doc_id, "document": { "kind": "top", "source": path, "pid": state.pid } }))
            }
        }
        "email.openNested" => state.source_of(&params).and_then(|parent| {
            let attachment = params["attachmentId"].as_str().unwrap_or_default();
            if attachment.contains("notnested") {
                return Err(("not_nested_email", format!("{attachment} is not an email")));
            }
            let source = format!("{parent}>{attachment}");
            let doc_id = state.new_doc(source.clone());
            Ok(json!({ "docId": doc_id, "document": { "kind": "nested", "source": source, "pid": state.pid } }))
        }),
        "document.close" => {
            let doc_id = params["docId"].as_str().unwrap_or_default();
            match state.docs.lock().unwrap().remove(doc_id) {
                Some(_) => Ok(json!({})),
                None => Err(("document_not_found", format!("no document {doc_id}"))),
            }
        }
        "attachment.save" => state.source_of(&params).map(|source| {
            let target = params["targetPath"].as_str().unwrap_or_default();
            json!({ "path": format!("{target}|{source}|pid{}", state.pid) })
        }),
        "attachment.saveAll" => state.source_of(&params).map(|source| {
            let dir = params["targetDir"].as_str().unwrap_or_default();
            json!({
                "saved": [{ "attachmentId": "a1", "path": format!("{dir}/{source}") }],
                "failed": [{ "attachmentId": "a2", "code": "write_failed", "message": "disk full" }],
            })
        }),
        "attachment.extractForOpen" => state.source_of(&params).and_then(|source| {
            let attachment = params["attachmentId"].as_str().unwrap_or_default();
            if attachment.ends_with(".exe") {
                Err(("executable_refused", format!("{attachment} can only be saved")))
            } else {
                Ok(json!({ "path": format!("/tmp/fake/{}/{source}/{attachment}", state.pid) }))
            }
        }),
        // 测试专用：列出当前打开的文档
        "fake.docs" => Ok(json!({ "docs": *state.docs.lock().unwrap() })),
        other => Err(("method_not_found", format!("unknown method {other}"))),
    };
    state.reply(&id, result);

    if all.contains("exit-after") {
        exit(0);
    }
}

fn main() {
    let pid = std::process::id();
    eprintln!("fake-sidecar: started (pid {pid})");
    if env_flag("FAKE_CRASH_ON_START") {
        exit(3);
    }

    let state = Arc::new(State {
        pid,
        counter: AtomicU64::new(0),
        docs: Mutex::new(HashMap::new()),
        out: Mutex::new(std::io::stdout()),
    });
    let mut workers = Vec::new();
    for line in std::io::stdin().lock().lines() {
        let Ok(line) = line else { break };
        match serde_json::from_str::<Value>(&line) {
            Ok(request) if request.is_object() => {
                let state = state.clone();
                workers.push(thread::spawn(move || handle(&state, request)));
            }
            _ => state.write_line(
                r#"{"id": null, "error": {"code": "invalid_request", "message": "not a request"}}"#,
            ),
        }
    }

    // stdin 关闭：先处理完已收到的请求，再退出
    eprintln!("fake-sidecar: stdin closed");
    for worker in workers {
        let _ = worker.join();
    }
    if env_flag("FAKE_IGNORE_EOF") {
        loop {
            thread::sleep(Duration::from_secs(3600));
        }
    }
}
