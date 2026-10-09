import * as React from 'react'
import { FileCode2, FolderSearch, Pause, Play, Save, Square, Wand2, X } from 'lucide-react'
import {
  Badge,
  Button,
  Card,
  Checkbox,
  EmptyState,
  Field,
  GroupCard,
  Input,
  ListShell,
  Row,
  Select,
  Separator,
  Textarea,
} from '~/components/ui'
import { PathRow } from '~/components/common'
import { useApp, useWorkspaceValue, pickFile, pickSave } from '~/state'
import * as api from '~/lib/api'
import type { AvsPlugins } from '~/lib/types'
import { VIDEO_FORMATS } from '~/lib/types'
import { changeExt } from '~/lib/utils'

const TEMPLATE = `LoadPlugin("ffms2.dll")

src = FFVideoSource("input.mkv")
aud = FFAudioSource("input.mkv")

AudioDub(src, aud)
`

/** 「生成」用到的滤镜参数（属于这一趟，不进设置文件）。 */
interface FilterState {
  undot: boolean
  tweak: boolean
  tweakChroma: number
  tweakSaturation: number
  tweakBrightness: number
  tweakContrast: number
  levels: boolean
  levelsValue: number
  resize: boolean
  resizeWidth: number
  resizeHeight: number
  sharpen: boolean
  sharpenValue: number
  crop: boolean
  cropArgs: string
  addBorders: boolean
  borderArgs: [number, number, number, number]
  trim: boolean
  trimStart: number
  trimEnd: number
}

const EMPTY_FILTERS: FilterState = {
  undot: false,
  tweak: false,
  tweakChroma: 1,
  tweakSaturation: 1,
  tweakBrightness: 0,
  tweakContrast: 1,
  levels: false,
  levelsValue: 1.16,
  resize: false,
  resizeWidth: 1280,
  resizeHeight: 720,
  sharpen: false,
  sharpenValue: 0.2,
  crop: false,
  cropArgs: '0,0,0,0',
  addBorders: false,
  borderArgs: [0, 0, 0, 0],
  trim: false,
  trimStart: 0,
  trimEnd: 0,
}

/** 清单里只显示文件名 —— 那里没有横着放整条路径的余地。 */
function baseName(p: string) {
  const i = Math.max(p.lastIndexOf('\\'), p.lastIndexOf('/'))
  return i >= 0 ? p.slice(i + 1) : p
}

function extOf(p: string) {
  const b = baseName(p)
  const i = b.lastIndexOf('.')
  return i >= 0 ? b.slice(i).toLowerCase() : ''
}

/** `<名字>.<扩展名>` → 同目录的 `<名字>_preview.avs`。 */
function previewPathFor(p: string) {
  const i = Math.max(p.lastIndexOf('\\'), p.lastIndexOf('/'))
  const dir = i >= 0 ? p.slice(0, i + 1) : ''
  return `${dir}${baseName(p).replace(/\.[^.]*$/, '')}_preview.avs`
}

