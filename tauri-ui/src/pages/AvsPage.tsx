import * as React from 'react'
import { FileCode2, Pause, Play, Save, Square } from 'lucide-react'
import {
  Button,
  Card,
  Checkbox,
  Field,
  GroupCard,
  Input,
  Row,
  Select,
  Separator,
  Textarea,
} from '~/components/ui'
import { PathRow } from '~/components/common'
import { useApp, pickFile, pickSave } from '~/state'
import * as api from '~/lib/api'
import { VIDEO_FORMATS } from '~/lib/types'
import { changeExt } from '~/lib/utils'

const TEMPLATE = `LoadPlugin("ffms2.dll")

src = FFVideoSource("input.mkv")
aud = FFAudioSource("input.mkv")

AudioDub(src, aud)
`

export function AvsPage() {
  const { video, patchVideo, audio, notify, appendLog, running, paused, run, cancel, togglePause } = useApp()
  const [script, setScript] = React.useState(TEMPLATE)
  const [scriptPath, setScriptPath] = React.useState('')

  const saveScript = async () => {
    let p = scriptPath
    if (!p) {
      p = (await pickSave('保存 AVS 脚本', 'script.avs', [{ name: 'AVS', extensions: ['avs'] }])) ?? ''
      if (!p) return
      setScriptPath(p)
    }
    try {
      await api.writeTextFile(p, script)
      appendLog(`写入脚本：${p}`, 'app')
      notify('脚本已保存')
    } catch (e) {
      notify(String(e), 'error')
    }
  }

  const start = async () => {
    if (!scriptPath) {
      notify('请先指定脚本保存位置', 'error')
      return
    }
    try {
      await api.writeTextFile(scriptPath, script)
      // 原版 `txtAVS_TextChanged`：从脚本里 `Source("...")` 抠出源文件，
      // 在它旁边输出 `<源名>_AVS.mp4`（抠不到就退回脚本自己旁边）。
      const src = /[Ss]ource\("([A-Za-z]:\\[^"]+?\.\w+)"\)/.exec(script)?.[1] ?? ''
      const out =
        video.output || (await api.defaultAvsOutput(src || scriptPath)) || changeExt(scriptPath, '.mp4')
      if (!video.output) patchVideo({ output: out })
      const cmds = await api.planAvs(
        { script, scriptPath, spec: { ...video, input: scriptPath, output: out } },
        audio,
      )
      await run(cmds, scriptPath)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    }
  }

  return (
    <div className="flex h-full min-h-0 flex-wrap gap-3">
      <div className="flex min-h-0 min-w-[380px] flex-1 flex-col gap-3">
        <GroupCard title="AVS 脚本" className="flex min-h-0 flex-1 flex-col">
          <Textarea
            value={script}
            onChange={(e) => setScript(e.target.value)}
            spellCheck={false}
            className="min-h-0 flex-1 resize-none text-[12.5px] leading-[1.65]"
          />
          <Row className="mt-2 gap-2">
            <Button size="sm" onClick={() => void saveScript()}>
              <Save className="size-3.5" />
              保存脚本
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                void pickFile('载入已有 AVS 脚本', [{ name: 'AVS', extensions: ['avs'] }]).then((p) => {
                  if (!p) return
                  setScriptPath(p)
                  api.readTextFile(p).then(setScript).catch((e) => notify(String(e), 'error'))
                })
              }
            >
              <FileCode2 className="size-3.5" />
              载入脚本
            </Button>
          </Row>
        </GroupCard>
      </div>

      <div className="flex w-[320px] min-w-[300px] flex-1 flex-col gap-3">
        <Card className="space-y-2 p-3">
          <PathRow
            label="脚本路径"
            labelWidth={64}
            value={scriptPath}
            onChange={setScriptPath}
            onBrowse={() =>
              void pickSave('保存 AVS 脚本', 'script.avs', [{ name: 'AVS', extensions: ['avs'] }]).then(
                (p) => p && setScriptPath(p),
              )
            }
          />
          <PathRow
            label="输出"
            labelWidth={64}
            value={video.output}
            onChange={(v) => patchVideo({ output: v })}
            onBrowse={() =>
              void pickSave('选择输出文件', video.output || 'output.mp4', [
                { name: '视频', extensions: ['mp4', 'mkv', 'mov'] },
              ]).then((p) => p && patchVideo({ output: p }))
            }
            onDropFile={(paths) => patchVideo({ output: paths[0] })}
          />
        </Card>

        <GroupCard title="压制参数">
          <div className="grid grid-cols-1 gap-y-2">
            <Field label="格式" labelWidth={64}>
              <Select
                value={video.format}
                onValueChange={(v) => patchVideo({ format: v })}
                options={VIDEO_FORMATS}
              />
            </Field>
            <Field label="质量值" labelWidth={64}>
              <Input
                type="number"
                step="0.5"
                value={video.crf}
                onChange={(e) => patchVideo({ crf: Number(e.target.value) })}
              />
            </Field>
            <Row className="gap-2">
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
            </Row>
          </div>
          <Separator className="my-2" />
          <Checkbox
            checked={video.maintainResolution}
            onCheckedChange={(v) => patchVideo({ maintainResolution: v })}
            label="保持原分辨率"
          />
        </GroupCard>

        <Row className="gap-2">
          <Button
            variant="default"
            size="sm"
            className="h-8 flex-1"
            onClick={() => void start()}
            disabled={running}
          >
            <Play className="size-3.5" />
            压制
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
