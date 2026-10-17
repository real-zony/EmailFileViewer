import { splitFileName } from './format.ts'
import { cx } from './cx.ts'

// 文件名：主体部分截断，扩展名始终可见。完整名称由父元素的 title 提供。
export function FileName({ name, className }: { name: string; className?: string }) {
  const [stem, ext] = splitFileName(name)
  return (
    <span className={cx('fname', className)}>
      <span className="fname__stem">{stem}</span>
      <span className="fname__ext">{ext}</span>
    </span>
  )
}
