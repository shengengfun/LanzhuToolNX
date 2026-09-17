import * as React from 'react'
import { Gauge, Pause, Sparkles, Square, TriangleAlert } from 'lucide-react'
import {
  Badge,
  Button,
  Card,
  Checkbox,
  Field,
  GroupCard,
  Input,
  Row,
  Select,
  Separator,
} from '~/components/ui'
import { PathRow } from '~/components/common'
import { useApp, pickFile, pickSave } from '~/state'
import * as api from '~/lib/api'
import {
  CUGAN_DENOISE,
  CUGAN_MODELS,
  ENHANCE_AUDIO,
  ENHANCE_CONTAINERS,
  ENHANCE_ENCODERS,
  ESR_MODELS,
  INTERP_ENGINES,
  INTERP_MODES,
  UPSCALE_ENGINES,
  type EnhanceSpec,
  type MediaInfo,
  type ModelTool,
} from '~/lib/types'
import { cn } from '~/lib/utils'

/** 本地模型没装时的默认值；具体有哪些由 `enhance_tools` 补全。 */
const DEFAULT_SPEC: EnhanceSpec = {
  input: '',
  output: '',
  interp: true,
  interpEngine: 'rife',
  interpFps: 0,
  interpMult: 2,
  interpModel: 'rife-v4.6',
  interpMode: 'mci',
  interpTta: false,
  upscale: false,
  upscaleEngine: 'lanczos',
  upscaleMult: 2,
  width: 0,
  height: 0,
  upscaleModel: 'realesr-animevideov3',
  cuganModel: 'models-se',
  cuganDenoise: -1,
  gpuIndex: 0,
  jobs: '2:2:2',
  audio: 'copy',
  encoder: 'libx264',
  encodeParams: '-crf 18 -preset medium -pix_fmt yuv420p',
  container: 'mp4',
}

/**
 * 插帧 / 超分。
 *
 * 这一页有两套完全不同的实现，界面上必须让它看得见：
 *   - 「内置」= 纯 ffmpeg 滤镜，一条命令搞定，装完就能用；
 *   - 「本地模型」= tools/ 下的 ncnn-vulkan 程序，要先把视频拆成 PNG 序列。
 * 后者慢得多（实测核显上 Real-CUGAN 2 倍大约 2 秒/帧），
 * 所以界面上给了明确的耗时提醒，别让用户拿两小时的片子去试水。
 */
