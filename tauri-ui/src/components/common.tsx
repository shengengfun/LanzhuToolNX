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

/* ================================================================== *
 * 滑条 —— 渐变轨道 + 圆形滑块，视觉规格照搬 RinaDown 的颜色选择器
 * （lib/src/pages/settings_page.dart 的 _GradientSlider / _HueSlider）
 *
 * 为什么不用 <input type="range">：
 *   1. 轨道只能是一条实色，"拖到这儿会变成什么颜色"画不出来；
 *   2. 滑块是系统画的原生控件，各 WebView 版本长相不一，白描边/投影调不了。
 * 规格：容器 22px 高，轨道 14px 圆角 + 0.5px 描边，滑块 16px 圆（2px 白描边
 * + 3px 投影），滑块左端位置 clamp 在轨道内（0 → 贴左，1 → 贴右）。
 * ================================================================== */

export function Slider({
  value,
  min = 0,
  max = 100,
  step = 1,
  onChange,
  gradient,
  thumbColor,
  label,
  className,
}: {
  value: number
  min?: number
  max?: number
  step?: number
  /** 拖动与键盘都会调它，值已按 step 吸附并夹在 [min, max] */
  onChange: (v: number) => void
  /** 轨道背景（任意 CSS background 值）。不给就是一条实色轨道。 */
  gradient?: string
  /** 滑块填充色。不给就用强调色。 */
  thumbColor?: string
  /** 无障碍名称（读写屏要用） */
  label: string
  className?: string
}) {
  const span = max - min
  const frac = span > 0 ? Math.min(1, Math.max(0, (value - min) / span)) : 0

  /** 按 step 吸附并去掉浮点毛刺（0.1 步长下 3×0.1 = 0.30000000000000004）。 */
  const emit = (v: number) => {
    const clamped = Math.min(max, Math.max(min, v))
    onChange(Number((Math.round((clamped - min) / step) * step + min).toFixed(6)))
  }

  const fromClientX = (el: HTMLElement, clientX: number) => {
    const r = el.getBoundingClientRect()
    if (r.width <= 0) return
    emit(min + Math.min(1, Math.max(0, (clientX - r.left) / r.width)) * span)
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    const jump: Record<string, number> = {
      ArrowRight: step,
      ArrowUp: step,
      ArrowLeft: -step,
      ArrowDown: -step,
      PageUp: step * 10,
      PageDown: -step * 10,
    }
    if (e.key === 'Home' || e.key === 'End') {
      e.preventDefault()
      emit(e.key === 'Home' ? min : max)
      return
    }
    const d = jump[e.key]
    if (d === undefined) return
    e.preventDefault()
    emit(value + d)
  }

  /** 指针捕获失败（合成事件、极老的 WebView）不该连拖动一起废掉，吞掉即可。 */
  const capture = (el: HTMLElement, pointerId: number, on: boolean) => {
    try {
      if (on) el.setPointerCapture(pointerId)
      else el.releasePointerCapture(pointerId)
    } catch {
      /* 忽略 */
    }
  }

  return (
    <div
      role="slider"
      tabIndex={0}
      aria-label={label}
      aria-valuemin={min}
      aria-valuemax={max}
      aria-valuenow={Math.round(value)}
      onPointerDown={(e) => {
        if (e.button !== 0) return
        capture(e.currentTarget, e.pointerId, true)
        fromClientX(e.currentTarget, e.clientX)
      }}
      // 用 buttons 判拖动而不是 hasPointerCapture：捕获拿不到时依然能拖
      onPointerMove={(e) => {
        if (e.buttons === 1) fromClientX(e.currentTarget, e.clientX)
      }}
      onPointerUp={(e) => capture(e.currentTarget, e.pointerId, false)}
      onPointerCancel={(e) => capture(e.currentTarget, e.pointerId, false)}
      onKeyDown={onKeyDown}
      className={cn(
        'group relative h-5.5 min-w-0 flex-1 cursor-pointer touch-none select-none',
        'focus-visible:outline-none',
        className,
      )}
    >
      {/* 轨道 */}
      <div
        className={cn(
          'pointer-events-none absolute inset-x-0 top-1/2 h-3.5 -translate-y-1/2',
          'rounded-sm border-[0.5px] border-border/70 shadow-2xs',
          'group-focus-visible:ring-2 group-focus-visible:ring-ring/70',
          !gradient && 'bg-muted',
        )}
        style={gradient ? { background: gradient } : undefined}
      />

      {/* 滑块：left 用 calc 让 0 / 1 两端正好贴住轨道内沿，不用再 JS 夹一次 */}
      <div
        className={cn(
          'pointer-events-none absolute top-1/2 size-4 -translate-y-1/2 rounded-full',
          'border-2 border-white shadow-[0_0_3px_rgba(0,0,0,0.3)]',
          'transition-transform group-hover:scale-105 group-focus-visible:scale-105',
        )}
        style={{
          left: `calc(${frac * 100}% - ${frac * 16}px)`,
          backgroundColor: thumbColor ?? 'var(--primary)',
        }}
      />
    </div>
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
