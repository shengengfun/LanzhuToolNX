// ------------------------------------------------------------------
// Copyright (C) 2011-2016 Maruko Toolbox Project
// 
//  Authors: komaruchan <sandy_0308@hotmail.com>
//           LunarShaddow <aflyhorse@hotmail.com>
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

using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace mp4box
{
    static class Program
    {
        private class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern bool SetDllDirectory(string path);

            [DllImport("user32.dll")]
            public static extern bool SetProcessDPIAware();

            public static void SetUnmanagedDllDirectory()
            {
                string path = Path.Combine(Application.StartupPath, "tools");
                if (IntPtr.Size == 8)
                    path = Path.Combine(path, "x64");
                if (!SetDllDirectory(path))
                    throw new System.ComponentModel.Win32Exception();
            }
        }

        private static void AddToolDirectoriesToPath()
        {
            string toolsRoot = Path.Combine(Application.StartupPath, "tools");
            string[] toolDirectories = new string[]
            {
                toolsRoot,
                Path.Combine(toolsRoot, "runtime", "common"),
                Path.Combine(toolsRoot, "video", "mkvtoolnix"),
                Path.Combine(toolsRoot, "video", "ffmpeg"),
                Path.Combine(toolsRoot, "video", "flv"),
                Path.Combine(toolsRoot, "audio", "encoders"),
                Path.Combine(toolsRoot, "avs"),
                Path.Combine(toolsRoot, "qtfiles"),
                Path.Combine(toolsRoot, "x64")
            };

            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string newPath = string.Join(";", toolDirectories) + ";" + currentPath;
            Environment.SetEnvironmentVariable("PATH", newPath);
        }

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // 检查程序路径是否可写入
            if (!Util.IsDirWriteable(Path.GetDirectoryName(Application.ExecutablePath)))
            {
                bool bRunElevated = false;
                //检测程序是否以高权限运行
                foreach (string strParam in args)
                {
                    if (strParam.Equals("-elevate"))
                    {
                        bRunElevated = true;
                        break;
                    }
                }

                // 如果需要则提升权限
                if (!bRunElevated)
                {
                    try
                    {
                        Process p = new Process();
                        p.StartInfo.FileName = Application.ExecutablePath;
                        p.StartInfo.Arguments = "-elevate";
                        p.StartInfo.Verb = "runas";
                        p.Start();
                        return;
                    }
                    catch { }
                }
                MessageBox.Show("岚珠工具箱无法启动因为当前目录无法写入\r\n\r\n请赋予岚珠工具箱所需权限或者将应用程序移动到未受保护的目录下", "岚珠工具箱 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 必须尽早声明 DPI 感知（清单里也已声明，这里只是双保险）：
            // 否则在 125% / 150% 缩放的屏幕上，整个窗口会被系统位图拉伸 →
            // 文字发虚、线条对不齐。
            try { NativeMethods.SetProcessDPIAware(); }
            catch { }

            NativeMethods.SetUnmanagedDllDirectory();
            AddToolDirectoriesToPath();

            // var modulename = Process.GetCurrentProcess().MainModule.ModuleName;
            // var procesname = Path.GetFileNameWithoutExtension(modulename);
            // Process[] processes = Process.GetProcessesByName(procesname);
            // if (processes.Length > 1)
            // {
            // MessageBox.Show("你已经打开了一个岚珠工具箱喔！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            // Application.Exit();
            // return;
            // }

            if (ConfigurationManager.AppSettings["SplashScreen"] == "True")
            {
                Application.Run(new mycontext());
            }
            else
            {
                Application.Run(new MainForm());
            }
        }
    }
}
