import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react'
import { Icon, type IconName } from './Icon.tsx'
import { cx } from './cx.ts'

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'warn' | 'link'
export type ButtonSize = 'sm' | 'md' | 'lg'

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  /** 文字前的图标 */
  icon?: IconName
  ref?: Ref<HTMLButtonElement>
  children?: ReactNode
}

export function Button({ variant = 'ghost', size = 'md', icon, className, children, type = 'button', ref, ...rest }: ButtonProps) {
  return (
    <button
      ref={ref}
      type={type}
      className={cx('btn', variant !== 'ghost' && `btn--${variant}`, size !== 'md' && `btn--${size}`, className)}
      {...rest}
    >
      {icon && <Icon name={icon} />}
      {children}
    </button>
  )
}

export interface IconButtonProps extends Omit<ButtonProps, 'icon' | 'children' | 'aria-label'> {
  icon: IconName
  /** 可访问名称，同时作为默认的悬停提示 */
  label: string
}

// 只有图标的按钮：必须有可访问名称
export function IconButton({ icon, label, title, className, ...rest }: IconButtonProps) {
  return (
    <Button {...rest} className={cx('btn--icon', className)} aria-label={label} title={title ?? label}>
      <Icon name={icon} />
    </Button>
  )
}

export function Kbd({ children }: { children: ReactNode }) {
  return <kbd className="kbd">{children}</kbd>
}
