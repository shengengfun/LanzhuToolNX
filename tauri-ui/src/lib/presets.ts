/** 从原版 preset.xml 原样搬过来的预设。 */
export const VIDEO_PRESETS: { name: string; args: string }[] = [
  {
    name: 'default',
    args: '--crf 24 --preset 8 -r 6 -b 6 -i 1 --scenecut 60 -f 1:1 --qcomp 0.5 --psy-rd 0.3:0 --aq-mode 2 --aq-strength 0.8 --vf resize:960,540,,,,lanczos',
  },
  {
    name: 'DVDRIP不切边,16:9',
    args: '-p1 --crf 20 --aq-mode 2 --sar 32:27 --vf yadif:, --stat "TEMP.stat" --slow-firstpass --ref 8 --preset 8 --subme 10',
  },
  {
    name: 'DVDRIP不切边,sar40:33',
    args: '-p1 --crf 20 --aq-mode 2 --sar 40:33 --vf yadif:, --stat "TEMP.stat" --slow-firstpass --ref 8 --preset 8 --subme 10',
  },
  {
    name: 'DVDRIP切边,sar40:33',
    args: '-p1 --crf 20 --aq-mode 2 --sar 40:33 --vf yadif:,/crop:8,0,8,0 --stat "TEMP.stat" --slow-firstpass --ref 8 --preset 8 --subme 10',
  },
  { name: 'iOS', args: '--profile high --level 3.1 --level-force --device iphone' },
  {
    name: 'MAD',
    args: '--crf 25 --preset placebo --subme 10 --ref 7 --bframes 7 --qcomp 0.75 --psy-rd 0:0 --keyint infinite --min-keyint 1',
  },
  {
    name: 'PSP',
    args: '--profile main --level 3.0 --ref 3 --b-pyramid none --weightp 1 --vbv-maxrate 10000 --vbv-bufsize 10000 --vf resize:480,272,,,,lanczos --device psp',
  },
]

/** 音频预设，按编码器分组。 */
export const AUDIO_PRESETS: Record<string, { name: string; args: string }[]> = {
  NeroAAC: [
    { name: 'NeroAAC_HE-32Kbps', args: '-he -br 32000' },
    { name: 'NeroAAC_HE-48Kbps', args: '-he -br 48000' },
    { name: 'NeroAAC_HE-64Kbps', args: '-he -br 64000' },
    { name: 'NeroAAC_LC-128Kbps', args: '-lc -br 128000' },
    { name: 'NeroAAC_LC-192Kbps', args: '-lc -br 192000' },
    { name: 'NeroAAC_LC-256Kbps', args: '-lc -br 256000' },
    { name: 'NeroAAC_LC-Q1', args: '-q 1 -lc' },
  ],
  FDKAAC: [
    { name: 'FDKAAC_VBR1', args: '-m 1' },
    { name: 'FDKAAC_VBR2', args: '-m 2' },
    { name: 'FDKAAC_VBR3', args: '-m 3' },
    { name: 'FDKAAC_VBR4', args: '-m 4' },
    { name: 'FDKAAC_VBR5', args: '-m 5' },
    { name: 'FDKAAC_HE-_CBR64Kbps', args: '-p 5 -b 64' },
    { name: 'FDKAAC_LC-_CBR128Kbps', args: '-b 128' },
    { name: 'FDKAAC_LC-_CBR192Kbps', args: '-b 192' },
    { name: 'FDKAAC_LC-_CBR256Kbps', args: '-b 256' },
  ],
  QAAC: [
    { name: 'QAAC_HE-CBR64Kbps', args: '--he -c 64 -q 2 --no-optimize' },
    { name: 'QAAC_HE-CVBR64Kbps', args: '--he -v 64 -q 2 --no-optimize' },
    { name: 'QAAC_LC-CBR128Kbps', args: '-c 128 -q 2 --no-optimize' },
    { name: 'QAAC_LC-CVBR128Kbps', args: '-v 128 -q 2 --no-optimize' },
    { name: 'QAAC_LC-CBR256Kbps', args: '-c 256 -q 2 --no-optimize' },
    { name: 'QAAC_LC-CVBR256Kbps', args: '-v 256 -q 2 --no-optimize' },
    { name: 'QAAC_TVBR_V90', args: '-V 90 -q 2 --no-optimize' },
    { name: 'QAAC_TVBR_V127', args: '-V 127 -q 2 --no-optimize' },
  ],
}
