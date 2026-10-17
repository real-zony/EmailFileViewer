/* EmailFileViewer UI draft.
   Each render function below maps to one React component (see README "Components").
   Plain script, no modules, so the page works from file://. */
(function () {
  "use strict";

  // ---------------------------------------------------------------- utils
  function esc(s) {
    return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }
  var isMac = /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);

  var ICON = {
    folder: '<path d="M3.5 7.5a2 2 0 0 1 2-2h3.6l2 2h7.4a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z"/>',
    save: '<path d="M12 4.5v10m0 0-4-4m4 4 4-4M5 19.5h14"/>',
    gear: '<path d="M4 7h9M17 7h3M4 17h3M11 17h9"/><circle cx="15" cy="7" r="2"/><circle cx="9" cy="17" r="2"/>',
    clip: '<path d="m19.5 11.2-7.4 7.4a4.6 4.6 0 0 1-6.5-6.5l8-8a3.1 3.1 0 0 1 4.4 4.4l-8 8a1.5 1.5 0 0 1-2.2-2.2l7.4-7.4"/>',
    shield: '<path d="M12 3.2 19 6v5.3c0 4.3-2.9 7.9-7 9.5-4.1-1.6-7-5.2-7-9.5V6z"/>',
    lock: '<rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8.5 10.5V7.8a3.5 3.5 0 0 1 7 0v2.7"/>',
    seal: '<circle cx="12" cy="9.5" r="5.5"/><path d="m9 14.2-1.2 7 4.2-2.2 4.2 2.2-1.2-7"/>',
    alert: '<path d="M10.3 4.4 2.9 17.6A2 2 0 0 0 4.6 20.5h14.8a2 2 0 0 0 1.7-2.9L13.7 4.4a2 2 0 0 0-3.4 0z"/><path d="M12 9.5v4.5M12 17.2v.1"/>',
    imageOff: '<rect x="3.5" y="5" width="17" height="14" rx="2"/><path d="m3.5 15.5 4.5-4.5 4 4 2.5-2.5 6 6M3 3l18 18"/>',
    x: '<path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/>',
    back: '<path d="M14.5 6 8.5 12l6 6"/>',
    envelope: '<rect x="3.5" y="5.5" width="17" height="13" rx="2"/><path d="m4 7 8 6 8-6"/>',
    file: '<path d="M6.5 3.5h7l4.5 4.5v12.5h-11.5z"/><path d="M13.5 3.5V8H18"/>',
    fileX: '<path d="M6.5 3.5h7l4.5 4.5v12.5h-11.5z"/><path d="M13.5 3.5V8H18M10 12.5l4 4M14 12.5l-4 4"/>',
    plus: '<path d="M12 5.5v13M5.5 12h13"/>',
    info: '<circle cx="12" cy="12" r="8.5"/><path d="M12 11v5.2M12 7.9v.1"/>',
    pdf: '<path d="M6.5 3.5h7l4.5 4.5v12.5h-11.5z"/><path d="M13.5 3.5V8H18M12 11v6m0 0-2.5-2.5M12 17l2.5-2.5"/>',
    refresh: '<path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4.5v4h-4"/>',
    check: '<path d="m5.5 12.5 4 4 9-9"/>',
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2.8v2M12 19.2v2M21.2 12h-2M4.8 12h-2M18.5 5.5l-1.4 1.4M6.9 17.1l-1.4 1.4M18.5 18.5l-1.4-1.4M6.9 6.9 5.5 5.5"/>',
    moon: '<path d="M19.5 14.5A7.5 7.5 0 0 1 9.5 4.5a7.5 7.5 0 1 0 10 10z"/>',
    monitor: '<rect x="3.5" y="4.5" width="17" height="12" rx="2"/><path d="M9 20h6M12 16.5V20"/>'
  };
  function ic(name, cls) {
    return '<svg class="' + (cls || "") + '" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' + ICON[name] + "</svg>";
  }

  // ------------------------------------------------------------------ i18n
  var STR = {
    en: {
      appName: "EmailFileViewer",
      open: "Open…", openTip: "Open email files", openFile: "Open file…", orPress: "or press",
      settings: "Settings", back: "Back", close: "Close", newTab: "Open file",
      exportPdf: "Export PDF", planned: "Planned",
      from: "From", to: "To", cc: "Cc", bcc: "Bcc", replyTo: "Reply to", date: "Date",
      more: "+{n} more", showLess: "Show less", showSubject: "Show full subject", hideSubject: "Show less",
      attachments: "Attachments", attSummary: "{n} files · {size}", attSummaryOne: "1 file · {size}",
      saveAll: "Save all…", save: "Save…", saveName: "Save {name}…", openAtt: "Open", openMsgTip: "Opens here, in a new level of this tab", emailKind: "Email", saveOnly: "Save only",
      showAll: "Show all {n}", showFewer: "Show fewer",
      bodyLabel: "Message content · shown in isolation, scripts off",
      bodyFrame: "Message content",
      fmtHtml: "HTML", fmtText: "Plain text", fmtRtf: "RTF",
      remoteTitle: "Remote images and styles are blocked.",
      remoteWhy: "Loading them lets the sender see that you opened this message.",
      loadRemote: "Load remote content", thisOnly: "This message only",
      remoteLoaded: "Remote content is loaded for this message. Other messages still block it.",
      signed: "Signed", notVerified: "signature not verified", aboutSignature: "About this signature",
      signedHelp: "This message has a digital signature, but EmailFileViewer doesn't check signatures. It can't confirm who sent the message or that it wasn't changed.",
      encrypted: "Encrypted",
      encTitle: "This message is encrypted",
      encBody: "EmailFileViewer can't decrypt S/MIME messages, so the content can't be shown. The details above were sent unencrypted. To read it, open the file in a mail app that has the recipient's certificate.",
      emptyTitle: "Open an email file",
      emptyBody: "Drop .eml or .msg files anywhere in this window, or choose them from your computer.",
      emptyPrivacy: "Messages are read on this computer. Nothing is uploaded unless you choose to.",
      dropTitle: "Release to open {n} files", dropBody: "Each file opens in its own tab. Text and links are ignored.",
      r_unsupported: "Unsupported format", r_corrupt: "Damaged file", r_unreadable: "Can't read file",
      t_unsupported: "This file isn't a supported email",
      b_unsupported: "EmailFileViewer opens .eml and .msg files. This file's contents don't match either format, whatever its extension says.",
      t_corrupt: "This file is damaged",
      b_corrupt: "Parts of the file are missing or malformed, so the message can't be read. If someone sent it to you, ask them for a new copy.",
      t_unreadable: "This file can't be read",
      b_unreadable: "It may have been moved or deleted, or you may not have permission to open it.",
      tryAgain: "Try again", openAnother: "Open another file", closeTab: "Close tab",
      partialTitle: "Opened {ok} of {all} files",
      partialText: "{name} couldn't be opened: the file is damaged.",
      show: "Show", dismiss: "Dismiss", notifications: "Notifications",
      exeTitle: "“{name}” can't be opened here",
      exeBody: "Programs and scripts can change things on your computer, so EmailFileViewer won't open them. You can save the file and decide what to do with it yourself.",
      exeExtra: "Run it only if you trust the sender and were expecting this file.",
      cancel: "Cancel",
      settingsTitle: "Settings", done: "Done",
      language: "Language", languageHint: "Changes apply right away. Email content is never translated.",
      appearance: "Appearance", mode: "Mode", modeLight: "Light", modeDark: "Dark", modeSystem: "Match system", palette: "Colour scheme", selected: "Selected", appearanceHint: "Applies to the app only. Messages always appear on a light page, the way the sender designed them.",
      about: "About", version: "Version", versionValue: "0.1.0 · draft", license: "License", licenseValue: "MIT · free and open source", source: "Source code",
      tabsLabel: "Open files"
    },
    zh: {
      appName: "EmailFileViewer",
      open: "打开…", openTip: "打开邮件文件", openFile: "打开文件…", orPress: "或按",
      settings: "设置", back: "返回", close: "关闭", newTab: "打开文件",
      exportPdf: "导出 PDF", planned: "计划中",
      from: "发件人", to: "收件人", cc: "抄送", bcc: "密送", replyTo: "回复至", date: "日期",
      more: "另外 {n} 人", showLess: "收起", showSubject: "显示完整主题", hideSubject: "收起",
      attachments: "附件", attSummary: "{n} 个文件 · {size}", attSummaryOne: "1 个文件 · {size}",
      saveAll: "全部保存…", save: "保存…", saveName: "保存 {name}…", openAtt: "打开", openMsgTip: "在当前标签页中逐层打开", emailKind: "邮件", saveOnly: "仅可保存",
      showAll: "显示全部 {n} 个", showFewer: "收起",
      bodyLabel: "邮件内容 · 隔离显示，脚本已禁用",
      bodyFrame: "邮件内容",
      fmtHtml: "HTML", fmtText: "纯文本", fmtRtf: "RTF",
      remoteTitle: "已阻止远程图片和样式。",
      remoteWhy: "加载后，发件人可以得知你打开了这封邮件。",
      loadRemote: "加载远程内容", thisOnly: "仅对此邮件生效",
      remoteLoaded: "已为此邮件加载远程内容。其他邮件仍默认阻止。",
      signed: "已签名", notVerified: "签名未验证", aboutSignature: "关于此签名",
      signedHelp: "这封邮件带有数字签名，但 EmailFileViewer 不验证签名，因此无法确认发件人身份，也无法确认内容未被改动。",
      encrypted: "已加密",
      encTitle: "这封邮件已加密",
      encBody: "EmailFileViewer 无法解密 S/MIME 邮件，因此无法显示邮件内容。上方的信息在发送时未加密。如需阅读，请在装有收件人证书的邮件客户端中打开此文件。",
      emptyTitle: "打开邮件文件",
      emptyBody: "把 .eml 或 .msg 文件拖到窗口任意位置，或从电脑中选择。",
      emptyPrivacy: "邮件只在本机读取。除非你主动选择，否则不会上传任何内容。",
      dropTitle: "松开以打开 {n} 个文件", dropBody: "每个文件在单独的标签页中打开。拖入的文字和链接会被忽略。",
      r_unsupported: "不支持的格式", r_corrupt: "文件已损坏", r_unreadable: "无法读取",
      t_unsupported: "这不是受支持的邮件文件",
      b_unsupported: "EmailFileViewer 可以打开 .eml 和 .msg 文件。无论扩展名是什么，这个文件的内容都不符合这两种格式。",
      t_corrupt: "这个文件已损坏",
      b_corrupt: "文件的部分内容缺失或格式错误，无法读取邮件。如果是别人发给你的，请对方重新发送一份。",
      t_unreadable: "无法读取这个文件",
      b_unreadable: "文件可能已被移动或删除，或者你没有打开它的权限。",
      tryAgain: "重试", openAnother: "打开其他文件", closeTab: "关闭标签页",
      partialTitle: "已打开 {all} 个文件中的 {ok} 个",
      partialText: "{name} 无法打开：文件已损坏。",
      show: "查看", dismiss: "关闭", notifications: "通知",
      exeTitle: "无法在此打开“{name}”",
      exeBody: "程序和脚本可以修改你电脑上的内容，因此 EmailFileViewer 不会打开它们。你可以先保存文件，再自行决定如何处理。",
      exeExtra: "只有在你信任发件人、并且确实在等这个文件时，才运行它。",
      cancel: "取消",
      settingsTitle: "设置", done: "完成",
      language: "语言", languageHint: "更改立即生效。邮件内容不会被翻译。",
      appearance: "外观", mode: "模式", modeLight: "浅色", modeDark: "深色", modeSystem: "跟随系统", palette: "配色方案", selected: "已选择", appearanceHint: "只作用于应用界面。邮件始终显示在浅色底上，保持发件人设计的样子。",
      about: "关于", version: "版本", versionValue: "0.1.0 · 草稿", license: "许可", licenseValue: "MIT · 免费开源", source: "源代码",
      tabsLabel: "已打开的文件"
    }
  };
  function t(key, vars) {
    var s = (STR[state.lang] && STR[state.lang][key]) || STR.en[key] || key;
    if (vars) Object.keys(vars).forEach(function (k) { s = s.split("{" + k + "}").join(vars[k]); });
    return s;
  }
  function locale() { return state.lang === "zh" ? "zh-CN" : "en-US"; }

  function fmtSize(b) {
    if (b < 1024) return b + " B";
    if (b < 1024 * 1024) return Math.round(b / 1024) + " KB";
    var mb = b / 1048576;
    return (mb < 10 ? mb.toFixed(1) : Math.round(mb)) + " MB";
  }
  function fmtDate(iso) {
    var opts = state.lang === "zh"
      ? { year: "numeric", month: "long", day: "numeric", weekday: "short", hour: "2-digit", minute: "2-digit", hourCycle: "h23" }
      : { weekday: "short", year: "numeric", month: "short", day: "numeric", hour: "numeric", minute: "2-digit" };
    return new Intl.DateTimeFormat(locale(), opts).format(new Date(iso));
  }
  function splitName(name) {
    var i = name.lastIndexOf(".");
    if (i <= 0 || name.length - i > 8) return [name, ""];
    return [name.slice(0, i), name.slice(i)];
  }
  function fileName(name, cls) {
    var p = splitName(name);
    return '<span class="fname ' + (cls || "") + '"><span class="fname__stem">' + esc(p[0]) + '</span><span class="fname__ext">' + esc(p[1]) + "</span></span>";
  }

  // ------------------------------------------------------------------ data
  function A(name, addr) { return { name: name, addr: addr }; }
  var EXEC = /\.(exe|bat|cmd|com|msi|ps1|vbs|js|jse|wsf|scr|lnk|hta|cpl|app|command|pkg|sh|desktop|appimage|run)$/i;
  var MSG = /\.(eml|msg)$/i;

  var DOCS = {
    aw26: {
      file: "AW26 trade preview.msg",
      subject: "Autumn/Winter ’26 trade preview: catalogue, price list and order form inside",
      from: A("Halden & Moss Trade", "trade@haldenmoss.example"),
      replyTo: [A("Ines Varga", "ines.varga@haldenmoss.example")],
      to: [A("Mira Okafor", "mira@okafor-outfitters.example"), A("Okafor Outfitters Orders", "orders@okafor-outfitters.example")],
      cc: [A("Ines Varga", "ines.varga@haldenmoss.example")],
      date: "2026-10-05T09:00:00-07:00", rawDate: "Mon, 5 Oct 2026 09:00:00 -0700",
      format: "fmtHtml", remote: true, body: "marketing",
      atts: [
        ["AW26-trade-catalogue.pdf", 5033165], ["AW26-wholesale-price-list.xlsx", 319488],
        ["Order-form-AW26.docx", 90112], ["RE: Pallet delivery dates.msg", 47104, "nested"]
      ]
    },
    q4: {
      file: "第四季度对账通知.eml",
      subject: "关于 2026 年第四季度供应商对账及发票开具安排的通知",
      from: A("陈思远", "chen.siyuan@lanxi-trading.example"),
      to: [A("华东区供应商联络组", "suppliers-east@lanxi-trading.example"), A("王立新", "wang.lixin@hengyuan-pack.example")],
      cc: [A("刘静（财务部）", "liu.jing@lanxi-trading.example")],
      date: "2026-10-12T10:24:00+08:00", rawDate: "Mon, 12 Oct 2026 10:24:00 +0800",
      format: "fmtHtml", signed: true, body: "chinese",
      atts: [["2026年第四季度对账单（华东区）.xlsx", 190464], ["开票信息确认函.pdf", 246784]]
    },
    board: {
      file: "Board pack Q3.eml",
      subject: "Board pack for 14 October: please review before Tuesday",
      from: A("Teodor Lind", "t.lind@corvane.example"),
      to: [A("Amara Nwosu", "amara.nwosu@corvane.example"), A("Felix Haas", "felix.haas@corvane.example"), A("Mira Okafor", "mira@okafor-outfitters.example")],
      date: "2026-10-09T18:41:00+02:00", rawDate: "Fri, 9 Oct 2026 18:41:00 +0200",
      encrypted: true, atts: []
    },
    venue: {
      file: "RE- RE- FW- Venue contract, catering and AV for the Lisbon partner summit (final, signed).msg",
      subject: "RE: RE: FW: Venue contract, catering numbers and AV requirements for the Lisbon partner summit — final countersigned copies, revised floor plan (rev C) and the outstanding questions from Thursday’s call",
      from: A("Sofia Almeida", "sofia.almeida@tejo-events.example"),
      to: [
        A("Daniel Reyes", "daniel.reyes@northbeam-partners.example"), A("Priya Raman", "priya.raman@northbeam-partners.example"),
        A("Jonas Eklund", "j.eklund@northbeam-partners.example"), A("Aiko Tanaka", "aiko.tanaka@northbeam-partners.example"),
        A("Marcus Webb", "marcus.webb@northbeam-partners.example"), A("Lucía Fernández", "lucia.fernandez@northbeam-partners.example"),
        A("Tomás Novak", "tomas.novak@northbeam-partners.example"), A("Grace Mensah", "grace.mensah@northbeam-partners.example"),
        A("Henrik Dahl", "henrik.dahl@northbeam-partners.example"), A("Olivia Brandt", "olivia.brandt@northbeam-partners.example"),
        A("Rafael Costa", "rafael.costa@tejo-events.example"), A("Beatriz Lopes", "beatriz.lopes@tejo-events.example"),
        A("", "events@northbeam-partners.example"), A("张伟", "zhang.wei@northbeam-partners.example")
      ],
      cc: [
        A("Catering Desk", "catering@palacio-ribeira.example"), A("Rui Matos", "rui.matos@palacio-ribeira.example"),
        A("AV Bookings", "av@lumen-av.example"), A("Hannah Osei", "hannah.osei@northbeam-partners.example"),
        A("Accounts Payable", "ap@tejo-events.example"), A("Nuno Pires", "nuno.pires@tejo-events.example")
      ],
      bcc: [A("", "archive@tejo-events.example")],
      date: "2026-10-02T17:55:00+01:00", rawDate: "Fri, 2 Oct 2026 17:55:00 +0100",
      format: "fmtText", body: "plain",
      atts: [
        ["Lisbon-partner-summit_venue-contract_FINAL_countersigned_2026-10-02.pdf", 2516582],
        ["Floor plan rev C (ground floor, terrace and breakout rooms).pdf", 6396314],
        ["FW: Catering quote v3.msg", 389120, "nested"],
        ["venue-floorplan-viewer-setup.exe", 14889779],
        ["Catering numbers by day.xlsx", 59392], ["AV-requirements-main-hall.docx", 114688],
        ["terrace-north.jpg", 3460300], ["terrace-south.jpg", 3250585], ["breakout-room-A.jpg", 2306867],
        ["breakout-room-B.jpg", 2097152], ["insurance-certificate.pdf", 419840], ["supplier-list.csv", 9216],
        ["run-of-show_v7.docx", 98304], ["badge-template.ai", 1887437], ["parking-permits.zip", 757760],
        ["accessibility-audit.pdf", 1153434], ["dietary-requirements.xlsx", 31744], ["speaker-briefing.pptx", 9332326],
        ["invoice-deposit-INV-20931.pdf", 90112], ["shuttle-timetable.pdf", 143360], ["signage-proofs.pdf", 5872026],
        ["contacts.vcf", 4096], ["install-fonts.command", 2048]
      ],
      text:
        "Hi all,\n\nAttached are the countersigned venue contract and revision C of the floor plan. The terrace layout changed: the bar moved to the north side, so the AV desk now sits by the east doors.\n\n" +
        "Still open from Thursday's call:\n\n  1. Final headcount for the dinner on the 12th (currently 184)\n  2. Whether we need the terrace after 22:00 (the noise curfew applies)\n  3. A second projector for breakout room B\n\n" +
        "The floor plan viewer is the venue's own tool. The PDF has the same drawings if you'd rather not install anything.\n\n" +
        "Best,\nSofia\n\n--\nSofia Almeida\nEvents Lead, Tejo Events\n+351 21 000 0000\n\n" +
        "> On Thu, 1 Oct 2026 at 16:40, Daniel Reyes wrote:\n> Thanks Sofia. Can we get the signed copies by Monday? Legal wants\n> them on file before we confirm the deposit.\n>\n> Daniel"
    },
    pallet: {
      file: "RE: Pallet delivery dates.msg",
      subject: "RE: Pallet delivery dates for the AW26 order",
      from: A("Ines Varga", "ines.varga@haldenmoss.example"),
      to: [A("Mira Okafor", "mira@okafor-outfitters.example")],
      date: "2026-09-29T14:12:00+01:00", rawDate: "Tue, 29 Sep 2026 14:12:00 +0100",
      format: "fmtHtml", body: "nested",
      atts: [["Delivery-schedule-wk43-44.pdf", 208896], ["Carrier confirmation.eml", 22528, "nested"]]
    },
    carrier: {
      file: "Carrier confirmation.eml",
      subject: "Booking confirmed: 2 pallets, Kendal to Leeds, 22 Oct",
      from: A("Fellgate Freight Bookings", "bookings@fellgate-freight.example"),
      to: [A("Ines Varga", "ines.varga@haldenmoss.example")],
      date: "2026-09-29T11:03:00+01:00", rawDate: "Tue, 29 Sep 2026 11:03:00 +0100",
      format: "fmtText", body: "plain", atts: [],
      text: "Booking reference FG-662-0471\n\nCollection:  Tue 20 Oct, 08:00-12:00, Halden & Moss, Kendal\nDelivery:    Thu 22 Oct, 09:00-13:00, Okafor Outfitters, Leeds\nPallets:     2 (1200 x 1000 mm, 410 kg total)\n\nReply to this email to change the booking."
    },
    bank: {
      file: "Account notice.eml",
      subject: "Action needed: confirm your account details by 20 October",
      from: A("Ferncliff Bank", "notices@ferncliff-bank.example"),
      to: [A("Mira Okafor", "mira@okafor-outfitters.example")],
      date: "2026-10-14T07:30:00+00:00", rawDate: "Wed, 14 Oct 2026 07:30:00 +0000",
      format: "fmtHtml", body: "bank", atts: []
    }
  };

  var ERRORS = {
    unsupported: { file: "holiday-photo.png", icon: "fileX" },
    corrupt: { file: "Invoice_88213.msg", icon: "fileX" },
    unreadable: { file: "Q3 board minutes.eml", icon: "fileX" }
  };

  // ----------------------------------------------------------------- screens
  var MAIN_TABS = [
    { id: "aw26", doc: "aw26", screen: "viewer" },
    { id: "q4", doc: "q4", screen: "signed" },
    { id: "board", doc: "board", screen: "encrypted" }
  ];

  var SCREENS = [
    { id: "empty", group: "启动", label: "空状态",
      title: "空状态：首次启动",
      desc: "整个窗口都是拖放区。没有打开任何文件时不显示标签栏；工具栏只保留设置。底部一句话说明隐私：本地读取，除非用户主动选择否则不上传（为将来需要用户同意的云端导出留余地，D7）。v1 不做“最近打开”。",
      notes: ["按钮文案用“打开文件…”（省略号表示会弹出对话框）。快捷键提示按平台显示 ⌘O / Ctrl+O。", "信封插图里的斜纹边就是全局的“接缝”图形（见查看界面），首次出现即建立这个视觉记忆。"] },
    { id: "drag", group: "启动", label: "拖入文件",
      title: "拖放：文件悬停在窗口上方",
      desc: "Tauri 的 drag-enter 事件带有路径列表，所以可以直接说出文件数量。已有邮件打开时，同样的遮罩盖在整个窗口内容上方（标签栏除外）。",
      notes: ["拖入的是文字或链接时（无文件路径）不显示遮罩，松开后什么也不发生，对应规格“拖入非文件内容”。", "非 .eml/.msg 的文件照样接收，交给解析器判断后给出“不支持的格式”错误（规格按内容识别格式，不能按扩展名预先拒绝）。"] },
    { id: "viewer", group: "查看", label: "邮件查看（远程已阻止）",
      title: "邮件查看主界面：营销邮件，远程内容已阻止",
      desc: "从上到下：标签栏 → 工具栏 → 邮件头 → 附件 → 正文纸面。纸面顶部的斜纹“接缝”是信任边界：接缝以上全部由应用绘制（可信），接缝以下是沙箱 iframe 里的邮件内容（不可信）。远程内容提示条放在接缝以上，邮件内容无法伪造它的位置。",
      notes: ["地址：名称用界面字体，邮箱用等宽字体，便于分辨 l/I、0/O、rn/m 这类仿冒字符。", "日期悬停可看到原始的 Date 头（含原时区）。", "正文里的图片未加载时，邮件自己的 alt 文本和占位色会显示出来；这是邮件本身的样子，界面不做额外处理。", "点击“加载远程内容”即可切到已加载状态（只对当前邮件）。附件“RE: Pallet delivery dates.msg”可以点“打开邮件”。"] },
    { id: "loaded", group: "查看", label: "远程内容已加载",
      title: "远程内容已加载（仅当前邮件）",
      desc: "警示条收起为一行安静的确认文字，说明作用范围。切换到其他标签页或重新打开，仍然默认阻止。",
      notes: ["v1 不提供“重新阻止”（D7 已定）：关闭标签页再打开即恢复阻止，选择不持久化。"] },
    { id: "signed", group: "查看", label: "已签名 · 中文邮件",
      title: "已签名邮件（中文内容）",
      desc: "签名徽标用中性色，不用绿色或对勾：因为签名未经验证，任何“通过”的暗示都是误导。徽标右侧的 ⓘ 打开说明。界面语言切换不影响邮件主题、正文和附件名。",
      notes: ["中文主题和地址名称在两种界面语言下都保持原样。", "日期按界面语言格式化：英文 “Mon, Oct 12, 2026, 10:24 AM”，中文 “2026年10月12日周一 10:24”。"] },
    { id: "encrypted", group: "查看", label: "加密邮件",
      title: "加密邮件",
      desc: "邮件头照常显示，正文位置换成虚线占位框，没有斜纹接缝：接缝只用来包住“来自发件人的内容”，这里没有任何发件人内容可显示。",
      notes: ["加密邮件的 smime.p7m 不作为附件列出（它就是加密后的正文）。"] },
    { id: "edge", group: "边界", label: "长主题 / 多收件人 / 多附件",
      title: "边界情况：长主题、18 个收件人、23 个附件、长文件名、纯文本正文",
      desc: "主题超过两行时折叠并提供“显示完整主题”；每个地址栏最多先显示 3 个，其余折叠为“另外 N 人”；附件超过 6 个时先显示 6 个。纯文本正文保留换行，使用比例字体。",
      notes: ["长文件名只截断主体部分，扩展名始终可见（.pdf / .exe 必须看得到）。完整文件名在 title 中。", "可执行附件（.exe、.command）标“仅可保存”，没有“打开”按钮；双击或回车触发默认动作时弹出说明（见“可执行附件被拒绝”）。", "切到 800px 看窄窗口：日期换到单独一行，工具栏按钮只留图标，附件变两列。"] },
    { id: "nested", group: "边界", label: "嵌套邮件",
      title: "嵌套邮件：在应用内逐层打开",
      desc: "在同一个标签页里向下钻取，工具栏出现面包屑和“返回”。嵌套是父子关系，用面包屑比新开标签页更能表达“这封信在那封信里”。",
      notes: ["本例中的“Carrier confirmation.eml”可以继续打开第三层。", "面包屑每一级都可以点击返回；Alt+← / ⌘[ 返回上一级。"] },
    { id: "link", group: "边界", label: "链接悬停",
      title: "链接悬停显示真实地址",
      desc: "正文中的链接由 sidecar 统一加上 title=实际地址，悬停时由操作系统画出原生提示框（下图中的提示框只是示意）。沙箱里不能运行脚本，界面也拿不到悬停事件，所以不做自定义状态栏。",
      notes: ["示例里链接文字是 ferncliff-bank.example，实际地址是 ferncliff-verify.example.net：这正是规格要防的情况。", "如果新窗口拦截在某个平台上不可靠，设计文档的退路是“链接不可点击 + 显示地址 + 复制链接”，届时再出补充稿。"] },
    { id: "exe", group: "边界", label: "可执行附件被拒绝",
      title: "可执行附件被拒绝打开",
      desc: "弹窗说清楚原因和唯一的出路（保存）。主按钮是“保存…”，不是“确定”；不提供“仍然打开”。",
      notes: ["使用 alertdialog，焦点默认落在“取消”上，避免回车直接保存可疑文件。", "规则按扩展名（design.md D6 的列表），三个平台统一处理。"] },
    { id: "err-unsupported", group: "错误", label: "不支持的格式",
      title: "错误：不支持的格式", desc: "错误状态占据内容区，必定带文件名和原因类别。三类错误使用同一个组件，只换图标以外的文字。", notes: ["原因类别用小号大写标签，标题说发生了什么，正文说下一步怎么做。错误文案不道歉。"] },
    { id: "err-corrupt", group: "错误", label: "文件已损坏",
      title: "错误：文件已损坏", desc: "解析进程崩溃也落到这一类（规格“解析过程中崩溃”）。界面保持可用。", notes: [] },
    { id: "err-unreadable", group: "错误", label: "无法读取",
      title: "错误：无法读取", desc: "权限不足、文件被移动或删除。提供“重试”，因为这一类往往是暂时性的。", notes: [] },
    { id: "partial", group: "错误", label: "多文件部分失败",
      title: "一次打开 3 个文件，其中 1 个损坏",
      desc: "（D7 已定）成功的文件各自开标签页，焦点在第一个成功的文件上；失败的文件也占一个标签页（红色图标），内容区就是对应的错误状态；同时右上角出现一条汇总通知，避免用户漏看。",
      notes: ["点击通知里的“查看”或红色标签页即可看到错误详情；关闭该标签页就清除了这条错误。", "通知不自动消失（含错误信息的通知需要用户确认）。"] },
    { id: "settings", group: "其他", label: "设置",
      title: "设置：语言与外观",
      desc: "设置是一个小对话框。语言保留；新增“外观”：模式（浅色 / 深色 / 跟随系统，默认跟随系统）和配色方案。每个方案卡片里的预览是用该方案自己的令牌实时渲染的迷你界面（标签栏、文字、白色纸面和接缝、主按钮），按当前模式显示。",
      notes: ["所有选项立即生效并持久保存；在原型里点一下就能看到整个界面换配色。草稿导航里的“模式 / 配色”和这里是同一份状态。", "配色只影响应用界面；邮件纸面在任何方案、任何模式下都是白底。", "语言选项用各自的语言书写，不翻译；配色名称随界面语言切换。", "macOS 上同时挂在应用菜单“设置…（⌘,）”；Windows/Linux 用工具栏齿轮。"] },
    { id: "pdf", group: "其他", label: "未来：导出 PDF 位置",
      title: "将来的“导出 PDF”入口（不在 v1）",
      desc: "位置：工具栏右侧、设置齿轮之前，作为当前邮件的主要动作；同时进入原生菜单“文件 → 导出为 PDF…”。v1 不渲染这个控件，图中的虚线框只是占位标注。",
      notes: ["付费云端功能，上线时需要单独的说明和确认对话框（上传前告知会把邮件发往服务器），那是 add-cloud-pdf-export 的设计范围。"] },
    { id: "multi", group: "其他", label: "多文件方案（已定）",
      title: "多文件展示：已采用单窗口 + 标签页（D7）", board: true, desc: "第一轮评审已确认采用标签页。方案 B 保留在这里只作为决策记录，标为“未采用”。", notes: [] }
  ];
  var SCREEN_BY_ID = {};
  SCREENS.forEach(function (s) { SCREEN_BY_ID[s.id] = s; });

  // ------------------------------------------------------------------ state
  var state = {
    screen: "viewer", mode: "system", palette: "verdigris", lang: "en", width: "wide", notes: false,
    open: {}, // transient expand flags
    dialog: null, popover: false, toast: true, partialTab: "aw26", nestedDepth: 1
  };
  try {
    var saved = JSON.parse(localStorage.getItem("efv-draft") || "{}");
    ["mode", "palette", "lang", "width", "notes"].forEach(function (k) { if (k in saved) state[k] = saved[k]; });
  } catch (e) { /* storage unavailable */ }
  if (location.hash && SCREEN_BY_ID[location.hash.slice(1)]) state.screen = location.hash.slice(1);
  // Shareable review links, e.g. index.html?mode=dark&palette=plum&lang=zh&width=narrow&notes=1#edge
  (location.search.slice(1).split("&")).forEach(function (kv) {
    var p = kv.split("=");
    if (p[0] === "mode" || p[0] === "palette" || p[0] === "lang" || p[0] === "width") state[p[0]] = p[1];
    if (p[0] === "theme") state.mode = p[1]; // first-round links
    if (p[0] === "notes") state.notes = p[1] === "1";
  });
  function persist() {
    try { localStorage.setItem("efv-draft", JSON.stringify({ mode: state.mode, palette: state.palette, lang: state.lang, width: state.width, notes: state.notes })); } catch (e) { /* ignore */ }
  }

  // ------------------------------------------------------------- components

  function TabBar(tabs, activeId) {
    return '<div class="tabbar" role="tablist" aria-label="' + esc(t("tabsLabel")) + '" data-note="标签栏：只有 1 个文件时隐藏，≥2 个时出现">' +
      tabs.map(function (tb) {
        var label = tb.error ? tb.file : DOCS[tb.doc].file;
        var stem = splitName(label)[0];
        var sel = tb.id === activeId;
        return '<div class="tab' + (tb.error ? " tab--error" : "") + '" aria-selected="' + sel + '" title="' + esc(label) + '">' +
          ic(tb.error ? "alert" : "envelope", "tab__icon") +
          '<button class="tab__label" role="tab" aria-selected="' + sel + '" tabindex="' + (sel ? 0 : -1) + '" data-act="tab" data-id="' + tb.id + '">' + esc(stem) + "</button>" +
          '<button class="btn btn--icon tab__close" aria-label="' + esc(t("close")) + " " + esc(stem) + '" tabindex="-1">' + ic("x") + "</button></div>";
      }).join("") +
      '<button class="btn btn--icon tabbar__add" aria-label="' + esc(t("newTab")) + '" title="' + esc(t("newTab")) + '">' + ic("plus") + "</button></div>";
  }

  function Toolbar(opts) {
    opts = opts || {};
    var h = '<div class="toolbar" role="toolbar" aria-label="' + esc(t("appName")) + '">';
    if (!opts.minimal) {
      h += '<button class="btn btn--secondary" data-act="open" title="' + esc(t("openTip")) + " (" + (isMac ? "⌘O" : "Ctrl+O") + ')">' + ic("folder") + '<span class="tool-label">' + esc(t("open")) + "</span></button>";
    }
    if (opts.crumbs) {
      h += '<span class="toolbar__divider" aria-hidden="true"></span>' +
        '<button class="btn btn--icon" data-act="crumb" data-depth="' + (opts.crumbs.length - 2) + '" aria-label="' + esc(t("back")) + '" title="' + esc(t("back")) + '">' + ic("back") + "</button>" +
        '<nav class="crumbs" aria-label="Breadcrumb" data-note="嵌套邮件面包屑：每一级可点击返回"><ol>' +
        opts.crumbs.map(function (c, i) {
          var stem = esc(splitName(DOCS[c].file)[0]);
          return i === opts.crumbs.length - 1
            ? '<li><span aria-current="page" title="' + esc(DOCS[c].file) + '">' + stem + "</span></li>"
            : '<li><button class="btn" data-act="crumb" data-depth="' + i + '" title="' + esc(DOCS[c].file) + '"><span>' + stem + "</span></button></li>";
        }).join("") + "</ol></nav>";
    }
    h += '<span class="toolbar__spacer"></span>';
    if (opts.future) {
      h += '<span class="future-slot" aria-hidden="true" title="" data-note="未来：导出 PDF（云端，付费）。v1 不渲染此控件" data-note-pos="below-right">' + ic("pdf") +
        '<span class="future-slot__name">' + esc(t("exportPdf")) + '</span><span class="future-slot__tag">' + esc(t("planned")) + "</span></span>";
    }
    h += '<button class="btn btn--icon" data-act="settings" aria-label="' + esc(t("settings")) + '" title="' + esc(t("settings")) + (isMac ? " (⌘,)" : "") + '">' + ic("gear") + "</button>";
    return h + "</div>";
  }

  function Address(a, cls) {
    if (!a.name || a.name === a.addr) return '<span class="addr ' + (cls || "") + '"><span class="addr__email">' + esc(a.addr) + "</span></span>";
    return '<span class="addr ' + (cls || "") + '"><span class="addr__name">' + esc(a.name) + '</span><span class="addr__email">' + esc(a.addr) + "</span></span>";
  }

  function AddressRow(field, list, limit) {
    if (!list || !list.length) return ""; // missing header: no row, no placeholder
    var key = "rcpt-" + field;
    var expanded = !!state.open[key];
    var shown = expanded || list.length <= limit ? list : list.slice(0, limit);
    var more = "";
    if (list.length > limit) {
      more = expanded
        ? '<button class="btn btn--link addr-more" data-act="toggle" data-key="' + key + '" aria-expanded="true">' + esc(t("showLess")) + "</button>"
        : '<button class="btn btn--link addr-more" data-act="toggle" data-key="' + key + '" aria-expanded="false">' + esc(t("more", { n: list.length - limit })) + "</button>";
    }
    return '<span class="hlabel">' + esc(t(field)) + '</span><span class="hvalue">' + shown.map(function (a) { return Address(a); }).join("") + more + "</span>";
  }

  function MessageHeader(d) {
    var long = d.subject.length > 110;
    var subjOpen = !!state.open.subject;
    var h = '<header class="msghead" data-note="邮件头：缺失的字段整行不显示">';
    h += '<div class="subject-wrap"><h1 class="subject' + (long && !subjOpen ? " subject--clamped" : "") + '" title="' + esc(d.subject) + '">' + esc(d.subject) + "</h1>";
    if (long) h += '<button class="btn btn--link addr-more" data-act="toggle" data-key="subject" aria-expanded="' + subjOpen + '">' + esc(t(subjOpen ? "hideSubject" : "showSubject")) + "</button>";
    h += "</div>";
    h += '<div class="hgrid">';
    h += '<span class="hlabel">' + esc(t("from")) + '</span><span class="hvalue">' + Address(d.from, "addr--from") + "</span>";
    h += '<span class="hlabel hlabel--date">' + esc(t("date")) + '</span><time class="hdate" datetime="' + d.date + '" title="' + esc(d.rawDate) + '">' + esc(fmtDate(d.date)) + "</time>";
    h += AddressRow("replyTo", d.replyTo, 3) + AddressRow("to", d.to, 3) + AddressRow("cc", d.cc, 3) + AddressRow("bcc", d.bcc, 3);
    h += "</div>";
    if (d.signed || d.encrypted) {
      h += '<div class="badges" style="position:relative">';
      if (d.signed) {
        h += '<span class="badge" data-note="中性色：未验证的签名不能看起来像“通过”">' + ic("seal") + "<span>" + esc(t("signed")) + ' <span class="badge__muted">· ' + esc(t("notVerified")) + "</span></span>" +
          '<button class="btn btn--icon" data-act="popover" aria-expanded="' + state.popover + '" aria-controls="sig-help" aria-label="' + esc(t("aboutSignature")) + '">' + ic("info") + "</button></span>";
        if (state.popover) h += '<div class="popover" id="sig-help" role="note" style="top:30px;left:0">' + esc(t("signedHelp")) + "</div>";
      }
      if (d.encrypted) h += '<span class="badge">' + ic("lock") + "<span>" + esc(t("encrypted")) + "</span></span>";
      h += "</div>";
    }
    return h + "</header>";
  }

  function AttachmentItem(a) {
    var name = a[0], size = a[1], nested = a[2] === "nested" || MSG.test(name), exec = EXEC.test(name);
    var ext = splitName(name)[1].replace(".", "");
    var cls = nested ? " att--message" : exec ? " att--exec" : "";
    var type = nested ? ic("envelope") : esc(ext.slice(0, 4) || "file");
    var actions;
    if (nested) {
      actions = '<button class="btn btn--secondary" data-act="nested" data-name="' + esc(name) + '" title="' + esc(t("openMsgTip")) + '">' + esc(t("openAtt")) + "</button>";
    } else if (exec) {
      actions = "";
    } else {
      actions = '<button class="btn btn--secondary">' + esc(t("openAtt")) + "</button>";
    }
    actions += '<button class="btn btn--icon" aria-label="' + esc(t("saveName", { name: name })) + '" title="' + esc(t("save")) + '">' + ic("save") + "</button>";
    return '<li class="att' + cls + '" title="' + esc(name) + '"' + (exec ? ' tabindex="0" data-act="exec" data-name="' + esc(name) + '"' : "") + ">" +
      '<span class="att__type" aria-hidden="true">' + type + "</span>" +
      '<span class="att__body">' + fileName(name) +
      '<span class="att__size">' + (nested ? esc(t("emailKind")) + " · " : "") + esc(fmtSize(size)) + (exec ? '<span class="att__flag">' + ic("lock") + esc(t("saveOnly")) + "</span>" : "") + "</span></span>" +
      '<span class="att__actions">' + actions + "</span></li>";
  }

  function AttachmentList(d) {
    if (!d.atts || !d.atts.length) return "";
    var total = d.atts.reduce(function (s, a) { return s + a[1]; }, 0);
    var many = d.atts.length > 6;
    var expanded = !!state.open.atts;
    var h = '<section class="atts' + (many && !expanded ? " atts--collapsed" : "") + '" aria-labelledby="atts-h" data-note="附件：内嵌（cid:）图片不在此列出">' +
      '<div class="atts__head"><h2 class="atts__title" id="atts-h">' + ic("clip") + esc(t("attachments")) + "</h2>" +
      '<span class="atts__meta">' + esc(d.atts.length === 1 ? t("attSummaryOne", { size: fmtSize(total) }) : t("attSummary", { n: d.atts.length, size: fmtSize(total) })) + "</span>" +
      '<span class="toolbar__spacer"></span>' +
      (d.atts.length > 1 ? '<button class="btn btn--secondary btn--sm">' + ic("save") + esc(t("saveAll")) + "</button>" : "") + "</div>" +
      '<ul class="atts__grid">' + d.atts.map(AttachmentItem).join("") + "</ul>";
    if (many) {
      h += '<div><button class="btn btn--link" data-act="toggle" data-key="atts" aria-expanded="' + expanded + '">' + esc(expanded ? t("showFewer") : t("showAll", { n: d.atts.length })) + "</button></div>";
    }
    return h + "</section>";
  }

  function BodySheet(d, opts) {
    opts = opts || {};
    if (d.encrypted) {
      return '<section class="sheet sheet--placeholder" aria-labelledby="enc-t" data-note="没有发件人内容可显示：虚线框，无接缝">' +
        '<div class="state"><div class="state__icon state__icon--neutral">' + ic("lock") + "</div>" +
        '<h2 class="state__title" id="enc-t">' + esc(t("encTitle")) + '</h2><p class="state__body">' + esc(t("encBody")) + "</p></div></section>";
    }
    var notice = "";
    if (d.remote && !opts.loaded) {
      notice = '<div class="notice notice--warn" role="status" data-note="远程内容提示条：在接缝以上（应用层），只作用于当前邮件">' + ic("imageOff", "notice__icon") +
        '<span class="notice__text"><strong>' + esc(t("remoteTitle")) + '</strong> <span class="notice__sub">' + esc(t("remoteWhy")) + "</span></span>" +
        '<span class="notice__actions"><span class="notice__scope">' + esc(t("thisOnly")) + '</span><button class="btn btn--warn" data-act="load-remote">' + esc(t("loadRemote")) + "</button></span></div>";
    } else if (d.remote && opts.loaded) {
      notice = '<div class="notice notice--quiet" role="status">' + ic("check", "notice__icon") + '<span class="notice__text">' + esc(t("remoteLoaded")) + "</span></div>";
    }
    return '<section class="sheet" aria-label="' + esc(t("bodyFrame")) + '">' +
      '<div class="sheet__chrome"><div class="sheet__label" data-note="接缝以上 = 应用；以下 = 沙箱 iframe" data-note-pos="right">' + ic("shield") + '<span class="sheet__label-text">' + esc(t("bodyLabel")) + '</span><span class="sheet__format">' + esc(t(d.format)) + "</span></div>" + notice + "</div>" +
      '<div class="sheet__seam" aria-hidden="true"></div>' +
      '<div class="sheet__body"><iframe class="sheet__frame" sandbox="allow-popups allow-popups-to-escape-sandbox" referrerpolicy="no-referrer" title="' + esc(t("bodyFrame")) + '" data-body="' + d.body + '" data-doc="' + opts.docId + '" data-loaded="' + (opts.loaded ? 1 : 0) + '"></iframe>' + (opts.extra || "") + "</div></section>";
  }

  function Viewer(docId, opts) {
    opts = opts || {};
    var d = DOCS[docId];
    opts.docId = docId;
    return '<div class="viewer"><div class="viewer__top">' + MessageHeader(d) + AttachmentList(d) + "</div>" + BodySheet(d, opts) + "</div>";
  }

  function EnvelopeArt() {
    return '<svg class="dropzone__art" viewBox="0 0 132 96" aria-hidden="true" focusable="false">' +
      '<defs><pattern id="efv-hatch" width="10" height="10" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="5" height="10" style="fill:var(--efv-accent)"/></pattern></defs>' +
      '<rect x="3" y="10" width="126" height="80" rx="7" style="fill:var(--efv-raised)" stroke="currentColor" stroke-width="1.5"/>' +
      '<rect x="9" y="16" width="114" height="68" rx="3" fill="none" stroke="url(#efv-hatch)" stroke-width="4" opacity=".55"/>' +
      '<path d="M14 22 66 56l52-34" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/>' +
      '<rect x="94" y="56" width="20" height="22" rx="1.5" fill="none" stroke="currentColor" stroke-width="1.2" stroke-dasharray="2 2"/></svg>';
  }

  function EmptyState(dragging) {
    var shortcut = isMac ? "⌘O" : "Ctrl+O";
    var inner = '<div class="state">' + EnvelopeArt() +
      (dragging
        ? '<h1 class="state__title">' + esc(t("dropTitle", { n: 3 })) + '</h1><p class="state__body">' + esc(t("dropBody")) + "</p>"
        : '<h1 class="state__title">' + esc(t("emptyTitle")) + '</h1><p class="state__body">' + esc(t("emptyBody")) + "</p>" +
          '<div class="state__actions"><button class="btn btn--primary btn--lg" data-act="goto" data-screen="viewer">' + ic("folder") + esc(t("openFile")) + "</button></div>" +
          '<span class="dropzone__hint">' + esc(t("orPress")) + ' <span class="kbd">' + shortcut + "</span></span>") +
      "</div>";
    var foot = '<p class="dropzone__foot">' + ic("lock") + "<span>" + esc(t("emptyPrivacy")) + "</span></p>";
    return '<div class="empty"><div class="dropzone' + (dragging ? " dropzone--active" : "") + '" data-note="' + (dragging ? "拖放悬停：斜纹边 + 强调色底" : "整个窗口都是拖放区") + '" data-note-pos="inside">' + inner + foot + "</div></div>";
  }

  function ErrorState(kind, opts) {
    opts = opts || {};
    var e = ERRORS[kind];
    var actions = kind === "unreadable"
      ? '<button class="btn btn--primary">' + ic("refresh") + esc(t("tryAgain")) + '</button><button class="btn btn--secondary">' + esc(t("openAnother")) + "</button>"
      : '<button class="btn btn--secondary">' + ic("folder") + esc(t("openAnother")) + "</button>";
    if (opts.inTab) actions += '<button class="btn">' + esc(t("closeTab")) + "</button>";
    return '<div class="viewer"><div class="state" role="alert" data-note="错误状态：原因类别 + 标题 + 文件名 + 下一步">' +
      '<div class="state__icon state__icon--danger">' + ic(e.icon) + "</div>" +
      '<span class="reason">' + esc(t("r_" + kind)) + "</span>" +
      '<h1 class="state__title">' + esc(t("t_" + kind)) + "</h1>" +
      '<span class="filechip" title="' + esc(e.file) + '">' + ic("file") + fileName(e.file) + "</span>" +
      '<p class="state__body">' + esc(t("b_" + kind)) + "</p>" +
      '<div class="state__actions">' + actions + "</div></div></div>";
  }

  function PartialToast() {
    if (!state.toast) return "";
    var name = ERRORS.corrupt.file;
    var text = esc(t("partialText", { name: "\u0000" })).replace("\u0000", fileName(name));
    return '<div class="toasts" role="region" aria-label="' + esc(t("notifications")) + '"><div class="toast" role="status" data-note="窗口级通知：右上角，不自动消失" data-note-pos="below">' + ic("alert", "toast__icon") +
      '<div class="toast__body"><span class="toast__title">' + esc(t("partialTitle", { ok: 2, all: 3 })) + '</span><span class="toast__text">' + text + "</span>" +
      '<div class="toast__actions"><button class="btn btn--secondary btn--sm" data-act="tab" data-id="err">' + esc(t("show")) + '</button><button class="btn btn--sm" data-act="dismiss">' + esc(t("dismiss")) + "</button></div></div></div></div>";
  }

  function ExeDialog(name) {
    return '<div class="scrim" data-act="scrim"><div class="dialog" role="alertdialog" aria-modal="true" aria-labelledby="exe-t" aria-describedby="exe-b">' +
      '<div class="dialog__icon">' + ic("lock") + "</div>" +
      '<h2 class="dialog__title" id="exe-t">' + esc(t("exeTitle", { name: name })) + "</h2>" +
      '<p class="dialog__body" id="exe-b">' + esc(t("exeBody")) + "</p>" +
      '<p class="dialog__body" style="font-size:var(--efv-text-xs)">' + esc(t("exeExtra")) + "</p>" +
      '<div class="dialog__actions"><button class="btn btn--secondary" data-act="close-dialog" data-autofocus>' + esc(t("cancel")) + '</button><button class="btn btn--primary" data-act="close-dialog">' + ic("save") + esc(t("save")) + "</button></div></div></div>";
  }

  // Live preview: the swatch carries its own data-palette / data-mode, so themes.css
  // resolves that palette's tokens inside it. Same mechanism as the real root.
  function PalettePreview(id) {
    return '<span class="pal__preview" data-palette="' + id + '" data-mode="' + state.mode + '" aria-hidden="true">' +
      '<span class="pp-tabs"><i class="pp-tab pp-tab--on"></i><i class="pp-tab"></i></span>' +
      '<span class="pp-body"><span class="pp-text"><i class="pp-l1"></i><i class="pp-l2"></i></span>' +
      '<span class="pp-sheet"><i class="pp-seam"></i></span><span class="pp-btn"></span></span></span>';
  }

  function Appearance() {
    var P = window.EFV_PALETTES.PALETTES;
    var modes = [["light", "modeLight", "sun"], ["dark", "modeDark", "moon"], ["system", "modeSystem", "monitor"]];
    var h = '<fieldset class="field"><legend class="field__label">' + esc(t("appearance")) + "</legend>";
    h += '<span class="field__sub" id="mode-l">' + esc(t("mode")) + "</span>" +
      '<div class="segmented" role="radiogroup" aria-labelledby="mode-l">' + modes.map(function (m) {
        return '<label class="segmented__opt"><input type="radio" name="efv-mode" value="' + m[0] + '" data-act="mode"' + (state.mode === m[0] ? " checked" : "") + ">" +
          ic(m[2]) + "<span>" + esc(t(m[1])) + "</span></label>";
      }).join("") + "</div>";
    h += '<span class="field__sub" id="pal-l">' + esc(t("palette")) + "</span>" +
      '<div class="palettes" role="radiogroup" aria-labelledby="pal-l">' + P.map(function (p) {
        var on = state.palette === p.id;
        return '<label class="pal" title="' + esc(p.blurb[state.lang]) + '"><input class="sr-only" type="radio" name="efv-palette" value="' + p.id + '" data-act="palette"' + (on ? " checked" : "") + ">" +
          PalettePreview(p.id) +
          '<span class="pal__meta"><span class="pal__name">' + esc(p.name[state.lang]) + "</span>" +
          (on ? '<span class="pal__check">' + ic("check") + '<span class="sr-only">' + esc(t("selected")) + "</span></span>" : "") + "</span></label>";
      }).join("") + "</div>";
    return h + '<p class="field__hint">' + esc(t("appearanceHint")) + "</p></fieldset>";
  }

  function SettingsDialog() {
    function choice(val, name, sub) {
      return '<label class="choice"><input type="radio" name="efv-lang" value="' + val + '"' + (state.lang === val ? " checked" : "") + ' data-act="lang">' +
        '<span class="choice__text"><span class="choice__name" lang="' + (val === "zh" ? "zh-CN" : "en") + '">' + name + '</span>' + (sub ? '<span class="choice__sub">' + sub + "</span>" : "") + "</span></label>";
    }
    return '<div class="scrim" data-act="scrim"><div class="dialog settings" role="dialog" aria-modal="true" aria-labelledby="set-t">' +
      '<div class="settings__head"><h2 class="dialog__title" id="set-t">' + esc(t("settingsTitle")) + '</h2><button class="btn btn--icon" data-act="close-dialog" data-autofocus aria-label="' + esc(t("close")) + '">' + ic("x") + "</button></div>" +
      '<fieldset class="field"><legend class="field__label">' + esc(t("language")) + "</legend>" +
      '<div class="choice-row">' + choice("en", "English", "") + choice("zh", "简体中文", "") + "</div>" +
      '<p class="field__hint">' + esc(t("languageHint")) + "</p></fieldset>" +
      Appearance() +
      '<hr class="rule"><dl class="kv"><dt>' + esc(t("version")) + "</dt><dd>" + esc(t("versionValue")) + "</dd><dt>" + esc(t("license")) + "</dt><dd>" + esc(t("licenseValue")) + "</dd>" +
      "<dt>" + esc(t("source")) + '</dt><dd><button class="btn btn--link" title="https://github.com/real-zony/EmailFileViewer">github.com/real-zony/EmailFileViewer</button></dd></dl>' +
      '<div class="dialog__actions"><button class="btn btn--primary" data-act="close-dialog">' + esc(t("done")) + "</button></div></div></div>";
  }

  function linkHoverMock() {
    return '<div class="tooltip-mock" style="left:200px;top:198px" data-note="示意：操作系统原生 title 提示框" data-note-pos="below">https://ferncliff-verify.example.net/login?session=8841&amp;r=acct</div>' +
      '<svg class="cursor-mock" style="left:186px;top:168px" viewBox="0 0 18 22" aria-hidden="true"><path d="M5 1.5c0-.6.7-.9 1.1-.5l.1.1v8.2c0-.7 1.8-.9 2 0 .1-.8 1.8-.9 2 0 .2-.7 1.8-.6 2 .2V15c0 2.9-2 5.5-5 5.5S2.6 19 1.5 16.7L.6 13c-.3-1 1.2-1.6 1.8-.6L4 14.6V1.5z" fill="#fff" stroke="#000" stroke-width="1"/></svg>';
  }

  // ------------------------------------------------------------- screen map
  function compose() {
    var s = state.screen;
    var out = { title: "", tabs: null, active: null, toolbar: {}, main: "", overlay: "" };
    var mainTabs = function (active) { out.tabs = MAIN_TABS; out.active = active; };

    switch (s) {
      case "empty":
        out.title = t("appName");
        out.toolbar = { minimal: true };
        out.main = EmptyState(false);
        break;
      case "drag":
        out.title = t("appName");
        out.toolbar = { minimal: true };
        out.main = EmptyState(true);
        break;
      case "viewer": case "pdf": case "settings":
        mainTabs("aw26");
        out.main = Viewer("aw26");
        if (s === "pdf") out.toolbar.future = true;
        if (s === "settings") out.overlay = SettingsDialog();
        break;
      case "loaded":
        mainTabs("aw26");
        out.main = Viewer("aw26", { loaded: true });
        break;
      case "signed":
        mainTabs("q4");
        out.main = Viewer("q4");
        break;
      case "encrypted":
        mainTabs("board");
        out.main = Viewer("board");
        break;
      case "edge": case "exe":
        out.main = Viewer("venue");
        if (s === "exe") out.overlay = ExeDialog("venue-floorplan-viewer-setup.exe");
        break;
      case "nested": {
        mainTabs("aw26");
        var chain = ["aw26", "pallet", "carrier"].slice(0, state.nestedDepth + 1);
        out.toolbar.crumbs = chain;
        out.main = Viewer(chain[chain.length - 1]);
        break;
      }
      case "link":
        out.main = Viewer("bank", { extra: linkHoverMock() });
        break;
      case "err-unsupported": case "err-corrupt": case "err-unreadable": {
        var kind = s.slice(4);
        out.title = ERRORS[kind].file + " — " + t("appName");
        out.main = ErrorState(kind);
        break;
      }
      case "partial":
        out.tabs = [MAIN_TABS[0], MAIN_TABS[1], { id: "err", error: true, file: ERRORS.corrupt.file }];
        out.active = state.partialTab;
        out.main = state.partialTab === "err" ? ErrorState("corrupt", { inTab: true }) : Viewer(state.partialTab === "q4" ? "q4" : "aw26");
        out.toastLayer = PartialToast();
        break;
    }
    if (state.dialog === "settings" && s !== "settings") out.overlay = SettingsDialog();
    if (state.dialog && state.dialog.exe && s !== "exe") out.overlay = ExeDialog(state.dialog.exe);
    if (!out.title) {
      var docId = s === "nested" ? out.toolbar.crumbs[out.toolbar.crumbs.length - 1] : (out.active && out.active !== "err" ? (MAIN_TABS.filter(function (x) { return x.id === out.active; })[0] || {}).doc : null);
      if (!docId) docId = { edge: "venue", exe: "venue", link: "bank" }[s];
      out.title = (docId ? DOCS[docId].file + " — " : "") + t("appName");
    }
    return out;
  }

  // ---------------------------------------------------------------- render
  var stage = document.getElementById("stage");

  function renderNav() {
    var groups = {};
    var order = [];
    SCREENS.forEach(function (s) { if (!groups[s.group]) { groups[s.group] = []; order.push(s.group); } groups[s.group].push(s); });
    document.getElementById("chips").innerHTML = order.map(function (g) {
      return '<span class="chips__group">' + g + "</span>" + groups[g].map(function (s) {
        return '<button class="chip" data-screen="' + s.id + '"' + (s.id === state.screen ? ' aria-current="true"' : "") + ">" + s.label + "</button>";
      }).join("");
    }).join("");
    var P = window.EFV_PALETTES.PALETTES;
    document.getElementById("palette-seg").innerHTML = P.map(function (p) {
      return '<button type="button" data-set="palette:' + p.id + '" title="' + esc(p.blurb.zh) + '">' + p.name.zh + "</button>";
    }).join("");
    document.querySelectorAll("[data-set]").forEach(function (b) {
      var p = b.getAttribute("data-set").split(":");
      b.setAttribute("aria-pressed", String(state[p[0]] === p[1]));
    });
    document.getElementById("notes-toggle").checked = state.notes;
  }

  function renderBoard() {
    return '<div class="board">' +
      '<article class="option option--pick"><span class="option__tag">已采用</span><h2>方案 A：单窗口 + 标签页</h2>' +
      '<div class="sketch">' +
      '<div class="mini" style="left:10%;top:12%;width:80%;height:76%"><div class="mini__title">AW26 trade preview.msg — EmailFileViewer</div>' +
      '<div class="mini__tabs"><span class="mini__tab mini__tab--on">AW26 trade preview</span><span class="mini__tab">第四季度对账通知</span><span class="mini__tab mini__tab--err">⚠ Invoice_88213</span></div>' +
      '<div class="mini__tool"></div><div class="mini__body"><div class="mini__line" style="width:60%"></div><div class="mini__line" style="width:40%"></div><div class="mini__sheet"></div></div></div></div>' +
      "<h4>理由</h4><ul>" +
      "<li><b>单实例转发天然对齐</b>：规格要求再次打开文件时由已运行实例处理并带到前台。标签页只有“加一个标签、激活窗口”这一种行为；多窗口则要决定新文件开在哪、哪个窗口置前。</li>" +
      "<li><b>部分失败好表达</b>：失败的文件也有自己的标签页和错误状态，加一条汇总通知即可；多窗口下要么弹出“只装着一条错误”的窗口，要么错误无处安放。</li>" +
      "<li><b>一次拖入 10 个文件不会刷屏</b>：10 个标签而不是 10 个窗口。</li>" +
      "<li><b>安全面更小</b>：Tauri capabilities 只需授予一个主窗口；多窗口要为动态创建的窗口逐一配置和验证 iframe 隔离。</li>" +
      "<li><b>Linux 上更稳</b>：各桌面环境对多窗口的定位、聚焦行为不一致，标签页不受影响。</li></ul>" +
      "<h4>代价与缓解</h4><ul><li>无法并排对比两封邮件。缓解：后续可加“在新窗口中打开”（右键标签），数据层按 docId 设计，已经支持。</li>" +
      "<li>macOS 用户习惯“一文档一窗口”。缓解：只有 1 个文件时不显示标签栏，看起来就是单文档窗口；⌘W 关闭标签，最后一个关闭后回到空状态而不是退出。</li></ul></article>" +
      '<article class="option option--rejected"><span class="option__tag option__tag--alt">未采用</span><h2>方案 B：每个文件一个窗口</h2>' +
      '<div class="sketch">' +
      '<div class="mini" style="left:6%;top:8%;width:52%;height:58%"><div class="mini__title">AW26 trade preview.msg</div><div class="mini__tool"></div><div class="mini__body"><div class="mini__line" style="width:60%"></div><div class="mini__sheet"></div></div></div>' +
      '<div class="mini" style="left:26%;top:24%;width:52%;height:58%"><div class="mini__title">第四季度对账通知.eml</div><div class="mini__tool"></div><div class="mini__body"><div class="mini__line" style="width:50%"></div><div class="mini__sheet"></div></div></div>' +
      '<div class="mini" style="left:46%;top:40%;width:48%;height:52%"><div class="mini__title">Invoice_88213.msg</div><div class="mini__tool"></div><div class="mini__body" style="align-items:center;justify-content:center;font-size:9px;color:var(--efv-danger-ink)">⚠ This file is damaged</div></div>' +
      "</div>" +
      "<h4>优点</h4><ul><li>符合 macOS 文档类应用的习惯（预览、TextEdit）。</li><li>可以并排比较两封邮件，可以分到不同显示器。</li></ul>" +
      "<h4>缺点</h4><ul><li>拖入多个文件会一次弹出多个窗口；错误文件要单独占一个窗口。</li>" +
      "<li>单实例转发后“哪个窗口置前”需要额外规则；Windows 任务栏会出现多个条目。</li>" +
      "<li>每个窗口都要单独做 capabilities 授权和 iframe 隔离验证，测试矩阵翻倍。</li>" +
      "<li>嵌套邮件也要决定：开新窗口还是在原窗口钻取，两套行为并存更难理解。</li></ul></article></div>";
  }

  function render() {
    var root = document.documentElement;
    root.setAttribute("data-mode", state.mode);
    root.setAttribute("data-palette", state.palette);
    root.lang = state.lang === "zh" ? "zh-CN" : "en";
    document.body.classList.toggle("show-notes", state.notes || state.screen === "pdf");
    var meta = SCREEN_BY_ID[state.screen];
    var narrow = state.width === "narrow";
    document.body.style.setProperty("--stage-w", narrow ? "800px" : "1200px");
    document.body.style.setProperty("--stage-h", narrow ? "640px" : "760px");

    var head = '<div class="stage__head"><h1 class="stage__title">' + meta.title + "</h1>" + (meta.desc ? '<p class="stage__desc">' + meta.desc + "</p>" : "") + "</div>";
    if (meta.board) {
      stage.innerHTML = head + renderBoard();
      renderNav();
      return;
    }
    var o = compose();
    var win = '<div class="win win--frame" id="win">' +
      '<div class="win__titlebar" data-note="系统原生标题栏（示意）" data-note-pos="inside">' + esc(o.title) + "</div>" +
      (o.tabs ? TabBar(o.tabs, o.active) : "") +
      Toolbar(o.toolbar) +
      '<div class="win__main">' + o.main + (o.toastLayer || "") + "</div>" + o.overlay + "</div>";
    var notes = meta.notes && meta.notes.length ? '<section class="notes"><h3>评审备注</h3><ul>' + meta.notes.map(function (n) { return "<li>" + n + "</li>"; }).join("") + "</ul></section>" : "";
    stage.innerHTML = head + win + notes;

    stage.querySelectorAll("iframe[data-body]").forEach(function (f) {
      var kind = f.getAttribute("data-body"), docId = f.getAttribute("data-doc");
      var B = window.MAIL_BODIES;
      f.srcdoc = kind === "marketing" ? B.marketing(f.getAttribute("data-loaded") === "1")
        : kind === "plain" ? B.plain(DOCS[docId].text)
        : B[kind]();
    });
    var af = stage.querySelector("[data-autofocus]");
    if (af) af.focus();
    renderNav();
  }

  // ---------------------------------------------------------------- events
  function go(screen) {
    state.screen = screen;
    state.open = {};
    state.dialog = null;
    state.popover = false;
    state.toast = true;
    state.partialTab = "aw26";
    state.nestedDepth = 1;
    if (history.replaceState) history.replaceState(null, "", "#" + screen);
    render();
    window.scrollTo(0, 0);
  }

  document.addEventListener("click", function (e) {
    var chip = e.target.closest(".chip[data-screen]");
    if (chip) return go(chip.getAttribute("data-screen"));
    var set = e.target.closest("[data-set]");
    if (set) {
      var p = set.getAttribute("data-set").split(":");
      state[p[0]] = p[1];
      persist();
      return render();
    }
    var el = e.target.closest("[data-act]");
    if (!el) return;
    var act = el.getAttribute("data-act");
    switch (act) {
      case "toggle": state.open[el.getAttribute("data-key")] = !state.open[el.getAttribute("data-key")]; render(); break;
      case "popover": state.popover = !state.popover; render(); break;
      case "load-remote": go("loaded"); break;
      case "goto": go(el.getAttribute("data-screen")); break;
      case "settings": state.dialog = "settings"; render(); break;
      case "close-dialog":
        if (state.screen === "settings" || state.screen === "exe") { state.dialog = null; go(state.screen === "settings" ? "viewer" : "edge"); }
        else { state.dialog = null; render(); }
        break;
      case "scrim": if (e.target === el) { el.querySelector("[data-act=close-dialog]").click(); } break;
      case "exec": state.dialog = { exe: el.getAttribute("data-name") }; render(); break;
      case "dismiss": state.toast = false; render(); break;
      case "nested":
        if (state.screen === "nested") { state.nestedDepth = Math.min(2, state.nestedDepth + 1); render(); } else go("nested");
        break;
      case "crumb": {
        var depth = +el.getAttribute("data-depth");
        if (state.screen === "nested" && depth >= 1) { state.nestedDepth = depth; render(); } else go("viewer");
        break;
      }
      case "tab": {
        var id = el.getAttribute("data-id");
        if (state.screen === "partial") { state.partialTab = id; state.toast = id === "err" ? false : state.toast; render(); break; }
        var tb = MAIN_TABS.filter(function (x) { return x.id === id; })[0];
        if (tb) go(tb.screen);
        break;
      }
    }
  });
  document.addEventListener("dblclick", function (e) {
    var el = e.target.closest(".att--exec");
    if (el) { state.dialog = { exe: el.getAttribute("data-name") }; render(); }
  });
  document.addEventListener("change", function (e) {
    if (e.target.id === "notes-toggle") { state.notes = e.target.checked; persist(); render(); }
    var a = e.target.getAttribute("data-act");
    if (a === "mode" || a === "palette") {
      state[a] = e.target.value;
      persist();
      render();
      var m = stage.querySelector('input[name="efv-' + a + '"]:checked');
      if (m) m.focus();
    }
    if (e.target.getAttribute("data-act") === "lang") {
      state.lang = e.target.value;
      persist();
      render();
      var r = stage.querySelector('input[name="efv-lang"]:checked');
      if (r) r.focus();
    }
  });
  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape" && stage.querySelector(".scrim")) { stage.querySelector(".scrim [data-act=close-dialog]").click(); }
    if (e.key === "Enter" && e.target.classList && e.target.classList.contains("att--exec")) { state.dialog = { exe: e.target.getAttribute("data-name") }; render(); }
    if (e.key === "Escape" && state.popover) { state.popover = false; render(); }
  });

  render();
})();
