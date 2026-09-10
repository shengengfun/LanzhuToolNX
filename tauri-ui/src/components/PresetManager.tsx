import * as React from 'react'
import { Check, Pencil, Plus, Search, Sparkles, Trash2, X } from 'lucide-react'
import { Badge, Button, Field, Input, Row, Separator, Select } from './ui'
import { cn } from '~/lib/utils'
import {
  PRESET_GROUPS,
  allPresets,
  presetBitrateHint,
  recommendPresets,
} from '~/lib/encodePresets'
import type { EncodePreset } from '~/lib/types'

/**
 * 压制预设管理器。
 *
 * 做成**应用内的大弹窗**而不是第二个原生窗口：一是它本来就是模态操作
 * （选完就应用、关掉），二是跨窗口还得同步设置与任务状态，多一层出错机会。
 * 视觉上按"窗口"来做：标题栏 + 左分类 / 中列表 / 右详情，和 Vegas 的渲染模板
 * 同一套信息结构，但把每个预设的"人话说明 + 码率量级 + 适配分辨率"都摆出来了 ——
 * Vegas 那个列表最大的问题就是只有名字，看不出体积和画质。
 */
export function PresetManager({
  open,
  onClose,
  current,
  source,
  custom,
  onApply,
  onChangeCustom,
}: {
  open: boolean
  onClose: () => void
  /** 当前生效的 preset id（无则空串） */
  current: string
  /** 源文件信息，用于"按分辨率推荐" */
  source: { width: number; height: number; fps: number } | null
  /** 用户自建预设 */
  custom: EncodePreset[]
  onApply: (p: EncodePreset) => void
  /** 保存/删除用户预设 */
  onChangeCustom: (next: EncodePreset[]) => void
}) {
  const [group, setGroup] = React.useState<string>('推荐')
  const [q, setQ] = React.useState('')
  const [picked, setPicked] = React.useState<string>('')
  const [editing, setEditing] = React.useState<EncodePreset | null>(null)

  const list = allPresets(custom)
  const recommended = React.useMemo(
    () => (source ? recommendPresets(source, custom) : list.filter((p) => p.group === '常用')),
    [source, custom, list],
  )

  const shown = React.useMemo(() => {
    const base = group === '推荐' ? recommended : list.filter((p) => p.group === group)
    const kw = q.trim().toLowerCase()
    if (!kw) return base
    return base.filter(
      (p) =>
        p.name.toLowerCase().includes(kw) ||
        p.desc.toLowerCase().includes(kw) ||
        p.encoder.toLowerCase().includes(kw),
    )
  }, [group, list, recommended, q])

  // 每次打开都把高亮对准当前生效的预设
  React.useEffect(() => {
    if (open) {
      setPicked(current)
      setEditing(null)
      setQ('')
    }
  }, [open, current])

  React.useEffect(() => {
    if (!open) return
    const onEsc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onEsc)
    return () => document.removeEventListener('keydown', onEsc)
  }, [open, onClose])

  if (!open) return null

  const sel = list.find((p) => p.id === picked) ?? null

  return (
    <div className="fixed inset-0 z-[150] flex items-center justify-center bg-foreground/25 p-6 backdrop-blur-sm">
      <div className="flex h-[min(640px,92vh)] w-[min(1000px,96vw)] flex-col overflow-hidden rounded-2xl border border-border/70 bg-card shadow-lg">
        {/* 窗口标题栏 */}
        <div className="flex h-11 shrink-0 items-center gap-2 border-b border-border/60 px-3">
          <Sparkles className="size-4 text-primary" />
          <span className="text-[13.5px] tracking-wide">
            <span className="font-bold text-primary">压制</span>
            <span className="font-semibold">预设管理器</span>
          </span>
          {source && source.height > 0 && (
            <Badge variant="muted">
              源 {source.width}×{source.height}
              {source.fps > 0 ? ` ${source.fps.toFixed(2)}fps` : ''}
            </Badge>
          )}
          <span className="flex-1" />
          <button
            type="button"
            onClick={onClose}
            title="关闭"
            className="flex size-7 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-destructive hover:text-white"
          >
            <X className="size-4" />
          </button>
        </div>

        <div className="flex min-h-0 flex-1">
          {/* 左：分类 */}
          <div className="flex w-[132px] shrink-0 flex-col gap-1 border-r border-border/60 p-2">
            {['推荐', ...PRESET_GROUPS].map((g) => (
              <button
                key={g}
                type="button"
                onClick={() => setGroup(g)}
                className={cn(
                  'rounded-lg px-2.5 py-1.5 text-left text-[12.5px] transition-colors',
                  group === g
                    ? 'bg-primary/12 font-semibold text-primary'
                    : 'text-muted-foreground hover:bg-accent/60',
                )}
              >
                {g}
                {g === '推荐' && source && <span className="ml-1 text-[10px]">①</span>}
              </button>
            ))}
            <span className="flex-1" />
            <Button
              size="sm"
              variant="outline"
              className="w-full"
              onClick={() => setEditing(blankPreset())}
            >
              <Plus className="size-3.5" />
              新建
            </Button>
          </div>

          {/* 中：列表 */}
          <div className="flex min-w-0 flex-1 flex-col border-r border-border/60">
            <div className="flex shrink-0 items-center gap-1.5 border-b border-border/60 px-2 py-1.5">
              <Search className="size-3.5 shrink-0 text-muted-foreground" />
              <input
                value={q}
                onChange={(e) => setQ(e.target.value)}
                placeholder="搜名字 / 说明 / 编码器…"
                className="h-7 min-w-0 flex-1 bg-transparent text-[12.5px] outline-none placeholder:text-muted-foreground"
              />
            </div>
            <div className="min-h-0 flex-1 overflow-y-auto p-1.5">
              {shown.length === 0 ? (
                <div className="flex h-full items-center justify-center text-[12px] text-muted-foreground">
                  没有匹配的预设
                </div>
              ) : (
                shown.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => setPicked(p.id)}
                    onDoubleClick={() => {
                      onApply(p)
                      onClose()
                    }}
                    className={cn(
                      'mb-0.5 w-full rounded-lg px-2.5 py-1.5 text-left transition-colors',
                      picked === p.id ? 'bg-primary/12 text-primary' : 'hover:bg-accent/50',
                    )}
                  >
                    <div className="flex items-center gap-1.5">
                      <span className="truncate text-[12.5px] font-medium">{p.name}</span>
                      {!p.builtin && <Badge variant="muted">自建</Badge>}
                      {p.id === current && <Badge variant="accent">在用</Badge>}
                    </div>
                    <div className="truncate text-[11px] text-muted-foreground">{p.desc}</div>
                  </button>
                ))
              )}
            </div>
          </div>

          {/* 右：详情 / 编辑 */}
          <div className="flex w-[330px] shrink-0 flex-col">
            {editing ? (
              <PresetEditor
                value={editing}
                onChange={setEditing}
                onCancel={() => setEditing(null)}
                onSave={(p) => {
                  const next = [...custom.filter((x) => x.id !== p.id), p]
                  onChangeCustom(next)
                  setPicked(p.id)
                  setEditing(null)
                }}
              />
            ) : sel ? (
              <PresetDetail
                preset={sel}
                onEdit={() =>
                  setEditing({
                    ...sel,
                    // 内置预设不能原地改：另存一份自建的
                    id: sel.builtin ? `custom-${Date.now()}` : sel.id,
                    builtin: false,
                    name: sel.builtin ? `${sel.name}（副本）` : sel.name,
                  })
                }
                onDelete={
                  sel.builtin
                    ? null
                    : () => {
                        onChangeCustom(custom.filter((x) => x.id !== sel.id))
                        setPicked('')
                      }
                }
              />
            ) : (
              <div className="flex h-full items-center justify-center p-4 text-center text-[12px] text-muted-foreground">
                左边选一条看详情
              </div>
            )}
          </div>
        </div>

        {/* 底部操作条 */}
        <div className="flex h-12 shrink-0 items-center gap-2 border-t border-border/60 px-3">
          <span className="min-w-0 flex-1 truncate text-[11.5px] text-muted-foreground">
            {sel
              ? `${sel.encoder} · ${sel.container.toUpperCase()} · ${presetBitrateHint(sel)}`
              : '未选择预设'}
          </span>
          <Button
            variant="default"
            size="sm"
            className="h-8"
            disabled={!sel}
            onClick={() => {
              if (!sel) return
              onApply(sel)
              onClose()
            }}
          >
            <Check className="size-3.5" />
            应用这个预设
          </Button>
          <Button variant="outline" size="sm" className="h-8" onClick={onClose}>
            取消
          </Button>
        </div>
      </div>
    </div>
  )
}