export function EnhancePage() {
  const { notify, appendLog, running, paused, run, cancel, togglePause, settings } = useApp()
  const [spec, setSpec] = React.useState<EnhanceSpec>(DEFAULT_SPEC)
  const [tools, setTools] = React.useState<ModelTool[]>([])
  const [info, setInfo] = React.useState<MediaInfo | null>(null)

  const patch = (p: Partial<EnhanceSpec>) => setSpec((s) => ({ ...s, ...p }))

  // 模型工具的安装情况：进页面查一次，装完工具回来（toolsDir 变了）再查一次
  React.useEffect(() => {
    let alive = true
    api
      .enhanceTools()
      .then((t) => {
        if (!alive) return
        setTools(t)
        // 默认选一个真的装了的 rife 模型，省得用户第一次就点错
        const rife = t.find((x) => x.id === 'rife')
        if (rife?.models.length) {
          setSpec((s) => (rife.models.includes(s.interpModel) ? s : { ...s, interpModel: rife.models[0] }))
        }
      })
      .catch(() => void 0)
    return () => {
      alive = false
    }
  }, [settings.toolsDir])

  // 源信息：输入变了就探一次（用于算预计输出）
  React.useEffect(() => {
    const p = spec.input.trim()
    if (!p) {
      setInfo(null)
      return
    }
    let alive = true
    const t = window.setTimeout(() => {
      api
        .probeMedia(p)
        .then((m) => alive && setInfo(m.exists ? m : null))
        .catch(() => alive && setInfo(null))
    }, 350)
    return () => {
      alive = false
      window.clearTimeout(t)
    }
  }, [spec.input])

  const interpEngine = INTERP_ENGINES.find((e) => e.id === spec.interpEngine) ?? INTERP_ENGINES[0]
  const upEngine = UPSCALE_ENGINES.find((e) => e.id === spec.upscaleEngine) ?? UPSCALE_ENGINES[0]
  const tool = (id?: string) => tools.find((t) => t.id === id)
  const rifeTool = tool(interpEngine.tool)
  const upTool = tool(upEngine.tool)

  /* -------- 可用倍数：模型自己有限制（x4plus 只有 4 倍、nose 只有 2 倍） -------- */
  const upScales: number[] = React.useMemo(() => {
    if (spec.upscaleEngine === 'realesrgan') {
      return [...(ESR_MODELS.find((m) => m.id === spec.upscaleModel)?.scales ?? [4])]
    }
    if (spec.upscaleEngine === 'realcugan') {
      return [...(CUGAN_MODELS.find((m) => m.id === spec.cuganModel)?.scales ?? [2, 3, 4])]
    }
    return [...upEngine.scales]
  }, [spec.upscaleEngine, spec.upscaleModel, spec.cuganModel, upEngine])

  // 换了模型之后原来选的倍数可能不合法，自动收回第一个可用值
  React.useEffect(() => {
    if (spec.upscale && !upScales.includes(spec.upscaleMult)) {
      patch({ upscaleMult: upScales[0] })
    }
  }, [upScales, spec.upscale, spec.upscaleMult])

  /* -------- 预计输出 -------- */
  const srcFps = info?.video?.fps ?? 0
  const outFps = !spec.interp
    ? srcFps
    : spec.interpFps > 0
      ? spec.interpFps
      : Math.round(srcFps * spec.interpMult * 1000) / 1000
  const outW = spec.upscale ? spec.width || Math.round((info?.video?.width ?? 0) * spec.upscaleMult) : (info?.video?.width ?? 0)
  const outH = spec.upscale ? spec.height || Math.round((info?.video?.height ?? 0) * spec.upscaleMult) : (info?.video?.height ?? 0)
  const outFrames = info?.durationSec && outFps ? Math.round(info.durationSec * outFps) : 0

  /** 走本地模型就会拆帧，慢得多；顺便检查模型装没装。 */
  const usesModel = (spec.interp && interpEngine.kind === 'model') || (spec.upscale && upEngine.kind === 'model')
  const missingTools = [
    spec.interp && interpEngine.kind === 'model' && !rifeTool?.exe ? rifeTool?.name ?? 'RIFE' : '',
    spec.upscale && upEngine.kind === 'model' && !upTool?.exe ? upTool?.name ?? '超分模型' : '',
  ].filter(Boolean)

  // rife 只有 v4 系模型支持自定义帧数（`-n`）；2 倍时后端故意不传 `-n`，所以任何模型都能用
  const needCustomFrames =
    spec.interp && interpEngine.id === 'rife' && Math.abs(outFps - srcFps * 2) >= 0.005
  const rifeCustomOk = !!rifeTool?.customFrames.includes(spec.interpModel)

  const start = async () => {
    if (!spec.input.trim()) {
      notify('请先选择要处理的视频', 'error')
      return
    }
    if (!spec.interp && !spec.upscale) {
      notify('插帧和超分至少要开一个', 'error')
      return
    }
    if (missingTools.length) {
      notify(`还没装本地模型：${missingTools.join('、')}。可以切回内置引擎，或去「设置 → 工具获取」下载`, 'error')
      return
    }
    try {
      // 默认输出名由后端定（后缀带引擎与倍数，且重名会退避），别在前端拼
      const out = spec.output.trim() || (await api.defaultEnhanceOutput(spec))
      if (!out) {
        notify('请先指定输出文件', 'error')
        return
      }
      if (!spec.output.trim()) patch({ output: out })

      const cmds = await api.planEnhance({ ...spec, output: out })
      appendLog(`插帧超分：${spec.input} → ${out}`, 'app')
      await run(cmds, spec.input)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    }
  }

  return (
    <div className="flex h-full min-h-0 flex-wrap gap-3">
      {/* ---------------- 左：参数 ---------------- */}
      <div className="flex min-h-0 min-w-[420px] flex-1 flex-col gap-3 overflow-y-auto pt-3">
        <Card className="space-y-2 p-3">
          <PathRow
            label="素材"
            labelWidth={64}
            value={spec.input}
            onChange={(v) => patch({ input: v, output: '' })}
            onBrowse={() =>
              void pickFile('选择要处理的视频').then((p) => p && patch({ input: p, output: '' }))
            }
            onDropFile={(paths) => patch({ input: paths[0], output: '' })}
          />
          <PathRow
            label="输出"
            labelWidth={64}
            value={spec.output}
            onChange={(v) => patch({ output: v })}
            placeholder="留空则自动命名（源名_rife2x_cugan2x.mp4）"
            onBrowse={() =>
              void api
                .defaultEnhanceOutput(spec)
                .then((d) => pickSave('选择输出文件', d || 'output.mp4', [
                  { name: '视频', extensions: ['mp4', 'mkv', 'mov'] },
                ]))
                .then((p) => p && patch({ output: p }))
            }
            onDropFile={(paths) => patch({ output: paths[0] })}
          />
          <p className="text-[11.5px] leading-relaxed text-muted-foreground">
            插帧与超分可以只开一个，也可以一起开：先补帧、再放大，
            模型路径的每一步都会在日志区里回显命令，和自己的预期对不上时能立刻看出来。
          </p>
        </Card>

        {/* ---------------- 插帧 ---------------- */}
        <GroupCard
          title="插帧"
          right={<Badge variant={spec.interp ? 'success' : 'muted'}>{spec.interp ? '已启用' : '关闭'}</Badge>}
        >
          <Checkbox
            checked={spec.interp}
            onCheckedChange={(v) => patch({ interp: v })}
            label="提高帧率让动作更顺滑"
          />
          <div className={cn('mt-2 grid grid-cols-1 gap-y-2', !spec.interp && 'pointer-events-none opacity-50')}>
            <Field label="引擎" labelWidth={72}>
              <IdSelect
                items={INTERP_ENGINES}
                value={spec.interpEngine}
                onChange={(id) => {
                  const e = INTERP_ENGINES.find((x) => x.id === id)
                  // 换成模型引擎时自动挑一个装了的模型
                  const models = tool(e?.tool)?.models ?? []
                  patch({
                    interpEngine: id,
                    interpModel: models.length ? models[0] : spec.interpModel,
                  })
                }}
              />
            </Field>
            <p className="text-[11.5px] leading-relaxed text-muted-foreground">{interpEngine.desc}</p>

            {interpEngine.id === 'rife' && (
              <Field label="模型" labelWidth={72}>
                <Select
                  value={spec.interpModel}
                  onValueChange={(v) => patch({ interpModel: v })}
                  options={
                    rifeTool?.models.length
                      ? rifeTool.models
                      : ['（未检测到 RIFE，请先下载）', spec.interpModel]
                  }
                  disabled={!rifeTool?.models.length}
                />
              </Field>
            )}

            <Row className="gap-2">
              <Field label="倍数" labelWidth={72}>
                <Select
                  value={`${spec.interpMult}x`}
                  onValueChange={(v) => patch({ interpMult: Number(v.replace('x', '')) })}
                  options={interpEngine.scales.map((s) => `${s}x`)}
                  disabled={spec.interpFps > 0}
                />
              </Field>
              <Field label="目标帧率" labelWidth={72}>
                <Input
                  type="number"
                  value={spec.interpFps}
                  onChange={(e) => patch({ interpFps: Number(e.target.value) || 0 })}
                  placeholder="0 = 按倍数"
                />
              </Field>
            </Row>
            <p className="text-[11.5px] text-muted-foreground">
              目标帧率填了就以它为准（倍数那一栏会失效）；填 0 表示用「倍数 × 源帧率」。
            </p>

            {interpEngine.id === 'minterpolate' && (
              <Field label="模式" labelWidth={72}>
                <IdSelect
                  items={INTERP_MODES}
                  value={spec.interpMode}
                  onChange={(id) => patch({ interpMode: id })}
                />
              </Field>
            )}

            {interpEngine.id === 'rife' && (
              <Checkbox
                checked={spec.interpTta}
                onCheckedChange={(v) => patch({ interpTta: v })}
                label="TTA 模式（更准，慢一倍）"
              />
            )}

            {needCustomFrames && interpEngine.id === 'rife' && !rifeCustomOk && (
              <Warn>
                当前模型不支持自定义帧数（只有 <span className="font-mono">rife-v4 / v4.6</span> 支持）。
                要么换成 v4 系模型，要么把目标帧率调成源帧率的 2 倍。
              </Warn>
            )}
          </div>
        </GroupCard>

        {/* ---------------- 超分 ---------------- */}
        <GroupCard
          title="超分"
          right={<Badge variant={spec.upscale ? 'success' : 'muted'}>{spec.upscale ? '已启用' : '关闭'}</Badge>}
        >
          <Checkbox
            checked={spec.upscale}
            onCheckedChange={(v) => patch({ upscale: v })}
            label="放大分辨率并补出细节"
          />
          <div className={cn('mt-2 grid grid-cols-1 gap-y-2', !spec.upscale && 'pointer-events-none opacity-50')}>
            <Field label="引擎" labelWidth={72}>
              <IdSelect items={UPSCALE_ENGINES} value={spec.upscaleEngine} onChange={(id) => patch({ upscaleEngine: id })} />
            </Field>
            <p className="text-[11.5px] leading-relaxed text-muted-foreground">{upEngine.desc}</p>

            {spec.upscaleEngine === 'realesrgan' && (
              <Field label="模型" labelWidth={72}>
                <IdSelect
                  items={ESR_MODELS.filter((m) => !upTool?.models.length || upTool.models.includes(m.id))}
                  value={spec.upscaleModel}
                  onChange={(id) => patch({ upscaleModel: id })}
                  empty="（未检测到 Real-ESRGAN，请先下载）"
                />
              </Field>
            )}
            {spec.upscaleEngine === 'realcugan' && (
              <>
                <Field label="模型" labelWidth={72}>
                  <IdSelect
                    items={CUGAN_MODELS.filter((m) => !upTool?.models.length || upTool.models.includes(m.id))}
                    value={spec.cuganModel}
                    onChange={(id) => patch({ cuganModel: id })}
                    empty="（未检测到 Real-CUGAN，请先下载）"
                  />
                </Field>
                <Field label="降噪" labelWidth={72}>
                  <IdSelect
                    items={CUGAN_DENOISE.map((d) => ({ id: String(d.value), label: d.label }))}
                    value={String(spec.cuganDenoise)}
                    onChange={(id) => patch({ cuganDenoise: Number(id) })}
                  />
                </Field>
              </>
            )}

            <Row className="gap-2">
              <Field label="倍数" labelWidth={72}>
                <Select
                  value={`${spec.upscaleMult}x`}
                  onValueChange={(v) => patch({ upscaleMult: Number(v.replace('x', '')) })}
                  options={upScales.map((s) => `${s}x`)}
                />
              </Field>
              <Field label="分辨率" labelWidth={72}>
                <Row className="gap-1">
                  <Input
                    type="number"
                    value={spec.width}
                    onChange={(e) => patch({ width: Number(e.target.value) || 0 })}
                    placeholder="宽"
                  />
                  <Input
                    type="number"
                    value={spec.height}
                    onChange={(e) => patch({ height: Number(e.target.value) || 0 })}
                    placeholder="高"
                  />
                </Row>
              </Field>
            </Row>
            <p className="text-[11.5px] text-muted-foreground">
              分辨率留 0 就按倍数放大；填了则在放大之后再用 Lanczos 收口到指定尺寸。
            </p>
          </div>
        </GroupCard>

        {/* ---------------- 输出 ---------------- */}
        <GroupCard title="输出与编码">
          <div className="grid grid-cols-1 gap-y-2">
            <Field label="编码" labelWidth={72}>
              <Select
                value={ENHANCE_ENCODERS.find((e) => e.id === encoderId(spec))?.label ?? ENHANCE_ENCODERS[0].label}
                onValueChange={(v) => {
                  const hit = ENHANCE_ENCODERS.find((e) => e.label === v)
                  if (!hit) return
                  // 选「自定义」时保留当前参数作为可编辑的基线，别清成空白
                  patch({
                    encoder: hit.encoder,
                    encodeParams: hit.id === 'custom' ? spec.encodeParams : hit.params,
                  })
                }}
                options={ENHANCE_ENCODERS.map((e) => e.label)}
              />
            </Field>
            <Field label="参数" labelWidth={72}>
              <Input
                value={spec.encodeParams}
                onChange={(e) => patch({ encodeParams: e.target.value })}
                spellCheck={false}
                placeholder="-crf 18 -preset medium"
              />
            </Field>
            <Row className="gap-2">
              <Field label="容器" labelWidth={72}>
                <Select
                  value={spec.container}
                  onValueChange={(v) => patch({ container: v })}
                  options={[...ENHANCE_CONTAINERS]}
                />
              </Field>
              <Field label="音轨" labelWidth={72}>
                <IdSelect
                  items={ENHANCE_AUDIO}
                  value={spec.audio}
                  onChange={(id) => patch({ audio: id })}
                />
              </Field>
            </Row>
            <Row className="gap-2">
              <Field label="GPU 序号" labelWidth={72}>
                <Input
                  type="number"
                  value={spec.gpuIndex}
                  onChange={(e) => patch({ gpuIndex: Number(e.target.value) })}
                  placeholder="-1 = CPU"
                />
              </Field>
              <Field label="线程" labelWidth={72}>
                <Input
                  value={spec.jobs}
                  onChange={(e) => patch({ jobs: e.target.value })}
                  spellCheck={false}
                  placeholder="2:2:2"
                />
              </Field>
            </Row>
            <p className="text-[11.5px] text-muted-foreground">
              GPU 序号 <span className="font-mono">-1</span> 表示强制用 CPU 跑模型（慢，但没有可用显卡时的兜底）；
              线程是 <span className="font-mono">加载:推理:保存</span>，大分辨率调小一点更稳。
            </p>
          </div>
        </GroupCard>
      </div>

      {/* ---------------- 右：预估 / 模型状态 / 执行 ---------------- */}
      <div className="flex w-[320px] min-w-[300px] flex-1 flex-col gap-3">
        <Card className="space-y-1.5 p-3 text-[12.5px]">
          <Row className="justify-between">
            <span className="text-muted-foreground">源</span>
            <span className="font-mono">{fmtSrc(info)}</span>
          </Row>
          <Separator />
          <Row className="justify-between">
            <span className="text-muted-foreground">预计输出</span>
            <span className="font-mono">{outW && outH ? `${outW}×${outH}` : '—'}</span>
          </Row>
          <Row className="justify-between">
            <span className="text-muted-foreground">帧率</span>
            <span className="font-mono">
              {srcFps ? `${srcFps} → ${outFps || '—'}` : '—'}
            </span>
          </Row>
          <Row className="justify-between">
            <span className="text-muted-foreground">总帧数</span>
            <span className="font-mono">
              ≈{outFrames || '—'}
              {spec.interp && info?.durationSec
                ? `（${fmtDuration(info.durationSec)} 的片子）`
                : ''}
            </span>
          </Row>
        </Card>

        <GroupCard title="本地模型" right={<Gauge className="size-3.5 text-muted-foreground" />}>
          <div className="space-y-2">
            {tools.map((t) => (
              <div key={t.id} className="rounded-lg bg-muted/40 px-2.5 py-2">
                <Row className="justify-between">
                  <span className="text-[12.5px] font-semibold">{t.name}</span>
                  <Badge variant={t.exe ? 'success' : 'destructive'}>{t.exe ? '已就绪' : '未安装'}</Badge>
                </Row>
                <p className="mt-1 text-[11px] leading-relaxed text-muted-foreground">{t.hint}</p>
                {t.exe && t.models.length > 0 && (
                  <p className="mt-1 break-all text-[11px] text-muted-foreground">
                    可用：{t.models.join('、')}
                  </p>
                )}
              </div>
            ))}
            {!tools.length && (
              <p className="text-[12px] text-muted-foreground">
                没读到模型列表（预览模式或工具目录还没指定）。
              </p>
            )}
          </div>
        </GroupCard>

        {usesModel && (
          <Warn>
            本地模型会把整段视频拆成 PNG 再逐帧推理，<b>耗时远高于普通压制</b>
            （核显上 2 倍超分约 2 秒/帧）。建议先用一段短片确认效果与速度。
            中途会占用与「帧数 × 分辨率」同等量级的临时磁盘空间。
          </Warn>
        )}

        {!!missingTools.length && (
          <Warn>
            当前选的引擎需要 {missingTools.join('、')}，但 tools 目录下没找到。
            可以切回「内置」引擎，或者去<b>设置 → 工具获取</b>下载。
          </Warn>
        )}

        <div className="flex-1" />

        <Row className="gap-2">
          <Button variant="default" size="sm" className="h-8 flex-1" onClick={() => void start()} disabled={running}>
            <Sparkles className="size-3.5" />
            开始处理
          </Button>
          <Button variant="outline" size="sm" className="h-8" onClick={togglePause} disabled={!running}>
            <Pause className="size-3.5" />
            {paused ? '继续' : '暂停'}
          </Button>
          <Button variant="outline" size="sm" className="h-8" onClick={cancel} disabled={!running}>
            <Square className="size-3" />
            终止
          </Button>
        </Row>
      </div>
    </div>
  )
}

