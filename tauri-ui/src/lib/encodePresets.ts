import type { EncodePreset } from './types'

/**
 * 内置压制预设库。
 *
 * 为什么要有这个功能：
 * 原来「压制格式」只有 H.264 / HEVC / MOV / FLV 四类，**换个编码器就得手打自定义参数**，
 * 而 ProRes、DNxHR、AV1、无损归档这些又各有各的怪脾气（profile 号、像素格式、qscale）。
 * 所以把「编码器 + 参数 + 容器 + 目标分辨率」打成一个包，选一下就行。
 *
 * 设计取舍（对照 Vegas 那个渲染模板列表）：
 * - Vegas 是「一堆同质条目平铺 + Search/Recent/Use Case 三个假分类」，真实可用信息很少；
 *   这里改成 **按用途分组 + 每条给一句人话说明 + 标出码率量级**，
 *   再配一个「按当前源分辨率推荐」——源是 720p 就别推 4K ProRes 了。
 * - 容器只允许 mp4 / mov / mkv / avi 四种：音频链路走的是 AAC/FLAC/AC3/WAV，
 *   WebM 那类只认 Opus/Vorbis 的容器塞进去会直接失败，宁可不提供。
 */

export const PRESET_GROUPS = ['常用', '网络分发', '中间格式', '归档', '设备'] as const

/** 内置预设。id 稳定不变（设置里存的是 id）。 */
export const BUILTIN_PRESETS: EncodePreset[] = [
  /* ---------------- 常用 ---------------- */
  {
    id: 'std-h264-crf',
    name: 'H.264 8bit 标准',
    group: '常用',
    desc: '最通用的网络压制：兼容性最好，体积适中',
    container: 'mp4',
    encoder: 'libx264',
    params: '-crf 23 -preset slow -pix_fmt yuv420p',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: true,
  },
  {
    id: 'std-h265-crf',
    name: 'H.265 10bit 标准',
    group: '常用',
    desc: '同画质比 H.264 省约 35% 体积，需要播放器支持',
    container: 'mp4',
    encoder: 'libx265',
    params: '-crf 24 -preset medium -pix_fmt yuv420p10le -tag:v hvc1',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 720,
    builtin: true,
  },
  {
    id: 'std-h264-1080p',
    name: 'H.264 1080p 二压',
    group: '常用',
    desc: '统一缩到 1080p，适合把杂七杂八的素材归拢',
    container: 'mp4',
    encoder: 'libx264',
    params: '-crf 22 -preset slow -pix_fmt yuv420p',
    width: 1920,
    height: 1080,
    fps: 0,
    minSourceHeight: 1080,
    builtin: true,
  },

  /* ---------------- 网络分发 ---------------- */
  {
    id: 'web-av1',
    name: 'AV1 (SVT) 1080p',
    group: '网络分发',
    desc: '同画质体积最小，编码慢、老设备解不动',
    container: 'mkv',
    encoder: 'libsvtav1',
    params: '-crf 32 -preset 6 -pix_fmt yuv420p10le',
    width: 1920,
    height: 1080,
    fps: 0,
    minSourceHeight: 1080,
    builtin: true,
  },
  {
    id: 'web-vp9',
    name: 'VP9 1080p',
    group: '网络分发',
    desc: '油管同款编码器（音频仍是 AAC，所以装 MKV）',
    container: 'mkv',
    encoder: 'libvpx-vp9',
    params: '-crf 32 -b:v 0 -row-mt 1 -pix_fmt yuv420p',
    width: 1920,
    height: 1080,
    fps: 0,
    minSourceHeight: 1080,
    builtin: true,
  },
  {
    id: 'web-720p',
    name: 'H.264 720p 小体积',
    group: '网络分发',
    desc: '二次元番剧 720p 分享：小、快、到处都能播',
    container: 'mp4',
    encoder: 'libx264',
    params: '-crf 24 -preset slow -pix_fmt yuv420p',
    width: 1280,
    height: 720,
    fps: 0,
    minSourceHeight: 720,
    builtin: true,
  },
  {
    id: 'web-4k-h265',
    name: 'H.265 4K 10bit',
    group: '网络分发',
    desc: '4K 源保留原分辨率，走 10bit 压缩',
    container: 'mp4',
    encoder: 'libx265',
    params: '-crf 22 -preset medium -pix_fmt yuv420p10le -tag:v hvc1',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 2160,
    builtin: true,
  },

  /* ---------------- 中间格式（给剪辑软件/B 站投稿用） ---------------- */
  {
    id: 'prores-proxy',
    name: 'ProRes 422 Proxy',
    group: '中间格式',
    desc: '最省空间的剪辑代理（约 45 Mbps@1080p30）',
    container: 'mov',
    encoder: 'prores_ks',
    params: '-profile:v 0 -pix_fmt yuv422p10le -vendor apl0',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: true,
  },
  {
    id: 'prores-lt',
    name: 'ProRes 422 LT',
    group: '中间格式',
    desc: '代理与成片之间的折中（约 102 Mbps）',
    container: 'mov',
    encoder: 'prores_ks',
    params: '-profile:v 1 -pix_fmt yuv422p10le -vendor apl0',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: true,
  },
  {
    id: 'prores-422',
    name: 'ProRes 422',
    group: '中间格式',
    desc: '标准中间码（约 147 Mbps），剪辑软件通吃',
    container: 'mov',
    encoder: 'prores_ks',
    params: '-profile:v 2 -pix_fmt yuv422p10le -vendor apl0',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: true,
  },
  {
    id: 'prores-hq',
    name: 'ProRes 422 HQ',
    group: '中间格式',
    desc: '高码中间码（约 220 Mbps），多次调色也不掉画质',
    container: 'mov',
    encoder: 'prores_ks',
    params: '-profile:v 3 -pix_fmt yuv422p10le -vendor apl0',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: true,
  },
  {
    id: 'prores-4444',
    name: 'ProRes 4444',
    group: '中间格式',
    desc: '带 Alpha 通道，做特效合成用（体积很大）',
    container: 'mov',
    encoder: 'prores_ks',
    params: '-profile:v 4 -pix_fmt yuva444p10le -vendor apl0',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: true,
  },
  {
    id: 'dnxhr-sq',
    name: 'DNxHR SQ',
    group: '中间格式',
    desc: 'Avid / Premiere 友好的中间码（8bit 4:2:2）',
    container: 'mov',
    encoder: 'dnxhd',
    params: '-profile:v dnxhr_sq -pix_fmt yuv422p',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: true,
  },

  /* ---------------- 归档 ---------------- */
  {
    id: 'arch-lossless-x264',
    name: 'H.264 无损',
    group: '归档',
    desc: 'x264 qp=0，体积是源码率的几倍，换剪辑友好',
    container: 'mkv',
    encoder: 'libx264',
    params: '-qp 0 -preset medium -pix_fmt yuv444p',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: true,
  },
  {
    id: 'arch-ffv1',
    name: 'FFV1 无损（MKV）',
    group: '归档',
    desc: '真·无损归档格式，适合存母带',
    container: 'mkv',
    encoder: 'ffv1',
    params: '-level 3 -g 1 -slices 4 -slicecrc 1 -pix_fmt yuv420p',
    width: 0,
    height: 0,
    fps: 0,
    minSourceHeight: 0,
    builtin: true,
  },

  /* ---------------- 设备 ---------------- */
  {
    id: 'dev-psp',
    name: 'PSP / PSV 兼容',
    group: '设备',
    desc: '480×272 / Level 3.0，老掌机能播',
    container: 'mp4',
    encoder: 'libx264',
    params:
      '-profile:v main -level 3.0 -crf 24 -preset slow -refs 3 -bf 0 -weightp 1 -pix_fmt yuv420p',
    width: 480,
    height: 272,
    fps: 29.97,
    minSourceHeight: 0,
    builtin: true,
  },
  {
    id: 'dev-bluray',
    name: '蓝光兼容 H.264',
    group: '设备',
    desc: 'High@4.1 + 8bit，能直接塞进蓝光结构',
    container: 'mp4',
    encoder: 'libx264',
    params: '-profile:v high -level 4.1 -crf 20 -preset slow -pix_fmt yuv420p',
    width: 1920,
    height: 1080,
    fps: 0,
    minSourceHeight: 1080,
    builtin: true,
  },
  {
    id: 'dev-tv-mpeg2',
    name: 'MPEG-2 DVD 规格',
    group: '设备',
    desc: 'DVD/老播放机（视频走 MPEG-2，音频仍是 AAC）',
    container: 'mp4',
    encoder: 'mpeg2video',
    params: '-b:v 6000k -maxrate 8000k -bufsize 1835008 -pix_fmt yuv420p -g 15',
    width: 720,
    height: 480,
    fps: 29.97,
    minSourceHeight: 0,
    builtin: true,
  },
]

