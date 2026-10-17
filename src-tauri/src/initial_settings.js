// 由 Rust 在创建主窗口时作为 initialization_script 注入（见 settings.rs）。
// 下面的 settings 占位符会在注入前被替换为设置的 JSON，值都经过校验，只可能是已知的枚举值。
// ui/src/settings/__tests__/initScript.test.ts 会在 jsdom 里执行本文件。
(function () {
  // Windows 上初始化脚本也会进入子框架，邮件内容所在的 iframe 不能被改动
  if (window.top !== window) return;

  var settings = __EFV_SETTINGS_JSON__;
  window.__EFV_INITIAL_SETTINGS__ = settings;

  function apply() {
    var root = document.documentElement;
    if (!root) return false;
    root.setAttribute('data-mode', settings.mode);
    root.setAttribute('data-palette', settings.palette);
    root.setAttribute('lang', settings.language);
    return true;
  }

  if (apply()) return;
  // 脚本运行时 <html> 可能还没创建：它一出现就设置，赶在首次绘制之前
  var observer = new MutationObserver(function () {
    if (apply()) observer.disconnect();
  });
  observer.observe(document, { childList: true });
})();
