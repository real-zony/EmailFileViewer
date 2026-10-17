// 可执行文件和脚本类附件（design.md D6）。界面用它决定附件卡片是“可打开”还是“仅可保存”；
// 真正拒绝打开的是 Rust / sidecar（`executable_refused`），这里只是同一份清单的前端副本，用于显示。
// 按文件名最后一个扩展名判断，不区分大小写，不论来自哪个系统，只要扩展名在表里就算。
// 判断前先去掉结尾的点和空白（Windows 会忽略它们，“evil.exe.”落盘后就是 evil.exe），与 Core 的
// AttachmentFileNames.IsExecutable 一致；两份清单要同步修改。

const EXECUTABLE_EXTENSIONS = new Set([
  // Windows
  'exe', 'bat', 'cmd', 'com', 'msi', 'ps1', 'vbs', 'js', 'jse', 'wsf', 'scr', 'lnk', 'hta', 'cpl',
  'chm', 'appref-ms', 'diagcab', 'settingcontent-ms', 'library-ms', 'search-ms', 'wsc', 'sct', 'inf', 'msp',
  // macOS
  'app', 'command', 'pkg',
  'dmg', 'terminal', 'fileloc', 'inetloc', 'webloc',
  // Linux
  'sh', 'desktop', 'appimage', 'run',
  // D6 补充：代码和脚本、快捷方式、安装包、Excel 加载项、可挂载的磁盘映像
  'jar', 'vbe', 'ws', 'wsh', 'psm1', 'psd1', 'ps1xml', 'reg', 'msc',
  'scf', 'url', 'pif',
  'msix', 'msixbundle', 'appx', 'appxbundle', 'appinstaller', 'application',
  'xll',
  'iso', 'img', 'vhd', 'vhdx',
])

export function isExecutableFileName(fileName: string): boolean {
  // 去掉路径成分，邮件里的文件名可能是 ..\..\evil.exe 这样的
  const name = fileName.slice(Math.max(fileName.lastIndexOf('/'), fileName.lastIndexOf('\\')) + 1)
  // 结尾的点和空白可以任意交错（“evil.exe. .”）。用循环而不是正则：文件名来自邮件，正则对长串点和空格是平方级回溯
  let end = name.length
  while (end > 0 && (name[end - 1] === '.' || /\s/.test(name[end - 1]))) end--
  const base = name.slice(0, end)
  const dot = base.lastIndexOf('.')
  if (dot === -1) return false
  return EXECUTABLE_EXTENSIONS.has(base.slice(dot + 1).toLowerCase())
}
