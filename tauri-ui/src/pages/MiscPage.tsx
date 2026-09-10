import * as React from 'react'
import { Image as ImageIcon, Moon, Pause, Play, Scissors, Square } from 'lucide-react'
import { Button, Card, Field, GroupCard, Input, Row, Select } from '~/components/ui'
import { PathRow } from '~/components/common'
import { RoughCut } from '~/components/RoughCut'
import { useApp, pickFile, pickSave } from '~/state'
import * as api from '~/lib/api'
import { AUDIO_BITRATES } from '~/lib/types'
import { changeExt } from '~/lib/utils'

type Tab = 'cut' | 'onepic' | 'black'

export function MiscPage() {
  const [tab, setTab] = React.useState<Tab>('cut')
  const [tools, setTools] = React.useState<Record<string, string>>({})

  React.useEffect(() => {
    Promise.all(
      ['ffmpeg.exe'].map((n) => api.resolveTool(n).then((p) => [n, p] as const)),
    )
      .then((pairs) => setTools(Object.fromEntries(pairs)))
      .catch(() => void 0)
  }, [])

  return (
    <div className="flex h-full min-h-0 max-w-[1000px] gap-3">
      <div className="flex w-[148px] shrink-0 flex-col gap-1.5">
        <TabBtn
          icon={<Scissors className="size-4" />}
          active={tab === 'cut'}
          onClick={() => setTab('cut')}
          title="视频粗剪"
        />
        <TabBtn
          icon={<ImageIcon className="size-4" />}
          active={tab === 'onepic'}
          onClick={() => setTab('onepic')}
          title="单图转视频"
        />
        <TabBtn
          icon={<Moon className="size-4" />}
          active={tab === 'black'}
          onClick={() => setTab('black')}
          title="生成黑帧视频"
        />
      </div>

      <div className="min-w-0 flex-1 overflow-y-auto px-0.5 pt-1 pr-1">
        {tab === 'cut' && <RoughCut mode="video" />}
        {tab === 'onepic' && <OnePic tools={tools} />}
        {tab === 'black' && <BlackFrame tools={tools} />}
      </div>
    </div>
  )
}

