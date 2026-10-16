// 可执行文件和脚本类附件（design.md D6）。界面用它决定附件卡片是“可打开”还是“仅可保存”；
// 真正拒绝打开的是 Rust / sidecar（`executable_refused`），这里只是同一份清单的前端副本，用于显示。
// 按文件名最后一个扩展名判断，不区分大小写，不论来自哪个系统，只要扩展名在表里就算。

const EXECUTABLE_EXTENSIONS = new Set([
  // Windows
  'exe', 'bat', 'cmd', 'com', 'msi', 'ps1', 'vbs', 'js', 'jse', 'wsf', 'scr', 'lnk', 'hta', 'cpl',
  // macOS
  'app', 'command', 'pkg',
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
  const base = fileName.slice(Math.max(fileName.lastIndexOf('/'), fileName.lastIndexOf('\\')) + 1).trimEnd()
  const dot = base.lastIndexOf('.')
  if (dot === -1) return false
  return EXECUTABLE_EXTENSIONS.has(base.slice(dot + 1).toLowerCase())
}