function PresetDetail({
  preset,
  onEdit,
  onDelete,
}: {
  preset: EncodePreset
  onEdit: () => void
  onDelete: (() => void) | null
}) {
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="min-h-0 flex-1 space-y-2 overflow-y-auto p-3">
        <div className="text-[13px] font-semibold">{preset.name}</div>
        <p className="text-[11.5px] leading-relaxed text-muted-foreground">{preset.desc}</p>
        <Separator />
        <Info k="编码器" v={preset.encoder} />
        <Info k="容器" v={`.${preset.container}`} />
        <Info
          k="分辨率"
          v={preset.height > 0 ? `${preset.width}×${preset.height}` : '保持原分辨率'}
        />
        <Info k="帧率" v={preset.fps > 0 ? `${preset.fps}` : '不改变'} />
        <Info k="体积量级" v={presetBitrateHint(preset)} />
        <Separator />
        <div className="space-y-1">
          <div className="text-[11.5px] text-muted-foreground">编码参数</div>
          <pre className="overflow-x-auto rounded-lg bg-muted/35 p-2 font-mono text-[11px] whitespace-pre-wrap">
            {preset.params || '（无）'}
          </pre>
        </div>
      </div>
      <div className="flex shrink-0 gap-2 border-t border-border/60 p-2">
        <Button size="sm" variant="outline" className="flex-1" onClick={onEdit}>
          <Pencil className="size-3.5" />
          {preset.builtin ? '另存为自建' : '编辑'}
        </Button>
        {onDelete && (
          <Button size="sm" variant="outline" onClick={onDelete}>
            <Trash2 className="size-3.5" />
            删除
          </Button>
        )}
      </div>
    </div>
  )
}

