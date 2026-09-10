import * as React from 'react'
import { Loader2, Pause, Play, Scissors, Square, Waves } from 'lucide-react'
import {
  Button,
  Card,
  Checkbox,
  Field,
  GroupCard,
  Input,
  Radio,
  Row,
  Select,
  Separator,
} from '~/components/ui'
import { PathRow } from '~/components/common'
import { useApp, useDropZone, useMediaUrl, pickFile, pickSave } from '~/state'
import * as api from '~/lib/api'
import type { TrimSpec } from '~/lib/types'
import { accentHex } from '~/lib/appearance'
import { changeExt, fmtTime, parseTime, splitPath } from '~/lib/utils'

/**
 * 粗剪（视频 / 音频共用）。
 *
 * 设计要点：
 * - **时间轴 = 波形图 + 选区叠加层**。波形由后端 ffmpeg 的 `showwavespic` 生成，
 *   以 data URI 直接内联 —— 不用管临时文件、不用管缓存失效，改一次参数换一张图。
 * - **流复制 vs 重编码**：默认流复制（秒出片、无损），切点会吸到最近关键帧；
 *   要帧精确就勾"重编码"，顺便还能改质量。
 * - 预览用 `<video>` / `<audio>` 直接吃后端自绘的 `lzmedia://` 协议（支持 Range），
 *   所以拖进度条是真的在拖 —— 不是"下载完整个文件再说"。
 */

export type RoughCutMode = 'video' | 'audio'

/** 视频容器：这三个对 H.264/AAC 的 copy 都友好。 */
const VIDEO_CONTAINERS = ['mp4', 'mkv', 'mov']
/** 音频容器。 */
const AUDIO_CONTAINERS = ['m4a', 'mp3', 'flac', 'wav', 'aac']

