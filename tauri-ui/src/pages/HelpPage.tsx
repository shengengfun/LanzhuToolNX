import * as React from 'react'
import { Dices, ExternalLink, Github, Info, Loader2 } from 'lucide-react'
import { Badge, Button, Card, GroupCard, Row } from '~/components/ui'
import { useApp } from '~/state'
import * as api from '~/lib/api'
import type { Meme } from '~/lib/types'
import logo from '~/assets/logo.png'

export function HelpPage() {
  return (
    <div className="flex h-full min-h-0 max-w-[880px] flex-col gap-3 overflow-y-auto px-0.5 pt-1 pr-1">
      <Card className="p-4">
        <Row className="gap-3">
          <img
            src={logo}
            alt="岚珠工具箱"
            draggable={false}
            className="size-14 shrink-0 rounded-2xl object-cover shadow-sm ring-1 ring-border/60"
          />
          <div>
            <div className="flex items-center gap-2">
              <span className="text-[16px] tracking-wide">
                <span className="font-bold text-primary">岚珠</span>
                <span className="font-semibold text-foreground">工具箱</span>
              </span>
              <Badge>v{__APP_VERSION__}</Badge>
            </div>
            <div className="mt-0.5 text-[12px] text-muted-foreground">
              动画 / 番剧压制的瑞士军刀：压制、转码、剪切、封装、抽取一条龙。
            </div>
          </div>
        </Row>

        <div className="mt-3 space-y-1.5 text-[12px] leading-relaxed text-muted-foreground">
          <p>
            本工具是<b className="text-foreground">原 WinForms 版的重构</b>
            ：界面换成了 Tauri + React，但压制逻辑逐条对齐原版的命令行模板，
            所以出来的片子、参数习惯都和以前完全一致。
          </p>
          <p>
            所有编码参数最终都会拼成 ffmpeg / x264 / qaac 等工具的命令行，
            日志区里能直接看到将要执行的完整命令 —— 不存在"界面一套、实际另一套"。
          </p>
        </div>

        <div className="mt-3 grid grid-cols-2 gap-x-4 gap-y-1 text-[11.5px] text-muted-foreground sm:grid-cols-3">
          <span>· 视频压制：CRF / 2PASS / 自定义参数，支持 NVENC·QSV·AMF</span>
          <span>· 批量压制与同名歌词自动内嵌</span>
          <span>· 音频转码：NeroAAC / QAAC / FDK / FLAC / WAV / AC3</span>
          <span>· 波形粗剪：拖手柄划范围，流复制秒切</span>
          <span>· 视频粗剪：带预览的快速掐头去尾</span>
          <span>· 封装 / 封装转换：多音轨、批量换容器</span>
          <span>· 抽取轨道、AVS 脚本压制、MediaInfo 查看</span>
          <span>· 插帧超分：RIFE / Real-ESRGAN / Real-CUGAN 本地模型（Vulkan，无需 CUDA）</span>
          <span>· 13 套虹咲角色配色 + 自定义配色与背景图</span>
          <span>· 实时 CPU / GPU / 内存监控与托盘常驻</span>
        </div>
      </Card>

      <GroupCard title="更新日志">
        <div className="space-y-3 text-[12px] leading-relaxed">
          <Changelog
            version="v1.3.7"
            items={[
              '新：进度改画在 Windows 任务栏图标上（绿色进度条，暂停变黄、失败变红）；底栏那条横贯整屏的长条撤掉了，只留一句当前文件与百分比',
              '新：AVS 页补齐原版功能 —— 源视频 / 字幕 / 输出、生成 / 清空 / 预览，八组滤镜（Undot · Tweak · Levels · LanczosResize · Sharpen · Crop · AddBorders · Trim）一键出脚本',
              '新：AVS 页底下多了「已加载的外部滤镜 / 脚本」清单（点 dll 即插入 LoadPlugin），以及「应用到常规压制全局」开关',
              '新：托盘模式 —— 不勾时点 ✕ 直接退出；勾上后关窗 / 最小化都收进任务栏托盘',
              '改：记住上次用的参数收窄到视频 / 音频 / AVS / 插帧超分的参数与上次停留的页面；压制 / 封装 / 抽取不再记上次的文件',
              '修：即开即用 —— 启动画面读完设置就退场（不再硬停 550ms），工具目录体检挪到界面出来之后',
              '修：设置 → 窗口只留托盘模式 / 底栏监控 / 完成通知三个开关，去掉多余的「立即隐藏到托盘」按钮',
            ]}
          />
          <Changelog
            version="v1.3.6"
            items={[
              '修：压制过程中的实时进度以前一个字都不刷新（ffmpeg 的进度行以 \\r 结尾，被当成“没换行”）。现在日志里能看到 frame= 一路刷过，底栏与「任务」页签同步显示百分比 / 当前文件 / 编码速度',
              '新：Windows 任务栏图标上显示压制进度（暂停、失败也各有颜色），不用切回窗口就知道跑没跑完',
              '新：记住上次用的参数 —— 视频 / 音频 / 封装 / 抽取 / AVS / 插帧超分的参数与路径、以及上次停留的页面，重开就恢复',
              '修：部分下拉框文字被挤成“H....”（下拉框字号与其它控件对齐，同屏不再一大一小）',
              '修：字幕框双击清空回来了；换视频时会重新找同名字幕，找不到就清空',
              '修：启动画面不再谎称“正在准备工具链”，改成打开就用、读完设置就退场',
            ]}
          />
          <Changelog
            version="v1.3.5"
            items={[
              '新：插帧超分页（在 AVS 之后）。两套引擎：ffmpeg 内置滤镜（minterpolate / lanczos / xBR / hqx，不装任何东西就能用）与本地模型（RIFE 插帧、Real-ESRGAN / Real-CUGAN 超分）',
              '本地模型走 ncnn + Vulkan，核显也能跑，不需要 CUDA / Python / PyTorch',
              '设置 → 工具获取新增三个模型包；RIFE 官方包自带 11 个模型共 411MB，安装后自动只留 rife-v4.6 与 rife-anime',
            ]}
          />
          <Changelog
            version="v1.3.4"
            items={[
              '修：找回了自动输出文件名 —— 压制不再把源文件覆盖掉（1.mp4 → 1_h264.mp4，重名自动退到 1_new_file(1)_h264.mp4）',
              '修：音频页恢复自动填输出名（_AAC.mp4 / _WAV.wav / _FLAC.flac 等）；封装页 _Mux.mp4、AVS _AVS.mp4、一图流 _SP.mp4',
              '应用图标换成圆角方形（同 RinaDown 风格），安装包向导顶栏一起换了',
            ]}
          />
          <Changelog
            version="v1.3.3"
            items={[
              '修：1.3.2 里所有执行按钮都会报 invalid args（任务根本发不出去）',
              '修：自动输出名会丢掉一个反斜杠，生成 D:\\Trans片名.mp4 这种粘连路径',
              '外观：自定义主题色的 R/G/B 滑条改成渐变轨道，拖动前就能看出会变成什么颜色',
            ]}
          />
          <Changelog
            version="v1.3.2"
            items={[
              '安装目录改成纯英文的 %LOCALAPPDATA%\\lanzhutool（开始菜单和控制面板里依旧是「岚珠工具箱」）',
              '安装包套上了 app logo：setup.exe 的图标、安装向导顶栏的小图',
            ]}
          />
          <Changelog
            version="v1.3.1"
            items={[
              '配色选择重做：选中色块内打对号（对号颜色按底色亮度现算），去掉「强调色」标签',
              '自定义颜色改为 R/G/B 三根调整条 + HTML 色号输入，两者双向同步',
            ]}
          />
          <Changelog
            version="v1.3.0"
            items={[
              '新增「压制预设」：内置 18 条（ProRes / DNxHR / AV1 / VP9 / FFV1 / MPEG-2 …）+ 可自建，能按源分辨率推荐',
              '应用图标换成新 logo，标题栏「岚珠」二字跟随主题色',
              '关窗口（✕）收回托盘；托盘右键菜单新增暂停/终止任务、打开输出/工具目录',
              '日志：不再限制行数，改为可选记录范围（全部 / 警告↑ / 仅错误）',
              '设置：新增「重置」（可只重置外观），托盘选项精简',
              '修：拖入视频时闪命令提示符（ffprobe 没加 CREATE_NO_WINDOW）',
              '修：底栏 CPU 使用率显示到小数点后十几位',
            ]}
          />
          <Changelog
            version="v1.2.0"
            items={[
              '外观：虹咲 13 人应援色配色 + 任意 HTML 色号自定义，默认钟岚珠',
              '外观：自定义背景图、界面缩放、启动画面（可跳过）',
              '封装：多音轨导入、替换/保留源音轨、可选输出容器',
              '封装：重新带回「封装转换」（批量换容器，必要时把音频转 AAC）',
              '压制模式改名 CRF / 2PASS，且只在自定义参数模式显示参数框',
              '音频页改成「转码 / 波形粗剪」两个页签，修掉布局挤压',
              '设置：默认值合并成一行；托盘选项精简为最小化/监控/通知三项',
              '帮助页新增介绍、更新日志，以及一条可以换的烂梗',
            ]}
          />
          <Changelog
            version="v1.1.0"
            items={[
              '换壳：Tauri 2 + React 19 + Tailwind v4（原 WinForms 版保留在仓库里）',
              '视频页：自动输出文件名、自动匹配同名字幕、预计大小随参数实时变化',
              '常用：带预览的视频粗剪；音频：波形粗剪',
              '状态栏：就绪灯 + 总体进度 + CPU/GPU/内存监控',
              '设置：外观（主题/强调色/背景）、托盘、系统通知',
              'MediaInfo：最近打开 10 个文件',
            ]}
          />
          <Changelog
            version="v1.0.x"
            items={[
              'WinForms 版：现代 UI 外壳、DPI 感知、文字自适应、自绘滚动条',
              '.bat 写入改为 UTF-8，修好中日文路径乱码',
            ]}
          />
        </div>
      </GroupCard>

      <MemeCard />

      <Card className="flex items-center gap-3 p-3">
        <Info className="size-4 shrink-0 text-muted-foreground" />
        <span className="text-[12px] text-muted-foreground">项目主页</span>
        <span className="flex-1" />
        <Row className="gap-1.5">
          <Github className="size-3.5 text-muted-foreground" />
          <span className="text-[11.5px] text-muted-foreground">shengengfun/LanzhuToolNX</span>
          <ExternalLink className="size-3 text-muted-foreground" />
        </Row>
      </Card>
    </div>
  )
}

