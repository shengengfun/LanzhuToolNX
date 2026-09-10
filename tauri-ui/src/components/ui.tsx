import * as React from 'react'
import { Check, ChevronDown, Minus } from 'lucide-react'
import { cn } from '~/lib/utils'

/* ================================================================== *
 * Button —— variant × size 矩阵
 * 对应参考项目 components/ui/button.tsx 的 buttonVariants。
 * 全项目所有按钮都从这里出，不允许在调用处现写颜色。
 * ================================================================== */

export type ButtonVariant = 'default' | 'outline' | 'secondary' | 'ghost' | 'destructive'
export type ButtonSize = 'sm' | 'md' | 'lg' | 'icon'

const buttonBase =
  'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-xl font-semibold ' +
  'cursor-pointer transition-all duration-150 select-none ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/70 focus-visible:ring-offset-1 ' +
  'disabled:pointer-events-none disabled:opacity-50 active:scale-[0.985]'

const buttonVariants: Record<ButtonVariant, string> = {
  default: 'bg-primary text-primary-foreground shadow-xs hover:bg-primary/92',
  outline: 'border border-input/75 bg-card text-foreground shadow-2xs hover:bg-accent/65',
  secondary: 'bg-secondary text-secondary-foreground shadow-2xs hover:bg-secondary/80',
  ghost: 'text-foreground hover:bg-accent/65',
  destructive: 'bg-destructive text-destructive-foreground shadow-xs hover:bg-destructive/90',
}

const buttonSizes: Record<ButtonSize, string> = {
  sm: 'h-8 rounded-lg px-3 text-[13px]',
  md: 'h-10 px-4 text-sm',
  lg: 'h-11 px-6 text-base',
  icon: 'h-8 w-8 rounded-lg p-0',
}

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'outline', size = 'sm', ...props }, ref) => (
    <button
      ref={ref}
      className={cn(buttonBase, buttonVariants[variant], buttonSizes[size], className)}
      {...props}
    />
  ),
)
Button.displayName = 'Button'

/* ================================================================== *
 * 输入类 —— 统一「浅灰填充 + 圆角 + 淡边框 + 聚焦强调环」
 * ================================================================== */

const fieldBase =
  'w-full rounded-xl border border-input/60 bg-muted/40 px-3 text-sm text-foreground ' +
  'shadow-2xs transition-colors placeholder:text-muted-foreground ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/70 focus-visible:bg-card ' +
  'disabled:cursor-not-allowed disabled:opacity-55'

export const Input = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  ({ className, ...props }, ref) => (
    <input ref={ref} className={cn(fieldBase, 'h-8', className)} {...props} />
  ),
)
Input.displayName = 'Input'

export const Textarea = React.forwardRef<
  HTMLTextAreaElement,
  React.TextareaHTMLAttributes<HTMLTextAreaElement>
>(({ className, ...props }, ref) => (
  <textarea
    ref={ref}
    className={cn(fieldBase, 'py-2 leading-relaxed resize-none font-mono text-[13px]', className)}
    {...props}
  />
))
Textarea.displayName = 'Textarea'

/* ================================================================== *
 * Select —— 自绘下拉（原生 <select> 的弹层无法着色）
 * ================================================================== */

export interface SelectProps {
  value: string
  onValueChange: (v: string) => void
  options: readonly string[]
  className?: string
  disabled?: boolean
  placeholder?: string
  /** 列表宽度不够时允许更宽 */
  listClassName?: string
}

