import { FolderOpen } from 'lucide-react'
import { Button, Field } from './ui'
import { cn } from '~/lib/utils'
import { useDropZone } from '~/state'

/* ================================================================== *
 * 路径选择行 —— 原版「文本框 + 浏览按钮」的等价物，同时是拖放目标
 *
 * 注意：拖放走的是 useDropZone（内部用 Tauri 的拖放事件），
 * 不能用 HTML5 的 onDrop —— Tauri 会把 WebView 的原生拖放拦掉，
 * 这就是之前"拖动导入完全不能用"的原因。
 * ================================================================== */

export function PathRow({
  label,
  value,
  onChange,
  onBrowse,
  placeholder,
  labelWidth = 52,
  onDropFile,
}: {
  label: string
  value: string
  onChange?: (v: string) => void
  onBrowse: () => void
  placeholder?: string
  labelWidth?: number
  onDropFile?: (paths: string[]) => void
}) {
  const zone = useDropZone(onDropFile)

  return (
    <Field label={label} labelWidth={labelWidth}>
      <div
        {...zone.props}
        className={cn(
          'flex items-center gap-1.5 rounded-xl transition-all',
          zone.over && 'bg-primary/8 ring-2 ring-primary/60',
        )}
      >
        <input
          value={value}
          onChange={(e) => onChange?.(e.target.value)}
          placeholder={placeholder}
          spellCheck={false}
          className={cn(
            'h-8 min-w-0 flex-1 rounded-xl border border-input/60 bg-muted/40 px-3 text-[12.5px]',
            'shadow-2xs transition-colors placeholder:text-muted-foreground',
            'focus-visible:bg-card focus-visible:ring-2 focus-visible:ring-ring/70 focus-visible:outline-none',
          )}
          style={{ userSelect: 'text' }}
        />
        <Button variant="outline" size="icon" onClick={onBrowse} title="浏览">
          <FolderOpen className="size-4" />
        </Button>
      </div>
    </Field>
  )
}

/**
 * 页内页签（分段按钮）。
 *
 * 「封装 / 封装转换 / 抽取」「转码 / 波形粗剪」都用它 ——
 * 比再塞一个 TabControl 轻，也不必让每个面板都去做滚动适配。
 */
export function SegTab({
  active,
  onClick,
  icon,
  label,
  hint,
}: {
  active: boolean
  onClick: () => void
  icon?: React.ReactNode
  label: string
  hint?: string
}) {
  return (
    <button
      type="button"
      title={hint}
      onClick={onClick}
      className={cn(
        'flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-[12.5px] font-semibold transition-all',
        active
          ? 'border-primary/40 bg-primary/10 text-primary shadow-2xs'
          : 'border-border/60 bg-card/60 text-muted-foreground hover:bg-accent/50',
      )}
    >
      {icon}
      {label}
    </button>
  )
}
