import * as React from 'react'
import {
  AlertTriangle,
  Film,
  Info,
  Music,
  Package,
  Pause,
  Plus,
  Repeat,
  Scissors,
  Square,
  Trash2,
  XCircle,
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
  Row,
  Select,
  Separator,
} from '~/components/ui'
import { PathRow, SegTab } from '~/components/common'
import { useApp, useDropZone, pickFile, pickFiles, pickFolder, pickSave, AUDIO_FILTERS } from '~/state'
import * as api from '~/lib/api'
import { AAC_ENCODERS, MUX_FORMATS, type BatchMuxSpec, type MediaInfo, type MuxSpec } from '~/lib/types'
import { changeExt, splitPath } from '~/lib/utils'
import { ExtractPanel } from './ExtractPage'

/**
 * 封装 / 封装转换 / 抽取。
 *
 * 这三件事本来是不同页，但各自都没几个控件，翻来翻去反而麻烦 ——
 * 而且操作对象常常是同一个文件（先抽出音轨、再重新封装、最后换个容器）。
 * 合成一页后用顶部三个页签切换，顺手多了。
 */
export function MuxExtractPage() {
  const [tab, setTab] = React.useState<'mux' | 'convert' | 'extract'>('mux')

  return (
    <div className="flex h-full min-h-0 flex-col gap-3">
      <div className="flex shrink-0 items-center gap-1.5">
        <SegTab
          active={tab === 'mux'}
          onClick={() => setTab('mux')}
          icon={<Package className="size-3.5" />}
          label="重新封装"
          hint="视频 + 多条音轨塞进新容器，不重新编码"
        />
        <SegTab
          active={tab === 'convert'}
          onClick={() => setTab('convert')}
          icon={<Repeat className="size-3.5" />}
          label="封装转换"
          hint="批量换容器（flv/mp4/mkv/mov/avi/f4v），必要时把音频转成 AAC"
        />
        <SegTab
          active={tab === 'extract'}
          onClick={() => setTab('extract')}
          icon={<Scissors className="size-3.5" />}
          label="抽取轨道"
          hint="抽出视频 / 音频 / 指定流"
        />
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-0.5 pt-1 pr-1">
        {tab === 'mux' && <MuxPanel />}
        {tab === 'convert' && <ConvertPanel />}
        {tab === 'extract' && <ExtractPanel />}
      </div>
    </div>
  )
}

/* ================================================================== *
 * 重新封装
 * ================================================================== */