export function Select({
  value,
  onValueChange,
  options,
  className,
  disabled,
  placeholder,
  listClassName,
}: SelectProps) {
  const [open, setOpen] = React.useState(false)
  const boxRef = React.useRef<HTMLDivElement>(null)

  React.useEffect(() => {
    if (!open) return
    const onDoc = (e: MouseEvent) => {
      if (!boxRef.current?.contains(e.target as Node)) setOpen(false)
    }
    const onEsc = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', onDoc)
    document.addEventListener('keydown', onEsc)
    return () => {
      document.removeEventListener('mousedown', onDoc)
      document.removeEventListener('keydown', onEsc)
    }
  }, [open])

  return (
    <div ref={boxRef} className={cn('relative', className)}>
      <button
        type="button"
        disabled={disabled}
        onClick={() => setOpen((v) => !v)}
        className={cn(
          fieldBase,
          'flex h-8 items-center justify-between gap-1.5 text-left',
          open && 'bg-card ring-2 ring-ring/70',
          disabled && 'cursor-not-allowed opacity-55',
        )}
      >
        <span className={cn('truncate', !value && 'text-muted-foreground')} title={value}>
          {value || placeholder || ''}
        </span>
        <ChevronDown
          className={cn(
            'size-3.5 shrink-0 text-muted-foreground transition-transform',
            open && 'rotate-180',
          )}
        />
      </button>

      {open && (
        <div
          className={cn(
            'absolute z-50 mt-1 max-h-72 min-w-full max-w-[360px] overflow-auto rounded-xl border border-border/80',
            'bg-popover p-1 shadow-md backdrop-blur',
            listClassName,
          )}
        >
          {options.map((opt) => (
            <button
              key={opt}
              type="button"
              onClick={() => {
                onValueChange(opt)
                setOpen(false)
              }}
              className={cn(
                'flex w-full items-center gap-2 rounded-lg px-2.5 py-1.5 text-left text-[13px] transition-colors',
                opt === value
                  ? 'bg-accent/70 font-semibold text-accent-foreground'
                  : 'hover:bg-accent/50',
              )}
            >
              <Check className={cn('size-3.5 shrink-0', opt === value ? 'opacity-100' : 'opacity-0')} />
              <span className="truncate">{opt}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

/* ================================================================== *
 * Checkbox / Radio / Switch
 * ================================================================== */

export interface CheckboxProps {
  checked: boolean
  onCheckedChange: (v: boolean) => void
  label?: string
  disabled?: boolean
  className?: string
}

export function Checkbox({ checked, onCheckedChange, label, disabled, className }: CheckboxProps) {
  return (
    <label
      className={cn(
        'inline-flex cursor-pointer items-center gap-2 text-[13px] select-none',
        disabled && 'cursor-not-allowed opacity-55',
        className,
      )}
    >
      <span
        role="checkbox"
        aria-checked={checked}
        onClick={() => !disabled && onCheckedChange(!checked)}
        className={cn(
          'flex size-4 shrink-0 items-center justify-center rounded-[5px] border transition-all duration-150',
          checked
            ? 'border-primary bg-primary text-primary-foreground'
            : 'border-input bg-card hover:border-primary/70',
        )}
      >
        {checked && <Check className="size-3" strokeWidth={3} />}
      </span>
      {label && <span className="text-foreground">{label}</span>}
    </label>
  )
}

export function Radio({
  checked,
  onSelect,
  label,
  disabled,
}: {
  checked: boolean
  onSelect: () => void
  label?: string
  disabled?: boolean
}) {
  return (
    <label
      className={cn(
        'inline-flex cursor-pointer items-center gap-2 text-[13px] select-none',
        disabled && 'cursor-not-allowed opacity-55',
      )}
    >
      <span
        role="radio"
        aria-checked={checked}
        onClick={() => !disabled && onSelect()}
        className={cn(
          'flex size-4 shrink-0 items-center justify-center rounded-full border transition-all duration-150',
          checked ? 'border-primary bg-primary' : 'border-input bg-card hover:border-primary/70',
        )}
      >
        {checked && <span className="size-1.5 rounded-full bg-primary-foreground" />}
      </span>
      {label && <span className="text-foreground">{label}</span>}
    </label>
  )
}

export function Switch({
  checked,
  onCheckedChange,
  label,
}: {
  checked: boolean
  onCheckedChange: (v: boolean) => void
  label?: string
}) {
  return (
    <label className="inline-flex cursor-pointer items-center gap-2 text-[13px] select-none">
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        onClick={() => onCheckedChange(!checked)}
        className={cn(
          'relative h-5 w-9 shrink-0 rounded-full transition-colors',
          checked ? 'bg-primary' : 'bg-input',
        )}
      >
        <span
          className={cn(
            'absolute top-0.5 size-4 rounded-full bg-card shadow-xs transition-all',
            checked ? 'left-4.5' : 'left-0.5',
          )}
        />
      </button>
      {label && <span>{label}</span>}
    </label>
  )
}

/* ================================================================== *
 * 布局：Card / GroupCard / Field / Section
 * ================================================================== */

export function Card({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        'rounded-2xl border border-border/70 bg-card shadow-sm backdrop-blur-sm',
        className,
      )}
      {...props}
    />
  )
}

/**
 * 带标题的分组卡片。原版 GroupBox 的等价物，
 * 但标题是真的「压在边框上」而不是自己画一条假边框。
 */
export function GroupCard({
  title,
  children,
  className,
  right,
}: {
  title: string
  children: React.ReactNode
  className?: string
  right?: React.ReactNode
}) {
  return (
    <div
      className={cn(
        'relative rounded-xl border border-border/70 bg-card/60 px-3.5 pt-4 pb-3',
        className,
      )}
    >
      <div className="absolute -top-2 left-3 flex items-center gap-2 bg-card px-1.5">
        <span className="text-[12px] font-semibold text-muted-foreground">{title}</span>
        {right}
      </div>
      {children}
    </div>
  )
}

/**
 * 表单行：固定的标签列 + 控件列。
 * 标签列宽统一 → 右边缘天然对齐，这正是原版最缺的东西。
 */
export function Field({
  label,
  children,
  className,
  labelWidth = 68,
  align = 'end',
}: {
  label?: string
  children: React.ReactNode
  className?: string
  labelWidth?: number
  align?: 'start' | 'end'
}) {
  return (
    <div className={cn('flex items-center gap-2', className)}>
      {label !== undefined && (
        <span
          className={cn(
            'shrink-0 text-[13px] whitespace-nowrap text-muted-foreground',
            align === 'end' ? 'text-right' : 'text-left',
          )}
          style={{ width: labelWidth }}
        >
          {label}
        </span>
      )}
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  )
}

export function Row({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex items-center gap-2', className)} {...props} />
}

export function Separator({ className }: { className?: string }) {
  return <div className={cn('h-px w-full bg-border/70', className)} />
}

export function Badge({
  children,
  variant = 'muted',
  className,
}: {
  children: React.ReactNode
  variant?: 'muted' | 'accent' | 'success' | 'destructive'
  className?: string
}) {
  const styles = {
    muted: 'bg-muted text-muted-foreground',
    accent: 'bg-accent/70 text-accent-foreground',
    success: 'bg-primary/12 text-primary',
    destructive: 'bg-destructive/12 text-destructive',
  }[variant]
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-md px-1.5 py-0.5 text-[11px] font-semibold',
        styles,
        className,
      )}
    >
      {children}
    </span>
  )
}

export function Progress({ value, className }: { value: number; className?: string }) {
  return (
    <div className={cn('h-1.5 w-full overflow-hidden rounded-full bg-muted', className)}>
      <div
        className="h-full rounded-full bg-primary transition-all duration-300"
        style={{ width: `${Math.max(0, Math.min(100, value))}%` }}
      />
    </div>
  )
}

/* ================================================================== *
 * 列表（原版 ListBox 的等价物，但带空状态和更好的行样式）
 * ================================================================== */

export function ListShell({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        'overflow-auto rounded-xl border border-border/60 bg-muted/30',
        className,
      )}
    >
      {children}
    </div>
  )
}

export function ListRow({
  selected,
  onClick,
  children,
  className,
}: {
  selected?: boolean
  onClick?: () => void
  children: React.ReactNode
  className?: string
}) {
  return (
    <div
      onClick={onClick}
      className={cn(
        'cursor-default truncate px-2.5 py-[3px] text-[12.5px] transition-colors',
        selected ? 'bg-primary/12 text-primary' : 'hover:bg-accent/50',
        className,
      )}
    >
      {children}
    </div>
  )
}

export function EmptyState({
  icon,
  title,
  hint,
  className,
}: {
  icon?: React.ReactNode
  title: string
  hint?: string
  className?: string
}) {
  return (
    <div
      className={cn(
        'flex h-full flex-col items-center justify-center gap-1.5 p-6 text-center',
        className,
      )}
    >
      {icon && <div className="text-muted-foreground/60">{icon}</div>}
      <div className="text-[13px] font-medium text-muted-foreground">{title}</div>
      {hint && <div className="text-[12px] text-muted-foreground/70">{hint}</div>}
    </div>
  )
}

export { Minus }