function PresetEditor({
  value,
  onChange,
  onSave,
  onCancel,
}: {
  value: EncodePreset
  onChange: (p: EncodePreset) => void
  onSave: (p: EncodePreset) => void
  onCancel: () => void
}) {
  const patch = (p: Partial<EncodePreset>) => onChange({ ...value, ...p })
  const bad = !value.name.trim() || !value.encoder.trim() || !value.container.trim()

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="min-h-0 flex-1 space-y-2 overflow-y-auto p-3">
        <Field label="名称" labelWidth={54}>
          <Input value={value.name} onChange={(e) => patch({ name: e.target.value })} />
        </Field>
        <Field label="说明" labelWidth={54}>
          <Input value={value.desc} onChange={(e) => patch({ desc: e.target.value })} />
        </Field>
        <Field label="分类" labelWidth={54}>
          <Select value={value.group} onValueChange={(v) => patch({ group: v })} options={PRESET_GROUPS} />
        </Field>
        <Field label="编码器" labelWidth={54}>
          <Input
            value={value.encoder}
            onChange={(e) => patch({ encoder: e.target.value })}
            placeholder="libx264 / prores_ks / libsvtav1 …"
            className="font-mono text-[12px]"
          />
        </Field>
        <Field label="容器" labelWidth={54}>
          <Select
            value={value.container}
            onValueChange={(v) => patch({ container: v })}
            options={['mp4', 'mov', 'mkv', 'avi']}
          />
        </Field>
        <div className="grid grid-cols-3 gap-x-2">
          <Field label="宽" labelWidth={26}>
            <Input
              type="number"
              value={value.width}
              onChange={(e) => patch({ width: Number(e.target.value) })}
            />
          </Field>
          <Field label="高" labelWidth={26}>
            <Input
              type="number"
              value={value.height}
              onChange={(e) => patch({ height: Number(e.target.value) })}
            />
          </Field>
          <Field label="帧率" labelWidth={36}>
            <Input
              type="number"
              value={value.fps}
              onChange={(e) => patch({ fps: Number(e.target.value) })}
            />
          </Field>
        </div>
        <div className="space-y-1">
          <div className="text-[11.5px] text-muted-foreground">编码参数</div>
          <textarea
            value={value.params}
            onChange={(e) => patch({ params: e.target.value })}
            rows={3}
            spellCheck={false}
            placeholder="-crf 23 -preset slow -pix_fmt yuv420p"
            className="w-full resize-none rounded-lg border border-input/60 bg-muted/40 p-2 font-mono text-[11.5px] outline-none focus-visible:border-primary focus-visible:bg-card"
          />
          <p className="text-[11px] leading-relaxed text-muted-foreground">
            宽/高填 0 表示保持原分辨率；这些参数会原样拼在 <span className="font-mono">-c:v 编码器</span> 后面。
          </p>
        </div>
      </div>
      <div className="flex shrink-0 gap-2 border-t border-border/60 p-2">
        <Button
          size="sm"
          variant="default"
          className="flex-1"
          disabled={bad}
          onClick={() => onSave({ ...value, builtin: false })}
        >
          <Check className="size-3.5" />
          保存
        </Button>
        <Button size="sm" variant="outline" onClick={onCancel}>
          取消
        </Button>
      </div>
    </div>
  )
}

function Info({ k, v }: { k: string; v: string }) {
  return (
    <Row className="gap-2 text-[12px]">
      <span className="w-[68px] shrink-0 text-right text-muted-foreground">{k}</span>
      <span className="min-w-0 flex-1 truncate font-mono">{v}</span>
    </Row>
  )
}

function blankPreset(): EncodePreset {
  return {
    id: `custom-${Date.now()}`,
    name: '我的预设',
    group: '常用',
    desc: '',
    container: 'mp4',
    encoder: 'libx264',
    params: '-crf 23 -preset medium -pix_fmt yuv420p',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: false,
  }
}
