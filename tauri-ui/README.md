# 岚珠工具箱 · Tauri UI 重构

把原来 WinForms 的「岚珠工具箱」界面用 **Tauri 2 + React + Tailwind v4** 重做，
压制逻辑按**原版命令行模板逐条对齐**，以保证功能一致。

```
tauri-ui/
├─ src/                       前端（React + Tailwind v4）
│  ├─ globals.css             ① 设计令牌层（对应参考项目 ShiorikoTrans 的 globals.css）
│  ├─ components/ui.tsx       ③ 外观层：variant × size 矩阵 + 全部原语组件
│  ├─ components/             TitleBar / Sidebar / OutputPanel / StatusBar / common
│  ├─ pages/                  9 个页面
│  ├─ lib/                    api.ts（后端调用）/ types.ts / presets.ts / utils.ts
│  └─ state.tsx               全局状态 + 运行事件订阅
└─ src-tauri/                 后端（Rust）
   └─ src/
      ├─ cmd.rs               ★ 命令行拼装，逐字镜像原版 MainForm.cs
      ├─ run.rs               写 .bat → cmd 执行 → 流式回传输出
      ├─ probe.rs             ffprobe 媒体探测
      ├─ tools.rs             工具目录解析（递归找 exe，不污染工作目录）
      ├─ settings.rs          设置持久化
      └─ spec.rs              前后端共用的数据结构
```

---

## 一、怎么跑

```bash
cd tauri-ui
npm install          # 已配好 npmmirror 镜像（.npmrc）
npm run tauri dev    # 开发模式
npm run tauri build  # 出安装包（NSIS）
```

工具链要求：Node 18+、Rust 1.77+。

**工具目录**：程序会自动按这个顺序找 `ffmpeg.exe` 等：
设置里的目录 → 程序同级 `tools/` → 上级 `tools/` → 开发期仓库目录。
找到的 exe 会**按名字递归索引**，所以 `tools/video/ffmpeg/ffmpeg.exe` 这种嵌套结构能直接用，
**不需要**再跑原版的 `organize_tools.ps1` 去平铺。

---

## 二、功能对照：哪些是 1:1 搬过来的

核心原则：**原版最终就是把一段命令字符串写成 .bat 交给 cmd 跑**，
所以只要命令行一致，行为就一致。`cmd.rs` 里的每个函数都标注了它镜像的原版方法：

| 原版 `MainForm.cs` | 本项目 `cmd.rs` | 说明 |
|---|---|---|
| `ffmuxbat()` | `ffmuxbat()` | 视频+音频 copy 封装 |
| `BuildFfmpegVideoCommand()` | `build_video()` | **核心**：GPU/滤镜链/质量/二遍全在内 |
| `x264bat()` / `x265bat()` | 同上 | 原版本身就是 `BuildFfmpegVideoCommand` 的薄封装 |
| `audiobat()` | `audiobat()` | 7 种编码器，含 `ffmpeg → pcm_s16le → 编码器` 管道 |
| `extractAudio()` | `extract_audio()` | `-c:a copy` 无损抽取 |
| `VideoBatch()` | `video_pipeline()` | 完整链路：抽音频→压视频→封装→删临时文件 |
| `btnmux_Click()` | `mux()` | 裸流补 `-r` 与 `b sf metadata` |
| `ExtractAV()` / `ExtractTrack()` | `extract()` | 抽视频轨/音频轨/指定流/mkvextract |
| `preset.xml` | `src/lib/presets.ts` | x264 与音频预设原样搬运 |

**逐字保留的细节**（这些最容易在重写时丢掉）：

- `-b:v {n}k` 里数字的格式化：`23.5` 保持小数，`800.0` 输出 `800`
- GPU 模式**绝不能加 `-pix_fmt`** —— 硬解输出的是硬件帧，auto_scale 会失败并返回 `-40`
- 滤镜链顺序：`hwdownload` → `format=...` → `scale` / `subtitles` → `hwupload[_cuda]`，
  且用 `hwHeadCount` 判断是否需要 upload
