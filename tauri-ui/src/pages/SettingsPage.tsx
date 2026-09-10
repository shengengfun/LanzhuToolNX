import * as React from 'react'
import { CheckCircle2, FolderOpen, Image as ImageIcon, RefreshCw, XCircle } from 'lucide-react'
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
} from '~/components/ui'
import { PathRow } from '~/components/common'
import { ToolsFetch } from '~/components/ToolsFetch'
import { useApp, pickFile, pickFolder } from '~/state'
import * as api from '~/lib/api'
import { ACCENT_PRESETS } from '~/lib/appearance'
import { VIDEO_FORMATS } from '~/lib/types'
import { cn } from '~/lib/utils'

/** 这几个工具是硬依赖：缺了对应功能直接不可用。 */
const REQUIRED = [
  'ffmpeg.exe',
  'ffprobe.exe',
  'neroAacEnc.exe',
  'qaac.exe',
  'fdkaac.exe',
  'flac.exe',
  'refalac.exe',
  'mkvmerge.exe',
  'mkvextract.exe',
  'MP4Box.exe',
]

const THREADS = ['auto', '1', '2', '4', '6', '8', '12', '16', '24', '32']

/** 主题 id ↔ 界面文案。下拉框只能存字符串，所以两边手工映射。 */
const THEME_LABEL: Record<string, string> = {
  light: '亮色',
  dark: '暗色',
  system: '跟随系统',
}

