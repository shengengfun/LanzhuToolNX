import * as React from 'react'
import { Film, Music, Pause, Square, Scissors } from 'lucide-react'
import { Button, Card, Field, GroupCard, Input, ListRow, ListShell, Row } from '~/components/ui'
import { PathRow } from '~/components/common'
import { useApp, pickFile, pickSave } from '~/state'
import * as api from '~/lib/api'
import type { ExtractSpec, MediaInfo } from '~/lib/types'
import { fileStem, splitPath } from '~/lib/utils'

type Kind = ExtractSpec['kind']

const EXT: Record<Kind, string> = { video: '.mp4', audio: '.aac', track: '.mkv', mkv: '.mkv' }

/**
 * 抽取轨道面板。
 *
 * 以前它是独立的一页，但控件太少、页面太空 —— 现在作为「封装抽取」
 * 页里的一个页签存在（由 MuxPage 引入）。
 */
export function ExtractPanel() {
  const { running, paused, run, cancel, togglePause, notify } = useApp()
  const [input, setInput] = React.useState('')
  const [output, setOutput] = React.useState('')
  const [streamIndex, setStreamIndex] = React.useState(0)
  const [info, setInfo] = React.useState<MediaInfo | null>(null)

  const autoOutput = (src: string, kind: Kind, idx: number) => {
    const { dir } = splitPath(src)
    const label =
      kind === 'video' ? '抽取视频1' : kind === 'audio' ? `抽取音频${idx}` : `抽取流Index${idx}`
    return `${dir}${fileStem(src)}_${label}${EXT[kind]}`
  }

  const chooseInput = async (forced?: string) => {
    const p = forced ?? (await pickFile('选择来源文件'))
    if (!p) return
    setInput(p)
    setOutput(autoOutput(p, 'video', 0))
    try {
      setInfo(await api.probeMedia(p))
    } catch {
      setInfo(null)
    }
  }

  const start = async (kind: Kind) => {
    if (!input) {
      notify('请选择来源文件', 'error')
      return
    }
    const out = output || autoOutput(input, kind, streamIndex)
    setOutput(out)
    try {
      await run(await api.planExtract({ input, output: out, kind, streamIndex }), out)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    }
  }

  return (
    <div className="flex max-w-[860px] flex-col gap-3">
      <Card className="space-y-2 p-3">
        <PathRow
          label="来源"
          value={input}
          onChange={setInput}
          onBrowse={() => void chooseInput()}
          onDropFile={(paths) => void chooseInput(paths[0])}
          placeholder="mp4 / mkv / flv / 裸流"
        />
        <PathRow
          label="输出"
          value={output}
          onChange={setOutput}
          onBrowse={() =>
            void pickSave('选择输出文件', output || 'output.mp4', [
              { name: '媒体', extensions: ['mp4', 'mkv', 'aac', 'm4a', 'flac', 'wav'] },
            ]).then((p) => p && setOutput(p))
          }
          onDropFile={(paths) => setOutput(paths[0])}
        />
      </Card>

      <GroupCard title="轨道">
        <ListShell className="max-h-[180px] min-h-[92px]">
          {!info ? (
            <div className="flex h-[92px] items-center justify-center text-[12px] text-muted-foreground">
              选择文件后这里会列出视频 / 音频轨
            </div>
          ) : (
            <>
              {info.video && (
                <ListRow onClick={() => setStreamIndex(0)} className="font-mono text-[11.5px]">
                  [v:0] {info.video.codec} {info.video.width}x{info.video.height} · {info.video.fps}fps
                </ListRow>
              )}
              {info.audio && (
                <ListRow onClick={() => setStreamIndex(1)} className="font-mono text-[11.5px]">
                  [a:0] {info.audio.codec} · {info.audio.channels}ch · {info.audio.sampleRate}Hz
                </ListRow>
              )}
              {!info.video && !info.audio && (
                <div className="flex h-[92px] items-center justify-center text-[12px] text-muted-foreground">
                  未探测到可用轨道
                </div>
              )}
            </>
          )}
        </ListShell>

        <Row className="mt-2 gap-2">
          <Field label="流序号" labelWidth={52}>
            <Input
              type="number"
              value={streamIndex}
              onChange={(e) => setStreamIndex(Number(e.target.value))}
              className="w-[90px]"
            />
          </Field>
          <span className="flex-1" />
          <Button size="sm" variant="outline" disabled={running || !input} onClick={() => void start('video')}>
            <Film className="size-3.5" />
            抽取视频
          </Button>
          <Button size="sm" variant="outline" disabled={running || !input} onClick={() => void start('audio')}>
            <Music className="size-3.5" />
            抽取音频
          </Button>
          <Button size="sm" variant="outline" disabled={running || !input} onClick={() => void start('track')}>
            <Scissors className="size-3.5" />
            抽取轨道
          </Button>
        </Row>
      </GroupCard>

      <Row className="gap-2">
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
