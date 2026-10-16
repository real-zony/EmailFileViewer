// 与语言无关的展示用格式化

// 按 1024 进制；小于 10 MB 保留一位小数
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  const mb = bytes / 1048576
  return `${mb < 10 ? mb.toFixed(1) : Math.round(mb)} MB`
}

// 拆成主体和扩展名，扩展名最长 7 个字符（含点），否则视为没有扩展名
export function splitFileName(name: string): [stem: string, ext: string] {
  const i = name.lastIndexOf('.')
  if (i <= 0 || name.length - i > 8) return [name, '']
  return [name.slice(0, i), name.slice(i)]
}