- `EscapeFfmpegFilterPath`：subtitles 滤镜里盘符的 `:` 必须转义成 `\:`，单引号也要转义
- `getAudioExt()` 的 7 个映射（NeroAAC→`.mp4`、QAAC→`.m4a`、FDKAAC→`.m4a` …）
- 没有音轨时**强制** audioMode=1，并且视频直接落最终文件、跳过封装
- 每压完一个文件 `echo ===== one file is completed! =====`，
  本项目把它当作**真实进度信号**（`run://progress` 事件）
- `.bat` 以 UTF-8 无 BOM 写入 + 开头 `chcp 65001`，保证中日文路径不乱码

---

## 三、⚠️ 需要你验证的部分

我无法在本地跑真实压制（没有样片，一次压制要几分钟），所以**下面这些是按原版代码还原但我没能实测**的：

1. **`常用`页三个小工具**（单图转视频 / 黑帧 / 音频转码）——
   原版这几个 handler 我没有逐行读到，命令行是**按 `InitParameter()` 里的默认值还原**的
   （`OnePicFPSNum=1`、`OnePicCRFNum=24`、`BlackCRFNum=51`、`BlackBitrateNum=900`）。
   如果和老版输出不一致，以老版为准改 `MiscPage.tsx` 里的模板即可。
2. **`抽取`页的视频轨抽取命令** —— 原版这段实现被注释掉了，生效的是 `ExtractAV(...)`，
   我按保留下来的注释形态还原为 `ffmpeg -an -sn -c:v:0 copy`。
3. **批量压制的输出命名** —— 按 `<输出目录>\<文件名>_<格式后缀><扩展名>` 还原
   （后缀 hevc/mov/flv/h264）。
4. **`分离器`下拉** —— 界面有，但原版这个选项对命令行的影响我没查到，当前固定 `auto`。

**验证方法**：每个页面都有「命令预览」，点「预演」会显示将要执行的完整命令行。
**把它和老版正在跑的命令对照即可** —— 这是本项目刻意做的可验证性设计，
比"直接跑然后看结果不对"高效得多。

---

## 三点五、工具下载（设置页 → 工具获取）

不需要自己找工具。设置页里可以：

- **在线下载**：列出每个工具包的体积与安装状态，勾选后下载。
- **国内加速**：每个 GitHub 地址都会先套一遍加速前缀（默认 ghfast / gh-proxy /
  ghproxy.net / gh.llkk，可在界面上编辑），全部失败才回落直连。
  非 GitHub 的源（如 mkvtoolnix.download）不套镜像。
- **离线包**：「导出离线包」把当前 tools/ 打成一个 zip
  （自动排除 200MB+ 且本程序从不使用的 `ffplay.exe`），
  拷到别的机器用「从离线包导入」或「从文件夹导入」即可。

### ⚠️ 实测发现原项目自带的更新脚本已经坏了

`update_tools.ps1` 里两个地址**现在都是 404**：

| 脚本里的地址 | 实测 | 原因 |
|---|---|---|
| `github.com/gpac/gpac/releases/download/v2.5-DEV/...` | ❌ 404 | GPAC 新版 release 已不再挂 Windows 二进制（`assets=0`）|
| `github.com/mbunkus/mkvtoolnix/releases/download/release-82.0/...` | ❌ 404 | tag 命名变了 |

当前清单里的地址**全部经过 HTTP HEAD 实测**：