function TabBtn({
  icon,
  title,
  active,
  onClick,
}: {
  icon: React.ReactNode
  title: string
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex items-center gap-2 rounded-xl border px-2.5 py-2 text-left text-[12.5px] font-semibold transition-all ${
        active
          ? 'border-primary/40 bg-primary/10 text-primary shadow-2xs'
          : 'border-border/60 bg-card/60 text-muted-foreground hover:bg-accent/50'
      }`}
    >
      {icon}
      {title}
    </button>
  )
}

/** 压制 / 暂停 / 终止 —— 三个小工具共用 */
function Actions({ cmds }: { cmds: string[] }) {
  const { running, paused, run, cancel, togglePause, notify } = useApp()
  return (
    <Row className="gap-2">
      <Button
        variant="default"
        size="sm"
        className="h-8 flex-1"
        disabled={running || cmds.length === 0}
        onClick={() =>
          void run(cmds, cmds[0]).catch((e) => notify(String(e).replace(/^Error:\s*/, ''), 'error'))
        }
      >
        <Play className="size-3.5" />
        生成
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
  )
}

function OnePic({ tools }: { tools: Record<string, string> }) {
  const [image, setImage] = React.useState('')
  const [audio, setAudio] = React.useState('')
  const [output, setOutput] = React.useState('')
  const [fps, setFps] = React.useState(1)
  const [crf, setCrf] = React.useState(24)
  const [abr, setAbr] = React.useState('128')

  const cmds = React.useMemo(() => {
    const ff = tools['ffmpeg.exe']
    if (!ff || !image || !output) return []
    const parts = [`"${ff}"`, '-loop 1', `-i "${image}"`]
    if (audio) parts.push(`-i "${audio}"`)
    parts.push('-c:v libx264', `-crf ${crf}`, `-r ${fps}`, '-pix_fmt yuv420p')
    if (audio) parts.push('-c:a aac', `-b:a ${abr}k`, '-shortest')
    parts.push(`-y "${output}"`)
    return [parts.join(' ')]
  }, [tools, image, audio, output, fps, crf, abr])

  return (
    <div className="flex flex-col gap-3">
      <Card className="space-y-2 p-3">
        <PathRow
          label="图片"
          value={image}
          onChange={setImage}
          onBrowse={() =>
            void pickFile('选择图片', [
              { name: '图片', extensions: ['png', 'jpg', 'jpeg', 'bmp', 'webp'] },
            ]).then((p) => {
              if (!p) return
              setImage(p)
              setOutput((o) => o || changeExt(p, '.mp4'))
            })
          }
          onDropFile={(paths) => {
            setImage(paths[0])
            setOutput((o) => o || changeExt(paths[0], '.mp4'))
          }}
        />
        <PathRow
          label="音频"
          value={audio}
          onChange={setAudio}
          onBrowse={() => void pickFile('选择音频（可留空）').then((p) => p && setAudio(p))}
          onDropFile={(paths) => setAudio(paths[0])}
          placeholder="可留空，做无声视频"
        />
        <PathRow
          label="输出"
          value={output}
          onChange={setOutput}
          onBrowse={() =>
            void pickSave('选择输出文件', output || 'out.mp4', [
              { name: '视频', extensions: ['mp4'] },
            ]).then((p) => p && setOutput(p))
          }
          onDropFile={(paths) => setOutput(paths[0])}
        />
      </Card>
      <GroupCard title="参数">
        <div className="grid grid-cols-3 gap-x-4 gap-y-2">
          <Field label="帧率" labelWidth={52}>
            <Input type="number" value={fps} onChange={(e) => setFps(Number(e.target.value))} />
          </Field>
          <Field label="质量CRF" labelWidth={56}>
            <Input type="number" value={crf} onChange={(e) => setCrf(Number(e.target.value))} />
          </Field>
          <Field label="音频码率" labelWidth={64}>
            <Select value={abr} onValueChange={setAbr} options={AUDIO_BITRATES} />
          </Field>
        </div>
      </GroupCard>
      <Actions cmds={cmds} />
    </div>
  )
}

function BlackFrame({ tools }: { tools: Record<string, string> }) {
  const [output, setOutput] = React.useState('')
  const [w, setW] = React.useState(1280)
  const [h, setH] = React.useState(720)
  const [fps, setFps] = React.useState(1)
  const [sec, setSec] = React.useState(60)
  const [crf, setCrf] = React.useState(51)
  const [br, setBr] = React.useState(900)

  const cmds = React.useMemo(() => {
    const ff = tools['ffmpeg.exe']
    if (!ff || !output) return []
    return [
      `"${ff}" -f lavfi -i color=c=black:s=${w}x${h}:r=${fps} -t ${sec} -c:v libx264 -crf ${crf} -b:v ${br}k -pix_fmt yuv420p -y "${output}"`,
    ]
  }, [tools, output, w, h, fps, sec, crf, br])

  return (
    <div className="flex flex-col gap-3">
      <Card className="space-y-2 p-3">
        <PathRow
          label="输出"
          value={output}
          onChange={setOutput}
          onBrowse={() =>
            void pickSave('选择输出文件', output || 'black.mp4', [
              { name: '视频', extensions: ['mp4'] },
            ]).then((p) => p && setOutput(p))
          }
          onDropFile={(paths) => setOutput(paths[0])}
        />
      </Card>
      <GroupCard title="参数">
        <div className="grid grid-cols-3 gap-x-4 gap-y-2">
          <Field label="宽度" labelWidth={52}>
            <Input type="number" value={w} onChange={(e) => setW(Number(e.target.value))} />
          </Field>
          <Field label="高度" labelWidth={52}>
            <Input type="number" value={h} onChange={(e) => setH(Number(e.target.value))} />
          </Field>
          <Field label="帧率" labelWidth={52}>
            <Input type="number" value={fps} onChange={(e) => setFps(Number(e.target.value))} />
          </Field>
          <Field label="时长秒" labelWidth={52}>
            <Input type="number" value={sec} onChange={(e) => setSec(Number(e.target.value))} />
          </Field>
          <Field label="质量CRF" labelWidth={56}>
            <Input type="number" value={crf} onChange={(e) => setCrf(Number(e.target.value))} />
          </Field>
          <Field label="码率" labelWidth={52}>
            <Input type="number" value={br} onChange={(e) => setBr(Number(e.target.value))} />
          </Field>
        </div>
      </GroupCard>
      <Actions cmds={cmds} />
    </div>
  )
}

/* 原来的「音频转码」小工具已删除：它和音频页的转码功能完全重合，
   而且那边还能选编码器/预设/批量，留着只会让人不知道该用哪个。 */