export function RoughCut({ mode }: { mode: RoughCutMode }) {
  const { run, running, paused, cancel, togglePause, notify, pushRecent } = useApp()

  const [input, setInput] = React.useState('')
  const [output, setOutput] = React.useState('')
  const [outputTouched, setOutputTouched] = React.useState(false)
  const [dur, setDur] = React.useState(0)
  const [start, setStart] = React.useState(0)
  const [end, setEnd] = React.useState(0)
  const [pos, setPos] = React.useState(0)
  const [wave, setWave] = React.useState('')
  const [waveBusy, setWaveBusy] = React.useState(false)
  const [waveErr, setWaveErr] = React.useState('')
  const [container, setContainer] = React.useState(mode === 'audio' ? 'm4a' : 'mp4')
  const [reencode, setReencode] = React.useState(false)
  const [audioOnly, setAudioOnly] = React.useState(mode === 'audio')
  const [params, setParams] = React.useState('')
  const [loopSel, setLoopSel] = React.useState(mode === 'audio')
  const [busy, setBusy] = React.useState(false)

  const mediaRef = React.useRef<HTMLMediaElement | null>(null)
  const url = useMediaUrl(input)

  /** 输出扩展名。
   *  纯音频 + 流复制时用 `.mka`：Matroska 音频几乎能吃下任何编码，
   *  而 `.m4a` 只认 AAC —— 源是 FLAC/AC3 时复制进 m4a 会直接报错。 */
  const ext = React.useMemo(() => {
    if (audioOnly && !reencode) return '.mka'
    return `.${container}`
  }, [audioOnly, reencode, container])

  /* ---------------- 文件名联动 ---------------- */

  const applyInput = React.useCallback(
    (p: string) => {
      setInput(p)
      setOutputTouched(false)
      setDur(0)
      setStart(0)
      setEnd(0)
      setPos(0)
      setWave('')
      setWaveErr('')
      pushRecent(p)
    },
    [pushRecent],
  )

  React.useEffect(() => {
    if (!input || outputTouched) return
    const want = changeExt(input, ext)
    if (output !== want) setOutput(want)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [input, ext, outputTouched])

  /* ---------------- 探测时长 + 画波形 ---------------- */

  const load = React.useCallback(
    async (p: string) => {
      setWaveBusy(true)
      setWaveErr('')
      try {
        const mi = await api.probeMedia(p)
        if (mi.durationSec > 0) {
          setDur(mi.durationSec)
          setEnd((e) => (e > 0 && e < mi.durationSec ? e : mi.durationSec))
        }
      } catch {
        /* 探测失败就靠 <video>/<audio> 的 metadata 事件兜底 */
      }
      try {
        const png = await api.makeWaveform(p, 1800, 150, accentHex())
        setWave(png)
      } catch (e) {
        setWave('')
        setWaveErr(String(e).replace(/^Error:\s*/, ''))
      } finally {
        setWaveBusy(false)
      }
    },
    [],
  )

  React.useEffect(() => {
    if (!input) return
    void load(input)
  }, [input, load])

  /* ---------------- 预览播放 ---------------- */

  const seek = (t: number) => {
    const el = mediaRef.current
    if (el) el.currentTime = Math.max(0, t)
    setPos(t)
  }

  const onTimeUpdate = (e: React.SyntheticEvent<HTMLMediaElement>) => {
    const el = e.currentTarget
    setPos(el.currentTime)
    if (loopSel && end > 0 && el.currentTime >= end) {
      // 放着放着就到终点了：这是"试看选区"的预期行为
      el.pause()
      el.currentTime = start
      setPos(start)
    }
  }

  /** 选区拖动：两个手柄 + 点轨道定位。 */
  const onRange = (a: number, b: number) => {
    setStart(a)
    setEnd(b)
  }

  const clampEnd = (v: number) => (dur > 0 ? Math.min(dur, v) : v)

  const applyStart = (v: number) => onRange(Math.max(0, Math.min(v, end - 0.05)), end)
  const applyEnd = (v: number) => onRange(start, clampEnd(Math.max(v, start + 0.05)))

  /* ---------------- 执行 ---------------- */

  const cut = async () => {
    if (!input) {
      notify('请先选择要剪的文件', 'error')
      return
    }
    if (dur > 0 && end - start < 0.05) {
      notify('选区是空的，先把起点/终点拉开一点', 'error')
      return
    }
    const out = output || changeExt(input, ext)
    setOutput(out)
    const spec: TrimSpec = {
      input,
      output: out,
      start,
      end,
      audioOnly,
      reencode,
      params,
      audioBitrate: 192,
    }
    setBusy(true)
    try {
      const cmds = await api.planTrim(spec)
      await run(cmds, `${splitPath(input).stem} → 剪切`)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    } finally {
      setBusy(false)
    }
  }

  const dropZone = useDropZone((paths) => applyInput(paths[0]))

  return (
    <div className="flex min-h-0 flex-col gap-3">
      <Card className="space-y-2 p-3">
        <PathRow
          label={mode === 'audio' ? '音频' : '视频'}
          value={input}
          onChange={applyInput}
          onBrowse={() =>
            void (mode === 'audio'
              ? pickFile('选择音频/视频文件')
              : pickFile('选择视频文件')
            ).then((p) => p && applyInput(p))
          }
          onDropFile={(paths) => applyInput(paths[0])}
          placeholder="拖进来或点右侧选择，支持视频/音频"
        />
        <PathRow
          label="输出"
          value={output}
          onChange={(v) => {
            setOutputTouched(true)
            setOutput(v)
          }}
          onBrowse={() =>
            void pickSave('选择输出文件', output || changeExt(input || 'output', ext), [
              { name: '媒体', extensions: [ext.replace('.', '')] },
            ]).then((p) => {
              if (!p) return
              setOutputTouched(true)
              setOutput(p)
            })
          }
          onDropFile={(paths) => {
            setOutputTouched(true)
            setOutput(paths[0])
          }}
        />
      </Card>

      {/* ---------------- 预览 + 时间轴 ---------------- */}
      <GroupCard
        title="预览与选区"
        right={
          dur > 0 ? (
            <span className="font-mono text-[11px] text-muted-foreground">{fmtTime(dur)}</span>
          ) : null
        }
      >
        <div {...dropZone.props} className="flex flex-col gap-2">
          {mode === 'video' && (
            <div
              className={`overflow-hidden rounded-xl border bg-black/90 ${
                dropZone.over ? 'ring-2 ring-primary/60' : 'border-border/60'
              }`}
            >
              {url ? (
                <video
                  ref={(el) => {
                    mediaRef.current = el
                  }}
                  src={url}
                  controls
                  preload="metadata"
                  className="max-h-[220px] w-full"
                  onLoadedMetadata={(e) => {
                    const d = e.currentTarget.duration
                    if (Number.isFinite(d) && d > 0) {
                      setDur(d)
                      setEnd((v) => (v > 0 && v < d ? v : d))
                    }
                  }}
                  onTimeUpdate={onTimeUpdate}
                />
              ) : (
                <div className="flex h-[180px] items-center justify-center text-[12px] text-white/60">
                  选择文件后可在此预览
                </div>
              )}
            </div>
          )}

          {/* 音频模式：藏一个 audio 元素当播放器 */}
          {mode === 'audio' && url && (
            <audio
              ref={(el) => {
                mediaRef.current = el
              }}
              src={url}
              controls
              preload="metadata"
              className="h-9 w-full"
              onLoadedMetadata={(e) => {
                const d = e.currentTarget.duration
                if (Number.isFinite(d) && d > 0) {
                  setDur(d)
                  setEnd((v) => (v > 0 && v < d ? v : d))
                }
              }}
              onTimeUpdate={onTimeUpdate}
            />
          )}

          <Timeline
            dur={dur}
            start={start}
            end={end}
            pos={pos}
            wave={wave}
            waveBusy={waveBusy}
            hint={waveErr || (input ? '' : '选择文件后加载波形')}
            onRange={onRange}
            onSeek={seek}
          />
        </div>
      </GroupCard>

      {/* ---------------- 切点与输出参数 ---------------- */}
      <GroupCard title="切点">
        <div className="grid grid-cols-2 gap-x-4 gap-y-2">
          <TimeField
            label="起点"
            value={start}
            onCommit={applyStart}
            onUseCurrent={() => applyStart(pos)}
          />
          <TimeField label="终点" value={end} onCommit={applyEnd} onUseCurrent={() => applyEnd(pos)} />

          <Field label="时长" labelWidth={52}>
            <span className="font-mono text-[12.5px] text-muted-foreground">
              {fmtTime(Math.max(0, end - start))}
            </span>
          </Field>
          <Row className="flex-wrap gap-x-3 gap-y-1">
            <Checkbox checked={loopSel} onCheckedChange={setLoopSel} label="到终点自动停" />
            <Button size="sm" variant="ghost" className="h-7" onClick={() => seek(start)}>
              跳到起点
            </Button>
          </Row>
        </div>

        <Separator className="my-3" />

        <div className="grid grid-cols-2 gap-x-4 gap-y-2">
          <Field label="切点模式" labelWidth={64}>
            <Row className="gap-3">
              <Radio checked={!reencode} onSelect={() => setReencode(false)} label="流复制（快）" />
              <Radio checked={reencode} onSelect={() => setReencode(true)} label="重编码（精确）" />
            </Row>
          </Field>
          <Field label="容器" labelWidth={64}>
            <Select
              /* 纯音频 + 流复制时实际写的是 .mka（Matroska 音频什么都收），
                 所以这里显示的也是 mka，不然用户会以为输出的真是 m4a。 */
              value={audioOnly && !reencode ? 'mka（自适应）' : container}
              onValueChange={setContainer}
              options={mode === 'audio' ? AUDIO_CONTAINERS : VIDEO_CONTAINERS}
              disabled={audioOnly && !reencode}
            />
          </Field>

          <Field label="输出内容" labelWidth={64}>
            <Row className="gap-3">
              <Checkbox
                checked={audioOnly}
                onCheckedChange={setAudioOnly}
                label={mode === 'audio' ? '只保留音频' : '只保留音频（丢掉画面）'}
              />
            </Row>
          </Field>
          <Field label="附加参数" labelWidth={64}>
            <Input
              value={params}
              onChange={(e) => setParams(e.target.value)}
              disabled={!reencode}
              placeholder={reencode ? '-crf 18 -preset slow' : '流复制模式不可用'}
              className="font-mono text-[12px]"
            />
          </Field>
        </div>

        {!reencode && (
          <p className="mt-2 text-[11.5px] leading-relaxed text-muted-foreground">
            流复制不重新编码，几乎瞬时完成、画质无损；代价是起点会被吸到最近的关键帧
            （通常偏差 &lt; 2 秒）。要帧级精确请改用重编码。
          </p>
        )}
      </GroupCard>

      <Row className="gap-2">
        <Button
          variant="default"
          size="sm"
          className="h-8 flex-1"
          disabled={running || busy || !input || !output}
          onClick={() => void cut()}
        >
          {busy ? <Loader2 className="size-3.5 animate-spin" /> : <Scissors className="size-3.5" />}
          开始剪切
        </Button>
        <Button size="sm" variant="outline" className="h-8" onClick={() => seek(start)}>
          <Play className="size-3.5" />
          从起点试听
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
 * 时间轴
 * ================================================================== */

function Timeline({
  dur,
  start,
  end,
  pos,
  wave,
  waveBusy,
  hint,
  onRange,
  onSeek,
}: {
  dur: number
  start: number
  end: number
  pos: number
  wave: string
  waveBusy: boolean
  hint: string
  onRange: (a: number, b: number) => void
  onSeek: (t: number) => void
}) {
  const trackRef = React.useRef<HTMLDivElement>(null)
  const dragging = React.useRef<'start' | 'end' | null>(null)

  const pct = (t: number) => (dur > 0 ? Math.min(100, Math.max(0, (t / dur) * 100)) : 0)

  const ratioAt = (clientX: number) => {
    const el = trackRef.current
    if (!el) return 0
    const r = el.getBoundingClientRect()
    return Math.min(1, Math.max(0, (clientX - r.left) / r.width))
  }

  const onDown = (which: 'start' | 'end') => (e: React.PointerEvent) => {
    if (dur <= 0) return
    e.preventDefault()
    e.stopPropagation()
    dragging.current = which
    ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
  }

  const onMove = (e: React.PointerEvent) => {
    if (!dragging.current || dur <= 0) return
    const t = ratioAt(e.clientX) * dur
    if (dragging.current === 'start') onRange(Math.max(0, Math.min(t, end - 0.05)), end)
    else onRange(start, Math.min(dur, Math.max(t, start + 0.05)))
  }

  const onUp = () => {
    dragging.current = null
  }

  const clickTrack = (e: React.MouseEvent) => {
    if (dur <= 0) return
    onSeek(ratioAt(e.clientX) * dur)
  }

  const left = pct(start)
  const right = pct(end)

  return (
    <div
      ref={trackRef}
      onClick={clickTrack}
      onPointerMove={onMove}
      onPointerUp={onUp}
      onPointerCancel={onUp}
      className="relative h-[104px] w-full cursor-crosshair overflow-hidden rounded-xl border border-border/60 bg-muted/30"
    >
      {wave ? (
        <img
          src={wave}
          alt="波形"
          draggable={false}
          className="pointer-events-none absolute inset-0 size-full object-fill opacity-75"
        />
      ) : (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center gap-2 text-[12px] text-muted-foreground">
          {waveBusy ? <Loader2 className="size-3.5 animate-spin" /> : <Waves className="size-3.5" />}
          {waveBusy ? '正在生成波形…' : hint || '无波形（该文件可能没有音轨）'}
        </div>
      )}

      {/* 选区外压暗，让选中范围一目了然 */}
      {dur > 0 && (
        <>
          <div
            className="pointer-events-none absolute inset-y-0 left-0 bg-foreground/12"
            style={{ width: `${left}%` }}
          />
          <div
            className="pointer-events-none absolute inset-y-0 right-0 bg-foreground/12"
            style={{ width: `${100 - right}%` }}
          />
          <div
            className="pointer-events-none absolute inset-y-0 border-x-2 border-primary bg-primary/12"
            style={{ left: `${left}%`, width: `${Math.max(0, right - left)}%` }}
          />
        </>
      )}

      {/* 播放头 */}
      {dur > 0 && (
        <div
          className="pointer-events-none absolute inset-y-0 w-px bg-foreground/70"
          style={{ left: `${pct(pos)}%` }}
        />
      )}

      {/* 两个手柄 */}
      {dur > 0 && (
        <>
          <Handle style={{ left: `${left}%` }} onPointerDown={onDown('start')} label="起点" />
          <Handle style={{ left: `${right}%` }} onPointerDown={onDown('end')} label="终点" />
        </>
      )}

      {/* 刻度提示 */}
      <div className="pointer-events-none absolute inset-x-0 bottom-0 flex justify-between px-1.5 pb-0.5 font-mono text-[10.5px] text-muted-foreground">
        <span>{fmtTime(0)}</span>
        <span>{start > 0 || end > 0 ? `${fmtTime(start)} → ${fmtTime(end)}` : ''}</span>
        <span>{dur > 0 ? fmtTime(dur) : '--'}</span>
      </div>
    </div>
  )
}

function Handle({
  style,
  onPointerDown,
  label,
}: {
  style: React.CSSProperties
  onPointerDown: (e: React.PointerEvent) => void
  label: string
}) {
  return (
    <div
      role="slider"
      aria-label={label}
      title={label}
      onPointerDown={onPointerDown}
      style={style}
      className="absolute inset-y-0 -ml-[7px] flex w-[14px] cursor-ew-resize items-center justify-center"
    >
      <span className="h-8 w-[5px] rounded-full bg-primary shadow-xs ring-1 ring-primary-foreground/40" />
    </div>
  )
}

/* ================================================================== *
 * 时间输入框：输入中不打断（失焦/回车才提交）
 * ================================================================== */

function TimeField({
  label,
  value,
  onCommit,
  onUseCurrent,
}: {
  label: string
  value: number
  onCommit: (v: number) => void
  onUseCurrent: () => void
}) {
  const [text, setText] = React.useState(fmtTime(value))
  React.useEffect(() => setText(fmtTime(value)), [value])

  const commit = () => {
    const t = parseTime(text)
    if (t == null) {
      setText(fmtTime(value))
      return
    }
    onCommit(t)
  }

  return (
    <Field label={label} labelWidth={52}>
      <Row className="gap-1.5">
        <Input
          value={text}
          onChange={(e) => setText(e.target.value)}
          onBlur={commit}
          onKeyDown={(e) => e.key === 'Enter' && commit()}
          className="font-mono text-[12.5px]"
          placeholder="0:00.000"
        />
        <Button
          size="sm"
          variant="secondary"
          className="shrink-0 px-2 text-[12px]"
          onClick={onUseCurrent}
          title="用当前播放位置作为切点"
        >
          当前
        </Button>
      </Row>
    </Field>
  )
}