export function AvsPage() {
  const {
    video,
    patchVideo,
    audio,
    notify,
    appendLog,
    running,
    paused,
    run,
    cancel,
    togglePause,
  } = useApp()
  // 脚本正文与保存位置记住（原版存的就是 AVSScript）；源文件 / 字幕 / 输出属于"这一趟"，
  // 和压制页一样不落盘。
  const [script, setScript] = useWorkspaceValue('avsScript', TEMPLATE)
  const [scriptPath, setScriptPath] = useWorkspaceValue('avsScriptPath', '')
  const [applyGlobal, setApplyGlobal] = useWorkspaceValue('avsApplyGlobal', false)

  const [source, setSource] = React.useState('')
  const [subtitle, setSubtitle] = React.useState('')
  const [withAudio, setWithAudio] = React.useState(false)
  const [f, setF] = React.useState<FilterState>(EMPTY_FILTERS)
  const [avs, setAvs] = React.useState<AvsPlugins>({ dir: '', avisynth: false, plugins: [] })

  React.useEffect(() => {
    let alive = true
    api
      .listAvsPlugins()
      .then((p) => alive && setAvs(p))
      .catch(() => void 0)
    return () => {
      alive = false
    }
  }, [])

  const pluginPath = (name: string) => `${avs.dir}\\${name}`

  /**
   * 「生成」—— 逐行对齐原版 `GenerateAVS()`：
   * 先 LoadPlugin（源滤镜 + 字幕滤镜 + Undot），再叠用户勾中的滤镜，
   * 顺序 Undot → Tweak → Levels → LanczosResize → Sharpen → Crop → AddBorders
   * → 字幕 → Trim。这个顺序本身有意义（缩放要在裁剪之前、字幕要贴最终画面），照抄。
   */
  const generate = (state: FilterState = f, src = source, sub = subtitle) => {
    if (!src.trim()) {
      notify('请先选择源视频文件，再生成脚本', 'error')
      return
    }
    const out: string[] = []
    out.push(`LoadPlugin("${pluginPath('LSMASHSource.dll')}")`)
    if (extOf(sub) === '.sup') out.push(`LoadPlugin("${pluginPath('SupTitle.dll')}")`)
    else out.push(`LoadPlugin("${pluginPath('vsfilter.dll')}")`)
    if (state.undot) out.push(`LoadPlugin("${pluginPath('UnDot.dll')}")`)

    // MP4/MOV 系走 LSMASHVideoSource（原版的 extInput 分支），其余交给 LWLibav
    const lsmash = ['.mp4', '.mov', '.qt', '.3gp', '.3g2'].includes(extOf(src))
    out.push(lsmash ? `LSMASHVideoSource("${src}")` : `LWLibavVideoSource("${src}")`)
    out.push('ConvertToYV12()')

    if (state.undot) out.push('Undot()')
    if (state.tweak) {
      out.push(
        `Tweak(${state.tweakChroma}, ${state.tweakSaturation}, ${state.tweakBrightness}, ${state.tweakContrast})`,
      )
    }
    if (state.levels) out.push(`Levels(0,${state.levelsValue},255,0,255)`)
    if (state.resize) out.push(`LanczosResize(${state.resizeWidth},${state.resizeHeight})`)
    if (state.sharpen) out.push(`Sharpen(${state.sharpenValue})`)
    if (state.crop && state.cropArgs.trim()) out.push(`Crop(${state.cropArgs.trim()})`)
    if (state.addBorders) out.push(`AddBorders(${state.borderArgs.join(',')})`)
    if (sub.trim()) {
      const e = extOf(sub)
      out.push(
        e === '.idx'
          ? `vobsub("${sub}")`
          : e === '.sup'
            ? `SupTitle("${sub}")`
            : `TextSub("${sub}")`,
      )
    }
    if (state.trim) out.push(`Trim(${state.trimStart},${state.trimEnd})`)

    setScript(`${out.join('\r\n')}\r\n`)
  }

  /** 改滤镜参数就重算脚本 —— 原版每个 ValueChanged 都挂在 GenerateAVS 上。 */
  const patchAndGenerate = (p: Partial<FilterState>) => {
    const next = { ...f, ...p }
    setF(next)
    generate(next)
  }

  const saveScript = async () => {
    let p = scriptPath
    if (!p) {
      p =
        (await pickSave('保存 AVS 脚本', 'script.avs', [{ name: 'AVS', extensions: ['avs'] }])) ?? ''
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

  const loadScript = async () => {
    const p = await pickFile('载入已有 AVS 脚本', [{ name: 'AVS', extensions: ['avs'] }])
    if (!p) return
    setScriptPath(p)
    try {
      setScript(await api.readTextFile(p))
    } catch (e) {
      notify(String(e), 'error')
    }
  }

  /** 预览：把脚本落成 .avs 再交给系统默认程序（需要有能吃 AVS 的播放器）。 */
  const preview = async () => {
    if (!script.trim()) {
      notify('请输入正确的 AVS 脚本！', 'error')
      return
    }
    const target = scriptPath || (source ? previewPathFor(source) : '')
    if (!target) {
      notify('请先指定脚本路径（或选一个源视频）', 'error')
      return
    }
    try {
      await api.writeTextFile(target, script)
      await api.openLocal(target)
    } catch (e) {
      notify(`预览失败：${String(e).replace(/^Error:\s*/, '')}`, 'error')
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
      const src = source || /[Ss]ource\("([A-Za-z]:\\[^"]+?\.\w+)"\)/.exec(script)?.[1] || ''
      const out =
        video.output || (await api.defaultAvsOutput(src || scriptPath)) || changeExt(scriptPath, '.mp4')
      if (!video.output) patchVideo({ output: out })
      const cmds = await api.planAvs(
        {
          script,
          scriptPath,
          source: src,
          withAudio,
          spec: { ...video, input: scriptPath, output: out },
        },
        audio,
      )
      await run(cmds, scriptPath)
    } catch (e) {
      notify(String(e).replace(/^Error:\s*/, ''), 'error')
    }
  }

  const insertPlugin = (name: string) => {
    setScript(`LoadPlugin("${pluginPath(name)}")\r\n${script}`)
    notify(`已插入 ${name}`)
  }

  const dllCount = avs.plugins.filter((p) => p.kind === 'filter').length
  const avsiCount = avs.plugins.filter((p) => p.kind === 'script').length

  return (
    <div className="flex h-full min-h-0 flex-wrap gap-3">
      {/* ---------------- 左：脚本 + 外置滤镜清单 ---------------- */}
      <div className="flex min-h-0 min-w-[400px] flex-1 flex-col gap-3">
        <GroupCard title="AVS 脚本" className="flex min-h-0 flex-1 flex-col">
          <Textarea
            value={script}
            onChange={(e) => setScript(e.target.value)}
            spellCheck={false}
            className="min-h-0 flex-1 resize-none text-[12.5px] leading-[1.65]"
          />
          <Row className="mt-2 flex-wrap gap-2">
            <Button size="sm" onClick={() => void saveScript()}>
              <Save className="size-3.5" />
              保存脚本
            </Button>
            <Button size="sm" variant="outline" onClick={() => void loadScript()}>
              <FileCode2 className="size-3.5" />
              载入脚本
            </Button>
            <Button size="sm" variant="outline" onClick={() => generate()}>
              <Wand2 className="size-3.5" />
              生成
            </Button>
            <Button size="sm" variant="outline" onClick={() => setScript('')}>
              <X className="size-3.5" />
              清空
            </Button>
            <Button size="sm" variant="outline" onClick={() => void preview()}>
              <Play className="size-3.5" />
              预览
            </Button>
          </Row>
        </GroupCard>

        {/* 已加载的外部滤镜 / 脚本：点一条就把 LoadPlugin 插到脚本开头 */}
        <GroupCard
          title="已加载的外部滤镜 / 脚本"
          className="flex h-[178px] shrink-0 flex-col"
          right={
            <span className="flex items-center gap-1.5">
              <Badge variant={avs.avisynth ? 'success' : 'destructive'}>
                {avs.avisynth ? 'AviSynth 就绪' : '缺少 AviSynth.dll'}
              </Badge>
              <Badge>
                {dllCount} 滤镜 / {avsiCount} 脚本
              </Badge>
            </span>
          }
        >
          <div
            className="mb-1.5 flex items-center gap-1.5 text-[11.5px] text-muted-foreground"
            title={avs.dir}
          >
            <FolderSearch className="size-3 shrink-0" />
            <span className="truncate">{avs.dir || '（未找到 avs/plugins 目录）'}</span>
          </div>
          <ListShell className="min-h-0 flex-1">
            {avs.plugins.length === 0 ? (
              <EmptyState title="没有找到外置滤镜" hint="把 .dll / .avsi 丢进上面的目录即可" />
            ) : (
              <div className="grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] gap-0.5 p-1">
                {avs.plugins.map((p) => (
                  <button
                    key={`${p.kind}:${p.name}`}
                    type="button"
                    onClick={() => p.kind === 'filter' && insertPlugin(p.name)}
                    title={
                      p.kind === 'filter'
                        ? `点击插入 LoadPlugin("${pluginPath(p.name)}")`
                        : 'AviSynth 会自动加载 .avsi/.avs 脚本，无需插入'
                    }
                    className="flex min-w-0 items-center gap-1.5 rounded-md px-1.5 py-[3px] text-left text-[12px] hover:bg-accent/60"
                  >
                    <span
                      className={`size-1.5 shrink-0 rounded-full ${
                        p.kind === 'filter' ? 'bg-primary' : 'bg-muted-foreground/50'
                      }`}
                    />
                    <span className="truncate">{p.name}</span>
                  </button>
                ))}
              </div>
            )}
          </ListShell>
        </GroupCard>
      </div>

      {/* ---------------- 右：源 / 滤镜面板 / 压制参数 ---------------- */}
      <div className="flex max-h-full w-[352px] min-w-[320px] flex-1 flex-col gap-3 overflow-y-auto pr-1">
        <Card className="space-y-2 p-3">
          <PathRow
            label="源视频"
            labelWidth={60}
            value={source}
            onChange={setSource}
            onDoubleClick={() => setSource('')}
            hint="双击清空；改完点「生成」会按它重建脚本"
            onBrowse={() => void pickFile('选择源视频文件').then((p) => p && setSource(p))}
            onDropFile={(paths) => setSource(paths[0])}
          />
          <PathRow
            label="字幕"
            labelWidth={60}
            value={subtitle}
            onChange={setSubtitle}
            onDoubleClick={() => setSubtitle('')}
            hint="双击清空；支持 .ass/.srt/.ssa/.idx/.sup"
            onBrowse={() =>
              void pickFile('选择字幕文件', [
                { name: '字幕', extensions: ['ass', 'srt', 'ssa', 'idx', 'sub', 'sup'] },
                { name: '所有文件', extensions: ['*'] },
              ]).then((p) => p && setSubtitle(p))
            }
            onDropFile={(paths) => setSubtitle(paths[0])}
          />
          <PathRow
            label="脚本路径"
            labelWidth={60}
            value={scriptPath}
            onChange={setScriptPath}
            onBrowse={() =>
              void pickSave('保存 AVS 脚本', 'script.avs', [
                { name: 'AVS', extensions: ['avs'] },
              ]).then((p) => p && setScriptPath(p))
            }
          />
          <PathRow
            label="输出"
            labelWidth={60}
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

        <GroupCard title="滤镜与处理">
          <div className="space-y-2">
            <Checkbox
              checked={f.undot}
              onCheckedChange={(v) => patchAndGenerate({ undot: v })}
              label="Undot 降噪"
            />

            <FilterLine
              checked={f.tweak}
              onChange={(v) => patchAndGenerate({ tweak: v })}
              label="Tweak"
            >
              <MinNum
                label="色度"
                value={f.tweakChroma}
                onChange={(v) => patchAndGenerate({ tweakChroma: v })}
              />
              <MinNum
                label="饱和"
                value={f.tweakSaturation}
                onChange={(v) => patchAndGenerate({ tweakSaturation: v })}
              />
              <MinNum
                label="亮度"
                value={f.tweakBrightness}
                onChange={(v) => patchAndGenerate({ tweakBrightness: v })}
              />
              <MinNum
                label="对比"
                value={f.tweakContrast}
                onChange={(v) => patchAndGenerate({ tweakContrast: v })}
              />
            </FilterLine>

            <FilterLine
              checked={f.levels}
              onChange={(v) => patchAndGenerate({ levels: v })}
              label="Levels 亮度"
            >
              <MinNum
                label="gamma"
                value={f.levelsValue}
                onChange={(v) => patchAndGenerate({ levelsValue: v })}
              />
            </FilterLine>

            <FilterLine
              checked={f.resize}
              onChange={(v) => patchAndGenerate({ resize: v })}
              label="LanczosResize"
            >
              <MinNum
                label="宽"
                value={f.resizeWidth}
                onChange={(v) => patchAndGenerate({ resizeWidth: Math.round(v) })}
              />
              <MinNum
                label="高"
                value={f.resizeHeight}
                onChange={(v) => patchAndGenerate({ resizeHeight: Math.round(v) })}
              />
            </FilterLine>

            <FilterLine
              checked={f.sharpen}
              onChange={(v) => patchAndGenerate({ sharpen: v })}
              label="Sharpen 锐化"
            >
              <MinNum
                label="强度"
                value={f.sharpenValue}
                onChange={(v) => patchAndGenerate({ sharpenValue: v })}
              />
            </FilterLine>

            <FilterLine
              checked={f.crop}
              onChange={(v) => patchAndGenerate({ crop: v })}
              label="Crop 裁剪"
            >
              <Input
                value={f.cropArgs}
                onChange={(e) => patchAndGenerate({ cropArgs: e.target.value })}
                placeholder="左,上,右,下"
                className="h-7 w-full text-[12px]"
              />
            </FilterLine>

            <FilterLine
              checked={f.addBorders}
              onChange={(v) => patchAndGenerate({ addBorders: v })}
              label="AddBorders 黑边"
            >
              {(['左', '上', '右', '下'] as const).map((label, i) => (
                <MinNum
                  key={label}
                  label={label}
                  value={f.borderArgs[i]}
                  onChange={(v) => {
                    const b = [...f.borderArgs] as [number, number, number, number]
                    b[i] = Math.round(v)
                    patchAndGenerate({ borderArgs: b })
                  }}
                />
              ))}
            </FilterLine>

            <FilterLine
              checked={f.trim}
              onChange={(v) => patchAndGenerate({ trim: v })}
              label="Trim 截取"
            >
              <MinNum
                label="起始帧"
                value={f.trimStart}
                onChange={(v) => patchAndGenerate({ trimStart: Math.round(v) })}
              />
              <MinNum
                label="结束帧"
                value={f.trimEnd}
                onChange={(v) => patchAndGenerate({ trimEnd: Math.round(v) })}
              />
            </FilterLine>
          </div>
        </GroupCard>

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
          <Checkbox
            checked={withAudio}
            onCheckedChange={setWithAudio}
            label="压制音频（从源视频抽音轨）"
          />
          <Checkbox
            checked={applyGlobal}
            onCheckedChange={setApplyGlobal}
            label="应用到常规压制全局"
          />
          <p className="mt-1 text-[11.5px] leading-relaxed text-muted-foreground">
            勾上之后，「视频压制」页的每个文件都会先过这份脚本：画面走这里的滤镜链，
            音轨仍从源文件抽（不会把声音弄丢）。
          </p>
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
          <Button
            variant="outline"
            size="sm"
            className="h-8"
            onClick={togglePause}
            disabled={!running}
          >
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

/** 一行「复选框 + 它自己的参数」。没勾上时参数区变灰，避免误改。 */
function FilterLine({
  checked,
  onChange,
  label,
  children,
}: {
  checked: boolean
  onChange: (v: boolean) => void
  label: string
  children: React.ReactNode
}) {
  return (
    <div className="space-y-1">
      <Checkbox checked={checked} onCheckedChange={onChange} label={label} />
      <div
        className={`flex flex-wrap items-center gap-1.5 pl-5 ${
          checked ? '' : 'pointer-events-none opacity-45'
        }`}
      >
        {children}
      </div>
    </div>
  )
}

/** 带小标签的窄数字框（一行里能塞下四个）。 */
function MinNum({
  label,
  value,
  onChange,
}: {
  label: string
  value: number
  onChange: (v: number) => void
}) {
  return (
    <label className="flex items-center gap-1 text-[11px] text-muted-foreground">
      <span className="shrink-0">{label}</span>
      <Input
        type="number"
        step="0.01"
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className="h-7 w-[68px] px-1.5 text-[12px]"
      />
    </label>
  )
}
