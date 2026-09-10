import * as React from 'react'
import {
  Calculator,
  Info,
  Pause,
  Play,
  Plus,
  Square,
  Trash2,
  Wand2,
  XCircle,
  Zap,
} from 'lucide-react'
import {
  Badge,
  Button,
  Card,
  Checkbox,
  Field,
  GroupCard,
  Input,
  ListRow,
  ListShell,
  Radio,
  Row,
  Select,
  Separator,
  Textarea,
} from '~/components/ui'
import { PathRow } from '~/components/common'
import { PresetManager } from '~/components/PresetManager'
import {
  useApp,
  useDropZone,
  pickFile,
  pickFiles,
  pickFolder,
  pickSave,
  defaultVideo,
  SUB_FILTERS,
} from '~/state'
import * as api from '~/lib/api'
import { AUDIO_BITRATES, DEMUXERS, VIDEO_FORMATS, type EncodePreset, type GpuKind } from '~/lib/types'
import { allPresets, presetBitrateHint, recommendPresets } from '~/lib/encodePresets'
import { estimateSize, type SourceMetrics } from '~/lib/estimate'
import { changeExt, cn, extForFormat, humanSize } from '~/lib/utils'

export function VideoPage() {
  const {
    video,
    patchVideo,
    audio,
    patchAudio,
    notify,
    settings,
    patchSettings,
    running,
    paused,
    run,
    cancel,
    togglePause,
  } = useApp()
  const [batch, setBatch] = React.useState<string[]>([])
  const [outputDir, setOutputDir] = React.useState(settings.outputDir)
  const [embedSub, setEmbedSub] = React.useState(false)
  const [gpus, setGpus] = React.useState<{ index: number; label: string; kind: string }[]>([])

  /** 源文件量测（时长/分辨率/码率），只探测一次，预计大小靠它算 */
  const [src, setSrc] = React.useState<SourceMetrics | null>(null)
  /** 用户是否手动改过输出名。改过就尊重用户，不再自动跟随输入文件 */
  const [outputTouched, setOutputTouched] = React.useState(false)
  const [presetOpen, setPresetOpen] = React.useState(false)
  const autoSubRef = React.useRef('')

  /** 当前生效的预设（未用预设时为 null） */
  const activePreset = React.useMemo(() => {
    if (video.mode !== 3 || !video.presetEncoder) return null
    return (
      allPresets(settings.presets).find((p) => p.encoder === video.presetEncoder && p.params === video.presetParams) ??
      null
    )
  }, [video.mode, video.presetEncoder, video.presetParams, settings.presets])

  /** 应用一条预设：编码器/参数/容器/分辨率/帧率一并写入 */
  const applyPreset = (p: EncodePreset) => {
    // 帧率靠 -r 实现（预设里已经写了就不重复）
    const params =
      p.fps > 0 && !/(^|\s)-r\s/.test(p.params)
        ? `${p.params} -r ${p.fps}`.trim()
        : p.params
    patchVideo({
      mode: 3,
      presetName: p.name,
      presetEncoder: p.encoder,
      presetParams: params,
      presetContainer: p.container,
      width: p.width,
      height: p.height,
      maintainResolution: p.height === 0,
      customParams: '',
    })
    notify(`已应用预设：${p.name}`)
  }

  React.useEffect(() => {
    api
      .detectGpus()
      .then((list) => {
        setGpus(list)
        if (list.length) {
          const g = list.find((x) => x.index === video.gpuIndex) ?? list[0]
          patchVideo({ gpuIndex: g.index, gpuKind: g.kind as GpuKind })
        }
      })
      .catch(() => void 0)
    // 只在首次挂载时探测一次
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  React.useEffect(() => setOutputDir(settings.outputDir), [settings.outputDir])

  // 首次进入时把设置里的「压制默认值」套用上；用户已经改过就不再覆盖
  React.useEffect(() => {
    const d = defaultVideo()
    const p: Partial<typeof video> = {}
    if (video.format === d.format && settings.defaultFormat && settings.defaultFormat !== d.format) {
      p.format = settings.defaultFormat
    }
    if (video.threads === d.threads && settings.threadsDefault && settings.threadsDefault !== d.threads) {
      p.threads = settings.threadsDefault
    }
    if (Object.keys(p).length) patchVideo(p)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const refreshEstimate = React.useCallback(async (p: string) => {
    try {
      const mi = await api.probeMedia(p)
      const audioKbps = mi.audio && mi.audio.bitrate > 0 ? mi.audio.bitrate / 1000 : 0
      const totalKbps = mi.bitrate / 1000
      setSrc({
        durationSec: mi.durationSec,
        width: mi.video?.width ?? 0,
        height: mi.video?.height ?? 0,
        fps: mi.video?.fps ?? 0,
        // ffprobe 只给总码率，所以"视频码率"是总码率减掉音轨
        videoKbps: Math.max(0, totalKbps - audioKbps),
        audioKbps,
      })
    } catch {
      setSrc(null)
    }
  }, [])

  /*
   * 输入文件变了：
   *  1. 自动把输出名改成"同目录 + 当前格式的扩展名"
   *  2. 探测媒体信息（预计大小要用）
   *  3. 自动匹配同名字幕（.ass/.srt/.ssa/.sub）
   *
   * 以前这三件事都不做：拖进视频后输出名不动、字幕要手动选、预计大小是个死数。
   */
  React.useEffect(() => {
    const p = video.input
    if (!p) return
    const curSub = video.subtitle
    let alive = true
    const t = window.setTimeout(async () => {
      if (!alive) return
      void refreshEstimate(p)
      const sub = await api.detectSubtitle(p, 'none').catch(() => null)
      if (!alive) return
      // 用户手选过的字幕一律不碰
      if (curSub && curSub !== autoSubRef.current) return
      if (sub) {
        if (sub !== curSub) patchVideo({ subtitle: sub })
        autoSubRef.current = sub
      } else if (curSub && curSub === autoSubRef.current) {
        // 上一个文件自动配到的字幕，对这个文件不适用了 —— 清掉，别留个错字幕
        patchVideo({ subtitle: '' })
        autoSubRef.current = ''
      }
    }, 250)
    return () => {
      alive = false
      window.clearTimeout(t)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [video.input])

  /** 自动输出名：输入 / 格式 / 预设容器变了就跟着走，除非用户自己改过输出框。 */
  React.useEffect(() => {
    if (!video.input || outputTouched) return
    const ext =
      video.mode === 3 && video.presetContainer
        ? `.${video.presetContainer}`
        : extForFormat(video.format)
    const want = changeExt(video.input, ext)
    if (video.output !== want) patchVideo({ output: want })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [video.input, video.format, video.mode, video.presetContainer, outputTouched])

  /** 选输入视频 */
  const chooseInput = async () => {
    const p = await pickFile('选择视频文件')
    if (!p) return
    setOutputTouched(false)
    patchVideo({ input: p })
  }

  const takeInput = (p: string) => {
    setOutputTouched(false)
    patchVideo({ input: p })
  }

  /** 预计大小：纯函数，参数一变立刻重算（不再是"选了文件才算一次"的死数）。 */
  const estimate = React.useMemo(() => {
    const bytes = estimateSize({
      source: src,
      width: video.width,
      height: video.height,
      maintainResolution: video.maintainResolution,
      mode: video.mode,
      crf: video.crf,
      bitrate: video.bitrate,
      format: video.format,
      useGpu: video.useGpu,
      hybrid: video.hybrid,
      audioMode: video.audioMode,
      audioEncoder: audio.encoder,
      audioBitrate: audio.bitrate,
      frames: video.frames,
      seek: video.seek,
    })
    return bytes == null ? '--' : humanSize(bytes)
  }, [
    src,
    video.width,
    video.height,
    video.maintainResolution,
    video.mode,
    video.crf,
    video.bitrate,
    video.format,
    video.useGpu,
    video.hybrid,
    video.audioMode,
    video.frames,
    video.seek,
    audio.encoder,
    audio.bitrate,
  ])

  const startSingle = async () => {
    try {
      await run(await api.planVideo(video, audio), video.input)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    }
  }

  const startBatch = async () => {
    try {
      await run(
        await api.planBatch(batch, video, audio, outputDir, embedSub),
        `批量 ${batch.length} 个文件`,
      )
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    }
  }

  const dropList = useDropZone((paths) =>
    setBatch((b) => [...b, ...paths.filter((p) => !b.includes(p))]),
  )

  return (
    <div className="flex h-full min-h-0 flex-wrap gap-3">
      {/* ---------------- 左：单文件 ---------------- */}
      <div className="flex max-w-[512px] min-h-0 min-w-[352px] flex-1 flex-col gap-3 overflow-y-auto pr-1">
        <Card className="space-y-2 p-3">
          <PathRow
            label="视频"
            value={video.input}
            onChange={(v) => patchVideo({ input: v })}
            onBrowse={() => void chooseInput()}
            onDropFile={(paths) => takeInput(paths[0])}
            placeholder="拖进来，或点右边文件夹选择"
          />
          <PathRow
            label="输出"
            value={video.output}
            onChange={(v) => {
              setOutputTouched(true)
              patchVideo({ output: v })
            }}
            onBrowse={() =>
              void pickSave('选择输出文件', video.output || 'output.mp4', [
                { name: '视频', extensions: ['mp4', 'mkv', 'mov', 'flv'] },
              ]).then((p) => {
                if (!p) return
                setOutputTouched(true)
                patchVideo({ output: p })
              })
            }
            onDropFile={(paths) => {
              setOutputTouched(true)
              patchVideo({ output: paths[0] })
            }}
          />
          <PathRow
            label="字幕"
            value={video.subtitle}
            onChange={(v) => patchVideo({ subtitle: v })}
            onBrowse={() =>
              void pickFile('选择字幕文件', SUB_FILTERS).then((p) => {
                if (!p) return
                autoSubRef.current = ''
                patchVideo({ subtitle: p })
              })
            }
            onDropFile={(paths) => patchVideo({ subtitle: paths[0] })}
            placeholder="留空则不内嵌字幕；导入视频时会自动匹配同名字幕"
          />
          {src && src.durationSec > 0 && (
            <Row className="gap-1.5 pt-0.5 text-[11.5px] text-muted-foreground">
              <Info className="size-3 shrink-0" />
              <span className="truncate tabular-nums">
                {fmtDur(src.durationSec)} · {src.width}×{src.height} · {src.fps.toFixed(2)}fps
                {src.videoKbps > 0 ? ` · 视频 ${Math.round(src.videoKbps)}kbps` : ''}
              </span>
            </Row>
          )}
        </Card>

        <GroupCard title="编码设置">
          <div className="grid grid-cols-2 gap-x-4 gap-y-2">
            <Field label="压制格式" labelWidth={64}>
              <Select
                value={video.format}
                onValueChange={(v) => patchVideo({ format: v })}
                options={VIDEO_FORMATS}
              />
            </Field>
            <Field label="预计大小" labelWidth={64}>
              <div
                title={
                  src
                    ? "根据源文件时长 / 分辨率 / 帧率 + 当前编码参数估算（与原版同一套公式）"
                    : "import 视频后开始估算"
                }
                className="flex h-8 items-center gap-2 rounded-xl border border-border/50 bg-muted/25 px-3 text-[12.5px] text-muted-foreground"
              >
                <Calculator className="size-3.5" />
                <span className="tabular-nums">{estimate}</span>
              </div>
            </Field>

            <Field label="分离器" labelWidth={64}>
              <Select value="auto" onValueChange={() => void 0} options={DEMUXERS} />
            </Field>
            <Field label="音频模式" labelWidth={64}>
              <Select
                value={['压制音频', '不压制音频', '复制音频'][video.audioMode] ?? '压制音频'}
                onValueChange={(v) =>
                  patchVideo({ audioMode: v === '压制音频' ? 0 : v === '不压制音频' ? 1 : 2 })
                }
                options={['压制音频', '不压制音频', '复制音频']}
              />
            </Field>
          </div>

          {/* ---------------- 压制预设 ---------------- */}
          <div className="mt-2">
            <Field label="压制预设" labelWidth={64}>
              <div className="flex items-center gap-1.5">
                <div className="flex h-8 min-w-0 flex-1 items-center gap-2 rounded-xl border border-border/50 bg-muted/25 px-3 text-[12.5px]">
                  <Wand2 className="size-3.5 shrink-0 text-primary" />
                  <span className={cn('truncate', !activePreset && 'text-muted-foreground')}>
                    {activePreset
                      ? activePreset.name
                      : '未使用（按「压制格式」走内置模板）'}
                  </span>
                  {activePreset && (
                    <span className="ml-auto shrink-0 font-mono text-[11px] text-muted-foreground">
                      {activePreset.container.toUpperCase()} · {presetBitrateHint(activePreset)}
                    </span>
                  )}
                </div>
                <Button
                  size="sm"
                  variant="outline"
                  className="shrink-0"
                  onClick={() => setPresetOpen(true)}
                >
                  选择…
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  className="shrink-0"
                  disabled={!src || !src.height}
                  title={
                    src?.height
                      ? '按当前源分辨率挑一条最合适的'
                      : '导入视频后才能按分辨率推荐'
                  }
                  onClick={() => {
                    if (!src) return
                    const r = recommendPresets(src, settings.presets)
                    if (r[0]) applyPreset(r[0])
                  }}
                >
                  推荐
                </Button>
              </div>
            </Field>
          </div>

          <Separator className="my-3" />

          <Row className="flex-wrap gap-x-4 gap-y-1.5">
            <Radio
              checked={video.mode === 3}
              // 还没挑过预设就直接把管理器打开 —— 选了这个模式却没预设等于没设置
              onSelect={() => {
                patchVideo({ mode: 3 })
                if (!video.presetEncoder) setPresetOpen(true)
              }}
              label="预设"
            />
            <Radio
              checked={video.mode === 0}
              onSelect={() => patchVideo({ mode: 0 })}
              label="自定义参数"
            />
            <Radio checked={video.mode === 1} onSelect={() => patchVideo({ mode: 1 })} label="CRF" />
            <Radio checked={video.mode === 2} onSelect={() => patchVideo({ mode: 2 })} label="2PASS" />
            <span className="flex-1" />
            <Checkbox
              checked={video.autoShutdown}
              onCheckedChange={(v) => patchVideo({ autoShutdown: v })}
              label="完成后关机"
            />
          </Row>

          <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-2">
            {/* 参数框只在「自定义参数」模式下出现：
                CRF / 2PASS 已经各自有专门的质量值 / 码率输入框，
                再摆一个自由文本框只会让人不知道该填哪个。 */}
            {video.mode === 0 && (
              <div className="col-span-2 space-y-1">
                <span className="text-[11.5px] text-muted-foreground">
                  自定义参数（原样传给编码器）
                </span>
                <Textarea
                  rows={2}
                  value={video.customParams}
                  onChange={(e) => patchVideo({ customParams: e.target.value })}
                  placeholder="--crf 23 --preset 8 --aq-mode 2 --ref 8 --subme 10"
                  className="min-h-[46px] font-mono text-[12px]"
                />
              </div>
            )}

            {video.mode === 1 && (
              <Field label="质量值" labelWidth={64}>
                <Input
                  type="number"
                  step="0.5"
                  value={video.crf}
                  onChange={(e) => patchVideo({ crf: Number(e.target.value) })}
                />
              </Field>
            )}
            {video.mode === 2 && (
              <Field label="码率(kbps)" labelWidth={64}>
                <Input
                  type="number"
                  value={video.bitrate}
                  onChange={(e) => patchVideo({ bitrate: Number(e.target.value) })}
                />
              </Field>
            )}

            <Field label="音频码率" labelWidth={64}>
              <Select
                value={audio.bitrate}
                onValueChange={(v) => patchAudio({ bitrate: v, useBitrate: true })}
                options={AUDIO_BITRATES}
              />
            </Field>

            <Field label="起始帧" labelWidth={64}>
              <Input
                type="number"
                value={video.seek}
                onChange={(e) => patchVideo({ seek: Number(e.target.value) })}
              />
            </Field>
            <Field label="编码帧数" labelWidth={64}>
              <Input
                type="number"
                value={video.frames}
                onChange={(e) => patchVideo({ frames: Number(e.target.value) })}
              />
            </Field>

            <Field label="宽度" labelWidth={64}>
              <Input
                type="number"
                value={video.width}
                onChange={(e) => patchVideo({ width: Number(e.target.value) })}
                disabled={video.maintainResolution}
              />
            </Field>
            <Field label="高度" labelWidth={64}>
              <Input
                type="number"
                value={video.height}
                onChange={(e) => patchVideo({ height: Number(e.target.value) })}
                disabled={video.maintainResolution}
              />
            </Field>

            <Field label="线程" labelWidth={64}>
              <Input
                value={video.threads}
                onChange={(e) => patchVideo({ threads: e.target.value })}
                placeholder="auto"
              />
            </Field>
            <div className="flex items-center">
              <Checkbox
                checked={video.maintainResolution}
                onCheckedChange={(v) => patchVideo({ maintainResolution: v })}
                label="保持原分辨率"
              />
            </div>
          </div>
        </GroupCard>

        <GroupCard title="硬件加速" right={<Zap className="size-3 text-primary" />}>
          <Row className="flex-wrap gap-x-5 gap-y-1.5">
            <Checkbox
              checked={video.useGpu}
              onCheckedChange={(v) => patchVideo({ useGpu: v })}
              label="启用 GPU 加速"
            />
            <Checkbox
              checked={video.hybrid}
              onCheckedChange={(v) => patchVideo({ hybrid: v })}
              label="混合压制"
            />
          </Row>
          <div className="mt-2 grid grid-cols-2 gap-x-4">
            <Field label="显卡" labelWidth={64}>
              <Select
                value={gpus.find((g) => g.index === video.gpuIndex)?.label ?? '默认GPU'}
                onValueChange={(v) => {
                  const g = gpus.find((x) => x.label === v)
                  if (g) patchVideo({ gpuIndex: g.index, gpuKind: g.kind as GpuKind })
                }}
                options={gpus.length ? gpus.map((g) => g.label) : ['默认GPU']}
                disabled={!video.useGpu && !video.hybrid}
              />
            </Field>
            <Field label="编码器" labelWidth={64}>
              <div className="flex h-8 items-center">
                <Badge variant="accent">
                  {video.gpuKind === 'amf' ? 'AMF' : video.gpuKind === 'qsv' ? 'QSV' : 'NVENC'}
                </Badge>
              </div>
            </Field>
          </div>
        </GroupCard>

        <RunButtons
          running={running}
          paused={paused}
          disabled={!video.input || !video.output}
          onStart={() => void startSingle()}
          onPause={togglePause}
          onStop={cancel}
        />
      </div>

      {/* ---------------- 右：批量 ---------------- */}
      <div className="flex min-h-0 w-[320px] min-w-[300px] flex-1 flex-col">
        <GroupCard title="批量压制" className="flex min-h-0 flex-1 flex-col">
          <div
            {...dropList.props}
            className={`flex min-h-0 flex-1 flex-col rounded-xl border transition-colors ${
              dropList.over ? 'border-primary bg-primary/8' : 'border-transparent'
            }`}
          >
            <ListShell className="min-h-[160px] flex-1">
              {batch.length === 0 ? (
                <div className="flex h-full items-center justify-center p-4 text-center text-[12px] text-muted-foreground">
                  把视频拖进来，或点下面的「添加」
                </div>
              ) : (
                batch.map((f, i) => (
                  <ListRow key={`${f}-${i}`} onClick={() => void 0}>
                    {f}
                  </ListRow>
                ))
              )}
            </ListShell>
          </div>

          <Row className="mt-2 gap-1.5">
            <Button
              size="sm"
              onClick={() =>
                void pickFiles('选择要批量压制的视频').then((ps) =>
                  setBatch((b) => [...b, ...ps.filter((p) => !b.includes(p))]),
                )
              }
            >
              <Plus className="size-3.5" />
              添加
            </Button>
            <Button
              size="sm"
              disabled={batch.length === 0}
              onClick={() => setBatch((b) => b.slice(0, -1))}
            >
              <Trash2 className="size-3.5" />
              删除
            </Button>
            <Button size="sm" disabled={batch.length === 0} onClick={() => setBatch([])}>
              <XCircle className="size-3.5" />
              清空
            </Button>
          </Row>

          <div className="mt-2 space-y-2">
            <PathRow
              label="输出路径"
              labelWidth={64}
              value={outputDir}
              onChange={setOutputDir}
              onBrowse={() =>
                void pickFolder('选择输出目录').then((p) => {
                  if (p) {
                    setOutputDir(p)
                    notify(`输出目录已设为 ${p}`)
                  }
                })
              }
              onDropFile={(paths) => setOutputDir(paths[0])}
            />
            <Checkbox checked={embedSub} onCheckedChange={setEmbedSub} label="内嵌字幕" />
          </div>

          <div className="mt-3">
            <RunButtons
              running={running}
              paused={paused}
              disabled={batch.length === 0 || !outputDir}
              onStart={() => void startBatch()}
              onPause={togglePause}
              onStop={cancel}
              startLabel={`压制 ${batch.length || 0} 个`}
            />
          </div>
        </GroupCard>
      </div>

      <PresetManager
        open={presetOpen}
        onClose={() => setPresetOpen(false)}
        current={activePreset?.id ?? ''}
        source={src ? { width: src.width, height: src.height, fps: src.fps } : null}
        custom={settings.presets}
        onApply={applyPreset}
        onChangeCustom={(next) => patchSettings({ presets: next })}
      />
    </div>
  )
}

/** 秒 → `1:23:45` / `23:45` */
function fmtDur(sec: number) {
  const s = Math.max(0, Math.floor(sec))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const ss = s % 60
  const pad = (n: number) => String(n).padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(ss)}` : `${m}:${pad(ss)}`
}

/** 压制 / 暂停 / 终止 —— 单文件与批量共用 */
function RunButtons({
  running,
  paused,
  disabled,
  onStart,
  onPause,
  onStop,
  startLabel = '压制',
}: {
  running: boolean
  paused: boolean
  disabled: boolean
  onStart: () => void
  onPause: () => void
  onStop: () => void
  startLabel?: string
}) {
  return (
    <Row className="gap-2">
      <Button
        variant="default"
        size="sm"
        onClick={onStart}
        disabled={running || disabled}
        className="h-8 flex-1"
      >
        <Play className="size-3.5" />
        {startLabel}
      </Button>
      <Button variant="outline" size="sm" onClick={onPause} disabled={!running} className="h-8">
        <Pause className="size-3.5" />
        {paused ? '继续' : '暂停'}
      </Button>
      <Button variant="outline" size="sm" onClick={onStop} disabled={!running} className="h-8">
        <Square className="size-3" />
        终止
      </Button>
    </Row>
  )
}