function MuxPanel() {
  const { running, paused, run, cancel, togglePause, notify } = useApp()
  const [spec, setSpec] = React.useState<MuxSpec>({
    video: '',
    audios: [],
    output: '',
    fps: 'auto',
    par: '1:1',
    keepSourceAudio: true,
    format: 'mp4',
  })
  const [vinfo, setVinfo] = React.useState<MediaInfo | null>(null)
  /** 每条外部音轨探到的编码，用于提示「能不能直接复制」 */
  const [codecs, setCodecs] = React.useState<Record<string, string>>({})
  const patch = (p: Partial<MuxSpec>) => setSpec((s) => ({ ...s, ...p }))

  /**
   * 选视频时顺手填默认输出名：`1.mp4` -> `1_Mux.mp4`（原版 `txtout.Text`）。
   * 以前是 `changeExt(v, '.mp4')`，名跟源文件一模一样 —— 一点开始就把源视频覆盖了。
   */
  const takeVideo = React.useCallback(
    async (p: string) => {
      if (!p) return
      if (spec.output) {
        patch({ video: p })
        return
      }
      const out = await api.defaultMuxOutput(p).catch(() => '')
      patch({ video: p, output: out })
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [spec.output],
  )

  const isRaw = /\.(264|h264|hevc)$/i.test(spec.video)

  // 选完视频顺手探一下轨道：用户能直接看到"这些流能不能塞进这个容器"，
  // 而不是等 ffmpeg 报一句看不懂的错。
  React.useEffect(() => {
    const p = spec.video
    if (!p) {
      setVinfo(null)
      return
    }
    let alive = true
    api
      .probeMedia(p)
      .then((mi) => alive && setVinfo(mi))
      .catch(() => void 0)
    return () => {
      alive = false
    }
  }, [spec.video])

  /** 追加音轨（去重 + 保持顺序），顺便探一下编码 */
  const addAudios = React.useCallback((paths: string[]) => {
    setSpec((s) => {
      const added = paths.filter((p) => p && !s.audios.includes(p))
      if (!added.length) return s
      for (const p of added) {
        api
          .probeMedia(p)
          .then((mi) => {
            if (mi.audio?.codec) {
              setCodecs((m) => ({ ...m, [p]: mi.audio!.codec }))
            }
          })
          .catch(() => void 0)
      }
      return { ...s, audios: [...s.audios, ...added] }
    })
  }, [])

  const removeAudio = (i: number) => patch({ audios: spec.audios.filter((_, k) => k !== i) })
  const dropAudios = useDropZone((paths) => addAudios(paths))

  const ext = spec.output.toLowerCase().replace(/^.*(\.[^.\\/]+)$/, '$1')
  const warning = React.useMemo(() => {
    const vCodec = vinfo?.video?.codec ?? ''
    const source = spec.keepSourceAudio ? (vinfo?.audio?.codec ?? '') : ''
    // 源音轨 + 所有外部音轨一起看：任何一条装不进目标容器都要提醒
    for (const a of [source, ...spec.audios.map((p) => codecs[p] ?? '')]) {
      const w = containerWarning(ext, vCodec, a)
      if (w) return w
    }
    return null
  }, [ext, vinfo, spec.audios, spec.keepSourceAudio, codecs])

  const start = async () => {
    try {
      await run(await api.planMux(spec), spec.output)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    }
  }

  return (
    <div className="flex max-w-[860px] flex-col gap-3">
      <Card className="space-y-2 p-3">
        <PathRow
          label="视频"
          value={spec.video}
          onChange={(v) => void takeVideo(v)}
          onBrowse={() =>
            void pickFile('选择视频文件').then((p) => p && void takeVideo(p))
          }
          onDropFile={(paths) => void takeVideo(paths[0])}
          placeholder="mp4 / mkv / 裸流(.264/.h264/.hevc)"
        />
        {/* 多音轨：可以一次挂多条外部音轨，顺序就是输出顺序 */}
        <div className="flex items-start gap-2">
          <span className="mt-1.5 w-[52px] shrink-0 text-right text-[13px] text-muted-foreground">
            音轨
          </span>
          <div className="min-w-0 flex-1 space-y-1.5">
            <div
              {...dropAudios.props}
              className={`rounded-xl border transition-colors ${
                dropAudios.over ? 'border-primary bg-primary/8' : 'border-transparent'
              }`}
            >
              <ListShell className="max-h-[126px] min-h-[50px]">
                {spec.audios.length === 0 ? (
                  <div className="flex h-[50px] items-center justify-center px-3 text-center text-[12px] text-muted-foreground">
                    把音频拖进来，或点下面「添加音轨」
                  </div>
                ) : (
                  spec.audios.map((p, i) => (
                    <ListRow key={p} className="flex items-center gap-2">
                      <span className="shrink-0 text-[11px] text-muted-foreground">{i + 1}.</span>
                      <span className="min-w-0 flex-1 truncate" title={p}>
                        {splitPath(p).stem}
                        <span className="text-muted-foreground">{splitPath(p).ext}</span>
                      </span>
                      {codecs[p] && <Badge variant="muted">{codecs[p]}</Badge>}
                      <button
                        type="button"
                        title="移除这条音轨"
                        onClick={(e) => {
                          e.stopPropagation()
                          removeAudio(i)
                        }}
                        className="shrink-0 rounded p-0.5 text-muted-foreground transition-colors hover:text-destructive"
                      >
                        <Trash2 className="size-3" />
                      </button>
                    </ListRow>
                  ))
                )}
              </ListShell>
            </div>

            <Row className="gap-1.5">
              <Button
                size="sm"
                variant="outline"
                onClick={() =>
                  void pickFiles('选择要加入的音轨（可多选）', AUDIO_FILTERS).then(addAudios)
                }
              >
                <Plus className="size-3.5" />
                添加音轨
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={spec.audios.length === 0}
                onClick={() => patch({ audios: [] })}
              >
                <XCircle className="size-3.5" />
                清空
              </Button>
              <Checkbox
                checked={spec.keepSourceAudio}
                onCheckedChange={(v) => patch({ keepSourceAudio: v })}
                label="保留视频自带音轨"
              />
              {!spec.keepSourceAudio && spec.audios.length > 0 && (
                <Badge variant="accent">替换音频</Badge>
              )}
            </Row>
          </div>
        </div>

        <PathRow
          label="输出"
          value={spec.output}
          onChange={(v) => patch({ output: v })}
          onBrowse={() =>
            void pickSave('选择输出文件', spec.output || 'output.mp4', [
              { name: '视频', extensions: [...MUX_FORMATS] },
            ]).then((p) => p && patch({ output: p }))
          }
          onDropFile={(paths) => patch({ output: paths[0] })}
        />

        <div className="grid grid-cols-2 gap-x-4 gap-y-2">
          <Field label="容器" labelWidth={52}>
            <Select
              value={spec.format}
              onValueChange={(v) =>
                patch({
                  format: v,
                  // 换容器时顺手把输出扩展名也对上，免得 mp4 里写着 .mkv
                  output: spec.output ? changeExt(spec.output, `.${v}`) : spec.output,
                })
              }
              options={MUX_FORMATS}
            />
          </Field>
          <div className="flex items-center text-[11.5px] text-muted-foreground">
            多音轨时按上面列表的顺序写入；容器的轨道上限（如 mp4 的 4 条音频）由编码器决定。
          </div>
        </div>
      </Card>

      {/* 轨道信息：让"能不能封装"一目了然 */}
      {(vinfo?.video || vinfo?.audio) && (
        <GroupCard title="轨道信息">
          <div className="grid grid-cols-2 gap-x-4 gap-y-1.5 font-mono text-[11.5px]">
            {vinfo?.video && (
              <Row className="gap-1.5">
                <Film className="size-3 shrink-0 text-muted-foreground" />
                <span className="truncate">
                  {vinfo.video.codec} · {vinfo.video.width}×{vinfo.video.height} ·{' '}
                  {vinfo.video.fps}fps
                </span>
              </Row>
            )}
            {vinfo?.audio && (
              <Row className="gap-1.5">
                <Music className="size-3 shrink-0 text-muted-foreground" />
                <span className="truncate">
                  {vinfo.audio.codec} · {vinfo.audio.channels}ch · {vinfo.audio.sampleRate}Hz
                </span>
              </Row>
            )}
          </div>
          <p className="mt-1.5 text-[11.5px] text-muted-foreground">
            源文件音轨：{vinfo?.audio ? '1 条（上面显示的就是它）' : '没有音轨'}。
            这里只能看到第一条音轨，多音轨源文件以实际封装结果为准。
          </p>
          {warning && (
            <Row className="mt-2 items-start gap-1.5 rounded-lg bg-destructive/10 px-2 py-1.5 text-[11.5px] leading-relaxed text-destructive">
              <AlertTriangle className="mt-0.5 size-3.5 shrink-0" />
              <span>{warning}</span>
            </Row>
          )}
        </GroupCard>
      )}

      <GroupCard
        title="裸流参数"
        right={
          isRaw ? (
            <Badge variant="accent">检测到裸流</Badge>
          ) : (
            <Row className="gap-1 text-[11px] text-muted-foreground">
              <Info className="size-3" />
              仅裸流需要填
            </Row>
          )
        }
      >
        <div className="grid grid-cols-2 gap-x-4 gap-y-2">
          <Field label="帧率" labelWidth={64}>
            <Row className="gap-1.5">
              <Input
                value={spec.fps}
                onChange={(e) => patch({ fps: e.target.value })}
                disabled={!isRaw}
                placeholder="auto"
              />
              <Button size="sm" variant="secondary" onClick={() => patch({ fps: 'auto' })}>
                auto
              </Button>
            </Row>
          </Field>
          <Field label="像素比PAR" labelWidth={64}>
            <Row className="gap-1.5">
              <Input
                value={spec.par}
                onChange={(e) => patch({ par: e.target.value })}
                disabled={!isRaw}
                placeholder="1:1"
              />
              <Button size="sm" variant="secondary" onClick={() => patch({ par: '1:1' })}>
                1:1
              </Button>
            </Row>
          </Field>
        </div>
        <Separator className="my-3" />
        <p className="text-[11.5px] leading-relaxed text-muted-foreground">
          底层命令是 <span className="font-mono">-c copy</span>：不重新编码，秒完成、画质零损失。
          所以容器必须认识里面的编码 —— 上面的轨道信息就是给你核对这个的。
        </p>
      </GroupCard>

      <Row className="gap-2">
        <Button
          variant="default"
          size="sm"
          className="h-8 flex-1"
          onClick={() => void start()}
          disabled={running || !spec.video || !spec.output}
        >
          <Package className="size-3.5" />
          开始封装
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
  )
}

/* ================================================================== *
 * 封装转换（原版「批量封装」）
 * ================================================================== */

/**
 * 把一批文件换成别的容器。
 *
 * 这就是原版封装页上那个「输出格式 + AAC 编码器 + 批量封装」的组合，
 * 之前换 Tauri 外壳时整条链路丢了，这里补回来：
 * 源音轨不是 AAC、目标又不是 mkv 时，顺手把音频转成 AAC（原版行为）。
 */
function ConvertPanel() {
  const { running, paused, run, cancel, togglePause, notify } = useApp()
  const [spec, setSpec] = React.useState<BatchMuxSpec>({
    inputs: [],
    format: 'mp4',
    aacEncoder: 'aac',
    outputDir: '',
  })
  const patch = (p: Partial<BatchMuxSpec>) => setSpec((s) => ({ ...s, ...p }))

  const add = React.useCallback((paths: string[]) => {
    setSpec((s) => {
      const more = paths.filter((p) => p && !s.inputs.includes(p))
      return more.length ? { ...s, inputs: [...s.inputs, ...more] } : s
    })
  }, [])

  const drop = useDropZone((paths) => add(paths))

  // 已经是目标格式的会被后端跳过，这里先告诉用户有几条
  const skipped = spec.inputs.filter((p) => splitPath(p).ext.toLowerCase() === `.${spec.format}`)
    .length

  const start = async () => {
    try {
      await run(await api.planBatchMux(spec), `转换 ${spec.inputs.length} 个文件`)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    }
  }

  return (
    <div className="flex max-w-[860px] flex-col gap-3">
      <GroupCard title="待转换文件">
        <div
          {...drop.props}
          className={`rounded-xl border transition-colors ${
            drop.over ? 'border-primary bg-primary/8' : 'border-transparent'
          }`}
        >
          <ListShell className="max-h-[190px] min-h-[110px]">
            {spec.inputs.length === 0 ? (
              <div className="flex h-[110px] items-center justify-center text-[12px] text-muted-foreground">
                把视频拖进来，或点下面的「添加」
              </div>
            ) : (
              spec.inputs.map((f, i) => (
                <ListRow key={`${f}-${i}`} className="flex items-center gap-2">
                  <span className="min-w-0 flex-1 truncate" title={f}>
                    {splitPath(f).stem}
                    <span className="text-muted-foreground">{splitPath(f).ext}</span>
                  </span>
                  {splitPath(f).ext.toLowerCase() === `.${spec.format}` && (
                    <Badge variant="muted">已是目标格式</Badge>
                  )}
                </ListRow>
              ))
            )}
          </ListShell>
        </div>

        <Row className="mt-2 gap-1.5">
          <Button
            size="sm"
            variant="outline"
            onClick={() => void pickFiles('选择要转换的视频').then(add)}
          >
            <Plus className="size-3.5" />
            添加
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={spec.inputs.length === 0}
            onClick={() => patch({ inputs: spec.inputs.slice(0, -1) })}
          >
            <Trash2 className="size-3.5" />
            删除
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={spec.inputs.length === 0}
            onClick={() => patch({ inputs: [] })}
          >
            <XCircle className="size-3.5" />
            清空
          </Button>
        </Row>
      </GroupCard>

      <GroupCard title="转换参数">
        <div className="grid grid-cols-2 gap-x-4 gap-y-2">
          <Field label="目标容器" labelWidth={64}>
            <Select value={spec.format} onValueChange={(v) => patch({ format: v })} options={MUX_FORMATS} />
          </Field>
          <Field label="AAC 编码器" labelWidth={76}>
            <Select
              value={spec.aacEncoder}
              onValueChange={(v) => patch({ aacEncoder: v })}
              options={AAC_ENCODERS}
            />
          </Field>
        </div>
        <div className="mt-2">
          <PathRow
            label="输出目录"
            labelWidth={64}
            value={spec.outputDir}
            onChange={(v) => patch({ outputDir: v })}
            onBrowse={() =>
              void pickFolder('选择输出目录').then((p) => p && patch({ outputDir: p }))
            }
            placeholder="留空则写在源文件旁边"
          />
        </div>
        <Separator className="my-3" />
        <ul className="space-y-1 text-[11.5px] leading-relaxed text-muted-foreground">
          <li>· 视频轨一律 <span className="font-mono">-c:v copy</span>，不重新编码。</li>
          <li>
            · 源音轨不是 AAC 且目标不是 mkv 时，自动转成上面选的 AAC 编码器
            （<span className="font-mono">libfdk_aac</span> 需要工具链里带，没有就用{' '}
            <span className="font-mono">aac</span>）。
          </li>
          <li>· 目标格式与源相同的文件会自动跳过{skipped > 0 ? `（当前会跳过 ${skipped} 个）` : ''}。</li>
        </ul>
      </GroupCard>

      <Row className="gap-2">
        <Button
          variant="default"
          size="sm"
          className="h-8 flex-1"
          onClick={() => void start()}
          disabled={running || spec.inputs.length === 0}
        >
          <Repeat className="size-3.5" />
          开始转换 {spec.inputs.length || 0} 个
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
  )
}

/**
 * 容器的兼容性提醒。
 *
 * 流复制能不能成功，取决于容器认不认轨道里的编码 ——
 * 这是最常踩的坑（"怎么封出来放不了"），所以主动提示而不是等报错。
 */
export function containerWarning(ext: string, videoCodec: string, audioCodec: string) {
  const v = videoCodec.toLowerCase()
  const a = audioCodec.toLowerCase()
  if (!ext) return null

  if (ext === '.mkv') return null // Matroska 几乎什么都收

  if (ext === '.mp4' || ext === '.mov') {
    const okVideo = ['h264', 'hevc', 'mpeg4', 'av1', 'vp9', 'mpeg2video']
    const okAudio = ['aac', 'mp3', 'ac3', 'eac3', 'alac', 'opus', '']
    if (v && !okVideo.includes(v)) {
      return `MP4/MOV 容器不适合装 ${v} 视频，流复制可能失败或播放器不认。改用 MKV 更稳。`
    }
    if (a && !okAudio.includes(a)) {
      return `MP4/MOV 容器不适合装 ${a} 音频（建议 MKV，或先把音频转成 AAC）。`
    }
    return null
  }

  if (ext === '.flv') {
    if (v && v !== 'h264' && v !== 'flv1') {
      return `FLV 只认 H.264 视频，当前是 ${v}。改用 MKV 或 MP4。`
    }
    if (a && a !== 'aac' && a !== 'mp3') {
      return `FLV 只认 AAC/MP3 音频，当前是 ${a}。改用 MKV 或 MP4。`
    }
  }
  return null
}