function Changelog({ version, items }: { version: string; items: string[] }) {
  return (
    <div>
      <div className="flex items-center gap-2">
        <Badge variant="accent">{version}</Badge>
      </div>
      <ul className="mt-1 space-y-0.5 text-muted-foreground">
        {items.map((t) => (
          <li key={t}>· {t}</li>
        ))}
      </ul>
    </div>
  )
}

/**
 * 彩蛋：随机烂梗。
 *
 * 内容从 sb6657.cn 随机抓，抓不到就用内置文案（界面会标明来源）。
 * 抓取在 Rust 侧做（ureq，不需要给前端开任何网络权限）。
 */
function MemeCard() {
  const { settings } = useApp()
  const [meme, setMeme] = React.useState<Meme | null>(null)
  const [busy, setBusy] = React.useState(false)

  const roll = React.useCallback(async () => {
    setBusy(true)
    try {
      setMeme(await api.randomMeme(settings.memeUrl))
    } catch {
      setMeme(null)
    } finally {
      setBusy(false)
    }
  }, [settings.memeUrl])

  React.useEffect(() => {
    void roll()
  }, [roll])

  return (
    <Card className="flex items-center gap-3 p-3">
      <Dices className="size-4 shrink-0 text-primary" />
      <div className="min-w-0 flex-1">
        <div className="flex items-baseline gap-1.5">
          <span className="text-[11.5px] text-muted-foreground">今日烂梗</span>
          {meme && (
            <span className="text-[10.5px] text-muted-foreground/70">
              {meme.fallback ? '（网络不通，用的内置文案）' : `（${meme.source}）`}
            </span>
          )}
        </div>
        <div className="mt-0.5 truncate text-[12.5px]" title={meme?.text}>
          {busy && !meme ? '正在偷梗…' : (meme?.text ?? '——')}
        </div>
      </div>
      <Button size="sm" variant="outline" onClick={() => void roll()} disabled={busy}>
        {busy ? <Loader2 className="size-3.5 animate-spin" /> : <Dices className="size-3.5" />}
        换一换
      </Button>
    </Card>
  )
}
