import * as React from 'react'
import { Check } from 'lucide-react'
import { cn } from '~/lib/utils'
import { ACCENT_PRESETS, hexToRgb, inkOn, rgbToHex } from '~/lib/appearance'

/**
 * 强调色选择器。
 *
 * 三层结构，按"从粗到细"排：
 *   1. 13 个角色色板 —— 选中的色块里打对号（对号颜色按底色亮度现算，
 *      不然中须霞的黄底上白勾直接看不见）
 *   2. 选中"自定义"后才展开 R / G / B 三根调整条（实时生效）
 *   3. 再下面是 HTML 色号输入框，给习惯直接填色号的人
 *
 * 色号与滑条**双向同步**：拖滑条 → 色号跟着变；改色号 → 滑条跟着走。
 */
export function AccentPicker({
  accent,
  accentCustom,
  onPick,
  onCustom,
}: {
  accent: string
  accentCustom: string
  /** 选中某个角色配色（会清掉自定义色） */
  onPick: (id: string) => void
  /** 设置自定义色（空串 = 退出自定义） */
  onCustom: (hex: string) => void
}) {
  const preset = ACCENT_PRESETS.find((p) => p.id === accent) ?? ACCENT_PRESETS[0]
  const custom = accentCustom.trim()
  const active = !!custom
  /** 当前实际生效的色号（自定义优先，否则预设色） */
  const effective = custom || preset.color

  // 滑条状态：拖的时候是它自己，外部改了色号再回灌
  const [rgb, setRgb] = React.useState<[number, number, number]>(
    () => hexToRgb(effective) ?? [242, 153, 146],
  )
  React.useEffect(() => {
    const parsed = hexToRgb(effective)
    if (parsed) setRgb((cur) => (rgbToHex(cur) === rgbToHex(parsed) ? cur : parsed))
  }, [effective])

  const setChannel = (i: 0 | 1 | 2, v: number) => {
    const next: [number, number, number] = [...rgb] as [number, number, number]
    next[i] = v
    setRgb(next)
    onCustom(rgbToHex(next))
  }

  return (
    <div className="space-y-2.5">
      {/* ---- 1. 色板 ---- */}
      <div className="flex flex-wrap items-center gap-1.5">
        {ACCENT_PRESETS.map((p) => {
          const on = !active && accent === p.id
          return (
            <button
              key={p.id}
              type="button"
              title={`${p.name} ${p.color}`}
              aria-label={p.name}
              aria-pressed={on}
              onClick={() => onPick(p.id)}
              className={cn(
                'flex size-7 items-center justify-center rounded-full border-2 transition-transform hover:scale-110',
                on
                  ? 'border-foreground ring-2 ring-foreground/20'
                  : 'border-border/60 hover:border-foreground/40',
              )}
              style={{ backgroundColor: p.color }}
            >
              {on && <Check className="size-3.5" strokeWidth={3.5} style={{ color: inkOn(p.color) }} />}
            </button>
          )
        })}

        {/* 自定义入口：未启用时是虚线圈 + #，启用后直接显示当前自定义色 */}
        <button
          type="button"
          title="自定义颜色"
          aria-label="自定义颜色"
          aria-pressed={active}
          onClick={() => {
            if (active) return
            // 从"当前生效色"起步，滑条不会突然跳到某个莫名其妙的颜色
            onCustom(effective)
          }}
          className={cn(
            'flex size-7 items-center justify-center rounded-full border-2 transition-transform hover:scale-110',
            active
              ? 'border-foreground ring-2 ring-foreground/20'
              : 'border-dashed border-border/60 hover:border-foreground/40',
          )}
          style={active ? { backgroundColor: effective } : undefined}
        >
          {active ? (
            <Check className="size-3.5" strokeWidth={3.5} style={{ color: inkOn(effective) }} />
          ) : (
            <span className="text-[12px] font-bold text-muted-foreground">#</span>
          )}
        </button>

        {active && (
          <button
            type="button"
            onClick={() => onCustom('')}
            className="ml-0.5 rounded-lg px-2 py-1 text-[11.5px] text-muted-foreground transition-colors hover:bg-accent/60 hover:text-foreground"
          >
            退出自定义
          </button>
        )}
      </div>

      {/* ---- 2. 三原色调整条（只在自定义时出现） ---- */}
      {active && (
        <div className="max-w-[420px] space-y-1.5 rounded-xl border border-border/60 bg-muted/25 p-2.5">
          <Channel index={0} label="R" value={rgb[0]} color="#e5484d" onChange={setChannel} />
          <Channel index={1} label="G" value={rgb[1]} color="#46a758" onChange={setChannel} />
          <Channel index={2} label="B" value={rgb[2]} color="#0090ff" onChange={setChannel} />

          <div className="flex items-center gap-2 pt-0.5">
            <span
              className="size-6 shrink-0 rounded-lg border border-border/60"
              style={{ backgroundColor: effective }}
              title={effective}
            />
            {/* ---- 3. HTML 色号 ---- */}
            <HexField value={custom} fallback={effective} onCommit={onCustom} />
            <span className="text-[11px] text-muted-foreground">或直接填 HTML 色号</span>
          </div>
        </div>
      )}

      {!active && (
        <div className="flex items-center gap-2 text-[11.5px] text-muted-foreground">
          <span
            className="size-4 shrink-0 rounded-md border border-border/60"
            style={{ backgroundColor: effective }}
          />
          当前：{preset.name}（{preset.color}）
        </div>
      )}
    </div>
  )
}

function Channel({
  index,
  label,
  value,
  color,
  onChange,
}: {
  index: 0 | 1 | 2
  label: string
  value: number
  color: string
  onChange: (i: 0 | 1 | 2, v: number) => void
}) {
  return (
    <div className="flex items-center gap-2">
      <span className="w-3 shrink-0 font-mono text-[11.5px] font-semibold" style={{ color }}>
        {label}
      </span>
      <input
        type="range"
        min={0}
        max={255}
        value={value}
        onChange={(e) => onChange(index, Number(e.target.value))}
        style={{ accentColor: color }}
        className="h-4 min-w-0 flex-1 cursor-pointer"
      />
      <input
        type="number"
        min={0}
        max={255}
        value={value}
        onChange={(e) => onChange(index, Number(e.target.value))}
        className="h-6 w-[52px] shrink-0 rounded-md border border-input/60 bg-card px-1 text-right font-mono text-[11.5px] tabular-nums outline-none focus-visible:border-primary"
      />
    </div>
  )
}

/**
 * HTML 色号输入框。
 *
 * 输入过程中不校验（不然打一半就被吞了），失焦或回车才判断：
 * 合法（#rgb / #rrggbb）就提交，非法退回原值。
 */
function HexField({
  value,
  fallback,
  onCommit,
}: {
  value: string
  fallback: string
  onCommit: (v: string) => void
}) {
  const [text, setText] = React.useState(value)
  React.useEffect(() => setText(value), [value])

  const commit = () => {
    const t = text.trim()
    if (/^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(t)) onCommit(t.toUpperCase())
    else if (!t) onCommit('')
    else setText(value)
  }

  return (
    <input
      value={text}
      onChange={(e) => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => e.key === 'Enter' && commit()}
      spellCheck={false}
      placeholder={fallback}
      title="填 HTML 色号，回车生效"
      className="h-7 w-[110px] shrink-0 rounded-lg border border-input/60 bg-card px-2 font-mono text-[12px] uppercase outline-none transition-colors focus-visible:border-primary"
    />
  )
}