/** 全部可用预设 = 内置 + 用户自建（同名时用户的那条优先）。 */
export function allPresets(custom: EncodePreset[]) {
  const ids = new Set(custom.map((p) => p.id))
  return [...custom, ...BUILTIN_PRESETS.filter((p) => !ids.has(p.id))]
}

/**
 * 按源分辨率/帧率推荐几条。
 *
 * 规则很直白，但比 Vegas 那个纯字母序列表有用得多：
 * - 源高度小于预设的目标高度 → 不推（不放大）
 * - 720p 以下优先给"小体积"路线，1080p 起才推 10bit / 中间码
 */
export function recommendPresets(source: { height: number; fps: number }, custom: EncodePreset[]) {
  const h = source.height || 0
  const list = allPresets(custom)
  const score = (p: EncodePreset) => {
    let s = 0
    if (p.minSourceHeight > 0 && h > 0 && h + 32 < p.minSourceHeight) return -1 // 会放大，直接排除
    if (p.height > 0 && h > 0 && h + 32 < p.height) return -1
    // 目标分辨率越接近源越高分
    if (p.height > 0 && h > 0) s += Math.max(0, 30 - Math.abs(h - p.height) / 100)
    if (p.height === 0) s += 10 // 保持原分辨率：通用性好
    if (h >= 2160 && p.name.includes('4K')) s += 20
    if (h > 0 && h <= 720 && p.name.includes('720p')) s += 20
    if (p.group === '常用') s += 8
    if (p.group === '中间格式' && h >= 1080) s += 6
    if (source.fps > 50 && p.fps > 0 && p.fps < 50) s -= 10
    return s
  }
  return list
    .map((p) => ({ p, s: score(p) }))
    .filter((x) => x.s >= 0)
    .sort((a, b) => b.s - a.s)
    .slice(0, 4)
    .map((x) => x.p)
}

/** 粗略的码率量级提示，让用户对体积有概念（kbps @1080p30）。 */
export function presetBitrateHint(p: EncodePreset): string {
  const table: Record<string, string> = {
    prores_ks: '≈100-220 Mbps',
    dnxhd: '≈145 Mbps',
    'libsvtav1': '≈1-3 Mbps',
    'libvpx-vp9': '≈1.5-4 Mbps',
    ffv1: '≈源文件 2-4 倍',
    libx265: '≈2-5 Mbps',
    libx264: '≈3-8 Mbps',
    mpeg2video: '≈6 Mbps',
  }
  if (p.params.includes('-qp 0')) return '无损（很大）'
  return table[p.encoder] ?? '视素材而定'
}
