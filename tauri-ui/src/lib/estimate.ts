/**
 * 预计输出大小。
 *
 * 逐条移植原版 `MainForm.cs` 的 `EstimateOutputSize` / `EstimateQualityBitrate` /
 * `EstimateAudioBitrate`，公式保持不变，这样老用户看到的数字和以前一致。
 *
 * 关键点：**这是纯函数**，输入变一点输出就变一点，
 * 所以界面只要把相关字段喂进来就能实时跟着参数走
 * （以前的实现只在"选了输入文件"时算一次，参数怎么改都不动，等于死数）。
 */

export interface SourceMetrics {
  durationSec: number
  width: number
  height: number
  fps: number
  videoKbps: number
  audioKbps: number
}

export interface EstimateInput {
  source: SourceMetrics | null
  /** 输出分辨率（0 表示不缩放） */
  width: number
  height: number
  maintainResolution: boolean
  /** 0 自定义参数 / 1 质量 / 2 二遍码率 */
  mode: number
  crf: number
  bitrate: number
  /** "H.264 8bit" / "HEVC 10bit" / "MOV" / "FLV" */
  format: string
  useGpu: boolean
  hybrid: boolean
  /** 0 压制 / 1 不压制 / 2 复制 */
  audioMode: number
  /** 0 NeroAAC / 1 QAAC / 2 WAV / 3 ALAC / 4 FLAC / 5 FDKAAC / 6 AC3 */
  audioEncoder: number
  audioBitrate: string
  /** 只压一部分时按帧数截时长 */
  frames: number
  seek: number
}

/** 质量模式下的视频码率启发式（kbps）。 */
export function estimateQualityBitrate(
  crf: number,
  width: number,
  height: number,
  fps: number,
  format: string,
  useGpu: boolean,
  hybrid: boolean,
): number {
  const base = 4500 // 基准：1080p@30fps / H.264 / CRF 23
  const crfFactor = Math.pow(1.12, 23 - crf)
  const resFactor = (width * height) / (1920 * 1080)
  const fpsFactor = fps > 0 ? fps / 30 : 1
  const codecFactor = format.startsWith('HEVC') ? 0.62 : 1
  const bitDepth = format.includes('12bit') ? 12 : format.includes('10bit') ? 10 : 8
  const bitDepthFactor = bitDepth >= 12 ? 1.1 : bitDepth >= 10 ? 1.05 : 1
  const gpuFactor = useGpu || hybrid ? 1.08 : 1
  const v =
    base * crfFactor * resFactor * fpsFactor * codecFactor * bitDepthFactor * gpuFactor
  return Number.isFinite(v) && v > 0 ? v : base
}

/** 音频码率（kbps）。 */
export function estimateAudioBitrate(
  encoder: number,
  bitrateText: string,
  srcAudioKbps: number,
): number {
  switch (encoder) {
    case 2: // WAV：44.1kHz × 16bit × 2ch
      return 1411
    case 3: // ALAC
    case 4: // FLAC：无损，经验上约为源音频的 60%
      return srcAudioKbps > 0 ? srcAudioKbps * 0.6 : 512
    default: {
      const v = Number(bitrateText)
      return v > 0 ? v : 128
    }
  }
}

/**
 * 估算输出字节数；拿不到源时长等信息时返回 null（界面显示 `--`）。
 */
export function estimateSize(i: EstimateInput): number | null {
  const s = i.source
  if (!s || s.durationSec <= 0) return null

  let outW = i.width
  let outH = i.height
  if (!(outW > 0 && outH > 0) || i.maintainResolution) {
    outW = s.width
    outH = s.height
  }
  if (!(outW > 0 && outH > 0)) return null

  const videoKbps =
    i.mode === 2
      ? i.bitrate
      : i.mode === 1
        ? estimateQualityBitrate(i.crf, outW, outH, s.fps || 30, i.format, i.useGpu, i.hybrid)
        : s.videoKbps > 0
          ? s.videoKbps
          : 3000

  const audioKbps =
    i.audioMode === 1 ? 0 : estimateAudioBitrate(i.audioEncoder, i.audioBitrate, s.audioKbps)

  // 只压一段时按帧数折算时长
  const durationSec =
    i.frames > 0 && s.fps > 0 ? Math.min(s.durationSec, i.frames / s.fps) : s.durationSec

  const bytes = ((videoKbps + audioKbps) * 1000 * durationSec) / 8
  return bytes > 0 ? bytes : null
}