/* ================================================================== *
 * 局部小工具
 * ================================================================== */

/** 参数里的编码器反推出预设 id（认不出来就算自定义）。 */
function encoderId(spec: EnhanceSpec) {
  const hit = ENHANCE_ENCODERS.find(
    (e) => e.encoder === spec.encoder && e.params === spec.encodeParams,
  )
  return hit?.id ?? 'custom'
}

/** 用「显示名」当选择值的下拉：对外是 label，对内还是 id。 */
function IdSelect<T extends { id: string; label: string }>({
  items,
  value,
  onChange,
  empty = '（无可用项）',
}: {
  items: readonly T[]
  value: string
  onChange: (id: string) => void
  empty?: string
}) {
  if (!items.length) {
    return <Select value={empty} onValueChange={() => void 0} options={[empty]} disabled />
  }
  const cur = items.find((i) => i.id === value) ?? items[0]
  return (
    <Select
      value={cur.label}
      options={items.map((i) => i.label)}
      onValueChange={(v) => {
        const hit = items.find((i) => i.label === v)
        if (hit) onChange(hit.id)
      }}
    />
  )
}

function Warn({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex items-start gap-1.5 rounded-lg border border-amber-300/60 bg-amber-50 px-2.5 py-2 text-[11.5px] leading-relaxed text-amber-900">
      <TriangleAlert className="mt-0.5 size-3.5 shrink-0" />
      <span>{children}</span>
    </div>
  )
}

function fmtSrc(info: MediaInfo | null) {
  const v = info?.video
  if (!v) return info ? '（不是有效的视频）' : '—'
  return `${v.width}×${v.height} ${v.fps}fps`
}

function fmtDuration(sec: number) {
  const s = Math.round(sec)
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const ss = s % 60
  return h > 0
    ? `${h}:${String(m).padStart(2, '0')}:${String(ss).padStart(2, '0')}`
    : `${m}:${String(ss).padStart(2, '0')}`
}
