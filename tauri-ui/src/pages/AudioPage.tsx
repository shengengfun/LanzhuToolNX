import * as React from 'react'
import { Pause, Play, Plus, Square, Trash2, Waves, XCircle } from 'lucide-react'
import {
  Button,
  Card,
  Field,
  GroupCard,
  Input,
  ListRow,
  ListShell,
  Radio,
  Row,
  Select,
  Separator,
} from '~/components/ui'
import { PathRow } from '~/components/common'
import { RoughCut } from '~/components/RoughCut'
import { useApp, useDropZone, pickFile, pickFiles, pickSave } from '~/state'
import * as api from '~/lib/api'
import { AUDIO_BITRATES, AUDIO_ENCODERS } from '~/lib/types'
import { AUDIO_PRESETS } from '~/lib/presets'

const EXT: Record<number, string> = { 0: '.mp4', 1: '.m4a', 2: '.wav', 3: '.m4a', 4: '.flac', 5: '.m4a', 6: '.ac3' }

export function AudioPage() {
  const { audio, patchAudio, running, paused, run, cancel, togglePause, notify } = useApp()
  const [batch, setBatch] = React.useState<string[]>([])
  const [busy, setBusy] = React.useState(false)

  const encoder = AUDIO_ENCODERS[audio.encoder]
  const presets = AUDIO_PRESETS[encoder] ?? []

  const dropList = useDropZone((paths) =>
    setBatch((b) => [...b, ...paths.filter((p) => !b.includes(p))]),
  )

  const chooseInput = async (forced?: string) => {
    const p = forced ?? (await pickFile('选择音频/视频文件'))
    if (!p) return
    patchAudio({ input: p, output: audio.output || swapExt(p, EXT[audio.encoder] ?? '.aac') })
  }

  const start = async (input: string, output: string) => {
    if (!input || !output) {
      notify('请选择输入和输出文件', 'error')
      return
    }
    setBusy(true)
    try {
      const cmds = await api.planAudio({ ...audio, input, output })
      await run(cmds, input)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    } finally {
      setBusy(false)
    }
  }

  const startBatch = async () => {
    const out = audio.output ? audio.output.replace(/[^\\/]+$/, '') : ''
    if (!out) {
      notify('请先指定一个输出文件，以便确定输出目录', 'error')
      return
    }
    setBusy(true)
    try {
      const all: string[] = []
      for (const f of batch) {
        const stem = f.replace(/^.*[\\/]/, '').replace(/\.[^.]+$/, '')
        all.push(...(await api.planAudio({ ...audio, input: f, output: `${out}${stem}${EXT[audio.encoder] ?? '.aac'}` })))
      }
      await run(all, `批量 ${batch.length} 个`)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-3 overflow-y-auto px-0.5 pt-1 pr-1">
      <div className="flex min-h-0 flex-wrap gap-3">
        <div className="flex max-w-[560px] min-h-0 min-w-[360px] flex-1 flex-col gap-3">
        <Card className="space-y-2 p-3">
          <PathRow
            label="输入"
            value={audio.input}
            onChange={(v) => patchAudio({ input: v })}
            onBrowse={() => void chooseInput()}
            onDropFile={(paths) => void chooseInput(paths[0])}
          />
          <PathRow
            label="输出"
            value={audio.output}
            onChange={(v) => patchAudio({ output: v })}
            onBrowse={() =>
              void pickSave('选择输出文件', audio.output || `output${EXT[audio.encoder]}`, [
                { name: '音频', extensions: ['m4a', 'aac', 'mp4', 'flac', 'wav', 'ac3'] },
              ]).then((p) => p && patchAudio({ output: p }))
            }
            onDropFile={(paths) => patchAudio({ output: paths[0] })}
          />
        </Card>

        <GroupCard title="编码参数">
          <div className="grid grid-cols-2 gap-x-4 gap-y-2">
            <Field label="编码器" labelWidth={64}>
              <Select
                value={encoder}
                onValueChange={(v) => {
                  const i = (AUDIO_ENCODERS as readonly string[]).indexOf(v)
                  patchAudio({ encoder: i < 0 ? 0 : i })
                }}
                options={AUDIO_ENCODERS}
              />
            </Field>
            <Field label="预设" labelWidth={64}>
              <Select
                value=""
                onValueChange={(v) => {
                  const p = presets.find((x) => x.name === v)
                  if (p) patchAudio({ customParams: p.args, useBitrate: false })
                }}
                options={presets.map((p) => p.name)}
                placeholder="选择预设…"
                disabled={presets.length === 0}
              />
            </Field>
          </div>

          <Separator className="my-3" />

          <Row className="flex-wrap gap-x-5 gap-y-1.5">
            <Radio checked={audio.useBitrate} onSelect={() => patchAudio({ useBitrate: true })} label="按码率" />
            <Radio
              checked={!audio.useBitrate}
              onSelect={() => patchAudio({ useBitrate: false })}
              label="自定义参数"
            />
          </Row>

          <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-2">
            {audio.useBitrate ? (
              <Field label="比特率" labelWidth={64}>
                <Select
                  value={audio.bitrate}
                  onValueChange={(v) => patchAudio({ bitrate: v })}
                  options={AUDIO_BITRATES}
                />
              </Field>
            ) : (
              <Field label="自定义参数" labelWidth={64} className="col-span-2">
                <Input
                  value={audio.customParams}
                  onChange={(e) => patchAudio({ customParams: e.target.value })}
                  placeholder="-q 2 --no-optimize"
                  className="font-mono text-[12px]"
                />
              </Field>
            )}
          </div>
        </GroupCard>

        <Row className="gap-2">
          <Button
            variant="default"
            size="sm"
            className="h-8 flex-1"
            disabled={running || busy || !audio.input || !audio.output}
            onClick={() => void start(audio.input, audio.output)}
          >
            <Play className="size-3.5" />
            转码
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

      <div className="flex min-h-0 w-[320px] min-w-[300px] flex-1 flex-col">
        <GroupCard title="批量列表" className="flex min-h-0 flex-1 flex-col">
          <div
            {...dropList.props}
            className={`flex min-h-0 flex-1 flex-col rounded-xl border transition-colors ${
              dropList.over ? 'border-primary bg-primary/8' : 'border-transparent'
            }`}
          >
            <ListShell className="min-h-[160px] flex-1">
              {batch.length === 0 ? (
                <div className="flex h-full items-center justify-center p-4 text-center text-[12px] text-muted-foreground">
                  把文件拖进来，或点下面的「添加」
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
                void pickFiles('选择要批量转码的音频', [
                  { name: '音频', extensions: ['mp4', 'aac', 'mp2', 'mp3', 'm4a', 'ac3', 'flac', 'wav', 'mka'] },
                  { name: '所有文件', extensions: ['*'] },
                ]).then((ps) => setBatch((b) => [...b, ...ps.filter((p) => !b.includes(p))]))
              }
            >
              <Plus className="size-3.5" />
              添加
            </Button>
            <Button size="sm" disabled={batch.length === 0} onClick={() => setBatch((b) => b.slice(0, -1))}>
              <Trash2 className="size-3.5" />
              删除
            </Button>
            <Button size="sm" disabled={batch.length === 0} onClick={() => setBatch([])}>
              <XCircle className="size-3.5" />
              清空
            </Button>
          </Row>

          <div className="mt-3">
            <Button
              variant="default"
              size="sm"
              className="h-8 w-full"
              disabled={running || busy || batch.length === 0}
              onClick={() => void startBatch()}
            >
              <Play className="size-3.5" />
              转码 {batch.length || 0} 个
            </Button>
          </div>
        </GroupCard>
        </div>
      </div>

      {/* ---------------- 波形粗剪 ---------------- */}
      {/*
        放在页面最底下：转码是"整段处理"，粗剪是"截一段"，
        两件事都发生在同一个文件上是常态（先剪掉片头广告再转码），
        所以干脆合并到一页，省得来回切页签。
      */}
      <div className="space-y-2 pt-1">
        <div className="flex flex-wrap items-center gap-2 px-0.5">
          <Waves className="size-3.5 text-primary" />
          <span className="text-[13px] font-semibold">波形粗剪</span>
          <span className="text-[11.5px] text-muted-foreground">
            拖时间轴上的两个手柄选范围，默认流复制（秒切、无损）
          </span>
        </div>
        <RoughCut mode="audio" />
      </div>
    </div>
  )
}

function swapExt(p: string, ext: string) {
  return p.replace(/\.[^.\\/]+$/, '') + ext
}