| 工具 | 地址 | 实测 |
|---|---|---|
| FFmpeg | `BtbN/FFmpeg-Builds` → `ffmpeg-master-latest-win64-gpl.zip` | ✅ 185 MB |
| FFmpeg（备用）| `www.gyan.dev/ffmpeg/builds/ffmpeg-release-full.7z` | ✅ 跳转 9.0.1 |
| MKVToolNix | `mkvtoolnix.download/windows/releases/92.0/...7z` | ✅ 官方域名 |
| QAAC | `nu774/qaac` → `qaac_3.07.zip` | ✅ 6.4 MB |
| FLAC | `xiph/flac` → `flac-1.5.0-win.zip` | ✅ 1.3 MB |

**没有稳定直链、只能离线导入的**：MP4Box(GPAC)、FDK-AAC（上游无二进制）、NeroAAC
（官网要走下载表单）。这三个在界面上会直接标「需离线导入」，不会给一个点了必失败的按钮。
另外本程序的封装走 ffmpeg，**并不依赖 MP4Box**。

---

## 三点七、测试

`src-tauri/src/cmd.rs` 与 `src-tauri/src/tools_fetch.rs` 末尾共有 **23 条单元测试**：

```bash
cd src-tauri
cargo test                     # 23 条，瞬时完成
cargo test -- --ignored        # 额外跑一条真实的下载+解压集成测试
```

覆盖：GPU 滤镜链顺序与「不加 `-pix_fmt`」、二遍法第一遍写 `-f null NUL`、
`audioMode=2` 且源是 AAC 时走无损 copy、7 个音频扩展名映射、
`12bit` 必须先于 `10bit` 判断、整数 CRF 不能带小数尾巴、裸流才加 `-r` 与 bsf、
工具包安装检测、镜像拼接格式、以及“所有下载地址必须 https 且不含已失效的那个 GPAC 地址”。
**改完命令模板或工具清单务必重跑 `cargo test`。**

---

## 四、和原版的差异（有意为之）

| 项 | 原版 | 现在 | 原因 |
|---|---|---|---|
| 媒体探测 | `MediaInfoDLL`（进程内 COM） | `ffprobe -print_format json` | 不用 COM、不用额外 exe，字段更规整 |
| 工具平铺 | `organize_tools.ps1` 把 exe 复制到工作目录 | 递归索引，不复制 | 不污染工作目录，也不用先跑脚本 |
| 多语言 | 4 套 resx（en/ja-JP/zh-Hant） | 仅简体中文 | 未迁移，见下 |
| 设置存储 | 注册表 / 配置文件 | `%APPDATA%\LanzhuTool\settings.json` | 简单可读 |
| 状态栏资源 | WMI 查 CPU/内存 | 显示渲染进程堆占用 | 换 WebView 后 WMI 那套没意义了 |

---

## 五、还没做的

- **多语言**：原版 4 套 resx 没搬。要加的话在 `src/lib/i18n.ts` 建一份字符串表 + 一个 `t()` 即可，结构上已留好位置。
- **`预览`页**：右侧输出区的第三个标签目前只显示最近一条命令，原版的视频预览窗没做。
- **完成提示 / 托盘图标 / 完成后关机**：`autoShutdown` 选项在界面里有，但还没接实际动作。
- **更新器**：原版有 `FormUpdater`，没迁移。

---

## 六、为什么换掉 WinForms

不是语言问题，是**渲染模型**问题：

WinForms 里每个控件是一个独立 HWND 子窗口，系统各自绘制。
结果就是：圆角只能 `Region` 裁剪（带锯齿、要随尺寸重算）、
**兄弟控件之间无法半透明**、投影要自己画位图还会被邻近控件重绘擦掉、
布局只能绝对坐标（resx 写死）、没有动画。

Tauri 这边是一整页合成：`border-radius` 抗锯齿、`box-shadow`、`flex/grid`、
CSS transition 全部原生支持。之前那套"裁掉/盖住/挖洞/组合"的补丁，
在这里一个属性就解决了 —— 比如标题栏拖动就是 `data-tauri-drag-region`。

同时保留了好的部分：**命令行模板一致 → 行为一致**，所以这次重构是
"换壳不换芯"，不是重写引擎。
