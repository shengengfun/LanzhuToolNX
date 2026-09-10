import * as React from 'react'
import {
  AlertTriangle,
  Film,
  Info,
  Music,
  Package,
  Pause,
  Scissors,
  Square,
} from 'lucide-react'
import { Badge, Button, Card, Field, GroupCard, Input, Row, Separator } from '~/components/ui'
import { PathRow } from '~/components/common'
import { useApp, pickFile, pickSave } from '~/state'
import * as api from '~/lib/api'
import type { MediaInfo, MuxSpec } from '~/lib/types'
import { changeExt } from '~/lib/utils'
import { ExtractPanel } from './ExtractPage'

/**
 * 封装 + 抽取。
 *
 * 这两件事本来是两页，但各自没几个控件，翻来翻去反而麻烦 ——
 * 而且它们的操作对象常常是同一个文件（先抽出音轨、再重新封装）。
 * 合成一页后用顶部两个页签切换，顺手多了。
 */
export function MuxExtractPage() {
  const [tab, setTab] = React.useState<'mux' | 'extract'>('mux')

  return (
    <div className="flex h-full min-h-0 flex-col gap-3">
      <div className="flex shrink-0 items-center gap-1.5">
        <SegBtn
          active={tab === 'mux'}
          onClick={() => setTab('mux')}
          icon={<Package className="size-3.5" />}
          label="重新封装"
          hint="把视频流和音频流塞进一个新容器，不重新编码"
        />
        <SegBtn
          active={tab === 'extract'}
          onClick={() => setTab('extract')}
          icon={<Scissors className="size-3.5" />}
          label="抽取轨道"
          hint="抽出视频 / 音频 / 指定流"
        />
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-0.5 pt-1 pr-1">
        {tab === 'mux' ? <MuxPanel /> : <ExtractPanel />}
      </div>
    </div>
  )
}

function SegBtn({
  active,
  onClick,
  icon,
  label,
  hint,
}: {
  active: boolean
  onClick: () => void
  icon: React.ReactNode
  label: string
  hint: string
}) {
  return (
    <button
      type="button"
      title={hint}
      onClick={onClick}
      className={`flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-[12.5px] font-semibold transition-all ${
        active
          ? 'border-primary/40 bg-primary/10 text-primary shadow-2xs'
          : 'border-border/60 bg-card/60 text-muted-foreground hover:bg-accent/50'
      }`}
    >
      {icon}
      {label}
    </button>
  )
}

/* ================================================================== *
 * 重新封装
 * ================================================================== */

function MuxPanel() {
  const { running, paused, run, cancel, togglePause, notify } = useApp()
  const [spec, setSpec] = React.useState<MuxSpec>({
    video: '',
    audio: '',
    output: '',
    fps: 'auto',
    par: '1:1',
  })
  const [info, setInfo] = React.useState<{ v: MediaInfo | null; a: MediaInfo | null }>({
    v: null,
    a: null,
  })
  const patch = (p: Partial<MuxSpec>) => setSpec((s) => ({ ...s, ...p }))

  const isRaw = /\.(264|h264|hevc)$/i.test(spec.video)

  // 选完文件顺手探一下轨道：用户能直接看到"这俩能不能塞进这个容器"，
  // 而不是等 ffmpeg 报一句看不懂的错。
  React.useEffect(() => {
    const p = spec.video
    if (!p) {
      setInfo((s) => ({ ...s, v: null }))
      return
    }
    let alive = true
    api
      .probeMedia(p)
      .then((mi) => alive && setInfo((s) => ({ ...s, v: mi })))
      .catch(() => void 0)
    return () => {
      alive = false
    }
  }, [spec.video])

  const pickAudio = (p: string) => {
    patch({ audio: p })
    api
      .probeMedia(p)
      .then((mi) => setInfo((s) => ({ ...s, a: mi })))
      .catch(() => void 0)
  }

  const ext = spec.output.toLowerCase().replace(/^.*(\.[^.\\/]+)$/, '$1')
  const warning = React.useMemo(() => {
    const vCodec = info.v?.video?.codec ?? ''
    const aCodec = info.a?.audio?.codec ?? info.v?.audio?.codec ?? ''
    return containerWarning(ext, vCodec, aCodec)
  }, [ext, info])

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
          onChange={(v) => patch({ video: v, output: spec.output || changeExt(v, '.mp4') })}
          onBrowse={() =>
            void pickFile('选择视频文件').then(
              (p) => p && patch({ video: p, output: spec.output || changeExt(p, '.mp4') }),
            )
          }
          onDropFile={(paths) =>
            patch({ video: paths[0], output: spec.output || changeExt(paths[0], '.mp4') })
          }
          placeholder="mp4 / mkv / 裸流(.264/.h264/.hevc)"
        />
        <PathRow
          label="音频"
          value={spec.audio}
          onChange={(v) => patch({ audio: v })}
          onBrowse={() => void pickFile('选择音频文件').then((p) => p && pickAudio(p))}
          onDropFile={(paths) => pickAudio(paths[0])}
          placeholder="留空表示视频文件自带音轨"
        />
        <PathRow
          label="输出"
          value={spec.output}
          onChange={(v) => patch({ output: v })}
          onBrowse={() =>
            void pickSave('选择输出文件', spec.output || 'output.mp4', [
              { name: '视频', extensions: ['mp4', 'mkv', 'mov'] },
            ]).then((p) => p && patch({ output: p }))
          }
          onDropFile={(paths) => patch({ output: paths[0] })}
        />
      </Card>

      {/* 轨道信息：让"能不能封装"一目了然 */}
      {(info.v?.video || info.v?.audio || info.a?.audio) && (
        <GroupCard title="轨道信息">
          <div className="grid grid-cols-2 gap-x-4 gap-y-1.5 font-mono text-[11.5px]">
            {info.v?.video && (
              <Row className="gap-1.5">
                <Film className="size-3 shrink-0 text-muted-foreground" />
                <span className="truncate">
                  {info.v.video.codec} · {info.v.video.width}×{info.v.video.height} ·{' '}
                  {info.v.video.fps}fps
                </span>
              </Row>
            )}
            {(info.v?.audio || info.a?.audio) && (
              <Row className="gap-1.5">
                <Music className="size-3 shrink-0 text-muted-foreground" />
                <span className="truncate">
                  {(info.v?.audio ?? info.a?.audio)!.codec} ·{' '}
                  {(info.v?.audio ?? info.a?.audio)!.channels}ch ·{' '}
                  {(info.v?.audio ?? info.a?.audio)!.sampleRate}Hz
                </span>
              </Row>
            )}
          </div>
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
