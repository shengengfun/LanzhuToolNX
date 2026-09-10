import * as React from 'react'
import { Copy, Minus, Square, X } from 'lucide-react'
import { cn } from '~/lib/utils'
import { isTauri } from '~/lib/api'

/**
 * 自绘标题栏。
 *
 * `data-tauri-drag-region` 是 Tauri 内置的拖动支持 —— 只要打上这个属性，
 * 按住该区域就能拖动整个窗口，而且**不会**影响任何子控件的点击。
 * （这正是 WinForms 那边折腾很久才修好的问题，在 Tauri 里是一条属性。）
 *
 * 浏览器预览时没有 Tauri 运行时，窗口按钮降级成无操作，不抛错。
 */
export function TitleBar({ title, version }: { title: string; version: string }) {
  const [maximized, setMaximized] = React.useState(false)

  React.useEffect(() => {
    if (!isTauri()) return
    let alive = true
    let un: (() => void) | undefined

    // 动态 import，避免在非 Tauri 环境执行到窗口 API
    import('@tauri-apps/api/window')
      .then(async ({ getCurrentWindow }) => {
        const w = getCurrentWindow()
        const sync = async () => {
          const v = await w.isMaximized().catch(() => false)
          if (alive) setMaximized(v)
        }
        await sync()
        const f = await w.onResized(() => void sync()).catch(() => undefined)
        if (alive) un = f
        else f?.()
      })
      .catch(() => void 0)

    return () => {
      alive = false
      un?.()
    }
  }, [])

  const win = async () => {
    if (!isTauri()) return null
    const { getCurrentWindow } = await import('@tauri-apps/api/window')
    return getCurrentWindow()
  }

  return (
    <header
      data-tauri-drag-region
      className="flex h-11 shrink-0 items-center gap-3 border-b border-border/60 bg-card/70 pr-2 pl-4 backdrop-blur"
    >
      <div data-tauri-drag-region className="flex items-center gap-2.5">
        <BrandMark />
        <span data-tauri-drag-region className="text-[13.5px] font-semibold tracking-wide">
          {title}
        </span>
        <span className="rounded-md bg-muted px-1.5 py-0.5 text-[11px] text-muted-foreground">
          v{version}
        </span>
      </div>

      {/* 中间留白也是拖动区，双击最大化由 Tauri 默认行为处理 */}
      <div data-tauri-drag-region className="h-full flex-1" />

      <div className="flex items-center">
        <WinButton label="最小化" onClick={() => void win().then((w) => w?.minimize())}>
          <Minus className="size-3.5" />
        </WinButton>
        <WinButton
          label={maximized ? '还原' : '最大化'}
          onClick={() => void win().then((w) => w?.toggleMaximize())}
        >
          {maximized ? <Copy className="size-3 -scale-x-100" /> : <Square className="size-3" />}
        </WinButton>
        <WinButton label="关闭" danger onClick={() => void win().then((w) => w?.close())}>
          <X className="size-4" />
        </WinButton>
      </div>
    </header>
  )
}

function WinButton({
  children,
  onClick,
  label,
  danger,
}: {
  children: React.ReactNode
  onClick: () => void
  label: string
  danger?: boolean
}) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      onClick={onClick}
      className={cn(
        'flex h-8 w-10 items-center justify-center rounded-lg text-muted-foreground transition-colors',
        danger
          ? 'hover:bg-destructive hover:text-white'
          : 'hover:bg-accent/70 hover:text-foreground',
      )}
    >
      {children}
    </button>
  )
}

function BrandMark() {
  return (
    <span className="flex size-6 items-center justify-center rounded-lg bg-primary text-[13px] font-bold text-primary-foreground shadow-xs">
      岚
    </span>
  )
}
