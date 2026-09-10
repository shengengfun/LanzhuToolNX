// ------------------------------------------------------------------
// Copyright (C) 2011-2016 Maruko Toolbox Project
//
//  Authors: komaruchan <sandy_0308@hotmail.com>
//           LunarShaddow <aflyhorse@hotmail.com>
//           LYF <lyfjxymf@sina.com>
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either
// express or implied.
// See the License for the specific language governing permissions
// and limitations under the License.
// -------------------------------------------------------------------
//

using ControlExs;
using MediaInfoLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Management;
using System.Windows.Forms;
using System.Xml.Linq;

namespace mp4box
{
    public partial class MainForm : Form
    {
        public string workPath = "!undefined";
        public bool shutdownState = false;
        public bool trayMode = false;
        private XDocument xdoc;

        /// <summary>
        /// Encoding used for .bat scripts that are run by cmd.exe. Files are
        /// saved as UTF-8 (no BOM) and cmd is switched to code page 65001 first
        /// (see <see cref="WriteBatFile"/>), so every Unicode path (Japanese
        /// filenames, CJK Extension-B, ...) survives the trip through the batch
        /// file. Characters the local ANSI codepage (e.g. GBK) cannot hold used
        /// to be mangled or rejected before this change.
        /// </summary>
        private static readonly Encoding batEncoding = new UTF8Encoding(false);

        /// <summary>
        /// Writes a .bat that cmd.exe will run. Prepends a line that switches
        /// the code page to 65001 (UTF-8), then saves the content as UTF-8, so
        /// cmd.exe decodes every following line (incl. Unicode file paths) as
        /// UTF-8 regardless of the system locale.
        /// </summary>
        private static void WriteBatFile(string path, string content)
        {
            File.WriteAllText(path,
                "@chcp 65001>nul" + Environment.NewLine + content, batEncoding);
        }

        #region Private Members Declaration

        private StringBuilder avsBuilder = new StringBuilder(1000);
        private string syspath = Environment.GetFolderPath(Environment.SpecialFolder.System).Remove(1);
        private int indexofsource;
        private int indexoftarget;
        private byte x264mode = 1;
        private string clip = "";
        private string MIvideo = "";
        private string namevideo = "";
        private string namevideo2 = "";
        private string namevideo4 = "";
        private string namevideo5 = "";
        private string namevideo6 = "";
        private string nameaudio = "";
        private string nameaudio2 = "";
        private string nameaudio3 = "";
        private string namevideo8 = "";
        private string namevideo9 = "video";
        private string nameout;
        private string nameout2;
        private string nameout3;
        private string nameout5;
        private string nameout6;
        private string nameout9;
        private string namesub;
        private string namesub2 = "";
        private string namesub9 = "subtitle";
        private string MItext = "把视频文件拖到这里";
        private string mkvextract;
        private string mkvmerge;
        private string mux;
        private string x264;
        private string ffmpeg;
        private string aac;
        private string aextract;
        private string batpath;
        private string auto;
        private string startpath;
        private string avs = "";
        private string tempavspath = "";
        private string tempPic = "";
        private string logFileName, logPath;
        private string tempfilepath;
        private DateTime ReleaseDate = DateTime.Parse("2026-7-16 8:0:0");

        #endregion Private Members Declaration

        #region CPU Porocessors Number

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_INFO
        {
            public uint dwOemId;
            public uint dwPageSize;
            public uint lpMinimumApplicationAddress;
            public uint lpMaximumApplicationAddress;
            public uint dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public uint dwProcessorLevel;
            public uint dwProcessorRevision;
        }

        [DllImport("kernel32")]
        private static extern void GetSystemInfo(ref SYSTEM_INFO pSI);

        #endregion CPU Porocessors Number

        public MainForm()
        {
            string yearMonthPath = DateTime.Now.ToString("yyyy-MM");
            logPath = Application.StartupPath + "\\logs\\" + yearMonthPath;
            logFileName = logPath + "\\LogFile-" + DateTime.Now.ToString("yyyy'-'MM'-'dd'_'HH'-'mm'-'ss") + ".log";
            InitializeComponent();
            BindVideoDropHandlers();
            this.Shown += MainForm_Shown;
            ApplyLanIcon();
            // 预计大小：初始化多语言格式串并绑定参数变化事件
            InitEstimatedSize();
        }

        /// <summary>
        /// 初始化预计大小显示：绑定会影响大小的控件事件
        /// </summary>
        private void InitEstimatedSize()
        {
            if (EstimatedSizeLabel == null)
                return;

            // 绑定各种会影响预计大小的控件事件
            lbAuto.SelectedIndexChanged += delegate { UpdateEstimatedSize(); };
            x264CRFNum.ValueChanged += delegate { UpdateEstimatedSize(); };
            x264BitrateNum.ValueChanged += delegate { UpdateEstimatedSize(); };
            AudioBitrateComboBox.SelectedIndexChanged += delegate { UpdateEstimatedSize(); };
            AudioBitrateRadioButton.CheckedChanged += delegate { UpdateEstimatedSize(); };
            AudioCustomizeRadioButton.CheckedChanged += delegate { UpdateEstimatedSize(); };
            GpuComboBox.SelectedIndexChanged += delegate { UpdateEstimatedSize(); };
            x264WidthNum.ValueChanged += delegate { UpdateEstimatedSize(); };
            x264HeightNum.ValueChanged += delegate { UpdateEstimatedSize(); };
            VideoBatchFormatComboBox.SelectedIndexChanged += delegate { UpdateEstimatedSize(); };

            UpdateEstimatedSize();
        }

        private void BindVideoDropHandlers()
        {
            // Some Designer bindings can be lost after merge/regeneration;
            // wire core video import drag/drop at runtime to ensure availability.
            WireSingleFileDrop(txtvideo);
            WireSingleFileDrop(txtvideo4);
            WireSingleFileDrop(txtvideo5);
            WireSingleFileDrop(txtvideo6);
            WireSingleFileDrop(txtvideo8);
            WireSingleFileDrop(txtvideo9);
        }

        private void WireSingleFileDrop(TextBoxBase box)
        {
            if (box == null)
                return;

            box.AllowDrop = true;
            box.DragEnter -= FileTextBox_DragEnter;
            box.DragDrop -= FileTextBox_DragDrop;
            box.DragEnter += FileTextBox_DragEnter;
            box.DragDrop += FileTextBox_DragDrop;
        }

        private void FileTextBox_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void FileTextBox_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;

            string firstFile = files[0];
            if (!File.Exists(firstFile))
                return;

            TextBoxBase box = sender as TextBoxBase;
            if (box != null)
                box.Text = firstFile;
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            this.ShowIcon = true;
            this.ShowInTaskbar = true;
            ApplyLanIcon();
        }

        private void ApplyLanIcon()
        {
            string[] iconPaths = new string[]
            {
                Path.Combine(Application.StartupPath, "Res", "lan.ico")
            };

            foreach (string iconPath in iconPaths)
            {
                if (!File.Exists(iconPath))
                    continue;

                try
                {
                    this.Icon = new Icon(iconPath);
                    break;
                }
                catch { }
            }
        }

        public string GetMediaInfoString(string VideoName)
        {
            StringBuilder info = new StringBuilder();
            if (File.Exists(VideoName))
            {
                MediaInfo MI = new MediaInfo();
                MI.Open(VideoName);
                //全局
                string container = MI.Get(StreamKind.General, 0, "Format");
                string bitRate = MI.Get(StreamKind.General, 0, "BitRate/String");
                string duration = MI.Get(StreamKind.General, 0, "Duration/String1");
                string fileSize = MI.Get(StreamKind.General, 0, "FileSize/String");
                //视频
                string v_id = MI.Get(StreamKind.Video, 0, "ID");
                string v_format = MI.Get(StreamKind.Video, 0, "Format");
                string v_bitRate = MI.Get(StreamKind.Video, 0, "BitRate/String");
                string v_size = MI.Get(StreamKind.Video, 0, "StreamSize/String");
                string v_width = MI.Get(StreamKind.Video, 0, "Width");
                string v_height = MI.Get(StreamKind.Video, 0, "Height");
                string v_displayAspectRatio = MI.Get(StreamKind.Video, 0, "DisplayAspectRatio/String");
                string v_displayAspectRatio2 = MI.Get(StreamKind.Video, 0, "DisplayAspectRatio");
                string v_frameRate = MI.Get(StreamKind.Video, 0, "FrameRate/String");
                string v_colorSpace = MI.Get(StreamKind.Video, 0, "ColorSpace");
                string v_chromaSubsampling = MI.Get(StreamKind.Video, 0, "ChromaSubsampling");
                string v_bitDepth = MI.Get(StreamKind.Video, 0, "BitDepth/String");
                string v_scanType = MI.Get(StreamKind.Video, 0, "ScanType/String");
                string v_pixelAspectRatio = MI.Get(StreamKind.Video, 0, "PixelAspectRatio");
                string v_encodedLibrary = MI.Get(StreamKind.Video, 0, "Encoded_Library/String");
                string v_encodingSettings = MI.Get(StreamKind.Video, 0, "Encoded_Library_Settings");
                string v_encodedTime = MI.Get(StreamKind.Video, 0, "Encoded_Date");
                string v_codecProfile = MI.Get(StreamKind.Video, 0, "Codec_Profile");
                string v_frameCount = MI.Get(StreamKind.Video, 0, "FrameCount");

                //音频
                string a_id = MI.Get(StreamKind.Audio, 0, "ID");
                string a_format = MI.Get(StreamKind.Audio, 0, "Format");
                string a_bitRate = MI.Get(StreamKind.Audio, 0, "BitRate/String");
                string a_samplingRate = MI.Get(StreamKind.Audio, 0, "SamplingRate/String");
                string a_channel = MI.Get(StreamKind.Audio, 0, "Channel(s)");
                string a_size = MI.Get(StreamKind.Audio, 0, "StreamSize/String");

                string audioInfo = MI.Get(StreamKind.Audio, 0, "Inform")
                    + MI.Get(StreamKind.Audio, 1, "Inform")
                    + MI.Get(StreamKind.Audio, 2, "Inform")
                    + MI.Get(StreamKind.Audio, 3, "Inform");
                string videoInfo = MI.Get(StreamKind.Video, 0, "Inform");

                info = info.Append(Path.GetFileName(VideoName) + "\r\n");
                if (!string.IsNullOrEmpty(container))
                    info.Append("容器：" + container + "\r\n");
                if (!string.IsNullOrEmpty(bitRate))
                    info.Append("总码率：" + bitRate + "\r\n");
                if (!string.IsNullOrEmpty(fileSize))
                    info.Append("大小：" + fileSize + "\r\n");
                if (!string.IsNullOrEmpty(duration))
                    info.Append("时长：" + duration + "\r\n");

                if (!string.IsNullOrEmpty(v_format))
                    info.Append("\r\n" + "视频(" + v_id + ")：" + v_format + "\r\n");
                if (!string.IsNullOrEmpty(v_codecProfile))
                    info.Append("Profile：" + v_codecProfile + "\r\n");
                if (!string.IsNullOrEmpty(v_bitRate))
                    info.Append("码率：" + v_bitRate + "\r\n");
                if (!string.IsNullOrEmpty(v_size))
                    info.Append("文件大小：" + v_size + "\r\n");
                if (!string.IsNullOrEmpty(v_width) && !string.IsNullOrEmpty(v_height))
                    info.Append("分辨率：" + v_width + "x" + v_height + "\r\n");
                if (!string.IsNullOrEmpty(v_displayAspectRatio) && !string.IsNullOrEmpty(v_displayAspectRatio2))
                    info.Append("画面比例：" + v_displayAspectRatio + "(" + v_displayAspectRatio2 + ")" + "\r\n");
                if (!string.IsNullOrEmpty(v_pixelAspectRatio))
                    info.Append("像素宽高比：" + v_pixelAspectRatio + "\r\n");
                if (!string.IsNullOrEmpty(v_frameRate))
                    info.Append("帧率：" + v_frameRate + "\r\n");
                if (!string.IsNullOrEmpty(v_colorSpace))
                    info.Append("色彩空间：" + v_colorSpace + "\r\n");
                if (!string.IsNullOrEmpty(v_chromaSubsampling))
                    info.Append("色度抽样：" + v_chromaSubsampling + "\r\n");
                if (!string.IsNullOrEmpty(v_bitDepth))
                    info.Append("位深度：" + v_bitDepth + "\r\n");
                if (!string.IsNullOrEmpty(v_scanType))
                    info.Append("扫描方式：" + v_scanType + "\r\n");
                if (!string.IsNullOrEmpty(v_encodedTime))
                    info.Append("编码时间：" + v_encodedTime + "\r\n");
                if (!string.IsNullOrEmpty(v_frameCount))
                    info.Append("总帧数：" + v_frameCount + "\r\n");
                if (!string.IsNullOrEmpty(v_encodedLibrary))
                    info.Append("编码库：" + v_encodedLibrary + "\r\n");
                if (!string.IsNullOrEmpty(v_encodingSettings))
                    info.Append("编码设置：" + v_encodingSettings + "\r\n");

                if (!string.IsNullOrEmpty(a_format))
                    info.Append("\r\n" + "音频(" + a_id + ")：" + a_format + "\r\n");
                if (!string.IsNullOrEmpty(a_size))
                    info.Append("大小：" + a_size + "\r\n");
                if (!string.IsNullOrEmpty(a_bitRate))
                    info.Append("码率：" + a_bitRate + "\r\n");
                if (!string.IsNullOrEmpty(a_samplingRate))
                    info.Append("采样率：" + a_samplingRate + "\r\n");
                if (!string.IsNullOrEmpty(a_channel))
                    info.Append("声道数：" + a_channel + "\r\n");
                info.Append("\r\n====详细信息====\r\n" + videoInfo + "\r\n" + audioInfo + "\r\n");
                MI.Close();
            }
            else
                info.Append("文件不存在、非有效文件或者文件夹 无视频信息");
            return info.ToString();
        }

        public string ffmuxbat(string input1, string input2, string output)
        {
            return "\"" + workPath + "\\ffmpeg.exe\" -i \"" + input1 + "\" -i \"" + input2 + "\" -sn -map 0:v -map 1:a -c copy -y \"" + output + "\"\r\n";
        }

        private enum VideoPresetKind
        {
            H264,
            Hevc,
            Mov,
            Flv
        }

        private VideoPresetKind GetSelectedVideoPresetKind()
        {
            string presetText = x264ExeComboBox.SelectedItem == null ? string.Empty : x264ExeComboBox.SelectedItem.ToString().ToLowerInvariant();
            if (presetText.Contains("hevc"))
                return VideoPresetKind.Hevc;
            if (presetText.Contains("mov"))
                return VideoPresetKind.Mov;
            if (presetText.Contains("flv"))
                return VideoPresetKind.Flv;
            return VideoPresetKind.H264;
        }

        private int GetSelectedVideoBitDepth()
        {
            string presetText = x264ExeComboBox.SelectedItem == null ? string.Empty : x264ExeComboBox.SelectedItem.ToString().ToLowerInvariant();
            if (presetText.Contains("12bit"))
                return 12;
            if (presetText.Contains("10bit"))
                return 10;
            return 8;
        }

        private string GetSelectedVideoOutputExtension()
        {
            switch (GetSelectedVideoPresetKind())
            {
                case VideoPresetKind.Mov:
                    return ".mov";
                case VideoPresetKind.Flv:
                    return ".flv";
                default:
                    return ".mp4";
            }
        }

        private string GetSelectedVideoTempExtension()
        {
            switch (GetSelectedVideoPresetKind())
            {
                case VideoPresetKind.Hevc:
                    return ".hevc";
                case VideoPresetKind.Mov:
                    return ".mov";
                case VideoPresetKind.Flv:
                    return ".flv";
                default:
                    return ".mp4";
            }
        }

        private string GetSelectedVideoOutputSuffix()
        {
            switch (GetSelectedVideoPresetKind())
            {
                case VideoPresetKind.Hevc:
                    return "hevc";
                case VideoPresetKind.Mov:
                    return "mov";
                case VideoPresetKind.Flv:
                    return "flv";
                default:
                    return "h264";
            }
        }

        private static string EscapeFfmpegFilterPath(string path)
        {
            // subtitles filter uses ':' as option separator, so Windows drive letters
            // must escape ':' even when the filename is quoted.
            // Also normalize slashes and escape single quote characters.
            string p = path.Replace("\\", "/")
                           .Replace(":", "\\:")
                           .Replace("'", "\\'");
            return "'" + p + "'";
        }

        private string BuildFfmpegVideoCommand(string input, string output, int pass = 1, string sub = "")
        {
            string ffmpegPath = Path.Combine(workPath, "ffmpeg.exe");
            VideoPresetKind videoPresetKind = GetSelectedVideoPresetKind();
            int bitDepth = GetSelectedVideoBitDepth();
            bool useGpuAcceleration = GpuAccelerationCheckBox.Checked;
            bool useHybridProcessing = HybridProcessingCheckBox.Checked;
            bool useHevc = videoPresetKind == VideoPresetKind.Hevc;
            
            bool useGpu = useGpuAcceleration || useHybridProcessing;
            
            // 根据选择的GPU厂商决定编码器和硬件加速方式
            string gpuEncoder = null;
            string hwaccel = null;
            string hwaccelOutputFormat = null;
            int gpuIndex = GpuComboBox.SelectedIndex;
            string gpuLabel = (gpuIndex >= 0) ? GpuComboBox.SelectedItem as string : null;
            
            if (useGpu && gpuLabel != null)
            {
                if (gpuLabel.Contains("(AMF)"))
                {
                    gpuEncoder = useHevc ? "hevc_amf" : "h264_amf";
                    hwaccel = "d3d11va";
                    hwaccelOutputFormat = "d3d11";
                }
                else if (gpuLabel.Contains("(QSV)"))
                {
                    gpuEncoder = useHevc ? "hevc_qsv" : "h264_qsv";
                    hwaccel = "qsv";
                    hwaccelOutputFormat = "qsv";
                }
                else // NVENC
                {
                    gpuEncoder = useHevc ? "hevc_nvenc" : "h264_nvenc";
                    hwaccel = "cuda";
                    hwaccelOutputFormat = "cuda";
                }
            }
            // 如果GPU选择为默认或无法识别，使用NVENC作为默认
            if (useGpu && gpuEncoder == null)
            {
                gpuEncoder = useHevc ? "hevc_nvenc" : "h264_nvenc";
                hwaccel = "cuda";
                hwaccelOutputFormat = "cuda";
            }
            
            string encoder = useGpu
                ? gpuEncoder
                : (useHevc ? "libx265" : "libx264");

            StringBuilder sb = new StringBuilder();
            sb.Append(Util.FormatPath(ffmpegPath));
            
            // GPU加速和混合压制都需要硬件解码
            if (useGpu && !string.IsNullOrEmpty(hwaccel))
            {
                sb.Append(" -hwaccel ").Append(hwaccel);
                if (!string.IsNullOrEmpty(hwaccelOutputFormat))
                    sb.Append(" -hwaccel_output_format ").Append(hwaccelOutputFormat);
            }
            
            // 多GPU设备选择
            if (useGpu && gpuIndex >= 0)
            {
                if (gpuEncoder.Contains("nvenc"))
                {
                    // NVENC: -gpu N (使用WMI序号)
                    sb.Append(" -gpu ").Append(gpuIndex);
                }
                else if (gpuEncoder.Contains("qsv"))
                {
                    // QSV: -qsv_device N
                    sb.Append(" -qsv_device ").Append(gpuIndex);
                }
                // AMF: D3D11VA自动使用系统默认渲染GPU，无需额外指定设备
                // 如需指定非默认GPU，需通过 -adapter N 传递
                else if (gpuEncoder.Contains("amf") && gpuIndex > 0)
                {
                    sb.Append(" -adapter ").Append(gpuIndex);
                }
            }
            
            sb.Append(" -y -i \"").Append(input).Append("\"");
            sb.Append(" -an -sn");

            if (x264SeekNumericUpDown.Value != 0)
                sb.Append(" -ss ").Append(x264SeekNumericUpDown.Value.ToString());

            string threadsText = x264ThreadsTextBox.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(threadsText) || threadsText == "0" || threadsText == "auto")
                sb.Append(" -threads 0");
            else
            {
                int threads;
                if (int.TryParse(threadsText, out threads) && threads > 0)
                    sb.Append(" -threads ").Append(threads);
                else
                    sb.Append(" -threads 0");
            }

            string pixFmt = "yuv420p";
            if (encoder == "libx265")
            {
                if (bitDepth == 12)
                    pixFmt = "yuv420p12le";
                else if (bitDepth == 10)
                    pixFmt = "yuv420p10le";
            }
            else if (encoder == "hevc_nvenc" || encoder == "hevc_amf" || encoder == "hevc_qsv")
            {
                if (bitDepth == 10)
                    pixFmt = "p010le";
            }
            // h264_amf / h264_qsv / h264_nvenc: yuv420p (8bit)
            // GPU 硬件加速时不能加 -pix_fmt：
            //   硬解输出的硬件帧（qsv/cuda/d3d11）会被自动插入的 auto_scale 滤镜尝试转成
            //   软件帧而失败（Impossible to convert between the formats ... src: qsv，
            //   错误码 -40 Function not implemented）。
            //   无滤镜时硬件帧直接交给硬编；有滤镜时像素格式由滤镜链中的 format=nv12/p010le 控制。
            if (!useGpu)
                sb.Append(" -pix_fmt ").Append(pixFmt);

            List<string> filters = new List<string>();
            
            // GPU加速模式下需要处理滤镜链
            // 混合压制：GPU解码→CPU滤镜→GPU编码
            // 纯GPU加速：如果有字幕/缩放滤镜，也需要CPU处理
            bool needCpuFilters = (x264HeightNum.Value != 0 && x264WidthNum.Value != 0 && !MaintainResolutionCheckBox.Checked)
                || !string.IsNullOrEmpty(sub);
            int hwHeadCount = 0;
            
            if (useGpu && needCpuFilters)
            {
                // GPU→CPU（处理滤镜）
                filters.Add("hwdownload");
                hwHeadCount++;
                // 根据编码器确定下载后的格式
                if (encoder == "h264_nvenc" || encoder == "h264_amf" || encoder == "h264_qsv")
                    filters.Add("format=nv12");
                else if (encoder == "hevc_nvenc" || encoder == "hevc_amf" || encoder == "hevc_qsv")
                {
                    if (bitDepth == 10)
                        filters.Add("format=p010le");
                    else
                        filters.Add("format=yuv420p");
                }
                else if (encoder == "libx265")
                {
                    if (bitDepth == 12)
                        filters.Add("format=yuv420p12le");
                    else if (bitDepth == 10)
                        filters.Add("format=yuv420p10le");
                    else
                        filters.Add("format=yuv420p");
                }
                else
                    filters.Add("format=yuv420p");
                hwHeadCount++;
            }
            
            if (x264HeightNum.Value != 0 && x264WidthNum.Value != 0 && !MaintainResolutionCheckBox.Checked)
                filters.Add("scale=" + x264WidthNum.Value + ":" + x264HeightNum.Value + ":flags=lanczos");
            if (!string.IsNullOrEmpty(sub))
                filters.Add("subtitles=" + EscapeFfmpegFilterPath(sub));
            
            // CPU滤镜处理完后需要hwupload转回GPU
            if (useGpu && needCpuFilters && filters.Count > hwHeadCount)
            {
                // CPU→GPU（编码前转回）
                if (hwaccel == "cuda")
                    filters.Add("hwupload_cuda");
                else
                    filters.Add("hwupload");
            }
            else if (useGpu && !needCpuFilters)
            {
                // 纯GPU模式无CPU滤镜，不需要hwdownload/hwupload
                filters.Clear();
            }
            
            if (filters.Count > 0)
                sb.Append(" -vf \"").Append(string.Join(",", filters.ToArray())).Append("\"");

            sb.Append(" -c:v ").Append(encoder);

            if (x264mode == 0)
            {
                if (!string.IsNullOrEmpty(x264CustomParameterTextBox.Text))
                    sb.Append(" ").Append(x264CustomParameterTextBox.Text);
            }
            else if (x264mode == 1)
            {
                // 质量模式：不同编码器使用不同的质量参数
                if (gpuEncoder != null && gpuEncoder.Contains("amf"))
                    // AMF: -rc cqp -qp（不支持 -rc vbr / -cq）
                    sb.Append(" -rc cqp -qp ").Append(x264CRFNum.Value.ToString());
                else if (gpuEncoder != null && gpuEncoder.Contains("qsv"))
                    // QSV: -global_quality
                    sb.Append(" -global_quality ").Append(x264CRFNum.Value.ToString());
                else if (useGpu)
                    // NVENC: -rc vbr -cq
                    sb.Append(" -rc vbr -cq ").Append(x264CRFNum.Value.ToString());
                else
                    // CPU: -crf
                    sb.Append(" -crf ").Append(x264CRFNum.Value.ToString());
            }
            else if (x264mode == 2)
            {
                sb.Append(" -pass ").Append(pass.ToString()).Append(" -b:v ").Append(x264BitrateNum.Value.ToString()).Append("k");
                if (gpuEncoder != null && gpuEncoder.Contains("amf"))
                    sb.Append(" -rc vbr_peak");
                else if (useGpu && (gpuEncoder == null || !gpuEncoder.Contains("qsv")))
                    sb.Append(" -rc vbr");
            }

