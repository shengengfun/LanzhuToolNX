import * as React from 'react'
import { CheckCircle2, FolderOpen, Image as ImageIcon, RefreshCw, XCircle } from 'lucide-react'
import {
  Badge,
  Button,
  Card,
  Checkbox,
  Field,
  GroupCard,
  Row,
  Select,
} from '~/components/ui'
import { PathRow } from '~/components/common'
import { ToolsFetch } from '~/components/ToolsFetch'
import { useApp, pickFile, pickFolder, DEFAULT_SETTINGS } from '~/state'
import * as api from '~/lib/api'
import { ACCENT_PRESETS, accentHex } from '~/lib/appearance'
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

/** 日志记录范围：全部 / 警告以上 / 仅错误。 */
const LOG_LEVEL_LABEL: Record<string, string> = {
  all: '全部',
  warn: '警告↑',
  error: '仅错误',
}

/**
 * 直接填 HTML 色号的输入框。
 *
 * 输入过程中不校验（不然打一半就被吞了），失焦或回车时才判断：
 * 合法（#rgb / #rrggbb）就提交，非法就退回原值。
 */
function HexInput({
  value,
  fallback,
  onCommit,
}: {
  value: string
  /** 输入框为空时展示的占位色号（= 当前配色），让用户知道默认是什么 */
  fallback: string
  onCommit: (v: string) => void
}) {
  const [text, setText] = React.useState(value)
  React.useEffect(() => setText(value), [value])

  const commit = () => {
    const t = text.trim()
    if (/^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(t)) {
      onCommit(t.toUpperCase())
    } else if (!t) {
      onCommit('')
    } else {
      setText(value) // 非法输入：退回原值，别把设置改坏
    }
  }

  return (
    <input
      value={text}
      onChange={(e) => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => e.key === 'Enter' && commit()}
      spellCheck={false}
      placeholder={fallback}
      title="填 HTML 色号，回车生效"
      className="h-7 w-[104px] rounded-lg border border-input/60 bg-muted/40 px-2 font-mono text-[12px] uppercase transition-colors focus-visible:border-primary focus-visible:bg-card focus-visible:outline-none"
    />
  )
}