export function SettingsPage() {
  const { settings, patchSettings, notify } = useApp()
  const [tools, setTools] = React.useState<string[]>([])
  const [actual, setActual] = React.useState('')
  const [busy, setBusy] = React.useState(false)

  const reload = React.useCallback(async () => {
    setBusy(true)
    try {
      setActual(await api.resolveToolsDir())
      setTools(await api.listBundledTools())
    } catch {
      setTools([])
    } finally {
      setBusy(false)
    }
  }, [])

  React.useEffect(() => {
    void reload()
  }, [reload])

  const lower = tools.map((t) => t.toLowerCase())
  const missing = REQUIRED.filter((r) => !lower.includes(r.toLowerCase()))

  return (
    <div className="flex h-full min-h-0 max-w-[880px] flex-col gap-3 overflow-y-auto px-0.5 pt-3 pr-1">
      {/* ---------- 路径与存储 ---------- */}
      <GroupCard title="路径与存储">
        <div className="space-y-2">
          <PathRow
            label="工具目录"
            labelWidth={64}
            value={settings.toolsDir}
            onChange={(v) => patchSettings({ toolsDir: v })}
            onBrowse={() =>
              void pickFolder('选择工具目录').then((p) => p && patchSettings({ toolsDir: p }))
            }
            placeholder="留空则自动查找 exe 同级的 tools/"
          />
          <PathRow
            label="输出目录"
            labelWidth={64}
            value={settings.outputDir}
            onChange={(v) => patchSettings({ outputDir: v })}
            onBrowse={() =>
              void pickFolder('选择默认输出目录').then((p) => p && patchSettings({ outputDir: p }))
            }
            placeholder="留空则输出到源文件旁边"
          />
          <Row className="gap-3 pl-[72px]">
            <Button size="sm" variant="outline" onClick={() => void reload()} disabled={busy}>
              <RefreshCw className={busy ? 'size-3.5 animate-spin' : 'size-3.5'} />
              重新检测
            </Button>
            <span className="min-w-0 flex-1 truncate text-[11.5px] text-muted-foreground">
              当前生效：<span className="font-mono">{actual || '—'}</span>
            </span>
          </Row>
        </div>
      </GroupCard>

      {/* ---------- 压制默认值 ---------- */}
      <GroupCard title="压制默认值">
        <div className="grid grid-cols-2 gap-x-4 gap-y-2">
          <Field label="压制格式" labelWidth={64}>
            <Select
              value={settings.defaultFormat}
              onValueChange={(v) => patchSettings({ defaultFormat: v })}
              options={VIDEO_FORMATS}
            />
          </Field>
          <Field label="线程数" labelWidth={64}>
            <Select
              value={settings.threadsDefault}
              onValueChange={(v) => patchSettings({ threadsDefault: v })}
              options={THREADS}
            />
          </Field>
        </div>
      </GroupCard>

      {/* ---------- 日志 ---------- */}
      <GroupCard title="日志">
        <div className="grid grid-cols-2 gap-x-4 gap-y-2">
          <Field label="行数上限" labelWidth={64}>
            <Input
              type="number"
              min={200}
              step={200}
              value={settings.maxLogLines}
              onChange={(e) => patchSettings({ maxLogLines: Number(e.target.value) || 4000 })}
            />
          </Field>
          <Field label="自动滚动" labelWidth={64}>
            <Select
              value={settings.autoScrollLog ? '开' : '关'}
              onValueChange={(v) => patchSettings({ autoScrollLog: v === '开' })}
              options={['开', '关']}
            />
          </Field>
        </div>
      </GroupCard>

      {/* ---------- 外观 ---------- */}
      <GroupCard title="外观">
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-x-4 gap-y-2">
            <Field label="主题" labelWidth={64}>
              <Select
                value={THEME_LABEL[settings.theme] ?? '跟随系统'}
                onValueChange={(v) =>
                  patchSettings({ theme: Object.entries(THEME_LABEL).find(([, l]) => l === v)?.[0] ?? 'system' })
                }
                options={Object.values(THEME_LABEL)}
              />
            </Field>
            <Field label="界面缩放" labelWidth={64}>
              <Select
                value={`${Math.round((settings.uiScale || 1) * 100)}%`}
                onValueChange={(v) => patchSettings({ uiScale: Number(v.replace('%', '')) / 100 })}
                options={['85%', '90%', '100%', '110%']}
              />
            </Field>
          </div>

          <Field label="强调色" labelWidth={64} align="start">
            <div className="flex flex-wrap items-center gap-2">
              {ACCENT_PRESETS.map((p) => {
                const active = !settings.accentCustom && settings.accent === p.id
                return (
                  <button
                    key={p.id}
                    type="button"
                    title={p.name}
                    aria-label={p.name}
                    onClick={() => patchSettings({ accent: p.id, accentCustom: '' })}
                    className={cn(
                      'size-7 rounded-full border-2 transition-transform hover:scale-110',
                      active
                        ? 'border-foreground ring-2 ring-foreground/20'
                        : 'border-border/60 hover:border-foreground/40',
                    )}
                    style={{ backgroundColor: p.color }}
                  />
                )
              })}
              <label
                title="自定义颜色"
                className={cn(
                  'relative flex size-7 cursor-pointer items-center justify-center overflow-hidden rounded-full border-2 border-dashed transition-transform hover:scale-110',
                  settings.accentCustom
                    ? 'border-foreground ring-2 ring-foreground/20'
                    : 'border-border/60 hover:border-foreground/40',
                )}
              >
                <input
                  type="color"
                  className="absolute inset-0 size-full cursor-pointer opacity-0"
                  value={settings.accentCustom || '#37b484'}
                  onChange={(e) => patchSettings({ accentCustom: e.target.value })}
                />
                <span className="text-[12px] font-bold text-muted-foreground">#</span>
              </label>
              {settings.accentCustom && (
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-7"
                  onClick={() => patchSettings({ accentCustom: '' })}
                >
                  清除自定义色
                </Button>
              )}
            </div>
          </Field>

          <Field label="背景图" labelWidth={64}>
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="outline"
                onClick={() =>
                  void pickFile('选择背景图', [
                    { name: '图片', extensions: ['png', 'jpg', 'jpeg', 'webp', 'bmp', 'gif'] },
                  ]).then((p) => p && patchSettings({ background: p }))
                }
              >
                <ImageIcon className="size-3.5" />
                选择图片
              </Button>
              {settings.background && (
                <>
                  <span className="min-w-0 flex-1 truncate font-mono text-[11px] text-muted-foreground">
                    {settings.background}
                  </span>
                  <Button
                    size="sm"
                    variant="ghost"
                    className="h-7"
                    onClick={() => patchSettings({ background: '' })}
                  >
                    移除
                  </Button>
                </>
              )}
            </div>
          </Field>
        </div>
      </GroupCard>

      {/* ---------- 窗口与提醒 ---------- */}
      <GroupCard title="窗口与托盘">
        <div className="grid grid-cols-2 gap-x-4 gap-y-2">
          <Checkbox
            checked={settings.closeToTray}
            onCheckedChange={(v) => patchSettings({ closeToTray: v })}
            label="点关闭时收进托盘（不退出）"
          />
          <Checkbox
            checked={settings.minimizeToTray}
            onCheckedChange={(v) => patchSettings({ minimizeToTray: v })}
            label="最小化时收进托盘"
          />
          <Checkbox
            checked={settings.notifyOnFinish}
            onCheckedChange={(v) => patchSettings({ notifyOnFinish: v })}
            label="任务完成时弹系统通知"
          />
          <Checkbox
            checked={settings.showMonitor}
            onCheckedChange={(v) => patchSettings({ showMonitor: v })}
            label="底栏显示 CPU / GPU / 内存"
          />
        </div>
        <Row className="mt-2 gap-2">
          <Button size="sm" variant="outline" onClick={() => void api.hideToTray()}>
            立即隐藏到托盘
          </Button>
          <span className="text-[11.5px] text-muted-foreground">
            托盘图标：左键单击唤回窗口，右键出菜单。
          </span>
        </Row>
      </GroupCard>

      {/* ---------- 界面 ---------- */}
      <GroupCard title="语言">
        <Field label="界面语言" labelWidth={64}>
          <Select
            value={settings.language}
            onValueChange={(v) => patchSettings({ language: v })}
            options={['zh-CN']}
          />
        </Field>
      </GroupCard>

      {/* ---------- 工具 ---------- */}
      <ToolsFetch onChanged={() => void reload()} />

      <GroupCard
        title="工具体检"
        right={
          <Badge variant={missing.length === 0 ? 'success' : 'destructive'}>
            {tools.length} 个 / 缺 {missing.length}
          </Badge>
        }
      >
        <div className="grid grid-cols-2 gap-x-4 gap-y-1">
          {REQUIRED.map((r) => {
            const ok = lower.includes(r.toLowerCase())
            return (
              <Row key={r} className="gap-1.5 py-0.5">
                {ok ? (
                  <CheckCircle2 className="size-3.5 shrink-0 text-primary" />
                ) : (
                  <XCircle className="size-3.5 shrink-0 text-destructive" />
                )}
                <span className="font-mono text-[11.5px]">{r}</span>
                {!ok && <span className="text-[11px] text-destructive">缺失</span>}
              </Row>
            )
          })}
        </div>
        <Row className="mt-3 gap-2">
          <Button
            size="sm"
            variant="outline"
            onClick={() =>
              navigator.clipboard.writeText(tools.join('\n')).then(
                () => notify('工具清单已复制'),
                () => void 0,
              )
            }
            disabled={tools.length === 0}
          >
            <FolderOpen className="size-3.5" />
            复制工具清单
          </Button>
        </Row>
      </GroupCard>

      <Card className="p-3 text-[11.5px] leading-relaxed text-muted-foreground">
        设置保存在 <span className="font-mono">%APPDATA%\LanzhuTool\settings.json</span>，修改后立即生效。
      </Card>
    </div>
  )
}