            if (x264mode != 0)
            {
                if (!string.IsNullOrEmpty(x264extraLine.Text))
                    sb.Append(" ").Append(x264extraLine.Text);
                else if (useGpu)
                {
                    // 各 GPU 编码器的预设参数
                    if (gpuEncoder != null && gpuEncoder.Contains("amf"))
                        sb.Append(" -quality balanced");
                    else if (gpuEncoder != null && gpuEncoder.Contains("qsv"))
                        sb.Append(" -preset medium");
                    else
                        sb.Append(" -preset p5");
                }
                else
                {
                    // libx264/libx265 preset
                    if (useHevc)
                        sb.Append(" -preset medium");
                    else
                        sb.Append(" -preset fast");
                }
            }

            if (x264FramesNumericUpDown.Value != 0)
                sb.Append(" -frames:v ").Append(x264FramesNumericUpDown.Value.ToString());

            if (x264mode == 2 && pass == 1)
                sb.Append(" -f null NUL");
            else if (!string.IsNullOrEmpty(output))
                sb.Append(" \"").Append(output).Append("\"");

            sb.Append("\r\n");
            return sb.ToString();
        }

        public string x264bat(string input, string output, int pass = 1, string sub = "")
        {
            return BuildFfmpegVideoCommand(input, output, pass, sub);
        }

        public string x265bat(string input, string output, int pass = 1)
        {
            return BuildFfmpegVideoCommand(input, output, pass, string.Empty);
        }

        public static bool stringCheck(string str, string info = "")
        {
            if (string.IsNullOrEmpty(str))
            {
                MessageBox.Show("发现空或者无效的字符串 " + info);
            }
            return string.IsNullOrEmpty(str);
        }

        public string timeminus(int h1, int m1, int s1, int h2, int m2, int s2)
        {
            int h = 0;
            int m = 0;
            int s = 0;
            s = s2 - s1;
            if (s < 0)
            {
                m = -1;
                s = s + 60;
            }
            m = m + m2 - m1;
            if (m < 0)
            {
                h = -1;
                m = m + 60;
            }
            h = h + h2 - h1;
            return h.ToString() + ":" + m.ToString() + ":" + s.ToString();
        }

        public string timeplus(int h1, int m1, int s1, int h2, int m2, int s2)
        {
            int h = 0;
            int m = 0;
            int s = 0;
            s = s1 + s2;
            if (s >= 60)
            {
                m = 1;
                s = s - 60;
            }
            m = m + m1 + m2;
            if (m >= 60)
            {
                h = 1;
                m = m - 60;
            }
            h = h + h1 + h2;
            return h.ToString() + ":" + m.ToString() + ":" + s.ToString();
        }

        public string audiobat(string input, string output)
        {
            int AACbr = 1000 * Convert.ToInt32(AudioBitrateComboBox.Text);
            string br = AACbr.ToString();
            ffmpeg = "\"" + workPath + "\\ffmpeg.exe\" -i \"" + input + "\" -vn -sn -v 0 -c:a pcm_s16le -f wav pipe:|";
            switch (AudioEncoderComboBox.SelectedIndex)
            {
                case 0:
                    if (AudioBitrateRadioButton.Checked)
                    {
                        ffmpeg += "\"" + workPath + "\\neroAacEnc.exe\" -ignorelength -lc -br " + br + " -if - -of \"" + output + "\"";
                    }
                    if (AudioCustomizeRadioButton.Checked)
                    {
                        ffmpeg += "\"" + workPath + "\\neroAacEnc.exe\" -ignorelength " + AudioCustomParameterTextBox.Text.ToString() + " -if - -of \"" + output + "\"";
                    }
                    break;

                case 1:
                    if (AudioBitrateRadioButton.Checked)
                    {
                        ffmpeg += "\"" + workPath + "\\qaac.exe\" -q 2 --ignorelength -c " + AudioBitrateComboBox.Text + " - -o \"" + output + "\"";
                    }
                    if (AudioCustomizeRadioButton.Checked)
                    {
                        ffmpeg += "\"" + workPath + "\\qaac.exe\" --ignorelength " + AudioCustomParameterTextBox.Text.ToString() + " - -o \"" + output + "\"";
                    }
                    break;

                case 2:
                    if (Path.GetExtension(output) == ".aac")
                        output = Util.ChangeExt(output, ".wav");
                    ffmpeg = "\"" + workPath + "\\ffmpeg.exe\" -y -i \"" + input + "\" -f wav \"" + output + "\"";
                    break;

                case 3:
                    ffmpeg += "\"" + workPath + "\\refalac.exe\" --ignorelength - -o \"" + output + "\"";
                    break;

                case 4:
                    ffmpeg += "\"" + workPath + "\\flac.exe\" -f --ignore-chunk-sizes -5 - -o \"" + output + "\"";
                    break;

                case 5:
                    if (AudioBitrateRadioButton.Checked)
                    {
                        ffmpeg += "\"" + workPath + "\\fdkaac.exe\" --ignorelength -b " + AudioBitrateComboBox.Text + " - -o \"" + output + "\"";
                    }
                    if (AudioCustomizeRadioButton.Checked)
                    {
                        ffmpeg += "\"" + workPath + "\\fdkaac.exe\" --ignorelength " + AudioCustomParameterTextBox.Text.ToString() + " - -o \"" + output + "\"";
                    }
                    break;

                case 6:
                    ffmpeg = "\"" + workPath + "\\ffmpeg.exe\" -i \"" + input + "\" -c:a ac3 -b:a " + AudioBitrateComboBox.Text.ToString() + "k \"" + output + "\"";
                    break;

                default:
                    break;
            }
            aac = ffmpeg + "\r\n";
            return aac;
        }

        private string getAudioExt()
        {
            string ext = ".aac";
            switch (AudioEncoderComboBox.SelectedIndex)
            {
                case 0: ext = ".mp4"; break;
                case 1: ext = ".m4a"; break;
                case 2: ext = ".wav"; break;
                case 3: ext = ".m4a"; break;
                case 4: ext = ".flac"; break;
                case 5: ext = ".m4a"; break;
                case 6: ext = ".ac3"; break;
                default: ext = ".aac"; break;
            }
            return ext;
        }

        private void btnaudio_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.AUDIO_5); //"音频(*.mp4;*.aac;*.mp2;*.mp3;*.m4a;*.ac3)|*.mp4;*.aac;*.mp2;*.mp3;*.m4a;*.ac3|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameaudio = openFileDialog1.FileName;
                txtaudio.Text = nameaudio;
            }
        }

        private void btnvideo_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_9);//"视频(*.avi;*.mp4;*.m1v;*.m2v;*.m4v;*.264;*.h264;*.hevc)|*.avi;*.mp4;*.m1v;*.m2v;*.m4v;*.264;*.h264;*.hevc|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo = openFileDialog1.FileName;
                txtvideo.Text = namevideo;
            }
        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
        }

        private void btnout_Click(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_3);  //"视频(*.mp4)|*.mp4";
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameout = savefile.FileName;
                txtout.Text = nameout;
            }
        }

        private void btnmux_Click(object sender, EventArgs e)
        {
            if (namevideo == "")
            {
                ShowErrorMessage("请选择视频文件");
                return;
            }

            if (nameout == "")
            {
                ShowErrorMessage("请选择输出文件");
                return;
            }

            StringBuilder sb = new StringBuilder();
            string inputExt = Path.GetExtension(txtvideo.Text.Trim()).ToLower();
            bool isRawStream = (inputExt == ".264" || inputExt == ".h264" || inputExt == ".hevc");

            // ffmpeg 封装命令
            sb.Append(Util.FormatPath(workPath + "\\ffmpeg.exe"));

            // 对于裸流设置帧率
            if (isRawStream && cbFPS.Text != "auto" && cbFPS.Text != "")
                sb.Append(" -r " + cbFPS.Text);

            sb.Append(" -i \"" + namevideo + "\"");

            if (nameaudio != "")
                sb.Append(" -i \"" + nameaudio + "\"");

            // 映射流：视频流 + 音频流
            sb.Append(" -map 0:v -c:v copy");
            if (nameaudio != "")
                sb.Append(" -map 1:a -c:a copy");

            // PAR（像素宽高比）设置，仅对裸流生效
            if (isRawStream && Mp4BoxParComboBox.Text != "" && Mp4BoxParComboBox.Text != "1:1")
            {
                string par = Mp4BoxParComboBox.Text;
                if (inputExt == ".hevc")
                    sb.Append(" -bsf:v hevc_metadata=sample_aspect_ratio=" + par);
                else
                    sb.Append(" -bsf:v h264_metadata=sample_aspect_ratio=" + par);
            }

            sb.Append(" -sn -y \"" + nameout + "\"");
            sb.Append(" \r\n cmd");

            mux = sb.ToString();
            batpath = workPath + "\\mux.bat";
            WriteBatFile(batpath, mux);
            LogRecord(mux);
            Process.Start(batpath);
        }

        private void btnaextract_Click(object sender, EventArgs e)
        {
            //MP4 抽取音频1
            ExtractAV(namevideo, "a", 0);
            //if (namevideo == "")
            //{
            //    MessageBox.Show("请选择视频文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            //}
            //else
            //{
            //    //aextract = "\"" + workPath + "\\mp4box.exe\" -raw 2 \"" + namevideo + "\"";
            //    aextract = "";
            //    aextract += Cmd.FormatPath(workPath + "\\ffmpeg.exe");
            //    aextract += " -i " + Cmd.FormatPath(namevideo);
            //    aextract += " -vn -sn -c:a:0 copy ";
            //    string outfile = Cmd.GetDir(namevideo) +
            //        Path.GetFileNameWithoutExtension(namevideo) + "_抽取音频1" + Path.GetExtension(namevideo);
            //    aextract += Cmd.FormatPath(outfile);
            //    batpath = workPath + "\\aextract.bat";
            //    File.WriteAllText(batpath, aextract, Encoding.Default);
            //    LogRecord(aextract);
            //    Process.Start(batpath);
            //}
        }

        private void ExtractAV(string namevideo, string av, int streamIndex)
        {
            if (string.IsNullOrEmpty(namevideo))
            {
                ShowErrorMessage("请选择视频文件");
                return;
            }

            string ext = Path.GetExtension(namevideo);
            //aextract = "\"" + workPath + "\\mp4box.exe\" -raw 2 \"" + namevideo + "\"";
            string aextract = "";
            aextract += Util.FormatPath(workPath + "\\ffmpeg.exe");
            aextract += " -i " + Util.FormatPath(namevideo);
            if (av == "a")
            {
                aextract += " -vn -sn -c:a copy -y -map 0:a:" + streamIndex + " ";

                MediaInfo MI = new MediaInfo();
                MI.Open(namevideo);
                string audioFormat = MI.Get(StreamKind.Audio, streamIndex, "Format");
                string audioProfile = MI.Get(StreamKind.Audio, streamIndex, "Format_Profile");
                if (!string.IsNullOrEmpty(audioFormat))
                {
                    if (audioFormat.Contains("MPEG") && audioProfile == "Layer 3")
                        ext = ".mp3";
                    else if (audioFormat.Contains("MPEG") && audioProfile == "Layer 2")
                        ext = ".mp2";
                    else if (audioFormat.Contains("PCM")) //flv support(PCM_U8 * PCM_S16BE * PCM_MULAW * PCM_ALAW * ADPCM_SWF)
                        ext = ".wav";
                    else if (audioFormat == "AAC")
                        ext = ".aac";
                    else if (audioFormat == "AC-3")
                        ext = ".ac3";
                    else if (audioFormat == "ALAC")
                        ext = ".m4a";
                    else
                        ext = ".mka";
                }
                else
                {
                    ShowInfoMessage("该轨道无音频");
                    return;
                }
            }
            else if (av == "v")
            {
                aextract += " -an -sn -c:v copy -y -map 0:v:" + streamIndex + " ";
            }
            else
            {
                throw new Exception("未知流！");
            }
            string suf = "_audio_";
            if (av == "v")
            {
                suf = "_video_";
            }
            suf += "index" + streamIndex;
            string outfile = Util.GetDir(namevideo) +
                Path.GetFileNameWithoutExtension(namevideo) + suf + ext;
            aextract += Util.FormatPath(outfile);
            batpath = workPath + "\\" + av + "extract.bat";
            WriteBatFile(batpath, aextract);
            LogRecord(aextract);
            Process.Start(batpath);
        }

        private string ExtractAudio(string namevideo, string outfile, int streamIndex = 0)
        {
            if (string.IsNullOrEmpty(namevideo))
            {
                return "";
            }
            string ext = Path.GetExtension(namevideo);
            //aextract = "\"" + workPath + "\\mp4box.exe\" -raw 2 \"" + namevideo + "\"";
            string aextract = "";
            aextract += Util.FormatPath(workPath + "\\ffmpeg.exe");
            aextract += " -i " + Util.FormatPath(namevideo);
            aextract += " -vn -sn -c:a copy -y -map 0:a:" + streamIndex + " ";
            MediaInfo MI = new MediaInfo();
            MI.Open(namevideo);
            string audioFormat = MI.Get(StreamKind.Audio, streamIndex, "Format");
            string audioProfile = MI.Get(StreamKind.Audio, streamIndex, "Format_Profile");
            if (!string.IsNullOrEmpty(audioFormat))
            {
                if (audioFormat.Contains("MPEG") && audioProfile == "Layer 3")
                    ext = ".mp3";
                else if (audioFormat.Contains("MPEG") && audioProfile == "Layer 2")
                    ext = ".mp2";
                else if (audioFormat.Contains("PCM")) //flv support(PCM_U8 * PCM_S16BE * PCM_MULAW * PCM_ALAW * ADPCM_SWF)
                    ext = ".wav";
                else if (audioFormat == "AAC")
                    ext = ".aac";
                else if (audioFormat == "AC-3")
                    ext = ".ac3";
                else if (audioFormat == "ALAC")
                    ext = ".m4a";
                else
                    ext = ".mka";
            }
            else
            {
                return "";
            }
            aextract += Util.FormatPath(outfile) + "\r\n";
            return aextract;
        }

        private void ExtractTrack(string namevideo, int streamIndex)
        {
            if (string.IsNullOrEmpty(namevideo))
            {
                ShowErrorMessage("请选择视频文件");
                return;
            }

            string aextract = "";
            aextract += Util.FormatPath(workPath + "\\ffmpeg.exe");
            aextract += " -i " + Util.FormatPath(namevideo);
            aextract += " -map 0:" + streamIndex + " -c copy ";
            string suf = "_抽取流Index" + streamIndex;
            string outfile = Util.GetDir(namevideo) +
                Path.GetFileNameWithoutExtension(namevideo) + suf + '.' +
                FormatExtractor.Extract(workPath, namevideo)[streamIndex].Format;
            aextract += Util.FormatPath(outfile);
            batpath = workPath + "\\mkvextract.bat";
            WriteBatFile(batpath, aextract);
            LogRecord(aextract);
            Process.Start(batpath);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ShowInfoMessage(string.Format(" \r\n有任何建议或疑问可以通过以下方式联系。\nQQ：57655408\n\n\t\t\t发布日期：2026年7月16日\n\t\t\t- ( ゜- ゜)つロ 乾杯~"), "关于");
        }

        private void btnvextract_Click(object sender, EventArgs e)
        {
            //MP4抽取视频1
            ExtractAV(namevideo, "v", 0);
            //if (namevideo == "")
            //{
            //    MessageBox.Show("请选择视频文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            //}
            //else
            //{
            //    //vextract = "\"" + workPath + "\\mp4box.exe\" -raw 1 \"" + namevideo + "\"";
            //    vextract = "";
            //    vextract += Cmd.FormatPath(workPath + "\\ffmpeg.exe");
            //    vextract += " -i " + Cmd.FormatPath(namevideo);
            //    vextract += " -an -sn -c:v:0 copy ";
            //    string outfile = Cmd.GetDir(namevideo) +
            //        Path.GetFileNameWithoutExtension(namevideo) + "_抽取视频1" + Path.GetExtension(namevideo);
            //    vextract += Cmd.FormatPath(outfile);
            //    batpath = workPath + "\\vextract.bat";
            //    File.WriteAllText(batpath, vextract, Encoding.Default);
            //    LogRecord(vextract);
            //    Process.Start(batpath);
            //}
        }

        private void txtvideo_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (txtvideo.Text.Trim().Length > 0)
                {
                    if (!File.Exists(txtvideo.Text.Trim()))
                    {
                        throw new Exception("输入文件: \r\n\r\n" + txtvideo.Text.Trim() + "\r\n\r\n不存在!");
                    }
                    string inputExt = Path.GetExtension(txtvideo.Text.Trim()).ToLower();
                    //if (inputExt != ".avi"  //Only MPEG-4 SP/ASP video and MP3 audio supported at the current time. To import AVC/H264 video, you must first extract the avi track.
                    //        && inputExt != ".mp4" //MPEG-4 Video
                    //        && inputExt != ".m1v" //MPEG-1 Video
                    //        && inputExt != ".m2v" //MPEG-2 Video
                    //        && inputExt != ".m4v" //MPEG-4 Video
                    //        && inputExt != ".264" //AVC/H264 Video
                    //        && inputExt != ".h264" //AVC/H264 Video
                    //        && inputExt != ".hevc") //HEVC/H265 Video
                    //{
                    //    throw new Exception("输入文件: \r\n\r\n" + txtvideo.Text.Trim() + "\r\n\r\n是一个mp4box不支持的视频文件!");
                    //}
                    if (inputExt == ".264" || inputExt == ".h264" || inputExt == ".hevc")
                    {
                        ShowWarningMessage("H.264/HEVC裸流文件默认帧率为25fps\r\n如果你知道该文件的帧率建议手动设置");
                    }
                    namevideo = txtvideo.Text;
                    txtout.Text = Util.ChangeExt(txtvideo.Text, "_Mux.mp4");
                }
            }
            catch (Exception ex)
            {
                txtvideo.Text = string.Empty;
                ShowErrorMessage(ex.Message);
            }
        }

        private void txtaudio_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (txtaudio.Text.Trim().Length > 0)
                {
                    if (!File.Exists(txtaudio.Text.Trim()))
                    {
                        throw new Exception("输入文件: \r\n\r\n" + txtaudio.Text.Trim() + "\r\n\r\n不存在!");
                    }
                    nameaudio = txtaudio.Text;
                }
            }
            catch (Exception ex)
            {
                txtaudio.Text = string.Empty;
                ShowErrorMessage(ex.Message);
            }
        }

        private void txtout_TextChanged(object sender, EventArgs e)
        {
            nameout = txtout.Text;
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            #region Delete Temp Files

            if (SetupDeleteTempFileCheckBox.Checked && !workPath.Equals("!undefined"))
            {
                List<string> deleteFileList = new List<string>();

                string systemDisk = Environment.GetFolderPath(Environment.SpecialFolder.System).Substring(0, 3);
                string systemTempPath = systemDisk + @"windows\temp";

                //Delete all BAT files
                //DirectoryInfo theFolder = new DirectoryInfo(workPath);
                //foreach (FileInfo NextFile in theFolder.GetFiles())
                //{
                //    if (NextFile.Extension.Equals(".bat"))
                //        deleteFileList.Add(NextFile.FullName);
                //}
                string[] batFiles = Directory.GetFiles(workPath, "*.bat");

                if (Directory.Exists(tempfilepath))
                {
                    foreach (var item in Directory.GetFiles(tempfilepath))
                    {
                        deleteFileList.Add(item);
                    }
                }

                string[] deletedfiles = { "concat.txt", tempPic, tempavspath };
                deleteFileList.AddRange(deletedfiles);
                deleteFileList.AddRange(batFiles);
                foreach (string file in deleteFileList)
                {
                    File.Delete(file);
                }
            }

            #endregion Delete Temp Files

            #region Save Settings

            SaveSettings();

            #endregion Save Settings
        }

        private void txtvideo4_TextChanged(object sender, EventArgs e)
        {
            if (File.Exists(txtvideo4.Text.ToString()))
            {
                namevideo4 = txtvideo4.Text;
                //string finish = namevideo4.Insert(namevideo4.LastIndexOf(".")-1,"");
                //string ext = namevideo4.Substring(namevideo4.LastIndexOf(".") + 1, 3);
                //finish += "_clip." + ext;
                string finish = namevideo4.Insert(namevideo4.LastIndexOf("."), "_output");
                txtout5.Text = finish;
            }
        }

        private void txtout5_TextChanged(object sender, EventArgs e)
        {
            nameout5 = txtout5.Text;
        }

        private void btnvideo4_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_6); //"视频(*.mp4;*.flv;*.mkv)|*.mp4;*.flv;*.mkv|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo4 = openFileDialog1.FileName;
                txtvideo4.Text = namevideo4;
            }
        }

        private void btnout5_Click(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_1); //"视频(*.*)|*.*";
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameout5 = savefile.FileName;
                txtout5.Text = nameout5;
            }
        }

        public static bool IsWindowsVistaOrNewer
        {
            get { return (Environment.OSVersion.Platform == PlatformID.Win32NT) && (Environment.OSVersion.Version.Major >= 6); }
        }

        #region Settings

        /// <summary>
        /// 还原默认参数
        /// </summary>
        private void InitParameter()
        {
            #region Video Tab

            x264CRFNum.Value = 23.5m;
            x264BitrateNum.Value = 800;
            x264AudioParameterTextBox.Text = "--abitrate 128";
            x264AudioModeComboBox.SelectedIndex = 0;
            x264DemuxerComboBox.SelectedIndex = 0;
            x264WidthNum.Value = 0;
            x264HeightNum.Value = 0;
            x264CustomParameterTextBox.Text = "";
            x264PriorityComboBox.SelectedIndex = 2;
            x264FramesNumericUpDown.Value = 0;
            x264SeekNumericUpDown.Value = 0;
            x264Mode1RadioButton.Checked = true;
            x264ShutdownCheckBox.Checked = false;
            x265CheckBox.Visible = false;

            #endregion Video Tab

            #region Audio Tab

            AudioEncoderComboBox.SelectedIndex = 0;
            AudioPresetComboBox.SelectedIndex = 0;
            AudioBitrateComboBox.Text = "128";
            AudioBitrateRadioButton.Checked = true;

            #endregion Audio Tab

            #region General Tab

            OnePicAudioBitrateNum.Value = 128;
            OnePicFPSNum.Value = 1;
            OnePicCRFNum.Value = 24;

            BlackFPSNum.Value = 1;
            BlackCRFNum.Value = 51;
            BlackBitrateNum.Value = 900;

            maskb.Text = "000000";
            maske.Text = "000020";

            TransposeComboBox.SelectedIndex = 1;

            #endregion General Tab

            #region Mux Tab

            cbFPS.SelectedIndex = 0;
            Mp4BoxParComboBox.SelectedIndex = 0;
            MuxAacEncoderComboBox.SelectedIndex = 0;
            MuxFormatComboBox.Text = "flv";

            #endregion Mux Tab

            #region AVS Tab

            AVSwithAudioCheckBox.Checked = false;

            #endregion AVS Tab

            #region Setup Tab

            SplashScreenCheckBox.Checked = true;
            TrayModeCheckBox.Checked = false;
            x264PriorityComboBox.SelectedIndex = 2;
            x264ThreadsTextBox.Text = "auto";
            SetupDeleteTempFileCheckBox.Checked = true;
            CheckUpdateCheckBox.Checked = true;
            x265CheckBox.Checked = false;
            GpuAccelerationCheckBox.Checked = false;
            HybridProcessingCheckBox.Checked = false;

            #endregion Setup Tab
        }

        private void LoadSettings()
        {
            try
            {
                //load settings
                x264CRFNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["x264CRF"]);
                x264BitrateNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["x264Bitrate"]);
                x264AudioParameterTextBox.Text = ConfigurationManager.AppSettings["x264AudioParameter"];
                x264AudioModeComboBox.SelectedIndex = Convert.ToInt32(ConfigurationManager.AppSettings["x264AudioMode"]);
                if (int.Parse(ConfigurationManager.AppSettings["x264Exe"]) > x264ExeComboBox.Items.Count - 1)
                    x264ExeComboBox.SelectedIndex = 0;
                else
                    x264ExeComboBox.SelectedIndex = Convert.ToInt32(ConfigurationManager.AppSettings["x264Exe"]);
                x264DemuxerComboBox.SelectedIndex = Convert.ToInt32(ConfigurationManager.AppSettings["x264Demuxer"]);
                x264WidthNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["x264Width"]);
                x264HeightNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["x264Height"]);
                x264CustomParameterTextBox.Text = ConfigurationManager.AppSettings["x264CustomParameter"];
                x264PriorityComboBox.SelectedIndex = Convert.ToInt32(ConfigurationManager.AppSettings["x264Priority"]);
                x264extraLine.Text = ConfigurationManager.AppSettings["x264ExtraParameter"];
                bool gpuAccelerationEnabled;
                if (!bool.TryParse(ConfigurationManager.AppSettings["EnableGpuAcceleration"], out gpuAccelerationEnabled))
                    gpuAccelerationEnabled = false;
                GpuAccelerationCheckBox.Checked = gpuAccelerationEnabled;
                
                bool hybridProcessingEnabled;
                if (!bool.TryParse(ConfigurationManager.AppSettings["EnableHybridProcessing"], out hybridProcessingEnabled))
                    hybridProcessingEnabled = false;
                HybridProcessingCheckBox.Checked = hybridProcessingEnabled;
                
                AVSScriptTextBox.Text = ConfigurationManager.AppSettings["AVSScript"];
                AudioEncoderComboBox.SelectedIndex = Convert.ToInt32(ConfigurationManager.AppSettings["AudioEncoder"]);
                AudioBitrateComboBox.Text = ConfigurationManager.AppSettings["AudioBitrate"];
                OnePicAudioBitrateNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["OnePicAudioBitrate"]);
                OnePicFPSNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["OnePicFPS"]);
                OnePicCRFNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["OnePicCRF"]);
                BlackFPSNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["BlackFPS"]);
                BlackCRFNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["BlackCRF"]);
                BlackBitrateNum.Value = Convert.ToDecimal(ConfigurationManager.AppSettings["BlackBitrate"]);
                SetupDeleteTempFileCheckBox.Checked = Convert.ToBoolean(ConfigurationManager.AppSettings["SetupDeleteTempFile"]);
                CheckUpdateCheckBox.Checked = Convert.ToBoolean(ConfigurationManager.AppSettings["CheckUpdate"]);
                string savedThreads = ConfigurationManager.AppSettings["x264Threads"];
                int savedIndex;
                if (int.TryParse(savedThreads, out savedIndex))
                {
                    if (savedIndex == 0)
                        x264ThreadsTextBox.Text = "auto";
                    else
                        x264ThreadsTextBox.Text = savedIndex.ToString();
                }
                else
                    x264ThreadsTextBox.Text = "auto";
                x265CheckBox.Visible = false;
                TrayModeCheckBox.Checked = Convert.ToBoolean(ConfigurationManager.AppSettings["TrayMode"]);
                SplashScreenCheckBox.Checked = Convert.ToBoolean(ConfigurationManager.AppSettings["SplashScreen"]);
                SetupPlayerTextBox.Text = ConfigurationManager.AppSettings["PreviewPlayer"];
                string SubLangExt = Convert.ToString(ConfigurationManager.AppSettings["SubLanguageExtension"]);
                MuxFormatComboBox.SelectedIndex = Convert.ToInt32(ConfigurationManager.AppSettings["MuxFormat"]);
                x264BatchSubSpecialLanguage.DataSource = SubLangExt.Split(',');
                if (x264ExeComboBox.SelectedIndex == -1)
                {
                    x264ExeComboBox.SelectedIndex = 0;
                }

                if (int.Parse(ConfigurationManager.AppSettings["LanguageIndex"]) == -1)  //First Startup
                {
                    string culture = Thread.CurrentThread.CurrentCulture.Name;
                    switch (culture)
                    {
                        case "zh-CN":
                            languageComboBox.SelectedIndex = 0;
                            break;

                        case "zh-SG":
                            languageComboBox.SelectedIndex = 0;
                            break;

                        case "zh-TW":
                            languageComboBox.SelectedIndex = 1;
                            break;

                        case "zh-HK ":
                            languageComboBox.SelectedIndex = 1;
                            break;

                        case "zh-MO":
                            languageComboBox.SelectedIndex = 1;
                            break;

                        case "en-US":
                            languageComboBox.SelectedIndex = 2;
                            break;

                        case "ja-JP":
                            languageComboBox.SelectedIndex = 3;
                            break;

                        default:
                            break;
                    }
                }
                else
                    languageComboBox.SelectedIndex = int.Parse(ConfigurationManager.AppSettings["LanguageIndex"]);

                if (CheckUpdateCheckBox.Checked && Util.IsConnectInternet())
                {
                    DateTime d;
                    bool f;
                    CheckUpadateDelegate checkUpdateDelegate = CheckUpdate;
                    checkUpdateDelegate.BeginInvoke(out d, out f, new AsyncCallback(CheckUpdateCallBack), null);
                }
                x264ExeComboBox_SelectedIndexChanged(null, null);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void SaveSettings()
        {
            Configuration cfa = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            cfa.AppSettings.Settings["x264CRF"].Value = x264CRFNum.Value.ToString();
            cfa.AppSettings.Settings["x264Bitrate"].Value = x264BitrateNum.Value.ToString();
            cfa.AppSettings.Settings["x264AudioParameter"].Value = x264AudioParameterTextBox.Text;
            cfa.AppSettings.Settings["x264AudioMode"].Value = x264AudioModeComboBox.SelectedIndex.ToString();
            cfa.AppSettings.Settings["x264Exe"].Value = x264ExeComboBox.SelectedIndex.ToString();
            cfa.AppSettings.Settings["x264Demuxer"].Value = x264DemuxerComboBox.SelectedIndex.ToString();
            cfa.AppSettings.Settings["x264Width"].Value = x264WidthNum.Value.ToString();
            cfa.AppSettings.Settings["x264Height"].Value = x264HeightNum.Value.ToString();
            cfa.AppSettings.Settings["x264CustomParameter"].Value = x264CustomParameterTextBox.Text;
            cfa.AppSettings.Settings["x264Priority"].Value = x264PriorityComboBox.SelectedIndex.ToString();
            cfa.AppSettings.Settings["x264ExtraParameter"].Value = x264extraLine.Text;
            cfa.AppSettings.Settings["EnableGpuAcceleration"].Value = GpuAccelerationCheckBox.Checked.ToString();
            cfa.AppSettings.Settings["EnableHybridProcessing"].Value = HybridProcessingCheckBox.Checked.ToString();
            cfa.AppSettings.Settings["AVSScript"].Value = AVSScriptTextBox.Text;
            cfa.AppSettings.Settings["AudioEncoder"].Value = AudioEncoderComboBox.SelectedIndex.ToString();
            cfa.AppSettings.Settings["AudioParameter"].Value = AudioBitrateComboBox.Text;
            cfa.AppSettings.Settings["OnePicAudioBitrate"].Value = OnePicAudioBitrateNum.Value.ToString();
            cfa.AppSettings.Settings["OnePicFPS"].Value = OnePicFPSNum.Value.ToString();
            cfa.AppSettings.Settings["OnePicCRF"].Value = OnePicCRFNum.Value.ToString();
            cfa.AppSettings.Settings["BlackFPS"].Value = BlackFPSNum.Value.ToString();
            cfa.AppSettings.Settings["BlackCRF"].Value = BlackCRFNum.Value.ToString();
            cfa.AppSettings.Settings["BlackBitrate"].Value = BlackBitrateNum.Value.ToString();
            cfa.AppSettings.Settings["SetupDeleteTempFile"].Value = SetupDeleteTempFileCheckBox.Checked.ToString();
            cfa.AppSettings.Settings["CheckUpdate"].Value = CheckUpdateCheckBox.Checked.ToString();
            cfa.AppSettings.Settings["TrayMode"].Value = TrayModeCheckBox.Checked.ToString();
            cfa.AppSettings.Settings["LanguageIndex"].Value = languageComboBox.SelectedIndex.ToString();
            cfa.AppSettings.Settings["SplashScreen"].Value = SplashScreenCheckBox.Checked.ToString();
            string threadsText = x264ThreadsTextBox.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(threadsText) || threadsText == "0" || threadsText == "auto")
                cfa.AppSettings.Settings["x264Threads"].Value = "0";
            else
            {
                int threads;
                if (int.TryParse(threadsText, out threads) && threads > 0)
                    cfa.AppSettings.Settings["x264Threads"].Value = threads.ToString();
                else
                    cfa.AppSettings.Settings["x264Threads"].Value = "0";
            }
            cfa.AppSettings.Settings["PreviewPlayer"].Value = SetupPlayerTextBox.Text;
            cfa.AppSettings.Settings["MuxFormat"].Value = MuxFormatComboBox.SelectedIndex.ToString(); ;
            cfa.Save();
            ConfigurationManager.RefreshSection("appSettings"); // 刷新命名节，在下次检索它时将从磁盘重新读取它。记住应用程序要刷新节点
        }

        #endregion

        private void PresetXml()
        {
            if (!File.Exists("preset.xml"))
            {
                xdoc = new XDocument(
                    new XDeclaration("1.0", "utf-8", "yes"),
                    new XElement("root",
                        new XElement("Video",
                            new XElement("VideoEncoder",
                                new XElement("x264",
                                    new XElement("Parameter", "--crf 24 --preset 8 -r 6 -b 6 -i 1 --scenecut 60 -f 1:1 --qcomp 0.5 --psy-rd 0.3:0 --aq-mode 2 --aq-strength 0.8 --vf resize:960,540,,,,lanczos", new XAttribute("Name", "default")),
                                    new XElement("Parameter", "-p1 --crf 20 --aq-mode 2 --sar 32:27 --vf yadif:, --stat \"TEMP.stat\" --slow-firstpass --ref 8 --preset 8 --subme 10", new XAttribute("Name", "DVDRIP不切边,16:9")),
                                    new XElement("Parameter", "-p1 --crf 20 --aq-mode 2 --sar 40:33 --vf yadif:, --stat \"TEMP.stat\" --slow-firstpass --ref 8 --preset 8 --subme 10", new XAttribute("Name", "DVDRIP不切边,sar40:33")),
                                    new XElement("Parameter", "-p1 --crf 20 --aq-mode 2 --sar 40:33 --vf yadif:,/crop:8,0,8,0 --stat \"TEMP.stat\" --slow-firstpass --ref 8 --preset 8 --subme 10", new XAttribute("Name", "DVDRIP切边,sar40:33")),
                                    new XElement("Parameter", "--profile high --level 3.1 --level-force  --device iphone", new XAttribute("Name", "iOS")),
                                    new XElement("Parameter", "--crf 25 --preset placebo --subme 10 --ref 7 --bframes 7 --qcomp 0.75 --psy-rd 0:0 --keyint infinite --min-keyint 1", new XAttribute("Name", "MAD")),
                                    new XElement("Parameter", "--profile main --level 3.0 --ref 3 --b-pyramid none --weightp 1 --vbv-maxrate 10000 --vbv-bufsize 10000   --vf resize:480,272,,,,lanczos  --device psp", new XAttribute("Name", "PSP"))
                                    ),
                                new XElement("x265",
                                    new XElement("Parameter", "", new XAttribute("Name", "x265default"))
                                    )
                                )
                            ),
                        new XElement("Audio",
                            new XElement("AudioEncoder",
                                new XElement("NeroAAC",
                                    new XElement("Parameter", "-he -br 32000", new XAttribute("Name", "NeroAAC_HE-32Kbps")),
                                    new XElement("Parameter", "-he -br 48000", new XAttribute("Name", "NeroAAC_HE-48Kbps")),
                                    new XElement("Parameter", "-he -br 64000", new XAttribute("Name", "NeroAAC_HE-64Kbps")),
                                    new XElement("Parameter", "-lc -br 128000", new XAttribute("Name", "NeroAAC_LC-128Kbps")),
                                    new XElement("Parameter", "-lc -br 192000", new XAttribute("Name", "NeroAAC_LC-192Kbps")),
                                    new XElement("Parameter", "-lc -br 256000", new XAttribute("Name", "NeroAAC_LC-256Kbps")),
                                    new XElement("Parameter", "-q 1 -lc", new XAttribute("Name", "NeroAAC_LC-Q1"))
                                    ),
                                new XElement("FDKAAC",
                                     new XElement("Parameter", "-m 1", new XAttribute("Name", "FDKAAC_VBR1")),
                                     new XElement("Parameter", "-m 2", new XAttribute("Name", "FDKAAC_VBR2")),
                                     new XElement("Parameter", "-m 3", new XAttribute("Name", "FDKAAC_VBR3")),
                                     new XElement("Parameter", "-m 4", new XAttribute("Name", "FDKAAC_VBR4")),
                                     new XElement("Parameter", "-m 5", new XAttribute("Name", "FDKAAC_VBR5")),
                                     new XElement("Parameter", "-p 5 -b 64", new XAttribute("Name", "FDKAAC_HE-_CBR64Kbps")),
                                     new XElement("Parameter", "-b 128", new XAttribute("Name", "FDKAAC_LC-_CBR128Kbps")),
                                     new XElement("Parameter", "-b 192", new XAttribute("Name", "FDKAAC_LC-_CBR192Kbps")),
                                     new XElement("Parameter", "-b 256", new XAttribute("Name", "FDKAAC_LC-_CBR256Kbps"))
                                     ),
                                new XElement("QAAC",
                                    new XElement("Parameter", "--he -c 64 -q 2 --no-optimize", new XAttribute("Name", "QAAC_HE-CBR64Kbps")),
                                    new XElement("Parameter", "--he -v 64 -q 2 --no-optimize", new XAttribute("Name", "QAAC_HE-CVBR64Kbps")),
                                    new XElement("Parameter", "-c 128 -q 2 --no-optimize", new XAttribute("Name", "QAAC_LC-CBR128Kbps")),
                                    new XElement("Parameter", "-v 128 -q 2 --no-optimize", new XAttribute("Name", "QAAC_LC-CVBR128Kbps")),
                                    new XElement("Parameter", "-c 256 -q 2 --no-optimize", new XAttribute("Name", "QAAC_LC-CBR256Kbps")),
                                    new XElement("Parameter", "-v 256 -q 2 --no-optimize", new XAttribute("Name", "QAAC_LC-CVBR256Kbps")),
                                    new XElement("Parameter", "-V 90 -q 2 --no-optimize", new XAttribute("Name", "QAAC_TVBR_V90")),
                                    new XElement("Parameter", "-V 127 -q 2 --no-optimize", new XAttribute("Name", "QAAC_TVBR_V127"))
                                    )
                                )
                            )
                        )
                );
                xdoc.Save("preset.xml");
            }
            else
                xdoc = XDocument.Load("preset.xml");
        }

        private void LoadVideoPreset()
        {
            VideoPresetComboBox.Items.Clear();
            var xlsv = xdoc.Element("root").Element("Video").Element("VideoEncoder").Element("x264").Elements();
            foreach (var item in xlsv)
            {
                VideoPresetComboBox.Items.Add(item.Attribute("Name").Value);
            }
            if (VideoPresetComboBox.Items.Count > 0 && VideoPresetComboBox.SelectedIndex == -1)
                VideoPresetComboBox.SelectedIndex = 0;
        }

        private void LoadAudioPreset()
        {
            AudioPresetComboBox.Items.Clear();
            var xlsa = xdoc.Element("root").Element("Audio").Element("AudioEncoder").Element(AudioEncoderComboBox.Text).Elements();
            foreach (var item in xlsa)
            {
                AudioPresetComboBox.Items.Add(item.Attribute("Name").Value);
            }
            if (AudioPresetComboBox.Items.Count > 0 && AudioPresetComboBox.SelectedIndex == -1)
                AudioPresetComboBox.SelectedIndex = 0;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            SYSTEM_INFO pSI = new SYSTEM_INFO();
            GetSystemInfo(ref pSI);
            int processorNumber = (int)pSI.dwNumberOfProcessors;


            //Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("zh-TW");
            //use YAHEI in VistaOrNewer
            //if (IsWindowsVistaOrNewer)
            //{
            //    FontFamily myFontFamily = new FontFamily("微软雅黑"); //采用哪种字体
            //    Font myFont = new Font(myFontFamily, 9, FontStyle.Regular); //字是那种字体，显示的风格
            //    this.Font = myFont;
            //}

            //define workpath
            startpath = System.Windows.Forms.Application.StartupPath;
            workPath = startpath + "\\tools";
            if (!Directory.Exists(workPath))
            {
                MessageBox.Show("tools文件夹没有解压喔~ 工具箱里没有工具的话运行不起来的喔~", "（这只丸子）",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }

            EnsureToolCompatibilityFiles();
            //Directory.CreateDirectory(workPath);
            //string diskSymbol = startpath.Substring(0, 1);

            //string systemDisk = Environment.GetFolderPath(Environment.SpecialFolder.System).Substring(0, 3);
            //string systemTempPath = systemDisk + @"windows\temp";
            string systemTempPath = Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.Machine);
            tempavspath = systemTempPath + "\\temp.avs";
            tempPic = systemTempPath + "\\marukotemp.jpg";
            tempfilepath = startpath + "\\temp";

            //load x264 exe
            x264ExeComboBox.Items.Clear();
            x264ExeComboBox.Items.Add("H.264 8bit");
            x264ExeComboBox.Items.Add("H.264 10bit");
            x264ExeComboBox.Items.Add("H.264 12bit");
            x264ExeComboBox.Items.Add("HEVC 8bit");
            x264ExeComboBox.Items.Add("HEVC 10bit");
            x264ExeComboBox.Items.Add("HEVC 12bit");
            x264ExeComboBox.Items.Add("MOV");
            x264ExeComboBox.Items.Add("FLV");

            // avisynth未安装使用本地内置的avs
            if (string.IsNullOrEmpty(Util.CheckAviSynth()))
            {
                string sourceAviSynthdll = Path.Combine(workPath, @"avs\AviSynth.dll");
                string sourceDevILdll = Path.Combine(workPath, @"avs\DevIL.dll");
                if (File.Exists(sourceAviSynthdll) && File.Exists(sourceDevILdll))
                {
                    File.Copy(sourceAviSynthdll, Path.Combine(workPath, "AviSynth.dll"), true);
                    File.Copy(sourceDevILdll, Path.Combine(workPath, "DevIL.dll"), true);
                    LogRecord("未安装avisynth,使用本地内置avs.");
                }
            }
            else
            {
                File.Delete(Path.Combine(workPath, "AviSynth.dll"));
                File.Delete(Path.Combine(workPath, "DevIL.dll"));
            }

            //load AVS filter
            DirectoryInfo avspath = new DirectoryInfo(workPath + @"\avs\plugins");
            List<string> avsfilters = new List<string>();
            if (Directory.Exists(workPath + @"\avs\plugins"))
            {
                foreach (FileInfo FileName in avspath.GetFiles())
                {
                    if (Path.GetExtension(FileName.Name) == ".dll")
                    {
                        avsfilters.Add(FileName.Name);
                    }
                }
                AVSFilterComboBox.Items.AddRange(avsfilters.ToArray());
            }

            //ReleaseDate = System.IO.File.GetLastWriteTime(this.GetType().Assembly.Location); //获得程序编译时间
            ReleaseDatelabel.Text = ReleaseDate.ToString("yyyy-M-d");

            // load Help Text
            if (File.Exists(startpath + "\\help.rtf"))
            {
                HelpTextBox.LoadFile(startpath + "\\help.rtf");
            }

            PresetXml();
            LoadVideoPreset();
            LoadSettings();

            // 现代 UI 外壳（顶栏 / 左栏 / 右栏 / 底栏）
            InitModernShell();
        }

        private void EnsureToolCompatibilityFiles()
        {
            string[] requiredFiles = new string[]
            {
                "ffmpeg.exe",
                "ffplay.exe",
                "ffprobe.exe",
                "MediaInfo.dll",
                "mkvmerge.exe",
                "mkvextract.exe",
                "mkvinfo.exe",
                "mmg.exe",
                "neroAacEnc.exe",
                "qaac.exe",
                "fdkaac.exe",
                "flac.exe",
                "refalac.exe",
                "gMKVExtractGUI.exe",
                "gMKVExtractGUI.ini",
                "gMKVExtractGUI.exe.config"
            };

            foreach (string fileName in requiredFiles)
            {
                string targetRoot;
                if (Path.GetExtension(fileName).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                    targetRoot = startpath;
                else
                    targetRoot = workPath;
                string target = Path.Combine(targetRoot, fileName);
                if (File.Exists(target))
                    continue;

                try
                {
                    string found = Directory
                        .GetFiles(workPath, fileName, SearchOption.AllDirectories)
                        .FirstOrDefault(path => !path.Equals(target, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(found) && File.Exists(found))
                    {
                        File.Copy(found, target, true);
                        LogRecord("[tools-compat] copied " + fileName + " from " + found);
                    }
                }
                catch (Exception ex)
                {
                    LogRecord("[tools-compat] failed to copy " + fileName + ": " + ex.Message);
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_2);//"视频(*.mkv)|*.mkv";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo6 = openFileDialog1.FileName;
                txtvideo6.Text = namevideo6;
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (namevideo6 == "")
            {
                ShowErrorMessage("请选择视频文件");
            }
            else
            {
                mkvextract = workPath + "\\ mkvextract.exe tracks \"" + namevideo6 + "\" 1:video.h264 2:audio.aac";
                batpath = workPath + "\\mkvextract.bat";
                WriteBatFile(batpath, mkvextract);
                Process.Start(batpath);
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_3); //"视频(*.mp4)|*.mp4|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo5 = openFileDialog1.FileName;
                txtvideo5.Text = namevideo5;
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.AUDIO_1); //"音频(*.mp3)|*.mp3|音频(*.aac)|*.aac|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameaudio3 = openFileDialog1.FileName;
                txtaudio3.Text = nameaudio3;
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_2); //"视频(*.mkv)|*.mkv";
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameout6 = savefile.FileName;
                txtout6.Text = nameout6;
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            if (namevideo5 == "")
            {
                ShowErrorMessage("请选择视频文件");
            }
            else if (nameaudio3 == "")
            {
                ShowErrorMessage("请选择音频文件");
            }
            else if (nameout6 == "")
            {
                ShowErrorMessage("请选择输出文件");
            }
            else
            {
                mkvmerge = workPath + "\\mkvmerge.exe -o \"" + nameout6 + "\"   \"" + namevideo5 + "\"   \"" + nameaudio3 + "\"";
                batpath = workPath + "\\mkvmerge.bat";
                WriteBatFile(batpath, mkvmerge);
                Process.Start(batpath);
            }
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.ALL); //"所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo5 = openFileDialog1.FileName;
                txtvideo5.Text = namevideo5;
            }
        }

        private void button6_Click_1(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_2); //"视频(*.mkv)|*.mkv";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo6 = openFileDialog1.FileName;
                txtvideo6.Text = namevideo6;
            }
        }

        private void button7_Click_1(object sender, EventArgs e)
        {
            if (namevideo5 == "" && nameaudio3 == "")
            {
                ShowErrorMessage("请选择文件");
            }
            else
            {
                if (txtaudio3.Text != "" && txtsub.Text != "")
                {
                    mkvmerge = "\"" + workPath + "\\mkvmerge.exe\" -o \"" + nameout6 + "\" \"" + namevideo5 + "\" \"" + nameaudio3 + "\" \"" + namesub + "\"";
                }
                if (txtaudio3.Text == "" && txtsub.Text == "")
                {
                    mkvmerge = "\"" + workPath + "\\mkvmerge.exe\" -o \"" + nameout6 + "\" \"" + namevideo5 + "\"";
                }
                if (txtaudio3.Text != "" && txtsub.Text == "")
                {
                    mkvmerge = "\"" + workPath + "\\mkvmerge.exe\" -o \"" + nameout6 + "\" \"" + namevideo5 + "\" \"" + nameaudio3 + "\"";
                }
                if (txtaudio3.Text == "" && txtsub.Text != "")
                {
                    mkvmerge = "\"" + workPath + "\\mkvmerge.exe\" -o \"" + nameout6 + "\" \"" + namevideo5 + "\" \"" + namesub + "\"";
                }
                mkvmerge += "\r\ncmd";
                batpath = workPath + "\\mkvmerge.bat";
                WriteBatFile(batpath, mkvmerge);
                LogRecord(mkvmerge);
                Process.Start(batpath);
            }
        }

        private void button4_Click_1(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_2); //"视频(*.mkv)|*.mkv";
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameout6 = savefile.FileName;
                txtout6.Text = nameout6;
            }
        }

        private void button3_Click_1(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.AUDIO_2); //"音频(*.mp3;*.aac;*.ac3)|*.mp3;*.aac;*.ac3|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameaudio3 = openFileDialog1.FileName;
                txtaudio3.Text = nameaudio3;
            }
        }

        private void button5_Click_1(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.SUBTITLE_2); //"字幕(*.ass;*.ssa;*.srt;*.idx;*.sup)|*.ass;*.ssa;*.srt;*.idx;*.sup|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namesub = openFileDialog1.FileName;
                txtsub.Text = namesub;
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            if (namevideo6 == "")
            {
                ShowErrorMessage("请选择视频文件");
            }
            else
            {
                int i = namevideo6.IndexOf(".mkv");
                string mkvname = namevideo6.Remove(i);
                mkvextract = "\"" + workPath + "\\mkvextract.exe\" tracks \"" + namevideo6 + "\" 1:\"" + mkvname + "_video.h264\" 2:\"" + mkvname + "_audio.aac\"";
                batpath = workPath + "\\mkvextract.bat";
                WriteBatFile(batpath, mkvextract);
                Process.Start(batpath);
            }
        }

        private void txtvideo5_TextChanged(object sender, EventArgs e)
        {
            if (File.Exists(txtvideo5.Text.ToString()))
            {
                namevideo5 = txtvideo5.Text;
                string finish = namevideo5.Remove(namevideo5.LastIndexOf("."));
                finish += "_mkv封装.mkv";
                txtout6.Text = finish;
            }
        }

        private void txtaudio3_TextChanged(object sender, EventArgs e)
        {
            nameaudio3 = txtaudio3.Text;
        }

        private void txtsub_TextChanged(object sender, EventArgs e)
        {
            namesub = txtsub.Text;
        }

        private void txtout6_TextChanged_1(object sender, EventArgs e)
        {
            nameout6 = txtout6.Text;
        }

        private void txtvideo6_TextChanged(object sender, EventArgs e)
        {
            namevideo6 = txtvideo6.Text;
        }

        private void btnAutoAdd_Click(object sender, EventArgs e)
        {
            openFileDialog1.Multiselect = true;
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.ALL); //"所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                lbAuto.Items.AddRange(openFileDialog1.FileNames);
            }
            openFileDialog1.Multiselect = false;
        }

        private void btnAutoDel_Click(object sender, EventArgs e)
        {
            if (lbAuto.Items.Count > 0)
            {
                if (lbAuto.SelectedItems.Count > 0)
                {
                    int index = lbAuto.SelectedIndex;
                    lbAuto.Items.RemoveAt(lbAuto.SelectedIndex);
                    if (index == lbAuto.Items.Count)
                    {
                        lbAuto.SelectedIndex = index - 1;
                    }
                    if (index >= 0 && index < lbAuto.Items.Count && lbAuto.Items.Count > 0)
                    {
                        lbAuto.SelectedIndex = index;
                    }
                }
            }
        }

        private void btnAutoClear_Click(object sender, EventArgs e)
        {
            lbAuto.Items.Clear();
        }

        private void lbAuto_DragDrop(object sender, DragEventArgs e)
        {
            ListBox listbox = (ListBox)sender;
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false))
            {
                String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);
                foreach (String s in files)
                {
                    listbox.Items.Add(s);
                }
                return;
            }
            indexoftarget = listbox.IndexFromPoint(listbox.PointToClient(new Point(e.X, e.Y)));
            if (indexoftarget != ListBox.NoMatches)
            {
                string temp = listbox.Items[indexoftarget].ToString();
                listbox.Items[indexoftarget] = listbox.Items[indexofsource];
                listbox.Items[indexofsource] = temp;
                listbox.SelectedIndex = indexoftarget;
            }
        }

        private void lbAuto_DragEnter(object sender, DragEventArgs e)
        {
            //if (e.Data.GetDataPresent(DataFormats.FileDrop))
            //    e.Effect = DragDropEffects.All;
            //else e.Effect = DragDropEffects.None;
        }

        private void lbAuto_DragOver(object sender, DragEventArgs e)
        {
            //拖动源和放置的目的地一定是一个ListBox
            ListBox listbox = (ListBox)sender;
            if (e.Data.GetDataPresent(typeof(System.String)) && ((ListBox)sender).Equals(listbox))
            {
                e.Effect = DragDropEffects.Move;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Link;
            }
            else e.Effect = DragDropEffects.None;
        }

        private void lbAuto_MouseDown(object sender, MouseEventArgs e)
        {
            indexofsource = ((ListBox)sender).IndexFromPoint(e.X, e.Y);
            if (indexofsource == 65535)
                return;
            if (indexofsource != ListBox.NoMatches)
            {
                ((ListBox)sender).DoDragDrop(((ListBox)sender).Items[indexofsource].ToString(), DragDropEffects.All);
            }
        }

        public string VideoBatch(string input, string output)
        {
            bool hasAudio = false;
            string bat = "";
            string inputName = Path.GetFileNameWithoutExtension(input);
            string tempVideo = Path.Combine(tempfilepath, inputName + "_vtemp.mp4");
            string tempAudio = Path.Combine(tempfilepath, inputName + "_atemp" + getAudioExt());

            //检测是否含有音频
            MediaInfo MI = new MediaInfo();
            MI.Open(input);
            string audio = MI.Get(StreamKind.Audio, 0, "Format");
            if (!string.IsNullOrEmpty(audio)) { hasAudio = true; }
            string sub = (x264BatchSubCheckBox.Checked) ? GetSubtitlePath(input) : string.Empty;

            int audioMode = x264AudioModeComboBox.SelectedIndex;
            if (!hasAudio)
                audioMode = 1;
            switch (audioMode)
            {
                case 0:
                    aextract = audiobat(input, tempAudio);
                    break;
                case 1:
                    aextract = string.Empty;
                    break;
                case 2:
                    if (audio.ToLower() == "aac")
                    {
                        tempAudio = Path.Combine(tempfilepath, inputName + "_atemp.aac");
                        aextract = ExtractAudio(input, tempAudio);
                    }
                    else
                        aextract = audiobat(input, tempAudio);
                    break;
                default:
                    break;
            }

            tempVideo = Path.Combine(tempfilepath, inputName + "_vtemp" + GetSelectedVideoTempExtension());
            if (x264mode == 2)
                x264 = x264bat(input, tempVideo, 1, sub) + "\r\n" +
                       x264bat(input, tempVideo, 2, sub);
            else x264 = x264bat(input, tempVideo, 0, sub);
            if (audioMode == 1 || !hasAudio)
                x264 = x264.Replace(tempVideo, output);
            x264 += "\r\n";

            //封装
            mux = ffmuxbat(tempVideo, tempAudio, output);
            if (audioMode != 1 && hasAudio) //如果压制音频
                bat += aextract + x264 + mux + " \r\n";
            else
                bat += x264 + " \r\n";

            bat += "del \"" + tempAudio + "\"\r\n";
            bat += "del \"" + tempVideo + "\"\r\n";
            bat += "echo ===== one file is completed! =====\r\n";
            return bat;
        }

        private void btnBatchAuto_Click(object sender, EventArgs e)
        {
            if (lbAuto.Items.Count == 0)
            {
                ShowErrorMessage("请输入视频！");
                return;
            }

            if (x264ExeComboBox.SelectedIndex == -1)
            {
                ShowErrorMessage("请选择压制格式");
                return;
            }

            if (AudioEncoderComboBox.SelectedIndex != 0 && AudioEncoderComboBox.SelectedIndex != 1 && AudioEncoderComboBox.SelectedIndex != 5)
            {
                ShowWarningMessage("音频页面中的编码器未采用AAC将可能导致压制失败，建议将编码器改为QAAC、NeroAAC或FDKAAC。");
            }

            Util.ensureDirectoryExists(tempfilepath);
            string bat = string.Empty;
            for (int i = 0; i < this.lbAuto.Items.Count; i++)
            {
                string input = lbAuto.Items[i].ToString();
                string output;
                string outputRoot = x264PathTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(outputRoot))
                {
                    Util.ensureDirectoryExists(outputRoot);
                    output = Path.Combine(outputRoot,
                        Path.GetFileNameWithoutExtension(input) + "_batch." + VideoBatchFormatComboBox.Text);
                }
                else
                {
                    output = Util.ChangeExt(input, "_batch." + VideoBatchFormatComboBox.Text);
                }
                bat += VideoBatch(lbAuto.Items[i].ToString(), output);
            }

            LogRecord(bat);
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(GetCultureName());
            WorkingForm wf = new WorkingForm(bat, lbAuto.Items.Count);
            wf.Owner = this;
            wf.Show();
            //batpath = workPath + "\\auto.bat";
            //File.WriteAllText(batpath, bat, Encoding.Default);
            //Process.Start(batpath);
        }

        private void lbffmpeg_MouseDown(object sender, MouseEventArgs e)
        {
            indexofsource = ((ListBox)sender).IndexFromPoint(e.X, e.Y);
            if (indexofsource != ListBox.NoMatches)
            {
                ((ListBox)sender).DoDragDrop(((ListBox)sender).Items[indexofsource].ToString(), DragDropEffects.All);
            }
        }

        private void lbffmpeg_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false))
            {
                String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);
                foreach (String s in files)
                {
                    (sender as ListBox).Items.Add(s);
                }
            }
            ListBox listbox = (ListBox)sender;
            indexoftarget = listbox.IndexFromPoint(listbox.PointToClient(new Point(e.X, e.Y)));
            if (indexoftarget != ListBox.NoMatches)
            {
                string temp = listbox.Items[indexoftarget].ToString();
                listbox.Items[indexoftarget] = listbox.Items[indexofsource];
                listbox.Items[indexofsource] = temp;
                listbox.SelectedIndex = indexoftarget;
            }
        }

        private void lbffmpeg_DragOver(object sender, DragEventArgs e)
        {
            //拖动源和放置的目的地一定是一个ListBox
            if (e.Data.GetDataPresent(typeof(System.String)) && ((ListBox)sender).Equals(lbffmpeg))
            {
                e.Effect = DragDropEffects.Move;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Link;
            }
            else e.Effect = DragDropEffects.None;
        }

        private void btnffmpegAdd_Click(object sender, EventArgs e)
        {
            openFileDialog1.Multiselect = true;
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.ALL); //"所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                lbffmpeg.Items.AddRange(openFileDialog1.FileNames);
            }
            openFileDialog1.Multiselect = false;
        }

        private void btnffmpegDel_Click(object sender, EventArgs e)
        {
            if (lbffmpeg.Items.Count > 0)
            {
                if (lbffmpeg.SelectedItems.Count > 0)
                {
                    int index = lbffmpeg.SelectedIndex;
                    lbffmpeg.Items.RemoveAt(lbffmpeg.SelectedIndex);
                    if (index == lbffmpeg.Items.Count)
                    {
                        lbffmpeg.SelectedIndex = index - 1;
                    }
                    if (index >= 0 && index < lbffmpeg.Items.Count && lbffmpeg.Items.Count > 0)
                    {
                        lbffmpeg.SelectedIndex = index;
                    }
                }
            }
        }

        private void btnffmpegClear_Click(object sender, EventArgs e)
        {
            lbffmpeg.Items.Clear();
        }

        private void btnBatchMP4_Click(object sender, EventArgs e)
        {
            if (lbffmpeg.Items.Count != 0)
            {
                string ext = MuxFormatComboBox.Text;
                string mux = "";
                for (int i = 0; i < lbffmpeg.Items.Count; i++)
                {
                    string filePath = lbffmpeg.Items[i].ToString();
                    //如果是源文件的格式和目标格式相同则跳过
                    if (Path.GetExtension(filePath).Contains(ext))
                        continue;
                    string finish = Path.ChangeExtension(filePath, ext);
                    aextract = "";

                    //检测音频是否需要转换为AAC
                    MediaInfo MI = new MediaInfo();
                    MI.Open(filePath);
                    string audio = MI.Get(StreamKind.Audio, 0, "Format");
                    if (audio.ToLower() != "aac" && MuxFormatComboBox.Text != "mkv")
                    {
                        mux += "\"" + workPath + "\\ffmpeg.exe\" -y -i \"" + lbffmpeg.Items[i].ToString() + "\" -c:v copy -c:a " + MuxAacEncoderComboBox.Text + " -strict -2 \"" + finish + "\" \r\n";
                    }
                    else
                    {
                        mux += "\"" + workPath + "\\ffmpeg.exe\" -y -i \"" + lbffmpeg.Items[i].ToString() + "\" -c copy \"" + finish + "\" \r\n";
                    }
                }
                mux += "\r\ncmd";
                batpath = workPath + "\\mux.bat";
                WriteBatFile(batpath, mux);
                LogRecord(mux);
                Process.Start(batpath);
            }
            else ShowErrorMessage("请输入视频！");
        }

        private void txtvideo8_TextChanged(object sender, EventArgs e)
        {
            namevideo8 = txtvideo8.Text;
        }

        private void btnvextract8_Click(object sender, EventArgs e)
        {
            //FLV vcopy
            ExtractAV(namevideo8, "v", 0);
            //if (namevideo8 == "")
            //{
            //    MessageBox.Show("请选择视频文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            //}
            //else
            //{
            //    vextract = "\"" + workPath + "\\FLVExtractCL.exe\" -v \"" + namevideo8 + "\"";
            //    batpath = workPath + "\\vextract.bat";
            //    File.WriteAllText(batpath, vextract, Encoding.Default);
            //    LogRecord(vextract);
            //    Process.Start(batpath);
            //}
        }

        private void btnaextract8_Click(object sender, EventArgs e)
        {
            //FLV acopy
            ExtractAV(namevideo8, "a", 0);
            //if (namevideo8 == "")
            //{
            //    MessageBox.Show("请选择视频文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            //}
            //else
            //{
            //    aextract = "\"" + workPath + "\\FLVExtractCL.exe\" -a \"" + namevideo8 + "\"";
            //    batpath = workPath + "\\aextract.bat";
            //    File.WriteAllText(batpath, aextract, Encoding.Default);
            //    LogRecord(aextract);
            //    Process.Start(batpath);
            //}
        }

        private void btnvideo8_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_4); //"视频(*.flv;*.hlv)|*.flv;*.hlv";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo8 = openFileDialog1.FileName;
                txtvideo8.Text = namevideo;
            }
        }

        private void btnpreview9_Click(object sender, EventArgs e)
        {
            if (AVSScriptTextBox.Text != "")
            {
                string filepath = workPath + "\\temp.avs";
                File.WriteAllText(filepath, AVSScriptTextBox.Text.ToString(), Encoding.Default);
                if (File.Exists(SetupPlayerTextBox.Text))
                {
                    Process.Start(SetupPlayerTextBox.Text, filepath);
                }
                else
                {
                    PreviewForm pf = new PreviewForm();
                    pf.Show();
                    pf.axWindowsMediaPlayer1.URL = filepath;
                }
            }
            else
            {
                ShowErrorMessage("请输入正确的AVS脚本！");
            }
        }

        private void txtout_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtout.Text.ToString()))
            {
                Process.Start(txtout.Text.ToString());
            }
        }

        private void txtout3_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(AudioOutputTextBox.Text.ToString()))
            {
                Process.Start(AudioOutputTextBox.Text.ToString());
            }
        }

        private void txtout6_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtout6.Text.ToString()))
            {
                Process.Start(txtout6.Text.ToString());
            }
        }

        private void txtout9_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtout9.Text.ToString()))
            {
                Process.Start(txtout9.Text.ToString());
            }
        }

        private void txtvideo9_TextChanged(object sender, EventArgs e)
        {
            if (File.Exists(txtvideo9.Text.ToString()))
            {
                namevideo9 = txtvideo9.Text;
                string finish = namevideo9.Remove(namevideo9.LastIndexOf("."));
                finish += "_AVS压制.mp4";
                txtout9.Text = finish;
                GenerateAVS();
            }
            //if (txtvideo9.Text != "")
            //{
            //    if (txtAVS.Text != "")
            //    {
            //        txtAVS.Text = txtAVS.Text.Replace(prevideo9, txtvideo9.Text);
            //    }
            //    else
            //    {
            //        DirectoryInfo TheFolder = new DirectoryInfo("avsfilter");
            //        foreach (FileInfo FileName in TheFolder.GetFiles())
            //        {
            //            avs += "LoadPlugin(\"" + workpath + "\\avsfilter\\" + FileName + "\")\r\n";
            //        }
            //        avs += "\r\nDirectShowSource(\"" + namevideo9 + "\",23.976,convertFPS=True)\r\nConvertToYV12()\r\n" + "TextSub(\"" + namesub9 + "\")\r\n";
            //        txtAVS.Text = avs;
            //        avs = "";
            //    }
            //    prevideo9 = txtvideo9.Text;
            //}
        }

        private void txtsub9_TextChanged(object sender, EventArgs e)
        {
            namesub9 = txtsub9.Text;
            GenerateAVS();
            //if (txtAVS.Text != "")
            //{
            //    txtAVS.Text=txtAVS.Text.Replace(namesub9, txtsub9.Text);
            //    namesub9 = txtsub9.Text;
            //}
            //else
            //{
            //    namesub9 = txtsub9.Text;
            //    DirectoryInfo TheFolder = new DirectoryInfo("avsfilter");
            //    foreach (FileInfo FileName in TheFolder.GetFiles())
            //    {
            //        avs += "LoadPlugin(\"" + workpath + "\\avsfilter\\" + FileName + "\")\r\n";
            //    }
            //    avs += "\r\nDirectShowSource(\"" + namevideo9 + "\",23.976,convertFPS=True)\r\nConvertToYV12()\r\n" + "TextSub(\"" + namesub9 + "\")\r\n";
            //    txtAVS.Text = avs;
            //    avs = "";
            //}
        }

        private void txtout9_TextChanged(object sender, EventArgs e)
        {
            nameout9 = txtout9.Text;
        }

        private void btnAVS9_Click(object sender, EventArgs e)
        {
            x264DemuxerComboBox.SelectedIndex = 0; //压制AVS始终使用分离器为auto

            if (string.IsNullOrEmpty(nameout9))
            {
                ShowErrorMessage("请选择输出文件");
                return;
            }

            if (Path.GetExtension(nameout9).ToLower() != ".mp4")
            {
                ShowErrorMessage("仅支持MP4输出", "不支持的输出格式");
                return;
            }

            if (File.Exists(txtout9.Text.Trim()))
            {
                DialogResult dgs = ShowQuestion("目标文件:\r\n\r\n" + txtout9.Text.Trim() + "\r\n\r\n已经存在,是否覆盖继续压制？", "目标文件已经存在");
                if (dgs == DialogResult.No) return;
            }

            if (string.IsNullOrEmpty(Util.CheckAviSynth()) && string.IsNullOrEmpty(Util.CheckinternalAviSynth()))
            {
                if (ShowQuestion("检测到本机未安装avisynth无法继续压制，是否去下载安装", "avisynth未安装") == DialogResult.Yes)
                    Process.Start("http://sourceforge.net/projects/avisynth2/");
                return;
            }

            string inputName = Path.GetFileNameWithoutExtension(namevideo9);
            string tempVideo = Path.Combine(tempfilepath, inputName + "_vtemp.mp4");
            string tempAudio = Path.Combine(tempfilepath, inputName + "_atemp" + getAudioExt());
            Util.ensureDirectoryExists(tempfilepath);

            string filepath = tempavspath;
            //string filepath = workpath + "\\temp.avs";
            File.WriteAllText(filepath, AVSScriptTextBox.Text, Encoding.Default);

            //检测是否含有音频
            bool hasAudio = false;
            MediaInfo MI = new MediaInfo();
            MI.Open(namevideo9);
            string audio = MI.Get(StreamKind.Audio, 0, "Format");
            if (!string.IsNullOrEmpty(audio)) { hasAudio = true; }

            //audio
            if (AVSwithAudioCheckBox.Checked && hasAudio)
            {
                if (!File.Exists(txtvideo9.Text))
                {
                    ShowErrorMessage("请选择视频文件");
                    return;
                }
                aextract = audiobat(namevideo9, tempAudio);
            }
            else
                aextract = string.Empty;

            //video
            tempVideo = Path.Combine(tempfilepath, inputName + "_vtemp" + GetSelectedVideoTempExtension());
            if (x264mode == 2)
                x264 = x264bat(filepath, tempVideo, 1) + "\r\n" +
                       x264bat(filepath, tempVideo, 2);
            else x264 = x264bat(filepath, tempVideo);
            if (!AVSwithAudioCheckBox.Checked || !hasAudio)
                x264 = x264.Replace(tempVideo, nameout9);
            //mux
            if (AVSwithAudioCheckBox.Checked && hasAudio) //如果包含音频
                mux = ffmuxbat(tempVideo, tempAudio, nameout9);
            else
                mux = string.Empty;

            auto = aextract + x264 + "\r\n" + mux + " \r\n";
            auto += "\r\necho ===== one file is completed! =====\r\n";
            LogRecord(auto);
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(GetCultureName());
            WorkingForm wf = new WorkingForm(auto);
            wf.Owner = this;
            wf.Show();
            //auto += "\r\ncmd";
            //batpath = workPath + "\\x264avs.bat";
            //File.WriteAllText(batpath, auto, Encoding.Default);
            //Process.Start(batpath);
        }

        private void btnout9_Click(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_3); //"视频(*.mp4)|*.mp4";
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtout9.Text = nameout9 = savefile.FileName;
            }
        }

        private void button6_Click_2(object sender, EventArgs e)
        {
            //if (Directory.Exists("avsfilter"))
            //{
            //    DirectoryInfo TheFolder = new DirectoryInfo("avsfilter");
            //    foreach (FileInfo FileName in TheFolder.GetFiles())
            //    {
            //        avs += "LoadPlugin(\"" + workpath + "\\avsfilter\\" + FileName + "\")\r\n";
            //    }
            //}
            avs += "LoadPlugin(\"avs\\plugins\\VSFilter.DLL\")\r\n";
            avs += string.Format("\r\nLWLibavVideoSource(\"{0}\",23.976,convertFPS=True)\r\nConvertToYV12()\r\nCrop(0,0,0,0)\r\nAddBorders(0,0,0,0)\r\n" + "TextSub(\"{1}\")\r\n#LanczosResize(1280,960)\r\n", namevideo9, namesub9);
            //avs += "\r\nDirectShowSource(\"" + namevideo9 + "\",23.976,convertFPS=True)\r\nConvertToYV12()\r\nCrop(0,0,0,0)\r\nAddBorders(0,0,0,0)\r\n" + "TextSub(\"" + namesub9 + "\")\r\n#LanczosResize(1280,960)\r\n";
            AVSScriptTextBox.Text = avs;
            avs = "";
        }

        private void txth264_TextChanged(object sender, EventArgs e)
        {
        }

        private void txtvideo_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtvideo.Text.ToString()))
            {
                Process.Start(txtvideo.Text.ToString());
            }
        }

        private void txtvideo4_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtvideo4.Text.ToString()))
            {
                Process.Start(txtvideo4.Text.ToString());
            }
        }

        private void txtout5_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtout5.Text.ToString()))
            {
                Process.Start(txtout5.Text.ToString());
            }
        }

        private void txtvideo8_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtvideo8.Text.ToString()))
            {
                Process.Start(txtvideo8.Text.ToString());
            }
        }

        private void txtvideo9_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtvideo9.Text.ToString()))
            {
                Process.Start(txtvideo9.Text.ToString());
            }
        }

        private void txtvideo6_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtvideo6.Text.ToString()))
            {
                Process.Start(txtvideo6.Text.ToString());
            }
        }

        private void txtvideo5_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtvideo5.Text.ToString()))
            {
                Process.Start(txtvideo5.Text.ToString());
            }
        }

        private void txtaudio3_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtaudio3.Text.ToString()))
            {
                Process.Start(txtaudio3.Text.ToString());
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            Process[] processes = Process.GetProcesses();
            for (int i = 0; i < processes.GetLength(0); i++)
            {
                //我是要找到我需要的YZT.exe的进程,可以根据ProcessName属性判断
                if (processes[i].ProcessName.Equals(Path.GetFileNameWithoutExtension(x264ExeComboBox.Text)))
                {
                    switch (x264PriorityComboBox.SelectedIndex)
                    {
                        case 0: processes[i].PriorityClass = ProcessPriorityClass.Idle; break;
                        case 1: processes[i].PriorityClass = ProcessPriorityClass.BelowNormal; break;
                        case 2: processes[i].PriorityClass = ProcessPriorityClass.Normal; break;
                        case 3: processes[i].PriorityClass = ProcessPriorityClass.AboveNormal; break;
                        case 4: processes[i].PriorityClass = ProcessPriorityClass.High; break;
                        case 5: processes[i].PriorityClass = ProcessPriorityClass.RealTime; break;
                    }
                }
            }
        }

        private void btnsub9_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.SUBTITLE_2); //"字幕(*.ass;*.ssa;*.srt;*.idx;*.sup)|*.ass;*.ssa;*.srt;*.idx;*.sup|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namesub9 = openFileDialog1.FileName;
                txtsub9.Text = namesub9;
            }
        }

        private void btnvideo9_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_7); //"视频(*.mp4;*.flv;*.mkv;*.wmv)|*.mp4;*.flv;*.mkv;*.wmv|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo9 = openFileDialog1.FileName;
                txtvideo9.Text = namevideo9;
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            AVSScriptTextBox.Clear();
        }

        private void btnClip_Click(object sender, EventArgs e)
        {
            if (namevideo4 == "")
            {
                ShowErrorMessage("请选择视频文件");
            }
            else if (nameout5 == "")
            {
                ShowErrorMessage("请选择输出文件");
            }
            else
            {
                //int h1 = int.Parse(maskb.Text.ToString().Substring(0, 2));
                //int m1 = int.Parse(maskb.Text.ToString().Substring(3, 2));
                //int s1 = int.Parse(maskb.Text.ToString().Substring(6, 2));
                //int h2 = int.Parse(maske.Text.ToString().Substring(0, 2));
                //int m2 = int.Parse(maske.Text.ToString().Substring(3, 2));
                //int s2 = int.Parse(maske.Text.ToString().Substring(6, 2));
                //clip = "\"" + workPath + "\\ffmpeg.exe\" -ss " + maskb.Text + " -to " + maske.Text + " -i  \"" + namevideo4 + "\" -acodec copy -vcodec copy \"" + nameout5 + "\" \r\ncmd";

                // "<workPath>\ffmpeg.exe" -i "<namevideo4>" -ss <maskb.Text> -to <maske.Text> -c copy "<nameout5>"
                clip = string.Format(@"""{0}\ffmpeg.exe"" -i ""{1}"" -ss {2} -to {3} -y -c copy ""{4}""",
                    workPath, namevideo4, maskb.Text, maske.Text, nameout5) + Environment.NewLine + "cmd";
                batpath = workPath + "\\clip.bat";
                LogRecord(clip);
                WriteBatFile(batpath, clip);
                Process.Start(batpath);
            }
        }

        private void cbX264_SelectedIndexChanged(object sender, EventArgs e)
        {
            XElement xel = xdoc.Element("root").Element("Video").Element("VideoEncoder").Element("x264").Elements()
                              .Where(x => x.Attribute("Name").Value == VideoPresetComboBox.Text).First();
            x264CustomParameterTextBox.Text = xel.Value;
        }

        private void cbFPS_SelectedIndexChanged(object sender, EventArgs e)
        {
            string ext = Path.GetExtension(namevideo).ToLower();
            if (cbFPS.SelectedIndex != 0 && ext != ".264" && ext != ".h264" && ext != ".hevc")
            {
                ShowWarningMessage("只有扩展名为.264 .h264 .hevc的流文件设置帧率(fps)才有效");
                cbFPS.SelectedIndex = 0;
            }
        }

        private void btnMIopen_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_6); //"视频(*.mp4;*.flv;*.mkv)|*.mp4;*.flv;*.mkv|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                MIvideo = openFileDialog1.FileName;
                MediaInfoTextBox.Text = GetMediaInfoString(MIvideo);
            }
        }

        private void btnMIplay_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(MIvideo);
            }
            catch
            { }
        }

        private void btnMIcopy_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(MItext);
        }

        private void btnvideo7_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_2); //"视频(*.mkv)|*.mkv";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo6 = openFileDialog1.FileName;
                txtvideo6.Text = namevideo6;
            }
        }

        private void btnextract7_Click(object sender, EventArgs e)
        {
            //MKV抽0
            ExtractTrack(namevideo6, 0);
        }

        private void MkvExtract1Button_Click(object sender, EventArgs e)
        {
            //MKV 抽1
            ExtractTrack(namevideo6, 1);
        }

        private void MkvExtract2Button_Click(object sender, EventArgs e)
        {
            //MKV 抽2
            ExtractTrack(namevideo6, 2);
        }

        private void MkvExtract3Button_Click(object sender, EventArgs e)
        {
            //MKV 抽3
            ExtractTrack(namevideo6, 3);
        }

        private void MkvExtract4Button_Click(object sender, EventArgs e)
        {
            //MKV 抽4
            ExtractTrack(namevideo6, 4);
        }

        private void txtMI_TextChanged(object sender, EventArgs e)
        {
            MItext = MediaInfoTextBox.Text;
        }

        private void txtAVScreate_Click(object sender, EventArgs e)
        {
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://www.sosg.net/read.php?tid=480646");
        }

        private void btnaextract2_Click(object sender, EventArgs e)
        {
            //MP4 抽取音频2
            ExtractAV(namevideo, "a", 1);
            //if (namevideo == "")
            //{
            //    MessageBox.Show("请选择视频文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            //}
            //else
            //{
            //    //aextract = "\"" + workPath + "\\mp4box.exe\" -raw 3 \"" + namevideo + "\"";
            //    aextract = "";
            //    aextract += Cmd.FormatPath(workPath + "\\ffmpeg.exe");
            //    aextract += " -i " + Cmd.FormatPath(namevideo);
            //    aextract += " -vn -sn -c:a:1 copy ";
            //    string outfile = Cmd.GetDir(namevideo) +
            //        Path.GetFileNameWithoutExtension(namevideo) + "_抽取音频2" + Path.GetExtension(namevideo);
            //    aextract += Cmd.FormatPath(outfile);
            //    batpath = workPath + "\\aextract.bat";
            //    File.WriteAllText(batpath, aextract, Encoding.Default);
            //    LogRecord(aextract);
            //    Process.Start(batpath);
            //}
        }

        private void btnaextract3_Click(object sender, EventArgs e)
        {
            //MP4 抽取音频3
            ExtractAV(namevideo, "a", 2);
            //if (namevideo == "")
            //{
            //    MessageBox.Show("请选择视频文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            //}
            //else
            //{
            //    //aextract = "\"" + workPath + "\\mp4box.exe\" -raw 4 \"" + namevideo + "\"";
            //    aextract = "";
            //    aextract += Cmd.FormatPath(workPath + "\\ffmpeg.exe");
            //    aextract += " -i " + Cmd.FormatPath(namevideo);
            //    aextract += " -vn -sn -c:a:2 copy ";
            //    string outfile = Cmd.GetDir(namevideo) +
            //        Path.GetFileNameWithoutExtension(namevideo) + "_抽取音频3" + Path.GetExtension(namevideo);
            //    aextract += Cmd.FormatPath(outfile);
            //    batpath = workPath + "\\aextract.bat";
            //    File.WriteAllText(batpath, aextract, Encoding.Default);
            //    LogRecord(aextract);
            //    Process.Start(batpath);
            //}
        }

        private void txtvideo6_TextChanged_1(object sender, EventArgs e)
        {
            if (File.Exists(txtvideo6.Text.ToString()))
            {
                namevideo6 = txtvideo6.Text;
            }
        }

        #region 帮助页面

        private void AboutBtn_Click(object sender, EventArgs e)
        {
            DateTime CompileDate = File.GetLastWriteTime(this.GetType().Assembly.Location); //获得程序编译时间
            QQMessageBox.Show(
                this,
                "岚珠工具箱 七七版\r\n主页：http://www.maruko.in/ \r\n编译日期：" + CompileDate.ToString(),
                "关于",
                QQMessageBoxIcon.Information,
                QQMessageBoxButtons.OK);
        }

        private void HomePageBtn_Click(object sender, EventArgs e)
        {
            Process.Start("http://www.maruko.in/");
        }

        #endregion 帮助页面

        #region 视频页面

        private void x264VideoBtn_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_8); //"视频(*.mp4;*.flv;*.mkv;*.avi;*.wmv;*.mpg;*.avs)|*.mp4;*.flv;*.mkv;*.avi;*.wmv;*.mpg;*.avs|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo2 = openFileDialog1.FileName;
                x264VideoTextBox.Text = namevideo2;
            }
        }

        private void x264OutBtn_Click(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_D_3); //"MPEG-4 视频(*.mp4)|*.mp4|Flash 视频(*.flv)|*.flv|Matroska 视频(*.mkv)|*.mkv|AVI 视频(*.avi)|*.avi|H.264 流(*.raw)|*.raw";
            savefile.FileName = Path.GetFileName(x264OutTextBox.Text);
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameout2 = savefile.FileName;
                x264OutTextBox.Text = nameout2;
            }
        }

        private void x264SubBtn_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.SUBTITLE_1); //"字幕(*.ass;*.ssa;*.srt)|*.ass;*.ssa;*.srt|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namesub2 = openFileDialog1.FileName;
                x264SubTextBox.Text = namesub2;
            }
        }

        private void x264StartBtn_Click(object sender, EventArgs e)
        {
            #region validation

            if (string.IsNullOrEmpty(namevideo2))
            {
                ShowErrorMessage("请选择视频文件");
                return;
            }

            if (!string.IsNullOrEmpty(namesub2) && !File.Exists(namesub2))
            {
                ShowErrorMessage("字幕文件不存在，请重新选择");
                return;
            }

            if (string.IsNullOrEmpty(nameout2))
            {
                ShowErrorMessage("请选择输出文件");
                return;
            }

            if (x264ExeComboBox.SelectedIndex == -1)
            {
                ShowErrorMessage("请选择视频编码器");
                return;
            }

            if (AudioEncoderComboBox.SelectedIndex != 0 && AudioEncoderComboBox.SelectedIndex != 1 && AudioEncoderComboBox.SelectedIndex != 5)
            {
                ShowWarningMessage("音频页面中的编码器未采用AAC将可能导致压制失败，建议将编码器改为QAAC、NeroAAC或FDKAAC。");
            }

            //防止未选择 x264 thread
            if (string.IsNullOrEmpty(x264ThreadsTextBox.Text.Trim()))
            {
                x264ThreadsTextBox.Text = "auto";
            }

            //目标文件已经存在提示是否覆盖
            if (File.Exists(x264OutTextBox.Text.Trim()))
            {
                DialogResult dgs = ShowQuestion("目标文件:\r\n\r\n" + x264OutTextBox.Text.Trim() + "\r\n\r\n已经存在,是否覆盖继续压制？", "目标文件已经存在");
                if (dgs == DialogResult.No) return;
            }

            //如果是AVS复制到C盘根目录
            if (Path.GetExtension(x264VideoTextBox.Text) == ".avs")
            {
                if (string.IsNullOrEmpty(Util.CheckAviSynth()) && string.IsNullOrEmpty(Util.CheckinternalAviSynth()))
                {
                    if (ShowQuestion("检测到本机未安装avisynth无法继续压制，是否去下载安装", "avisynth未安装") == DialogResult.Yes)
                        Process.Start("http://sourceforge.net/projects/avisynth2/");
                    return;
                }
                //if (File.Exists(tempavspath)) File.Delete(tempavspath);
                File.Copy(x264VideoTextBox.Text, tempavspath, true);
                namevideo2 = tempavspath;
                x264DemuxerComboBox.SelectedIndex = 0; //压制AVS始终使用分离器为auto
            }

            #endregion validation

            string ext = Path.GetExtension(nameout2).ToLower();
            bool hasAudio = false;
            string inputName = Path.GetFileNameWithoutExtension(namevideo2);
            string tempVideo = Path.Combine(tempfilepath, inputName + "_vtemp.mp4");
            string tempAudio = Path.Combine(tempfilepath, inputName + "_atemp" + getAudioExt());
            Util.ensureDirectoryExists(tempfilepath);

            #region Audio

            //检测是否含有音频
            MediaInfo MI = new MediaInfo();
            MI.Open(namevideo2);
            string audio = MI.Get(StreamKind.Audio, 0, "Format");
            if (!string.IsNullOrEmpty(audio))
                hasAudio = true;
            int audioMode = x264AudioModeComboBox.SelectedIndex;
            if (!hasAudio && x264AudioModeComboBox.SelectedIndex != 1)
            {
                DialogResult r = ShowQuestion("原视频不包含音频流，音频模式是否改为无音频流？", "提示");
                if (r == DialogResult.Yes)
                    audioMode = 1;
            }
            switch (audioMode)
            {
                case 0:
                    aextract = audiobat(namevideo2, tempAudio);
                    break;
                case 1:
                    aextract = string.Empty;
                    break;
                case 2:
                    if (audio.ToLower() == "aac")
                    {
                        tempAudio = Path.Combine(tempfilepath, inputName + "_atemp.aac");
                        aextract = ExtractAudio(namevideo2, tempAudio);
                    }
                    else
                    {
                        ShowInfoMessage("因音频编码非AAC故无法复制音频流，音频将被重编码。");
                        aextract = audiobat(namevideo2, tempAudio);
                    }
                    break;
                default:
                    break;
            }

            #endregion

            #region Video
            tempVideo = Path.Combine(tempfilepath, inputName + "_vtemp" + GetSelectedVideoTempExtension());
            if (x264mode == 2)
                x264 = x264bat(namevideo2, tempVideo, 1, namesub2) + "\r\n" +
                       x264bat(namevideo2, tempVideo, 2, namesub2);
            else x264 = x264bat(namevideo2, tempVideo, 0, namesub2);
            if (audioMode == 1)
                x264 = x264.Replace(tempVideo, nameout2);
            x264 += "\r\n";

            #endregion

            #region Mux

            //封装
            if (audioMode != 1)
            {
                mux = ffmuxbat(tempVideo, tempAudio, Util.ChangeExt(nameout2, ext));
                x264 = aextract + x264 + mux + "\r\n"
                    + "del \"" + tempVideo + "\"\r\n"
                    + "del \"" + tempAudio + "\"\r\n";
            }
            x264 += "\r\necho ===== one file is completed! =====\r\n";

            #endregion

            LogRecord(x264);
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(GetCultureName());
            WorkingForm wf = new WorkingForm(x264);
            wf.Owner = this;
            wf.Show();
            //x264 += "\r\ncmd";
            //batpath = workPath + "\\x264.bat";
            //File.WriteAllText(batpath, x264, Encoding.Default);
            //Process.Start(batpath);
        }

        private void x264AddPresetBtn_Click(object sender, EventArgs e)
        {
            try
            {
                string vPresetName = InputBox.Show("请输入这个预设名称", "请为预置配置命名", "新预置名称");
                if (!string.IsNullOrEmpty(vPresetName))
                {
                    var xl = xdoc.Element("root").Element("Video").Element("VideoEncoder").Element("x264");
                    XElement xelnew = new XElement("Parameter", x264CustomParameterTextBox.Text,
                                          new XAttribute("Name", vPresetName));
                    foreach (var item in xl.Elements())
                    {
                        if (item.Attribute("Name").Value == vPresetName)
                        {
                            ShowErrorMessage("预设名称已经存在", "预设名称重复");
                            return;
                        }
                    }
                    xl.Add(xelnew);
                    xdoc.Save("preset.xml");
                    LoadVideoPreset();
                    VideoPresetComboBox.SelectedIndex = VideoPresetComboBox.FindString(vPresetName);
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("添加失败! Reason: " + ex.Message);
            }
        }

        private void x264DeletePresetBtn_Click(object sender, EventArgs e)
        {
            if (ShowQuestion("确定要删除这条预设参数？", "提示") == DialogResult.Yes)
            {
                try
                {
                    var xls = xdoc.Element("root").Element("Video").Element("VideoEncoder").Element("x264").Elements();
                    foreach (var item in xls)
                    {
                        if (item.Attribute("Name").Value == VideoPresetComboBox.Text)
                            item.Remove();
                    }
                    xdoc.Save("preset.xml");
                    LoadVideoPreset();
                }
                catch (Exception ex)
                {
                    ShowErrorMessage("删除失败! Reason: " + ex.Message);
                }
            }
        }

        private void x264Mode2RadioButton_CheckedChanged(object sender, EventArgs e)
        {
            x264mode = 2;
            lbrate.Visible = true;
            x264BitrateNum.Visible = true;
            label12.Visible = true;
            //x264FpsComboBox.Visible = true;
            //lbFPS2.Visible = true;
            lbwidth.Visible = true;
            lbheight.Visible = true;
            x264WidthNum.Visible = true;
            x264HeightNum.Visible = true;
            MaintainResolutionCheckBox.Visible = true;
            lbcrf.Visible = false;
            x264CRFNum.Visible = false;
            label4.Visible = false;
            x264CustomParameterTextBox.Visible = false;
            VideoPresetComboBox.Visible = false;
            x264AddPresetBtn.Visible = false;
            x264DeletePresetBtn.Visible = false;
            UpdateEstimatedSize();
        }

        private void x264Mode3RadioButton_CheckedChanged(object sender, EventArgs e)
        {
            x264mode = 0;
            label4.Visible = true;
            x264CustomParameterTextBox.Visible = true;
            VideoPresetComboBox.Visible = true;
            x264AddPresetBtn.Visible = true;
            x264DeletePresetBtn.Visible = true;
            lbwidth.Visible = false;
            lbheight.Visible = false;
            x264WidthNum.Visible = false;
            x264HeightNum.Visible = false;
            MaintainResolutionCheckBox.Visible = false;
            lbrate.Visible = false;
            x264BitrateNum.Visible = false;
            label12.Visible = false;
            lbcrf.Visible = false;
            x264CRFNum.Visible = false;
            //x264FpsComboBox.Visible = false;
            //lbFPS2.Visible = false;
            UpdateEstimatedSize();
        }

        private void x264Mode1RadioButton_CheckedChanged(object sender, EventArgs e)
        {
            x264mode = 1;
            lbcrf.Visible = true;
            x264CRFNum.Visible = true;
            //x264FpsComboBox.Visible = true;
            //lbFPS2.Visible = true;
            lbwidth.Visible = true;
            lbheight.Visible = true;
            x264WidthNum.Visible = true;
            x264HeightNum.Visible = true;
            MaintainResolutionCheckBox.Visible = true;
            lbrate.Visible = false;
            x264BitrateNum.Visible = false;
            label12.Visible = false;
            label4.Visible = false;
            x264CustomParameterTextBox.Visible = false;
            VideoPresetComboBox.Visible = false;
            x264AddPresetBtn.Visible = false;
            x264DeletePresetBtn.Visible = false;
            UpdateEstimatedSize();
        }

        private void x264PriorityComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            string processName = "ffmpeg";
            Process[] processes = Process.GetProcesses();
            //if (x264PriorityComboBox.SelectedIndex == 4 || x264PriorityComboBox.SelectedIndex == 5)
            //{
            //    if (MessageBox.Show("优先级那么高的话会严重影响其他进程的运行速度，\r\n是否继续？", "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
            //    {
            //        x264PriorityComboBox.SelectedIndex = 2;
            //    }
            //}
            //遍历电脑中的进程
            for (int i = 0; i < processes.GetLength(0); i++)
            {
                //我是要找到我需要的YZT.exe的进程,可以根据ProcessName属性判断
                if (processes[i].ProcessName.Equals(processName))
                {
                    switch (x264PriorityComboBox.SelectedIndex)
                    {
                        case 0: processes[i].PriorityClass = ProcessPriorityClass.Idle; break;
                        case 1: processes[i].PriorityClass = ProcessPriorityClass.BelowNormal; break;
                        case 2: processes[i].PriorityClass = ProcessPriorityClass.Normal; break;
                        case 3: processes[i].PriorityClass = ProcessPriorityClass.AboveNormal; break;
                        case 4: processes[i].PriorityClass = ProcessPriorityClass.High; break;
                        case 5: processes[i].PriorityClass = ProcessPriorityClass.RealTime; break;
                    }
                }
            }
        }

        private void x264VideoTextBox_TextChanged(object sender, EventArgs e)
        {
            string path = x264VideoTextBox.Text;
            if (File.Exists(path))
            {
                namevideo2 = path;
                int num = 1;
                string encType = GetSelectedVideoOutputSuffix();
                string outputExt = GetSelectedVideoOutputExtension();
                x264OutTextBox.Text = Util.ChangeExt(namevideo2, string.Format("_{0}{1}", encType, outputExt));
                while (namevideo2.Equals(x264OutTextBox.Text) || File.Exists(x264OutTextBox.Text))
                {
                    x264OutTextBox.Text = Util.ChangeExt(namevideo2, string.Format("_new_file({0})_{1}{2}", num, encType, outputExt));
                    num++;
                }

                if (Path.GetExtension(namevideo2) != ".avs")
                {
                    string[] subExt = { ".ass", ".ssa", ".srt" };
                    foreach (string ext in subExt)
                    {
                        if (File.Exists(Util.ChangeExt(namevideo2, ext)))
                        {
                            x264SubTextBox.Text = Util.ChangeExt(namevideo2, ext);
                            break;
                        }
                        else
                            x264SubTextBox.Text = string.Empty;
                    }
                }

            }
        }

        private void x264OutTextBox_TextChanged(object sender, EventArgs e)
        {
            nameout2 = x264OutTextBox.Text;
        }

        private void x264SubTextBox_TextChanged(object sender, EventArgs e)
        {
            namesub2 = x264SubTextBox.Text;
        }

        #region 预计大小

        /// <summary>
        /// 刷新预计输出大小显示
        /// </summary>
        private void UpdateEstimatedSize()
        {
            if (EstimatedSizeLabel == null)
                return;
            // 若文本仍是占位（含 "--"），说明初始化或语言切换后尚未更新，先推导当前语言的格式串
            // （"预计大小：--" -> "预计大小：{0}"）
            string current = EstimatedSizeLabel.Text ?? "";
            if (current.Contains("--"))
                EstimatedSizeLabel.Tag = current.Replace("--", "{0}");

            string sizeText = "--";
            string filePath = GetCurrentBatchInputFile();
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                double? sizeMb = EstimateOutputSize(filePath);
                if (sizeMb.HasValue)
                    sizeText = FormatSize(sizeMb.Value);
            }
            // 使用多语言格式串
            string fmt = EstimatedSizeLabel.Tag as string;
            if (string.IsNullOrEmpty(fmt))
                fmt = "预计大小：{0}";
            EstimatedSizeLabel.Text = string.Format(fmt, sizeText);
        }

        /// <summary>
        /// 获取当前用于估算的批量输入文件（优先当前选中项）
        /// </summary>
        private string GetCurrentBatchInputFile()
        {
            if (lbAuto == null || lbAuto.Items.Count == 0)
                return null;
            if (lbAuto.SelectedIndex >= 0)
                return lbAuto.Items[lbAuto.SelectedIndex].ToString();
            return lbAuto.Items[0].ToString();
        }

        /// <summary>
        /// 估算指定输入文件压制后的输出大小（MB）
        /// </summary>
        private double? EstimateOutputSize(string inputFile)
        {
            try
            {
                MediaInfo MI = new MediaInfo();
                if (MI.Open(inputFile) <= 0)
                    return null;

                // 时长（毫秒）
                double durationMs = ParseDouble(MI.Get(StreamKind.General, 0, "Duration"));
                if (durationMs <= 0)
                {
                    MI.Close();
                    return null;
                }
                double durationSec = durationMs / 1000.0;

                // 源视频信息
                double srcW = ParseDouble(MI.Get(StreamKind.Video, 0, "Width"));
                double srcH = ParseDouble(MI.Get(StreamKind.Video, 0, "Height"));
                double fps = ParseDouble(MI.Get(StreamKind.Video, 0, "FrameRate"));
                if (fps <= 0)
                    fps = 30.0;
                double srcVideoKbps = ParseDouble(MI.Get(StreamKind.Video, 0, "BitRate")) / 1000.0;
                double srcAudioKbps = ParseDouble(MI.Get(StreamKind.Audio, 0, "BitRate")) / 1000.0;
                MI.Close();

                // 输出分辨率（考虑缩放设置）
                double outW = srcW, outH = srcH;
                if (x264WidthNum.Value > 0 && x264HeightNum.Value > 0 && !MaintainResolutionCheckBox.Checked)
                {
                    outW = (double)x264WidthNum.Value;
                    outH = (double)x264HeightNum.Value;
                }
                if (outW <= 0 || outH <= 0)
                {
                    outW = srcW;
                    outH = srcH;
                }
                if (outW <= 0 || outH <= 0)
                    return null;

                // 视频码率估算
                double videoKbps;
                if (x264mode == 2)
                    videoKbps = (double)x264BitrateNum.Value;                            // 固定码率模式
                else if (x264mode == 1)
                    videoKbps = EstimateQualityBitrate((double)x264CRFNum.Value, outW, outH, fps); // CRF 质量模式（启发式）
                else
                    videoKbps = srcVideoKbps > 0 ? srcVideoKbps : 3000.0;                // 自定义参数：参考源视频码率

                // 音频码率估算
                double audioKbps = EstimateAudioBitrate(srcAudioKbps);

                double totalKbps = videoKbps + audioKbps;
                // kbps*1000(bit/s) -> byte/s (/8) -> 总字节 -> MB
                double sizeMb = totalKbps * 1000.0 / 8.0 * durationSec / (1024.0 * 1024.0);
                return sizeMb;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// CRF/质量模式下按启发式估算视频码率（kbps）
        /// </summary>
        private double EstimateQualityBitrate(double crf, double width, double height, double fps)
        {
            // 基准：1080p@30fps、H.264、CRF 23 约为 4500 kbps
            double baseKbps = 4500.0;
            // CRF 每降 1，码率约 ×1.12；每升 1，约 ÷1.12
            double crfFactor = Math.Pow(1.12, 23.0 - crf);
            // 分辨率：按面积比例（相对 1920×1080）
            double resFactor = (width * height) / (1920.0 * 1080.0);
            // 帧率：相对 30fps
            double fpsFactor = fps / 30.0;
            // 编码器：HEVC 约可节省 38% 码率
            double codecFactor = (GetSelectedVideoPresetKind() == VideoPresetKind.Hevc) ? 0.62 : 1.0;
            // 位深：10bit/12bit 码率略高
            int bitDepth = GetSelectedVideoBitDepth();
            double bitDepthFactor = bitDepth >= 12 ? 1.10 : (bitDepth >= 10 ? 1.05 : 1.0);
            // GPU 硬件编码效率略低于软件编码（同质量码率略高）
            double gpuFactor = (GpuAccelerationCheckBox.Checked || HybridProcessingCheckBox.Checked) ? 1.08 : 1.0;

            return baseKbps * crfFactor * resFactor * fpsFactor * codecFactor * bitDepthFactor * gpuFactor;
        }

        /// <summary>
        /// 估算音频码率（kbps）
        /// </summary>
        private double EstimateAudioBitrate(double srcAudioKbps)
        {
            double kbps = 128.0;
            int idx = AudioEncoderComboBox.SelectedIndex;
            switch (idx)
            {
                case 0: // NeroAAC
                case 1: // QAAC
                case 5: // FDKAAC
                case 6: // AC3
                    double v;
                    if (double.TryParse(AudioBitrateComboBox.Text, out v) && v > 0)
                        kbps = v;
                    // 自定义音频参数时仍以码率框数值作为粗略参考
                    break;
                case 2: // WAV（44100Hz×16bit×2ch ≈ 1411 kbps）
                    kbps = 1411.0;
                    break;
                case 3: // ALAC
                case 4: // FLAC（无损，约为源音频码率的 50-60%）
                    kbps = srcAudioKbps > 0 ? srcAudioKbps * 0.6 : 512.0;
                    break;
            }
            return kbps;
        }

        /// <summary>
        /// 按 MB/GB 格式化大小
        /// </summary>
        private static string FormatSize(double mb)
        {
            if (mb >= 1024.0)
                return (mb / 1024.0).ToString("0.0") + " GB";
            return mb.ToString("0.0") + " MB";
        }

        private static double ParseDouble(string s)
        {
            double d;
            double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out d);
            return d;
        }

        #endregion 预计大小

        private void x264BatchClearBtn_Click(object sender, EventArgs e)
        {
            lbAuto.Items.Clear();
            UpdateEstimatedSize();
        }

        private void x264BatchDeleteBtn_Click(object sender, EventArgs e)
        {
            if (lbAuto.Items.Count > 0)
            {
                if (lbAuto.SelectedItems.Count > 0)
                {
                    int index = lbAuto.SelectedIndex;
                    lbAuto.Items.RemoveAt(lbAuto.SelectedIndex);
                    if (index == lbAuto.Items.Count)
                    {
                        lbAuto.SelectedIndex = index - 1;
                    }
                    if (index >= 0 && index < lbAuto.Items.Count && lbAuto.Items.Count > 0)
                    {
                        lbAuto.SelectedIndex = index;
                    }
                }
            }
            UpdateEstimatedSize();
        }

        private void x264BatchAddBtn_Click(object sender, EventArgs e)
        {
            openFileDialog1.Multiselect = true;
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.ALL); //"所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                lbAuto.Items.AddRange(openFileDialog1.FileNames);
            }
            openFileDialog1.Multiselect = false;
            UpdateEstimatedSize();
        }

        #endregion 视频页面

        #region 音频界面

        // <summary>
        /// 是否安装 Apple Application Support
        /// </summary>
        /// <returns>true:安装 false:没有安装</returns>
        private bool isAppleAppSupportInstalled()
        {
            Microsoft.Win32.RegistryKey uninstallNode_1 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Wow6432Node\Apple Inc.\Apple Application Support"); //x64 OS
            Microsoft.Win32.RegistryKey uninstallNode_2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Apple Inc.\Apple Application Support"); //x86 OS
            if (uninstallNode_1 != null || uninstallNode_2 != null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void AudioEncoderComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (AudioEncoderComboBox.SelectedIndex)
            {
                case 0:
                    if (File.Exists(txtaudio2.Text))
                        AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_AAC.mp4");
                    AudioBitrateComboBox.Enabled = true;
                    AudioBitrateRadioButton.Enabled = true;
                    AudioCustomizeRadioButton.Enabled = true;
                    if (AudioCustomizeRadioButton.Checked)
                    {
                        audioDeleteBt.Visible = true;
                        audioAddBt.Visible = true;
                    }
                    break;

                case 1:
                    if (!isAppleAppSupportInstalled())
                    {
                        if (ShowQuestion("Apple Application Support未安装.\r\n音频编码器QAAC可能无法使用.\r\n\r\n是否前往QuickTime下载页面?", "Apple Application Support未安装") == DialogResult.Yes)
                            Process.Start("http://www.apple.com/cn/quicktime/download");
                    }
                    if (File.Exists(txtaudio2.Text))
                        AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_AAC.m4a");
                    AudioBitrateComboBox.Enabled = true;
                    AudioBitrateRadioButton.Enabled = true;
                    AudioCustomizeRadioButton.Enabled = true;
                    if (AudioCustomizeRadioButton.Checked)
                    {
                        audioDeleteBt.Visible = true;
                        audioAddBt.Visible = true;
                    }
                    break;

                case 2:
                    if (File.Exists(txtaudio2.Text))
                        AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_WAV.wav");
                    AudioBitrateComboBox.Enabled = false;
                    AudioBitrateRadioButton.Enabled = false;
                    AudioCustomizeRadioButton.Enabled = false;
                    audioDeleteBt.Visible = false;
                    audioAddBt.Visible = false;
                    break;

                case 3:
                    if (File.Exists(txtaudio2.Text))
                        AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_ALAC.m4a");
                    AudioBitrateComboBox.Enabled = false;
                    AudioBitrateRadioButton.Enabled = false;
                    AudioCustomizeRadioButton.Enabled = false;
                    audioDeleteBt.Visible = false;
                    audioAddBt.Visible = false;
                    break;

                case 4:
                    if (File.Exists(txtaudio2.Text))
                        AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_FLAC.flac");
                    AudioBitrateComboBox.Enabled = false;
                    AudioBitrateRadioButton.Enabled = false;
                    AudioCustomizeRadioButton.Enabled = false;
                    audioDeleteBt.Visible = false;
                    audioAddBt.Visible = false;
                    break;

                case 5:
                    if (File.Exists(txtaudio2.Text))
                        AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_AAC.m4a");
                    AudioBitrateComboBox.Enabled = true;
                    AudioBitrateRadioButton.Enabled = true;
                    AudioCustomizeRadioButton.Enabled = true;
                    if (AudioCustomizeRadioButton.Checked)
                    {
                        audioDeleteBt.Visible = true;
                        audioAddBt.Visible = true;
                    }
                    break;

                case 6:
                    if (File.Exists(txtaudio2.Text))
                        AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_AC3.ac3");
                    AudioBitrateComboBox.Enabled = true;
                    AudioBitrateRadioButton.Enabled = true;
                    AudioCustomizeRadioButton.Enabled = false;
                    audioDeleteBt.Visible = false;
                    audioAddBt.Visible = false;
                    break;

                default:
                    break;
            }

            XElement xAudios = xdoc.Element("root").Element("Audio").Element("AudioEncoder").Element(AudioEncoderComboBox.Text);
            if (xAudios != null)
            {
                AudioPresetComboBox.Items.Clear();
                AudioCustomParameterTextBox.Text = string.Empty;
                foreach (XElement item in xAudios.Elements())
                {
                    AudioPresetComboBox.Items.Add(item.Attribute("Name").Value);
                    AudioPresetComboBox.SelectedIndex = 0;
                }
            }
            else
            {
                AudioPresetComboBox.Items.Clear();
                AudioCustomParameterTextBox.Text = string.Empty;
            }
            UpdateEstimatedSize();

        }

        private void AudioListBox_DragDrop(object sender, DragEventArgs e)
        {
            ListBox listbox = (ListBox)sender;
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false))
            {
                String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);
                foreach (String s in files)
                {
                    listbox.Items.Add(s);
                }
                return;
            }
            indexoftarget = listbox.IndexFromPoint(listbox.PointToClient(new Point(e.X, e.Y)));
            if (indexoftarget != ListBox.NoMatches)
            {
                string temp = listbox.Items[indexoftarget].ToString();
                listbox.Items[indexoftarget] = listbox.Items[indexofsource];
                listbox.Items[indexofsource] = temp;
                listbox.SelectedIndex = indexoftarget;
            }
        }

        private void AudioListBox_DragOver(object sender, DragEventArgs e)
        {
            ListBox listbox = (ListBox)sender;
            if (e.Data.GetDataPresent(typeof(System.String)) && ((ListBox)sender).Equals(listbox))
            {
                e.Effect = DragDropEffects.Move;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Link;
            }
            else e.Effect = DragDropEffects.None;
        }

        private void AudioListBox_MouseDown(object sender, MouseEventArgs e)
        {
            indexofsource = ((ListBox)sender).IndexFromPoint(e.X, e.Y);
            if (indexofsource != ListBox.NoMatches)
            {
                ((ListBox)sender).DoDragDrop(((ListBox)sender).Items[indexofsource].ToString(), DragDropEffects.All);
            }
        }

        private void AudioBatchButton_Click(object sender, EventArgs e)
        {
            if (AudioListBox.Items.Count != 0)
            {
                string finish, outputExt, codec;
                aac = "";
                switch (AudioEncoderComboBox.SelectedIndex)
                {
                    case 0: outputExt = "mp4"; codec = "AAC"; break;
                    case 1: outputExt = "m4a"; codec = "AAC"; break;
                    case 2: outputExt = "wav"; codec = "WAV"; break;
                    case 3: outputExt = "m4a"; codec = "ALAC"; break;
                    case 4: outputExt = "flac"; codec = "FLAC"; break;
                    case 5: outputExt = "m4a"; codec = "AAC"; break;
                    case 6: outputExt = "ac3"; codec = "AC3"; break;
                    default: outputExt = "aac"; codec = "AAC"; break;
                }
                for (int i = 0; i < this.AudioListBox.Items.Count; i++)
                {
                    string outname = "_" + codec + "." + outputExt;
                    finish = Util.ChangeExt(AudioListBox.Items[i].ToString(), outname);
                    aac += audiobat(AudioListBox.Items[i].ToString(), finish);
                    aac += "\r\n";
                }
                aac += "\r\ncmd";
                batpath = workPath + "\\aac.bat";
                WriteBatFile(batpath, aac);
                LogRecord(aac);
                Process.Start(batpath);
            }
            else ShowErrorMessage("请输入文件！");
        }

        private void btnaudio2_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.ALL); //"所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameaudio2 = openFileDialog1.FileName;
                txtaudio2.Text = nameaudio2;
            }
        }

        private void btnout3_Click(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.ALL); //"所有文件(*.*)|*.*";
            //savefile.Filter = "音频(*.aac;*.wav;*.m4a;*.flac)|*.aac*.wav;*.m4a;*.flac;";
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                nameout3 = savefile.FileName + getAudioExt();
                AudioOutputTextBox.Text = nameout3;
            }
        }

        private void btnaac_Click(object sender, EventArgs e)
        {
            if (nameaudio2 == "")
            {
                ShowErrorMessage("请选择音频文件");
            }
            else if (nameout3 == "")
            {
                ShowErrorMessage("请选择输出文件");
            }
            else
            {
                batpath = workPath + "\\aac.bat";
                WriteBatFile(batpath, audiobat(nameaudio2, nameout3));
                LogRecord(audiobat(nameaudio2, nameout3));
                Process.Start(batpath);
            }
        }

        private void txtaudio2_TextChanged(object sender, EventArgs e)
        {
            if (File.Exists(txtaudio2.Text.ToString()))
            {
                nameaudio2 = txtaudio2.Text;
                switch (AudioEncoderComboBox.SelectedIndex)
                {
                    case 0: AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_AAC.mp4"); break;
                    case 1: AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_AAC.m4a"); break;
                    case 2: AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_WAV.wav"); break;
                    case 3: AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_ALAC.m4a"); break;
                    case 4: AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_FLAC.flac"); break;
                    case 5: AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_AAC.m4a"); break;
                    case 6: AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_AC3.ac3"); break;
                    default: AudioOutputTextBox.Text = Util.ChangeExt(txtaudio2.Text, "_AAC.aac"); break;
                }
            }
        }

        private void txtout3_TextChanged(object sender, EventArgs e)
        {
            nameout3 = AudioOutputTextBox.Text;
        }

        private void txtaudio2_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(txtaudio2.Text.ToString()))
            {
                Process.Start(txtaudio2.Text.ToString());
            }
        }

        private void radioButton5_CheckedChanged(object sender, EventArgs e)
        {
            lbaacrate.Visible = false;
            lbaackbps.Visible = false;
            AudioBitrateComboBox.Visible = false;
            AudioCustomParameterTextBox.Visible = true;
            AudioPresetLabel.Visible = true;
            AudioPresetComboBox.Visible = true;
            audioDeleteBt.Visible = true;
            audioAddBt.Visible = true;
        }

        private void radioButton4_CheckedChanged(object sender, EventArgs e)
        {
            lbaacrate.Visible = true;
            lbaackbps.Visible = true;
            AudioBitrateComboBox.Visible = true;
            AudioCustomParameterTextBox.Visible = false;
            AudioPresetLabel.Visible = false;
            AudioPresetComboBox.Visible = false;
            audioDeleteBt.Visible = false;
            audioAddBt.Visible = false;
        }

        private void AudioAddButton_Click(object sender, EventArgs e)
        {
            openFileDialog1.Multiselect = true;
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.ALL); //"所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                AudioListBox.Items.AddRange(openFileDialog1.FileNames);
            }
            openFileDialog1.Multiselect = false;
        }

        private void AudioDeleteButton_Click(object sender, EventArgs e)
        {
            if (AudioListBox.Items.Count > 0)
            {
                if (AudioListBox.SelectedItems.Count > 0)
                {
                    int index = AudioListBox.SelectedIndex;
                    AudioListBox.Items.RemoveAt(AudioListBox.SelectedIndex);
                    if (index == AudioListBox.Items.Count)
                    {
                        AudioListBox.SelectedIndex = index - 1;
                    }
                    if (index >= 0 && index < AudioListBox.Items.Count && AudioListBox.Items.Count > 0)
                    {
                        AudioListBox.SelectedIndex = index;
                    }
                }
            }
        }

        private void AudioClearButton_Click(object sender, EventArgs e)
        {
            AudioListBox.Items.Clear();
        }

        #endregion 音频界面

        #region AVS页面

        private void GenerateAVS()
        {
            //if (Directory.Exists("avsfilter"))
            //{
            //    DirectoryInfo TheFolder = new DirectoryInfo("avsfilter");
            //    foreach (FileInfo FileName in TheFolder.GetFiles())
            //    {
            //        avs += "LoadPlugin(\"" + workpath + "\\avsfilter\\" + FileName + "\")\r\n";
            //    }
            //}
            avsBuilder.Remove(0, avsBuilder.Length);
            string vsfilterDLLPath = Path.Combine(workPath, @"avs\plugins\VSFilter.DLL");
            string SupTitleDLLPath = Path.Combine(workPath, @"avs\plugins\SupTitle.dll");
            string LSMASHSourceDLLPath = Path.Combine(workPath, @"avs\plugins\LSMASHSource.DLL");
            string undotDLLPath = Path.Combine(workPath, @"avs\plugins\UnDot.DLL");
            string extInput = Path.GetExtension(namevideo9).ToLower();
            avsBuilder.AppendLine("LoadPlugin(\"" + LSMASHSourceDLLPath + "\")");
            if (Path.GetExtension(namesub9).ToLower() == ".sup")
                avsBuilder.AppendLine("LoadPlugin(\"" + SupTitleDLLPath + "\")");
            else
                avsBuilder.AppendLine("LoadPlugin(\"" + vsfilterDLLPath + "\")");
            if (UndotCheckBox.Checked)
                avsBuilder.AppendLine("LoadPlugin(\"" + undotDLLPath + "\")");
            if (extInput == ".mp4"
                   || extInput == ".mov"
                   || extInput == ".qt"
                   || extInput == ".3gp"
                   || extInput == ".3g2")
                avsBuilder.AppendLine("LSMASHVideoSource(\"" + namevideo9 + "\")");
            else
                avsBuilder.AppendLine("LWLibavVideoSource(\"" + namevideo9 + "\")");
            avsBuilder.AppendLine("ConvertToYV12()");
            if (UndotCheckBox.Checked)
                avsBuilder.AppendLine("Undot()");
            if (TweakCheckBox.Checked)
                avsBuilder.AppendLine("Tweak(" + TweakChromaNumericUpDown.Value.ToString() + ", " + TweakSaturationNumericUpDown.Value.ToString() + ", " + TweakBrightnessNumericUpDown.Value.ToString() + ", " + TweakContrastNumericUpDown.Value.ToString() + ")");
            if (LevelsCheckBox.Checked)
                avsBuilder.AppendLine("Levels(0," + LevelsNumericUpDown.Value.ToString() + ",255,0,255)");
            if (LanczosResizeCheckBox.Checked)
                avsBuilder.AppendLine("LanczosResize(" + AVSWidthNumericUpDown.Value.ToString() + "," + AVSHeightNumericUpDown.Value.ToString() + ")");
            if (SharpenCheckBox.Checked)
                avsBuilder.AppendLine("Sharpen(" + SharpenNumericUpDown.Value.ToString() + ")");
            if (CropCheckBox.Checked)
                avsBuilder.AppendLine("Crop(" + AVSCropTextBox.Text + ")");
            if (AddBordersCheckBox.Checked)
                avsBuilder.AppendLine("AddBorders(" + AddBorders1NumericUpDown.Value.ToString() + "," + AddBorders2NumericUpDown.Value.ToString() + "," + AddBorders3NumericUpDown.Value.ToString() + "," + AddBorders4NumericUpDown.Value.ToString() + ")");
            if (!string.IsNullOrEmpty(txtsub9.Text))
            {
                if (Path.GetExtension(namesub9).ToLower() == ".idx")
                    avsBuilder.AppendLine("vobsub(\"" + namesub9 + "\")");
                else if (Path.GetExtension(namesub9).ToLower() == ".sup")
                    avsBuilder.AppendLine("SupTitle(\"" + namesub9 + "\")");
                else
                    avsBuilder.AppendLine("TextSub(\"" + namesub9 + "\")");
            }
            if (TrimCheckBox.Checked)
                avsBuilder.AppendLine("Trim(" + TrimStartNumericUpDown.Value.ToString() + "," + TrimEndNumericUpDown.Value.ToString() + ")");
            AVSScriptTextBox.Text = avsBuilder.ToString();
        }

        #region 更改AVS

        private void TweakCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void LanczosResizeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void AddBordersCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void CropCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void TrimCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void LevelsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void SharpenCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void UndotCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void TweakChromaNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void TweakSaturationNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void TweakBrightnessNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void TweakContrastNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void AVSWidthNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void AVSHeightNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void AddBorders1NumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void AddBorders2NumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void AddBorders3NumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void AddBorders4NumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void AVSCropTextBox_TextChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void TrimStartNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void TrimEndNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void LevelsNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        private void SharpenNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            GenerateAVS();
        }

        #endregion 更改AVS

        #endregion AVS页面

        private void ExtractMP4Button_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_5); //"视频(*.mp4)|*.mp4|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                namevideo = openFileDialog1.FileName;
                ExtractMP4TextBox.Text = namevideo;
            }
        }

        private void txtAVS_TextChanged(object sender, EventArgs e)
        {
            Match m = Regex.Match(AVSScriptTextBox.Text, "ource\\(\"[a-zA-Z]:\\\\.+\\.\\w+\"");
            if (m.Success)
            {
                string str = m.ToString();
                str = str.Replace("ource(\"", "");
                str = str.Replace("\"", "");
                str = Util.ChangeExt(str, "_AVS.mp4");
                txtout9.Text = str;
            }
        }

        public void Log(string path)
        {
            ProcessStartInfo start = new ProcessStartInfo(path);//设置运行的命令行文件问ping.exe文件，这个文件系统会自己找到
            //如果是其它exe文件，则有可能需要指定详细路径，如运行winRar.exe
            start.CreateNoWindow = false;//不显示dos命令行窗口
            start.RedirectStandardOutput = true;//
            start.RedirectStandardInput = true;//
            start.UseShellExecute = false;//是否指定操作系统外壳进程启动程序
            Process p = Process.Start(start);
            StreamReader reader = p.StandardOutput;//截取输出流
            string line = reader.ReadLine();//每次读取一行
            StringBuilder log = new StringBuilder(2000);
            while (!reader.EndOfStream)
            {
                log.Append(line + "\r\n");
                line = reader.ReadLine();
            }
            p.WaitForExit();//等待程序执行完退出进程
            File.WriteAllText(startpath + "\\log_" + DateTime.Now.ToString("yyyy'-'MM'-'dd'_'HH'-'mm'-'ss'-'fff") + ".txt", log.ToString(), Encoding.Default);
            p.Close();//关闭进程
            reader.Close();//关闭流
        }

        public void LogRecord(string log)
        {
            Util.ensureDirectoryExists(logPath);
            File.AppendAllText(logFileName,
                "===========" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "===========\r\n" + log + "\r\n\r\n", Encoding.Default);
        }

        private void DeleteLogButton_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(logPath))
            {
                Util.DeleteDirectoryIfExists(logPath, true);
                ShowInfoMessage("已经删除日志文件。");
            }
            else ShowInfoMessage("没有找到日志文件。");
        }

        private void ViewLogButton_Click(object sender, EventArgs e)
        {
            if (File.Exists(logFileName))
            {
                Process.Start(logFileName);
            }
            else ShowInfoMessage("没有找到日志文件。");
        }

        private void x264PathButton_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK)
                x264PathTextBox.Text = fbd.SelectedPath;
        }

        private void ExtractMP4TextBox_TextChanged(object sender, EventArgs e)
        {
            namevideo = ExtractMP4TextBox.Text;
        }

        private void MaintainResolutionCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (MaintainResolutionCheckBox.Checked)
            {
                x264WidthNum.Value = 0;
                x264HeightNum.Value = 0;
                x264WidthNum.Enabled = false;
                x264HeightNum.Enabled = false;
            }
            else
            {
                x264WidthNum.Enabled = true;
                x264HeightNum.Enabled = true;
            }
            UpdateEstimatedSize();
        }

        #region globalization

        public static void SetLang(string lang, Form form, Type formType)
        {
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(lang);
            if (form != null)
            {
                ComponentResourceManager resources = new ComponentResourceManager(formType);
                resources.ApplyResources(form, "$this");
                AppLang(form, resources);
            }
        }

        private static void AppLang(Control control, ComponentResourceManager resources)
        {
            foreach (Control c in control.Controls)
            {
                resources.ApplyResources(c, c.Name);
                AppLang(c, resources);
            }
        }

        private void languageComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            //StreamReader sr;
            x264Mode1RadioButton.Checked = true;
            AudioBitrateRadioButton.Checked = true;
            int x264AudioModeComboBoxIndex = 0;
            switch (languageComboBox.SelectedIndex)
            {
                case 0:
                    SetLang("zh-CN", this, typeof(MainForm));
                    this.Text = string.Format("岚珠工具箱 {0}", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                    x264PriorityComboBox.Items.Clear();
                    x264PriorityComboBox.Items.AddRange(new string[] { "低", "低于标准", "普通", "高于标准", "高", "实时" });
                    x264PriorityComboBox.SelectedIndex = 2;
                    x264AudioModeComboBoxIndex = x264AudioModeComboBox.SelectedIndex;
                    x264AudioModeComboBox.Items.Clear();
                    x264AudioModeComboBox.Items.Add("压制音频");
                    x264AudioModeComboBox.Items.Add("无音频流");
                    x264AudioModeComboBox.Items.Add("复制音频流");
                    x264AudioModeComboBox.SelectedIndex = x264AudioModeComboBoxIndex;
                    x264VideoTextBox.EmptyTextTip = "可以把文件拖拽到这里";
                    x264SubTextBox.EmptyTextTip = "双击清空字幕文件文本框";
                    //x264OutTextBox.EmptyTextTip = "宽度和高度全为0即不改变分辨率";
                    x264PathTextBox.EmptyTextTip = "字幕与视频同名；不同名仅有语言后缀时在此输入";
                    //txtvideo3.EmptyTextTip = "音频参数在音频选项卡设定";
                    ExtractMP4TextBox.EmptyTextTip = "抽取的视频或音频在原视频目录下";
                    txtvideo8.EmptyTextTip = "抽取的视频或音频在原视频目录下";
                    txtvideo6.EmptyTextTip = "抽取的视频或音频在原视频目录下";
                    //load Help Text
                    if (File.Exists(startpath + "\\help.rtf"))
                    {
                        HelpTextBox.LoadFile(startpath + "\\help.rtf");
                    }
                    break;

                case 1:
                    SetLang("zh-TW", this, typeof(MainForm));
                    this.Text = string.Format("岚珠工具箱 {0}", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                    x264PriorityComboBox.Items.Clear();
                    x264PriorityComboBox.Items.AddRange(new string[] { "低", "在標準以下", "標準", "在標準以上", "高", "即時" });
                    x264PriorityComboBox.SelectedIndex = 2;
                    x264AudioModeComboBoxIndex = x264AudioModeComboBox.SelectedIndex;
                    x264AudioModeComboBox.Items.Clear();
                    x264AudioModeComboBox.Items.Add("壓制音頻");
                    x264AudioModeComboBox.Items.Add("無音頻流");
                    x264AudioModeComboBox.Items.Add("拷貝音頻流");
                    x264AudioModeComboBox.SelectedIndex = x264AudioModeComboBoxIndex;
                    x264VideoTextBox.EmptyTextTip = "可以把文件拖拽到這裡";
                    x264SubTextBox.EmptyTextTip = "雙擊清空字幕檔案文本框";
                    //x264OutTextBox.EmptyTextTip = "寬度和高度全為0即不改變解析度";
                    x264PathTextBox.EmptyTextTip = "字幕和視頻在同一資料夾下且同名，不同名僅有語言後綴時請在右方選擇或輸入";
                    //txtvideo3.EmptyTextTip = "音頻參數需在音頻選項卡设定";
                    ExtractMP4TextBox.EmptyTextTip = "新檔案生成在原資料夾";
                    txtvideo8.EmptyTextTip = "新檔案生成在原資料夾";
                    txtvideo6.EmptyTextTip = "新檔案生成在原資料夾";
                    //load Help Text
                    if (File.Exists(startpath + "\\help_zh_tw.rtf"))
                    {
                        HelpTextBox.LoadFile(startpath + "\\help_zh_tw.rtf");
                    }
                    break;

                case 2:
                    SetLang("en-US", this, typeof(MainForm));
                    this.Text = string.Format("Maruko Toolbox {0}", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                    x264PriorityComboBox.Items.Clear();
                    x264PriorityComboBox.Items.AddRange(new string[] { "Idle", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime" });
                    x264PriorityComboBox.SelectedIndex = 2;
                    x264AudioModeComboBoxIndex = x264AudioModeComboBox.SelectedIndex;
                    x264AudioModeComboBox.Items.Clear();
                    x264AudioModeComboBox.Items.Add("with audio");
                    x264AudioModeComboBox.Items.Add("no audio");
                    x264AudioModeComboBox.Items.Add("copy audio");
                    x264AudioModeComboBox.SelectedIndex = x264AudioModeComboBoxIndex;
                    x264VideoTextBox.EmptyTextTip = "Drag file here";
                    x264SubTextBox.EmptyTextTip = "Clear subtitle text box by double click";
                    //x264OutTextBox.EmptyTextTip = "Both the width and height equal zero means using original resolution";
                    x264PathTextBox.EmptyTextTip = "Subtitle and Video must be of the same name and in the same folder";
                    //txtvideo3.EmptyTextTip = "It is necessary to set audio parameter in the Audio tab";
                    ExtractMP4TextBox.EmptyTextTip = "New file will be created in the original folder";
                    txtvideo8.EmptyTextTip = "New file will be created in the original folder";
                    txtvideo6.EmptyTextTip = "New file will be created in the original folder";
                    //load Help Text
                    if (File.Exists(startpath + "\\help.rtf"))
                    {
                        HelpTextBox.LoadFile(startpath + "\\help.rtf");
                    }
                    break;

                case 3:
                    SetLang("ja-JP", this, typeof(MainForm));
                    this.Text = string.Format("Maruko Toolbox {0}", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                    x264PriorityComboBox.Items.Clear();
                    x264PriorityComboBox.Items.AddRange(new string[] { "低", "通常以下", "通常", "通常以上", "高", "リアルタイム" });
                    x264PriorityComboBox.SelectedIndex = 2;
                    x264AudioModeComboBoxIndex = x264AudioModeComboBox.SelectedIndex;
                    x264AudioModeComboBox.Items.Clear();
                    x264AudioModeComboBox.Items.Add("オーディオ付き");
                    x264AudioModeComboBox.Items.Add("オーディオなし");
                    x264AudioModeComboBox.Items.Add("オーディオ コピー");
                    x264AudioModeComboBox.SelectedIndex = x264AudioModeComboBoxIndex;
                    x264VideoTextBox.EmptyTextTip = "ビデオファイルをここに引きずってください";
                    x264SubTextBox.EmptyTextTip = "ダブルクリックで字幕を削除する";
                    //x264OutTextBox.EmptyTextTip = "Both the width and height equal zero means using original resolution";
                    x264PathTextBox.EmptyTextTip = "字幕とビデオは同じ名前と同じフォルダにある必要があります";
                    //txtvideo3.EmptyTextTip = "It is necessary to set audio parameter in the Audio tab";
                    ExtractMP4TextBox.EmptyTextTip = "新しいファイルはビデオファイルのあるディレクトリに生成する";
                    txtvideo8.EmptyTextTip = "新しいファイルはビデオファイルのあるディレクトリに生成する";
                    txtvideo6.EmptyTextTip = "新しいファイルはビデオファイルのあるディレクトリに生成する";
                    if (File.Exists(startpath + "\\help.rtf"))
                    {
                        HelpTextBox.LoadFile(startpath + "\\help.rtf");
                    }
                    break;

                default:
                    break;
            }

                    ApplyLanIcon();
                    UpdateEstimatedSize();
                    RestoreShellLayout();
        }

        private string GetCultureName()
        {
            string name = "zh-CN";
            switch (languageComboBox.SelectedIndex)
            {
                case 0:
                    name = "zh-CN";
                    break;

                case 1:
                    name = "zh-TW";
                    break;

                case 2:
                    name = "en-US";
                    break;

                case 3:
                    name = "ja-JP";
                    break;

                default:
                    break;
            }
            return name;
        }

        #endregion globalization

        public static void RunProcess(string exe, string arg)
        {
            Thread thread = new Thread(() =>
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, arg);
                psi.CreateNoWindow = true;
                Process p = new Process();
                p.StartInfo = psi;
                p.Start();
                p.WaitForExit();
                MessageBox.Show("ts");
                p.Close();
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void AVSSaveButton_Click(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.AVS); //"AVS(*.avs)|*.avs";
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                File.WriteAllText(savefile.FileName, AVSScriptTextBox.Text, Encoding.Default);
            }
        }

        private void MuxReplaceAudioButton_Click(object sender, EventArgs e)
        {
            if (namevideo == "")
            {
                ShowErrorMessage("请选择视频文件");
                return;
            }
            if (nameaudio == "")
            {
                ShowErrorMessage("请选择音频文件");
                return;
            }
            if (nameout == "")
            {
                ShowErrorMessage("请选择输出文件");
                return;
            }
            mux = "";
            //mux = "\"" + workPath + "\\ffmpeg.exe\" -y -i \"" + namevideo + "\" -c:v copy -an  \"" + workPath + "\\video_noaudio.mp4\" \r\n";
            //mux += "\"" + workPath + "\\ffmpeg.exe\" -y -i \"" + workPath + "\\video_noaudio.mp4\" -i \"" + nameaudio + "\" -vcodec copy  -acodec copy \"" + nameout + "\" \r\n";
            //mux += "del \"" + workPath + "\\video_noaudio.mp4\" \r\n";
            mux = "\"" + workPath + "\\ffmpeg.exe\" -y -i \"" + namevideo + "\" -i \"" + nameaudio + "\" -map 0:v -c:v copy -map 1:0 -c:a copy  \"" + txtout.Text + "\" \r\n";
            batpath = workPath + "\\mux.bat";
            WriteBatFile(batpath, mux);
            LogRecord(mux);
            Process.Start(batpath);
        }

        #region 一图流

        private void AudioPicButton_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.IMAGE); //"图片(*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                AudioPicTextBox.Text = openFileDialog1.FileName;
            }
        }

        private void AudioPicAudioButton_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.AUDIO_4); //"音频(*.aac;*.mp3;*.mp4;*.wav)|*.aac;*.mp3;*.mp4;*.wav|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                AudioPicAudioTextBox.Text = openFileDialog1.FileName;
            }
        }

        private void AudioOnePicOutputButton_Click(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_D_2); //"MP4视频(*.mp4)|*.mp4|FLV视频(*.flv)|*.flv";
            savefile.FileName = "Single";
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                AudioOnePicOutputTextBox.Text = savefile.FileName;
            }
        }

        public int SecondsFromHHMMSS(string hhmmss)
        {
            int hh = int.Parse(hhmmss.Substring(0, 2));
            int mm = int.Parse(hhmmss.Substring(3, 2));
            int ss = int.Parse(hhmmss.Substring(6, 2));
            return hh * 3600 + mm * 60 + ss;
        }

        private void AudioOnePicButton_Click(object sender, EventArgs e)
        {
            if (!File.Exists(AudioPicTextBox.Text))
            {
                ShowErrorMessage("请选择图片文件");
            }
            else if (!File.Exists(AudioPicAudioTextBox.Text))
            {
                ShowErrorMessage("请选择音频文件");
            }
            else if (AudioOnePicOutputTextBox.Text == "")
            {
                ShowErrorMessage("请选择输出文件");
            }
            else
            {
                System.Drawing.Image img = System.Drawing.Image.FromFile(AudioPicTextBox.Text);
                // if not even number, chop 1 pixel out
                int newWidth = (img.Width % 2 == 0 ? img.Width : img.Width - 1);
                int newHeight = (img.Height % 2 == 0 ? img.Height : img.Height - 1);
                Rectangle cropArea;
                if (img.Width % 2 != 0 || img.Height % 2 != 0)
                {
                    Bitmap bmp = new Bitmap(img);
                    cropArea = new Rectangle(0, 0, newWidth, newHeight);
                    img = (Image)bmp.Clone(cropArea, bmp.PixelFormat);
                }

                //if (img.Width % 2 != 0 || img.Height % 2 != 0)
                //{
                //    MessageBox.Show("图片的长和宽必须是偶数。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                //    img.Dispose();
                //    return;
                //}
                //if (img.RawFormat.Equals(ImageFormat.Jpeg))
                //{
                //    File.Copy(AudioPicTextBox.Text, tempPic, true);
                //}
                //else
                {
                    System.Drawing.Imaging.Encoder ImageEncoder = System.Drawing.Imaging.Encoder.Quality;
                    EncoderParameter ep = new EncoderParameter(ImageEncoder, 100L);
                    EncoderParameters eps = new EncoderParameters(1);
                    ImageCodecInfo ImageCoderType = getImageCoderInfo("image/jpeg");
                    eps.Param[0] = ep;
                    img.Save(tempPic, ImageCoderType, eps);
                    //img.Save(tempPic, ImageFormat.Jpeg);
                }
                //获得音频时长
                MediaInfo MI = new MediaInfo();
                MI.Open(AudioPicAudioTextBox.Text);
                string timeStr = MI.Get(StreamKind.General, 0, "Duration/String3");
                if (!string.IsNullOrEmpty(timeStr))
                    OnePicAudioSecondTxt.Text = SecondsFromHHMMSS(timeStr).ToString();
                int seconds = 0;
                bool SecondisInt = int.TryParse(OnePicAudioSecondTxt.Text, out seconds);
                if (!SecondisInt)
                {
                    ShowErrorMessage("未能获取正确时间，请手动输入秒数。");
                    return;
                }
                string ffPath = Path.Combine(workPath, "ffmpeg.exe");
                string neroPath = Path.Combine(workPath, "neroaacenc.exe");
                if (AudioCopyCheckBox.Checked)
                {
                    mux = "\"" + ffPath + "\" -loop 1 -r " + OnePicFPSNum.Value.ToString() + " -t " + seconds.ToString() + " -f image2 -i \"" + tempPic + "\" -c:v libx264 -crf " + OnePicCRFNum.Value.ToString() + " -y SinglePictureVideo.mp4\r\n";
                    mux += "\"" + ffPath + "\" -i SinglePictureVideo.mp4 -i \"" + AudioPicAudioTextBox.Text + "\" -c:v copy -c:a copy -y \"" + AudioOnePicOutputTextBox.Text + "\"\r\n";
                    mux += "del SinglePictureVideo.mp4\r\n";
                    mux += "cmd";
                }
                else
                {
                    mux = "\"" + ffPath + "\" -i \"" + AudioPicAudioTextBox.Text + "\" -f wav - |" + neroPath + " -br " + OnePicAudioBitrateNum.Value.ToString() + "000 -ignorelength -if - -of audio.mp4 -lc\r\n";
                    mux += "\"" + ffPath + "\" -loop 1 -r " + OnePicFPSNum.Value.ToString() + " -t " + seconds.ToString() + " -f image2 -i \"" + tempPic + "\" -c:v libx264 -crf " + OnePicCRFNum.Value.ToString() + " -y SinglePictureVideo.mp4\r\n";
                    mux += "\"" + ffPath + "\" -i SinglePictureVideo.mp4 -i audio.mp4 -c:v copy -c:a copy -y \"" + AudioOnePicOutputTextBox.Text + "\"\r\n";
                    mux += "del SinglePictureVideo.mp4\r\ndel audio.mp4\r\n";
                    mux += "cmd";
                }
                /*
                string audioPath = AddExt(Path.GetFileName(AudioPicAudioTextBox.Text), "_atmp.mp4");
                string videoPath = AddExt(Path.GetFileName(AudioPicAudioTextBox.Text), "_vtmp.mp4");
                string picturePath = "c:\\" + Path.GetFileNameWithoutExtension(AudioPicTextBox.Text) + "_tmp.jpg";
                if (AudioCopyCheckBox.Checked)
                {
                    mux = "ffmpeg -loop 1 -r " + AudioOnePicFPSNum.Value.ToString() + " -t " + seconds.ToString() + " -f image2 -i \"" + picturePath + "\" -vcodec libx264 -crf 24 -y \"" + videoPath + "\"\r\n";
                    mux += "ffmpeg -i \"" + videoPath + "\" -i \"" + AudioPicAudioTextBox.Text + "\" -c:v copy -c:a copy -y \"" + AudioOnePicOutputTextBox.Text + "\"\r\n";
                    mux += "del \"" + videoPath + "\"\r\ndel \"" + picturePath + "\"\r\n";
                }
                else
                {
                    mux = "ffmpeg -i \"" + AudioPicAudioTextBox.Text + "\" -f wav - |neroaacenc -br " + AudioOnePicAudioBitrateNum.Value.ToString() + "000 -ignorelength -if - -of \"" + audioPath + "\" -lc\r\n";
                    mux += "ffmpeg -loop 1 -r " + AudioOnePicFPSNum.Value.ToString() + " -t " + seconds.ToString() + " -f image2 -i \"" + picturePath + "\" -vcodec libx264 -crf 24 -y \"" + videoPath + "\"\r\n";
                    mux += "ffmpeg -i \"" + videoPath + "\" -i \"" + audioPath + "\" -c:v copy -c:a copy -y \"" + AudioOnePicOutputTextBox.Text + "\"\r\n";
                    mux += "del \"" + videoPath + "\"\r\ndel \"" + audioPath + "\"\r\ndel \"" + picturePath + "\"\r\n";
                }
                */
                batpath = Path.Combine(workPath, Path.GetRandomFileName() + ".bat");
                WriteBatFile(batpath, mux);
                LogRecord(mux);
                Process.Start(batpath);
            }
        }

        private void txtMI_DragDrop(object sender, DragEventArgs e)
        {
            MIvideo = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
            MediaInfoTextBox.Text = GetMediaInfoString(MIvideo);
        }

        private void txtMI_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Link;
            else e.Effect = DragDropEffects.None;
        }

        private void AudioPicAudioTextBox_TextChanged(object sender, EventArgs e)
        {
            if (File.Exists(AudioPicAudioTextBox.Text.ToString()))
            {
                AudioOnePicOutputTextBox.Text = Util.ChangeExt(AudioPicAudioTextBox.Text, "_SP.flv");
            }
        }

        /// <summary>
        /// 获取图片编码类型信息
        /// </summary>
        /// <param name="ImageCoderType">编码类型</param>
        /// <returns>ImageCodecInfo</returns>
        private ImageCodecInfo getImageCoderInfo(string ImageCoderType)
        {
            ImageCodecInfo[] coderTypeArray = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo coderType in coderTypeArray)
            {
                if (coderType.MimeType.Equals(ImageCoderType))
                    return coderType;
            }
            return null;
        }

        #endregion 一图流

        #region 后黑

        private void BlackVideoButton_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_D_1); //"FLV视频(*.flv)|*.flv";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                BlackVideoTextBox.Text = openFileDialog1.FileName;
            }
        }

        private void BlackOutputButton_Click(object sender, EventArgs e)
        {
            SaveFileDialog savefile = new SaveFileDialog();
            savefile.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.VIDEO_D_1); //"FLV视频(*.flv)|*.flv";
            DialogResult result = savefile.ShowDialog();
            if (result == DialogResult.OK)
            {
                BlackOutputTextBox.Text = savefile.FileName;
            }
        }

        private void BlackPicButton_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.IMAGE); //"图片(*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                BlackPicTextBox.Text = openFileDialog1.FileName;
            }
        }

        private void BlackStartButton_Click(object sender, EventArgs e)
        {
            //验证
            if (!File.Exists(BlackVideoTextBox.Text) || Path.GetExtension(BlackVideoTextBox.Text) != ".flv")
            {
                ShowErrorMessage("请选择FLV视频文件");
                return;
            }

            MediaInfo MI = new MediaInfo();
            MI.Open(BlackVideoTextBox.Text);
            double videobitrate = double.Parse(MI.Get(StreamKind.General, 0, "BitRate"));
            double targetBitrate = (double)BlackBitrateNum.Value;

            if (!File.Exists(BlackPicTextBox.Text) && BlackNoPicCheckBox.Checked == false)
            {
                ShowErrorMessage("请选择图片文件或勾选使用黑屏");
                return;
            }
            if (BlackOutputTextBox.Text == "")
            {
                ShowErrorMessage("请选择输出文件");
                return;
            }

            if (videobitrate < 1000000)
            {
                ShowInfoMessage("此视频不需要后黑。");
                return;
            }
            if (videobitrate > 5000000)
            {
                ShowInfoMessage("此视频码率过大，请先压制再后黑。");
                return;
            }

            //处理图片
            int videoWidth = int.Parse(MI.Get(StreamKind.Video, 0, "Width"));
            int videoHeight = int.Parse(MI.Get(StreamKind.Video, 0, "Height"));
            if (BlackNoPicCheckBox.Checked)
            {
                Bitmap bm = new Bitmap(videoWidth, videoHeight);
                Graphics g = Graphics.FromImage(bm);
                //g.FillRectangle(Brushes.White, new Rectangle(0, 0, 800, 600));
                g.Clear(Color.Black);
                bm.Save(tempPic, ImageFormat.Jpeg);
                g.Dispose();
                bm.Dispose();
            }
            else
            {
                System.Drawing.Image img = System.Drawing.Image.FromFile(BlackPicTextBox.Text);
                int sourceWidth = img.Width;
                int sourceHeight = img.Height;
                if (img.Width % 2 != 0 || img.Height % 2 != 0)
                {
                    ShowErrorMessage("图片的长和宽必须都是偶数。");
                    img.Dispose();
                    return;
                }
                if (img.Width != videoWidth || img.Height != videoHeight)
                {
                    ShowErrorMessage("图片的长和宽和视频不一致。");
                    img.Dispose();
                    return;
                }
                if (img.RawFormat.Equals(ImageFormat.Jpeg))
                {
                    File.Copy(BlackPicTextBox.Text, tempPic, true);
                }
                else
                {
                    System.Drawing.Imaging.Encoder ImageEncoder = System.Drawing.Imaging.Encoder.Quality;
                    EncoderParameter ep = new EncoderParameter(ImageEncoder, 100L);
                    EncoderParameters eps = new EncoderParameters(1);
                    ImageCodecInfo ImageCoderType = getImageCoderInfo("image/jpeg");
                    eps.Param[0] = ep;
                    img.Save(tempPic, ImageCoderType, eps);
                    //img.Save(tempPic, ImageFormat.Jpeg);
                }
            }
            int blackSecond = 300;
            //计算后黑时长
            if (BlackSecondComboBox.Text == "auto")
            {
                int seconds = SecondsFromHHMMSS(MI.Get(StreamKind.General, 0, "Duration/String3"));
                double s = videobitrate / 1000.0 * (double)seconds / targetBitrate - (double)seconds;
                blackSecond = (int)s;
                BlackSecondComboBox.Text = blackSecond.ToString();
            }
            else
            {
                blackSecond = int.Parse(Regex.Replace(BlackSecondComboBox.Text.ToString(), @"\D", "")); //排除除数字外的所有字符
            }

            //批处理
            mux = "\"" + workPath + "\\ffmpeg\" -loop 1 -r " + BlackFPSNum.Value.ToString() + " -t " + blackSecond.ToString() + " -f image2 -i \"" + tempPic + "\" -c:v libx264 -crf " + BlackCRFNum.Value.ToString() + " -y black.flv\r\n";
            mux += string.Format("\"{0}\\flvbind\" \"{1}\"  \"{2}\"  black.flv\r\n", workPath, BlackOutputTextBox.Text, BlackVideoTextBox.Text);
            mux += "del black.flv\r\n";

            batpath = Path.Combine(workPath, Path.GetRandomFileName() + ".bat");
            WriteBatFile(batpath, mux);
            LogRecord(mux);
            Process.Start(batpath);
        }

        private void BlackVideoTextBox_TextChanged(object sender, EventArgs e)
        {
            string path = BlackVideoTextBox.Text;
            if (File.Exists(path))
            {
                BlackOutputTextBox.Text = Util.ChangeExt(path, "_black.flv");
            }
        }

        #endregion 后黑

        private void BlackNoPicCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            BlackPicTextBox.Enabled = !BlackNoPicCheckBox.Checked;
            BlackPicButton.Enabled = !BlackNoPicCheckBox.Checked;
        }

        private void BlackSecondComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (BlackSecondComboBox.Text != "auto")
            {
                BlackBitrateNum.Enabled = false;
            }
            else
            {
                BlackBitrateNum.Enabled = true;
            }
        }

        private void SetDefaultButton_Click(object sender, EventArgs e)
        {
            DialogResult dr = ShowQuestion(string.Format("是否将所有界面参数恢复到默认设置？"), "提示");
            if (dr == DialogResult.Yes)
            {
                InitParameter();
                ShowInfoMessage("恢复默认设置完成！");
            }
        }

        //Ctrl+A 可以全选文本
        private void MediaInfoTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers == Keys.Control && e.KeyCode == Keys.A)
            {
                ((TextBox)sender).SelectAll();
            }
        }

        private void AVSScriptTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers == Keys.Control && e.KeyCode == Keys.A)
            {
                ((TextBox)sender).SelectAll();
            }
        }

        private void x264CustomParameterTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers == Keys.Control && e.KeyCode == Keys.A)
            {
                ((TextBox)sender).SelectAll();
            }
        }

        #region CheckUpdate

        private const string UpdateConfigJsonUrl = "https://maruko.appinn.me/config/version.json";
        private const string UpdateConfigLegacyUrl = "https://maruko.appinn.me/config/version.html";
        private const string OfficialSiteUrl = "https://www.maruko.in/";
        private const string ToolsUpdateConfigUrl = "https://maruko.appinn.me/config/tools_version.json";

        public delegate bool CheckUpadateDelegate(out DateTime newdate, out bool isFullUpdate);

        private static Version NormalizeVersion(Version version)
        {
            if (version == null)
                return new Version(0, 0, 0, 0);

            int major = version.Major < 0 ? 0 : version.Major;
            int minor = version.Minor < 0 ? 0 : version.Minor;
            int build = version.Build < 0 ? 0 : version.Build;
            int revision = version.Revision < 0 ? 0 : version.Revision;
            return new Version(major, minor, build, revision);
        }

        private static bool IsVersionGreater(Version newer, Version current)
        {
            Version normalizedNewer = NormalizeVersion(newer);
            Version normalizedCurrent = NormalizeVersion(current);
            return normalizedNewer.CompareTo(normalizedCurrent) > 0;
        }

        private static bool TryParseVersionString(string versionText, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(versionText))
                return false;

            versionText = versionText.Trim();

            if (Version.TryParse(versionText, out version))
                return true;

            int legacyMinor;
            if (int.TryParse(versionText, out legacyMinor))
            {
                version = new Version(0, legacyMinor, 0, 0);
                return true;
            }

            return false;
        }

        private static bool TryParseJsonUpdatePayload(string payload, out DateTime newDate, out Version newVersion, out bool hasFullUpdateFlag, out bool fullUpdateFlag)
        {
            newDate = DateTime.MinValue;
            newVersion = null;
            hasFullUpdateFlag = false;
            fullUpdateFlag = false;

            if (string.IsNullOrWhiteSpace(payload))
                return false;

            Match dateMatch = Regex.Match(payload, "\"date\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
            Match versionMatch = Regex.Match(payload, "\"version\"\\s*:\\s*(\"(?<v1>[^\"]+)\"|(?<v2>\\d+))", RegexOptions.IgnoreCase);
            Match fullUpdateMatch = Regex.Match(payload, "\"(isFullUpdate|fullUpdate)\"\\s*:\\s*(?<value>true|false)", RegexOptions.IgnoreCase);

            if (!dateMatch.Success || !versionMatch.Success)
                return false;

            if (!DateTime.TryParse(dateMatch.Groups["value"].Value, out newDate))
                return false;

            string versionText = versionMatch.Groups["v1"].Success
                ? versionMatch.Groups["v1"].Value
                : versionMatch.Groups["v2"].Value;

            if (!TryParseVersionString(versionText, out newVersion))
                return false;

            if (fullUpdateMatch.Success)
            {
                hasFullUpdateFlag = bool.TryParse(fullUpdateMatch.Groups["value"].Value, out fullUpdateFlag);
            }

            return true;
        }

        private static bool TryParseLegacyUpdatePayload(string payload, out DateTime newDate, out Version newVersion)
        {
            newDate = DateTime.MinValue;
            newVersion = null;

            if (string.IsNullOrWhiteSpace(payload))
                return false;

            Match dateMatch = Regex.Match(payload, @"Date20\S+Date");
            Match versionMatch = Regex.Match(payload, @"Version\d+Version");

            if (!dateMatch.Success || !versionMatch.Success)
                return false;

            string dateText = dateMatch.Value.Replace("Date", string.Empty);
            string versionText = versionMatch.Value.Replace("Version", string.Empty);

            if (!DateTime.TryParse(dateText, out newDate))
                return false;

            return TryParseVersionString(versionText, out newVersion);
        }

        private static bool TryDownloadString(string url, out string content)
        {
            content = null;
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("LanzhuTool/1.0");
                    HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                        return false;

                    content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return !string.IsNullOrWhiteSpace(content);
                }
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetUpdateInfo(out DateTime newDate, out Version newVersion, out bool isFullUpdate)
        {
            newDate = DateTime.MinValue;
            newVersion = null;
            isFullUpdate = false;

            string payload;
            bool hasFullUpdateFlag;
            bool fullUpdateFlag;

            if (TryDownloadString(UpdateConfigJsonUrl, out payload)
                && TryParseJsonUpdatePayload(payload, out newDate, out newVersion, out hasFullUpdateFlag, out fullUpdateFlag))
            {
                Version currentVersion = NormalizeVersion(Assembly.GetExecutingAssembly().GetName().Version);
                isFullUpdate = hasFullUpdateFlag ? fullUpdateFlag : IsVersionGreater(newVersion, currentVersion);
                return true;
            }

            if (TryDownloadString(UpdateConfigLegacyUrl, out payload)
                && TryParseLegacyUpdatePayload(payload, out newDate, out newVersion))
            {
                Version currentVersion = NormalizeVersion(Assembly.GetExecutingAssembly().GetName().Version);
                isFullUpdate = IsVersionGreater(newVersion, currentVersion);
                return true;
            }

            return false;
        }

        private void HandleUpdateResult(bool needUpdate, DateTime newDate, bool isFullUpdate, bool fromManualCheck)
        {
            if (needUpdate)
            {
                if (isFullUpdate)
                {
                    DialogResult dr = ShowQuestion(string.Format("新版已于{0}发布，是否前往官网下载？", newDate.ToString("yyyy-M-d")), "喜大普奔");
                    if (dr == DialogResult.Yes)
                    {
                        Process.Start(OfficialSiteUrl);
                    }
                }
                else
                {
                    DialogResult dr = ShowQuestion(string.Format("新版已于{0}发布，是否自动升级？（文件约1.5MB）", newDate.ToString("yyyy-M-d")), "喜大普奔");
                    if (dr == DialogResult.Yes)
                    {
                        FormUpdater formUpdater = new FormUpdater(startpath, newDate.ToString("yyyy-M-d"));
                        formUpdater.ShowDialog(this);
                    }
                }
            }
            else if (fromManualCheck)
            {
                ShowInfoMessage("已经是最新版了喵！");
            }
        }

        public void CheckUpdateCallBack(IAsyncResult ar)
        {
            DateTime newDate;
            bool isFullUpdate;
            AsyncResult result = (AsyncResult)ar;
            CheckUpadateDelegate func = (CheckUpadateDelegate)result.AsyncDelegate;

            try
            {
                bool needUpdate = func.EndInvoke(out newDate, out isFullUpdate, ar);
                BeginInvoke((MethodInvoker)delegate
                {
                    HandleUpdateResult(needUpdate, newDate, isFullUpdate, false);
                });
            }
            catch (Exception) { }
        }

        public bool CheckUpdate(out DateTime NewDate, out bool isFullUpdate)
        {
            Version newVersion;
            if (!TryGetUpdateInfo(out NewDate, out newVersion, out isFullUpdate))
            {
                NewDate = DateTime.Parse("1990-03-08");
                return false;
            }

            Version currentVersion = NormalizeVersion(Assembly.GetExecutingAssembly().GetName().Version);
            bool hasNewerVersion = IsVersionGreater(newVersion, currentVersion);
            bool hasNewerDate = DateTime.Compare(NewDate, ReleaseDate) > 0;
            return hasNewerDate || hasNewerVersion;
        }

        private void CheckUpdateButton_Click(object sender, EventArgs e)
        {
            if (Util.IsConnectInternet())
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    DateTime newDate;
                    bool isFullUpdate;
                    bool appNeedUpdate = CheckUpdate(out newDate, out isFullUpdate);

                    BeginInvoke((MethodInvoker)delegate
                    {
                        HandleUpdateResult(appNeedUpdate, newDate, isFullUpdate, true);

                        // 无论 APP 是否有更新，都检查 tools 更新（可选）
                        if (!appNeedUpdate)
                        {
                            // 在 APP 已是最新时，检查 tools 更新
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                DateTime toolsDate;
                                List<string> outdatedTools;
                                if (CheckToolsUpdate(out toolsDate, out outdatedTools))
                                {
                                    BeginInvoke((MethodInvoker)delegate
                                    {
                                        HandleToolsUpdateResult(true, toolsDate, outdatedTools);
                                    });
                                }
                            });
                        }
                        else
                        {
                            // APP 有更新时，延迟检查 tools 更新
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                System.Threading.Thread.Sleep(2000);
                                DateTime toolsDate;
                                List<string> outdatedTools;
                                if (CheckToolsUpdate(out toolsDate, out outdatedTools))
                                {
                                    BeginInvoke((MethodInvoker)delegate
                                    {
                                        HandleToolsUpdateResult(true, toolsDate, outdatedTools);
                                    });
                                }
                            });
                        }
                    });
                });
            }
            else
            {
                ShowErrorMessage("这台电脑似乎没有联网呢~");
            }
        }

        protected void OnResponse(IAsyncResult ar)
        {
            DateTime newDate;
            bool isFullUpdate;
            bool needUpdate = CheckUpdate(out newDate, out isFullUpdate);

            BeginInvoke((MethodInvoker)delegate
            {
                HandleUpdateResult(needUpdate, newDate, isFullUpdate, true);
            });
        }

        #region ToolsUpdate

        private static bool TryParseToolsUpdatePayload(string payload, out DateTime toolsDate, out List<string> outdatedTools, string toolsWorkPath = "")
        {
            toolsDate = DateTime.MinValue;
            outdatedTools = new List<string>();

            if (string.IsNullOrWhiteSpace(payload))
                return false;

            // Parse date from JSON
            Match dateMatch = Regex.Match(payload, "\"date\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
            if (!dateMatch.Success || !DateTime.TryParse(dateMatch.Groups["value"].Value, out toolsDate))
                return false;

            // Parse tools array
            Match toolsMatch = Regex.Match(payload, "\"tools\"\\s*:\\s*\\[(?<items>[\\s\\S]*?)\\]", RegexOptions.IgnoreCase);
            if (!toolsMatch.Success)
                return false;

            string toolsJson = toolsMatch.Groups["items"].Value;
            // Match individual tool objects
            MatchCollection toolMatches = Regex.Matches(toolsJson, "\\{([^}]*)\\}");
            foreach (Match toolMatch in toolMatches)
            {
                string toolObj = toolMatch.Value;
                Match nameMatch = Regex.Match(toolObj, "\"name\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
                Match versionMatch = Regex.Match(toolObj, "\"version\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
                Match pathMatch = Regex.Match(toolObj, "\"path\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);

                if (!nameMatch.Success || !versionMatch.Success || !pathMatch.Success)
                    continue;

                string toolName = nameMatch.Groups["value"].Value;
                string toolVersion = versionMatch.Groups["value"].Value;
                string toolPath = pathMatch.Groups["value"].Value.Replace("\\\\", "\\");

                // Check local version
                string localConfigPath = Path.Combine(toolsWorkPath, "tools_version.json");
                bool needUpdate = true;
                if (File.Exists(localConfigPath))
                {
                    try
                    {
                        string localContent = File.ReadAllText(localConfigPath);
                        Match localDateMatch = Regex.Match(localContent, "\"date\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
                        if (localDateMatch.Success)
                        {
                            DateTime localDate;
                            if (DateTime.TryParse(localDateMatch.Groups["value"].Value, out localDate))
                            {
                                // Also check individual tool version in local config
                                Match localToolsMatch = Regex.Match(localContent, "\"tools\"\\s*:\\s*\\[(?<items>[\\s\\S]*?)\\]", RegexOptions.IgnoreCase);
                                if (localToolsMatch.Success)
                                {
                                    string localToolsJson = localToolsMatch.Groups["items"].Value;
                                    string escapedName = Regex.Escape(toolName);
                                    Match localToolMatch = Regex.Match(localToolsJson,
                                        "\"name\"\\s*:\\s*\"" + escapedName + "\"[\\s\\S]*?\"version\"\\s*:\\s*\"(?<ver>[^\"]+)\"", RegexOptions.IgnoreCase);
                                    if (localToolMatch.Success && localToolMatch.Groups["ver"].Value == toolVersion)
                                    {
                                        string fullPath = Path.Combine(toolsWorkPath, toolPath);
                                        if (File.Exists(fullPath))
                                            needUpdate = false;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (needUpdate)
                    outdatedTools.Add(toolName);
            }

            return outdatedTools.Count > 0 || (toolMatches.Count == 0 && dateMatch.Success);
        }

        private bool CheckToolsUpdate(out DateTime toolsDate, out List<string> outdatedTools)
        {
            toolsDate = DateTime.MinValue;
            outdatedTools = new List<string>();

            string payload;
            if (!TryDownloadString(ToolsUpdateConfigUrl, out payload))
                return false;

            return TryParseToolsUpdatePayload(payload, out toolsDate, out outdatedTools, workPath);
        }

        private void RunToolsUpdate()
        {
            try
            {
                string scriptPath = Path.Combine(startpath, "update_tools.ps1");
                if (!File.Exists(scriptPath))
                {
                    // Fallback: try tools directory
                    scriptPath = Path.Combine(workPath, "update_tools.ps1");
                }

                if (!File.Exists(scriptPath))
                {
                    ShowErrorMessage("找不到工具更新脚本 update_tools.ps1，请重新安装程序。");
                    return;
                }

                string args = string.Format("-ExecutionPolicy Bypass -File \"{0}\" -ToolsRoot \"{1}\" -ConfigUrl \"{2}\"",
                    scriptPath, workPath, ToolsUpdateConfigUrl);

                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", args);
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                psi.WorkingDirectory = startpath;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowErrorMessage("启动工具更新失败：" + ex.Message);
            }
        }

        private void HandleToolsUpdateResult(bool needUpdate, DateTime toolsDate, List<string> outdatedTools)
        {
            if (needUpdate)
            {
                string toolList = string.Join("、", outdatedTools.Take(5));
                if (outdatedTools.Count > 5)
                    toolList += " 等";

                DialogResult dr = ShowQuestion(
                    string.Format("检测到 tools 文件夹中有 {0} 个工具可更新（{1}），是否运行自动更新脚本？\n\n更新将使用国内镜像源下载，可能需要几分钟。",
                        outdatedTools.Count, toolList),
                    "工具可选更新");

                if (dr == DialogResult.Yes)
                {
                    RunToolsUpdate();
                }
            }
        }

        #endregion ToolsUpdate

        #endregion CheckUpdate

        private void x264ShutdownCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            shutdownState = x264ShutdownCheckBox.Checked;
        }

        private void TrayModeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            trayMode = TrayModeCheckBox.Checked;
        }

        private void ReleaseDatelabel_DoubleClick(object sender, EventArgs e)
        {
            SplashForm sf = new SplashForm();
            sf.Owner = this;
            sf.Show();
        }

        private void AudioCopyCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            OnePicAudioBitrateNum.Enabled = !AudioCopyCheckBox.Checked;
        }

        private void HelpTextBox_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }

        private void labelAudio_Click(object sender, EventArgs e)
        {
            NavigateTo(1);
        }

        private void SetupAVSPlayerButton_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = Util.GetDialogFilter(Util.DialogFilterTypes.PROGRAM); //"程序(*.exe)|*.exe|所有文件(*.*)|*.*";
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                SetupPlayerTextBox.Text = openFileDialog1.FileName;
            }
        }

        private void AVSAddFilterButton_Click(object sender, EventArgs e)
        {
            string vsfilterDLLPath = Path.Combine(workPath, @"avs\plugins\" + AVSFilterComboBox.Text);
            string text = "LoadPlugin(\"" + vsfilterDLLPath + "\")" + "\r\n";
            AVSScriptTextBox.Text = text + AVSScriptTextBox.Text;
        }

        private void AudioJoinButton_Click(object sender, EventArgs e)
        {
            if (AudioListBox.Items.Count == 0)
            {
                ShowErrorMessage("请输入文件！");
                return;
            }
            else if (AudioOutputTextBox.Text == "")
            {
                ShowErrorMessage("请选择输出文件");
                return;
            }
            StringBuilder sb = new StringBuilder();
            ffmpeg = "";
            string ext = Path.GetExtension(AudioListBox.Items[0].ToString());
            string finish = Util.ChangeExt(AudioOutputTextBox.Text, ext);
            for (int i = 0; i < this.AudioListBox.Items.Count; i++)
            {
                if (Path.GetExtension(AudioListBox.Items[i].ToString()) != ext)
                {
                    ShowErrorMessage("只允许合并相同格式文件。");
                    return;
                }
                sb.AppendLine("file '" + AudioListBox.Items[i].ToString() + "'");
                File.WriteAllText("concat.txt", sb.ToString());
                ffmpeg = "\"" + workPath + "\\ffmpeg.exe\" -f concat  -i concat.txt -y -c copy " + finish;
            }
            ffmpeg += "\r\ncmd";
            batpath = workPath + "\\concat.bat";
            WriteBatFile(batpath, ffmpeg);
            LogRecord(aac);
            Process.Start(batpath);
        }

        #region TabControl

        /// <summary>
        /// 保留原 TabControl 的快捷键行为：Ctrl+1..9 切换到对应功能页。
        /// 序号顺序与原标签顺序一致（视频/音频/常用/封装/抽取/AVS/信息/设置/帮助）。
        /// 原来「拖动文件到标签上即切换页面」的行为，改由顶栏导航项承担
        /// （见 MainForm.Shell.cs 的 ShellNav_DragOver）。
        /// </summary>
        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers != Keys.Control)
                return;

            int index = -1;
            if (e.KeyCode == Keys.D1) index = 0;
            else if (e.KeyCode == Keys.D2) index = 1;
            else if (e.KeyCode == Keys.D3) index = 2;
            else if (e.KeyCode == Keys.D4) index = 3;
            else if (e.KeyCode == Keys.D5) index = 4;
            else if (e.KeyCode == Keys.D6) index = 5;
            else if (e.KeyCode == Keys.D7) index = 6;
            else if (e.KeyCode == Keys.D8) index = 7;
            else if (e.KeyCode == Keys.D9) index = 8;

            if (index >= 0)
                NavigateTo(index);
        }

        #endregion TabControl

        private void gmkvextractguibButton_Click(object sender, EventArgs e)
        {
            string path = workPath + "\\gMKVExtractGUI.exe";
            if (File.Exists(path))
                Process.Start(path);
            else
                ShowErrorMessage("请检查\r\n\r\n" + path + "\r\n\r\n是否存在", "未找到程序!");
        }

        private void FeedbackButton_Click(object sender, EventArgs e)
        {
            FeedbackForm ff = new FeedbackForm();
            ff.ShowDialog();
        }

        private void x264SubTextBox_DoubleClick(object sender, EventArgs e)
        {
            x264SubTextBox.Clear();
        }

        private void RotateButton_Click(object sender, EventArgs e)
        {
            if (namevideo4 == "")
            {
                ShowErrorMessage("请选择视频文件");
            }
            else if (nameout5 == "")
            {
                ShowErrorMessage("请选择输出文件");
            }
            else
            {
                clip = string.Format(@"""{0}\ffmpeg.exe"" -i ""{1}"" -vf ""transpose={2}"" -y ""{3}""",
                    workPath, namevideo4, TransposeComboBox.SelectedIndex, nameout5) + Environment.NewLine + "cmd";
                batpath = workPath + "\\clip.bat";
                WriteBatFile(batpath, clip);
                Process.Start(batpath);
            }
        }

        private void AudioPresetComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            XElement x = xdoc.Element("root").Element("Audio").Element("AudioEncoder").Element(AudioEncoderComboBox.Text).Elements()
                             .Where(_ => _.Attribute("Name").Value == AudioPresetComboBox.Text).First();
            AudioCustomParameterTextBox.Text = x.Value;
        }

        private void lbAuto_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= 65535)
                return;

            e.DrawBackground();
            SolidBrush BlueBrush = new SolidBrush(Color.Blue);
            SolidBrush BlackBrush = new SolidBrush(Color.Black);
            Color vColor = Color.Black;
            string input = lbAuto.Items[e.Index].ToString();
            if (!string.IsNullOrEmpty(GetSubtitlePath(input)))
            {
                e.Graphics.DrawString(Convert.ToString(lbAuto.Items[e.Index]), e.Font, BlueBrush, e.Bounds);
            }
            else
            {
                e.Graphics.DrawString(Convert.ToString(lbAuto.Items[e.Index]), e.Font, BlackBrush, e.Bounds);
            }
        }

        private string GetSubtitlePath(string videoPath)
        {
            string sub = "";
            string splang = "";
            string[] subExt = { ".ass", ".ssa", ".srt" };
            if (x264BatchSubSpecialLanguage.Text != "none")
                splang = "." + x264BatchSubSpecialLanguage.Text;
            foreach (string ext in subExt)
            {
                if (File.Exists(videoPath.Remove(videoPath.LastIndexOf(".")) + splang + ext))
                {
                    sub = videoPath.Remove(videoPath.LastIndexOf(".")) + splang + ext;
                    break;
                }
            }
            return sub;
        }

        private void x264ExeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (x264ExeComboBox.SelectedIndex == -1)
                return;

            x264SubTextBox.Enabled = true;
            x264SubBtn.Enabled = true;
            x264BatchSubCheckBox.Enabled = true;
            x264BatchSubSpecialLanguage.Enabled = true;
            x264DemuxerComboBox.Enabled = true;
            VideoBatchFormatComboBox.Enabled = true;
            UpdateEstimatedSize();
        }

        #region Form

        public String GetCurrentDirectory()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        public static void ShowErrorMessage(String argMessage)
        {
            MessageBox.Show(argMessage, "错误!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void ShowErrorMessage(String argMessage, String argTitle)
        {
            MessageBox.Show(argMessage, argTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void ShowWarningMessage(String argMessage)
        {
            MessageBox.Show(argMessage, "警告!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void ShowWarningMessage(String argMessage, String argTitle)
        {
            MessageBox.Show(argMessage, argTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void ShowInfoMessage(String argMessage)
        {
            MessageBox.Show(argMessage, "提示!", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void ShowInfoMessage(String argMessage, String argTitle)
        {
            MessageBox.Show(argMessage, argTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static DialogResult ShowQuestion(String argQuestion, String argTitle)
        {
            return MessageBox.Show(argQuestion, argTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        }

        #endregion

        private void audioDeleteBt_Click(object sender, EventArgs e)
        {
            if (ShowQuestion("确定要删除这条预设参数？", "提示") == DialogResult.Yes)
            {
                try
                {
                    var xls = xdoc.Element("root").Element("Audio").Element("AudioEncoder").Element(AudioEncoderComboBox.Text).Elements();
                    foreach (var item in xls)
                    {
                        if (item.Attribute("Name").Value == AudioPresetComboBox.Text)
                            item.Remove();
                    }
                    xdoc.Save("preset.xml");
                    LoadAudioPreset();
                }
                catch (Exception ex)
                {
                    ShowErrorMessage("删除失败! Reason: " + ex.Message);
                }
            }
        }

        private void audioAddBt_Click(object sender, EventArgs e)
        {
            try
            {
                string aPresetName = InputBox.Show("请输入这个预设名称", "请为预置配置命名", "新预置名称");
                if (!string.IsNullOrEmpty(aPresetName))
                {
                    var xl = xdoc.Element("root").Element("Audio").Element("AudioEncoder").Element(AudioEncoderComboBox.Text);
                    XElement xelnew = new XElement("Parameter", AudioCustomParameterTextBox.Text,
                                          new XAttribute("Name", aPresetName));
                    foreach (var item in xl.Elements())
                    {
                        if (item.Attribute("Name").Value == aPresetName)
                        {
                            ShowErrorMessage("预设名称已经存在", "预设名称重复");
                            return;
                        }
                    }
                    xl.Add(xelnew);
                    xdoc.Save("preset.xml");
                    LoadAudioPreset();
                    AudioPresetComboBox.SelectedIndex = AudioPresetComboBox.FindString(aPresetName);
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("添加失败! Reason: " + ex.Message);
            }
        }

        private void x265CheckBox_Click(object sender, EventArgs e)
        {
            if (ShowQuestion("你必须重新启动岚珠工具箱才能使设置的生效 是否现在重新启动？", "需要重新启动") == DialogResult.Yes)
                Application.Restart();
        }

        private void LoadGpuList()
        {
            GpuComboBox.Items.Clear();
            
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController"))
                {
                    int index = 0;
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"] as string;
                        string vendor = obj["AdapterCompatibility"] as string;
                        if (string.IsNullOrEmpty(name))
                            continue;
                        
                        string label;
                        if (vendor != null && vendor.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                            label = string.Format("[{0}] {1} (NVENC)", index, name);
                        else if (vendor != null && vendor.IndexOf("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                 (vendor != null && vendor.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0))
                            label = string.Format("[{0}] {1} (AMF)", index, name);
                        else if (vendor != null && vendor.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                            label = string.Format("[{0}] {1} (QSV)", index, name);
                        else
                            label = string.Format("[{0}] {1}", index, name);
                        
                        GpuComboBox.Items.Add(label);
                        index++;
                    }
                }
            }
            catch
            {
            }
            
            if (GpuComboBox.Items.Count == 0)
                GpuComboBox.Items.Add("默认GPU");
            
            GpuComboBox.SelectedIndex = 0;
        }

        private void GpuAccelerationCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // 当GPU加速被选中时，取消混合压制
            if (GpuAccelerationCheckBox.Checked && HybridProcessingCheckBox.Checked)
            {
                HybridProcessingCheckBox.Checked = false;
            }
            
            GpuComboBox.Enabled = GpuAccelerationCheckBox.Checked || HybridProcessingCheckBox.Checked;
            if ((GpuAccelerationCheckBox.Checked || HybridProcessingCheckBox.Checked) && GpuComboBox.Items.Count == 0)
            {
                LoadGpuList();
            }
            UpdateEstimatedSize();
        }

        private void HybridProcessingCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // 当混合压制被选中时，取消GPU加速
            if (HybridProcessingCheckBox.Checked && GpuAccelerationCheckBox.Checked)
            {
                GpuAccelerationCheckBox.Checked = false;
            }
            
            GpuComboBox.Enabled = GpuAccelerationCheckBox.Checked || HybridProcessingCheckBox.Checked;
            if ((GpuAccelerationCheckBox.Checked || HybridProcessingCheckBox.Checked) && GpuComboBox.Items.Count == 0)
            {
                LoadGpuList();
            }
            UpdateEstimatedSize();
        }

    }
}