export function SettingsPage() {
  const { settings, patchSettings, notify } = useApp()
  const [tools, setTools] = React.useState<string[]>([])
  const [actual, setActual] = React.useState('')
  const [busy, setBusy] = React.useState(false)
  /** 「重置所有设置」的两步确认 */
  const [confirmReset, setConfirmReset] = React.useState(false)

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

      {/* ---------- 默认值（压制 + 日志合并成一行，省掉大片留白） ---------- */}
      <GroupCard title="默认值">
        <div className="grid grid-cols-4 gap-x-3 gap-y-2">
          <Field label="格式" labelWidth={44}>
            <Select
              value={settings.defaultFormat}
              onValueChange={(v) => patchSettings({ defaultFormat: v })}
              options={VIDEO_FORMATS}
            />
          </Field>
          <Field label="线程" labelWidth={44}>
            <Select
              value={settings.threadsDefault}
              onValueChange={(v) => patchSettings({ threadsDefault: v })}
              options={THREADS}
            />
          </Field>
          <Field label="记录范围" labelWidth={56}>
            <Select
              value={LOG_LEVEL_LABEL[settings.logLevel] ?? '全部'}
              onValueChange={(v) =>
                patchSettings({
                  logLevel: Object.entries(LOG_LEVEL_LABEL).find(([, l]) => l === v)?.[0] ?? 'all',
                })
              }
              options={Object.values(LOG_LEVEL_LABEL)}
            />
          </Field>
          <Field label="自动滚动" labelWidth={56}>
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
          <div className="grid grid-cols-3 gap-x-3 gap-y-2">
            <Field label="主题" labelWidth={44}>
              <Select
                value={THEME_LABEL[settings.theme] ?? '跟随系统'}
                onValueChange={(v) =>
                  patchSettings({ theme: Object.entries(THEME_LABEL).find(([, l]) => l === v)?.[0] ?? 'system' })
                }
                options={Object.values(THEME_LABEL)}
              />
            </Field>
            <Field label="缩放" labelWidth={44}>
              <Select
                value={`${Math.round((settings.uiScale || 1) * 100)}%`}
                onValueChange={(v) => patchSettings({ uiScale: Number(v.replace('%', '')) / 100 })}
                options={['85%', '90%', '100%', '110%']}
              />
            </Field>
            <Field label="启动画面" labelWidth={56}>
              <Select
                value={settings.showSplash ? '显示' : '跳过'}
                onValueChange={(v) => patchSettings({ showSplash: v === '显示' })}
                options={['显示', '跳过']}
              />
            </Field>
          </div>

          <Field label="强调色" labelWidth={64} align="start">
            <div className="space-y-2">
              {/* 虹咲 13 人的应援色。鼠标悬停能看到是谁的颜色。 */}
              <div className="flex flex-wrap items-center gap-1.5">
                {ACCENT_PRESETS.map((p) => {
                  const active = !settings.accentCustom && settings.accent === p.id
                  return (
                    <button
                      key={p.id}
                      type="button"
                      title={`${p.name} ${p.color}`}
                      aria-label={p.name}
                      onClick={() => patchSettings({ accent: p.id, accentCustom: '' })}
                      className={cn(
                        'flex size-7 items-center justify-center rounded-full border-2 transition-transform hover:scale-110',
                        active
                          ? 'border-foreground ring-2 ring-foreground/20'
                          : 'border-border/60 hover:border-foreground/40',
                      )}
                      style={{ backgroundColor: p.color }}
                    />
                  )
                })}
              </div>

              <div className="flex flex-wrap items-center gap-2">
                <label
                  title="用取色器挑一个"
                  className={cn(
                    'relative flex size-7 shrink-0 cursor-pointer items-center justify-center overflow-hidden rounded-full border-2 border-dashed transition-transform hover:scale-110',
                    settings.accentCustom
                      ? 'border-foreground ring-2 ring-foreground/20'
                      : 'border-border/60 hover:border-foreground/40',
                  )}
                >
                  <input
                    type="color"
                    className="absolute inset-0 size-full cursor-pointer opacity-0"
                    value={settings.accentCustom || accentHex('#F69992')}
                    onChange={(e) => patchSettings({ accentCustom: e.target.value })}
                  />
                  <span className="text-[12px] font-bold text-muted-foreground">#</span>
                </label>

                {/* 也可以直接填 HTML 色号 */}
                <HexInput
                  value={settings.accentCustom}
                  fallback={
                    ACCENT_PRESETS.find((p) => p.id === settings.accent)?.color ?? '#F69992'
                  }
                  onCommit={(v) => patchSettings({ accentCustom: v })}
                />

                {settings.accentCustom && (
                  <Button
                    size="sm"
                    variant="ghost"
                    className="h-7"
                    onClick={() => patchSettings({ accentCustom: '' })}
                  >
                    恢复默认
                  </Button>
                )}
                {!settings.accentCustom && (
                  <span className="text-[11.5px] text-muted-foreground">
                    当前：{ACCENT_PRESETS.find((p) => p.id === settings.accent)?.name ?? '钟岚珠'}
                  </span>
                )}
              </div>
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

          {/* 烂梗接口留个口子：站点换域名/换接口时用户能自己填，
              不用等版本更新（默认留空 = 内置候选列表） */}
          <Field label="烂梗接口" labelWidth={64}>
            <div className="flex items-center gap-2">
              <input
                value={settings.memeUrl}
                onChange={(e) => patchSettings({ memeUrl: e.target.value })}
                spellCheck={false}
                placeholder="留空 = 自动试 sb6657.cn 的几个常见接口"
                className="h-7 min-w-0 flex-1 rounded-lg border border-input/60 bg-muted/40 px-2 font-mono text-[11.5px] transition-colors focus-visible:border-primary focus-visible:bg-card focus-visible:outline-none"
              />
              <span className="shrink-0 text-[11px] text-muted-foreground">帮助页彩蛋用</span>
            </div>
          </Field>
        </div>
      </GroupCard>

      {/* ---------- 窗口与提醒 ---------- */}
      <GroupCard title="窗口与提醒">
        {/* 只留真正会用到的三项。原来那个「立即隐藏到托盘」按钮 +
            「点关闭时收进托盘」纯属多余（前者是命令不是设置，
            后者会让用户找不到关闭按钮），已经移除。 */}
        <div className="grid grid-cols-3 gap-x-4 gap-y-2">
          <Checkbox
            checked={settings.minimizeToTray}
            onCheckedChange={(v) => patchSettings({ minimizeToTray: v })}
            label="最小化时收进托盘"
          />
          <Checkbox
            checked={settings.showMonitor}
            onCheckedChange={(v) => patchSettings({ showMonitor: v })}
            label="底栏显示 CPU / GPU 内存"
          />
          <Checkbox
            checked={settings.notifyOnFinish}
            onCheckedChange={(v) => patchSettings({ notifyOnFinish: v })}
            label="任务完成时弹系统通知"
          />
        </div>
        <p className="mt-2 text-[11.5px] text-muted-foreground">
          托盘图标：左键单击唤回窗口，右键出菜单（显示 / 隐藏 / 暂停任务 / 终止任务 /
          打开输出目录 / 打开工具目录 / 退出）。
          <br />
          点窗口右上角的 ✕ 会<b className="text-foreground">收回托盘</b>而不是退出；
          真要退出请用托盘菜单里那一条。
        </p>
      </GroupCard>

      {/* ---------- 重置 ---------- */}
      <GroupCard title="重置">
        <Row className="flex-wrap items-center gap-2">
          <Button
            size="sm"
            variant="outline"
            onClick={() => {
              patchSettings({
                theme: 'system',
                accent: 'lanzhu',
                accentCustom: '',
                background: '',
                uiScale: 1,
              })
              notify('外观已重置')
            }}
          >
            重置外观
          </Button>

          {/* 两步确认：第一次点击只是变个文案，避免误点就把所有设置清掉 */}
          <Button
            size="sm"
            variant={confirmReset ? 'destructive' : 'outline'}
            onClick={() => {
              if (!confirmReset) {
                setConfirmReset(true)
                window.setTimeout(() => setConfirmReset(false), 4000)
                return
              }
              setConfirmReset(false)
              patchSettings({
                ...DEFAULT_SETTINGS,
                // 路径与镜像不算"设置项"，清了还得重填，没必要
                toolsDir: settings.toolsDir,
                outputDir: settings.outputDir,
                language: settings.language,
                mirrors: settings.mirrors,
              })
              notify('设置已恢复到初始状态')
            }}
          >
            {confirmReset ? '再点一次确认重置' : '重置所有设置'}
          </Button>

          <span className="text-[11.5px] text-muted-foreground">
            重置不会动工具目录 / 输出目录 / 下载镜像
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
