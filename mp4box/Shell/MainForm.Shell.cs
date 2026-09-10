// ------------------------------------------------------------------
// 岚珠工具箱 —— 现代 UI 外壳：主窗体接线（partial class MainForm）
//
// 为了「最快 + 零回归」，这里刻意采用最小侵入的做法：
//
//   1. 9 个功能页全部留在 TabControl 里，一个控件都不搬家
//      （WinForms 不允许 TabPage 挂到普通 Panel 上，硬搬会抛异常）。
//   2. 左右各一只 TabControl，把它们自带的标签条裁掉，
//      左栏由顶栏导航驱动，右栏由自绘标签条驱动。
//   3. 顶栏 / 底栏 / 卡片 / 标签条全部代码构建，不写进任何 resx，
//      所以永远不会被 resources.ApplyResources() 覆盖。
//   4. 唯一需要防的是 resx 里的 $this.ClientSize(574×609)，
//      由 RestoreShellLayout() 在每次切换语言后纠正。
// ------------------------------------------------------------------

using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using mp4box.Shell;

namespace mp4box
{
    public partial class MainForm
    {
        private TopBar _topBar;
        private StatusBarEx _statusBar;
        private OutputPanel _output;
        private TabControl _rightTabs;

        private Panel _leftHost;
        private CardPanel _leftCard;
        private Panel _leftFrame;

        /// <summary>全部页面，顺序与原 TabControl 完全一致（Ctrl+1..9 沿用旧习惯）。</summary>
        private TabPage[] _allPages;
        /// <summary>左栏（功能区）页面，顺序即顶栏导航顺序。</summary>
        private TabPage[] _leftPages;
        /// <summary>右栏（输出区）页面。</summary>
        private TabPage[] _rightPages;

        private bool _shellReady;

        /// <summary>实测的窗体尺寸折算系数（DPI 感知下 WinForms 会额外折一次）。</summary>
        private double _winScale = 1.0;

        /// <summary>可用的最小客户区（像素）。</summary>
        private const int MinClientWidth = 1000;
        private const int MinClientHeight = 730;
        /// <summary>默认客户区上限（像素）。窗口不靠“越大越好”，保持紧凑。</summary>
        private const int MaxClientWidth = 1180;
        private const int MaxClientHeight = 736;

        /// <summary>左栏为纵向滚动条预留的宽度，避免出现横向滚动条。</summary>
        private const int LeftScrollGutter = 18;

        #region 初始化

        /// <summary>在 Form1_Load 末尾调用。</summary>
        private void InitModernShell()
        {
            if (_shellReady)
                return;
            _shellReady = true;

            Theme.ApplyControlExsTheme();

            SuspendLayout();
            BuildShell();
            MountPages();
            ResumeLayout(true);

            // 注意：本窗体是先构造、后 Show（SplashScreenApplicationContext），
            // 在 Load / Shown 里改窗口尺寸都会被 Show 的后续流程覆盖，
            // 所以推迟到消息循环跑完一轮之后再设（再做一次双保险）。
            Shown += delegate
            {
                BeginInvoke(new Action(delegate
                {
                    ApplyWindowSize(PickDefaultSize());
                    BeginInvoke(new Action(delegate
                    {
                        ApplyWindowSize(PickDefaultSize());
                        AuditTextClipping();

                        // 别让某个下拉框一上来就吃掉焦点（文字被系统反白成蓝块，很扎眼）
                        ActiveControl = tabControl;
#if DEBUG
                        DumpTopBar();
#endif
                    }));
                }));
            };

            ShowLeftPage(VideoTab);
            RestoreShellLayout();
            _statusBar.SetReady();
        }

        private void BuildShell()
        {
            // 原页面（功能区）统一换成正文字体，和现代壳保持一致
            Font = Theme.FormFont;
            BackColor = Theme.PageBg;

            // 无边框窗口：留 1px 给描边，子控件（顶栏/底栏/中部）会自动内缩
            Padding = new Padding(1);

            // ---------- 顶栏 ----------
            _topBar = new TopBar();
            _topBar.NavSelected = delegate(int i)
            {
                if (_leftPages != null && i >= 0 && i < _leftPages.Length)
                    ShowLeftPage(_leftPages[i]);
            };
            _topBar.ActionSelected = delegate(int i)
            {
                if (_rightPages != null && i >= 0 && i < _rightPages.Length)
                    _output.ShowPage(_rightPages[i]);
            };
            _topBar.WindowCommand = HandleWindowCommand;
            _topBar.AllowDrop = true;
            _topBar.DragEnter += ShellNav_DragEnter;
            _topBar.DragOver += ShellNav_DragOver;

            // ---------- 底栏 ----------
            _statusBar = new StatusBarEx();
            _statusBar.SetRight("v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3));

            // ---------- 中部：左（功能区） + 右（输出区） ----------
            int inset = Theme.CardPad + CardPanel.Pad;
            int cardWidth = Theme.LeftFrameWidth + inset * 2;
            int leftColumn = cardWidth + LeftScrollGutter;   // 预留滚动条位置，永不出现横向滚动条

            TableLayoutPanel middle = new TableLayoutPanel();
            middle.Dock = DockStyle.Fill;
            middle.ColumnCount = 2;
            middle.RowCount = 1;
            middle.BackColor = Color.Transparent;   // 透出窗体的渐变背景
            middle.Padding = new Padding(10, 8, 10, 6);
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, leftColumn));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // 左栏：固定宽度 + 垂直滚动，保证任何屏幕高度下都可用
            _leftHost = new Panel();
            _leftHost.Dock = DockStyle.Fill;
            _leftHost.BackColor = Color.Transparent;
            _leftHost.AutoScroll = true;

            _leftCard = new CardPanel();
            _leftCard.Location = new Point(0, 0);
            _leftCard.Size = new Size(cardWidth, Theme.LeftFrameHeight + inset * 2);
            _leftHost.Controls.Add(_leftCard);

            _leftFrame = new Panel();
            _leftFrame.Location = new Point(inset, inset);
            _leftFrame.Size = new Size(Theme.LeftFrameWidth, Theme.LeftFrameHeight);
            _leftFrame.BackColor = Theme.CardBg;
            _leftCard.Controls.Add(_leftFrame);

            // 左栏页面宿主 = 原来的 TabControl（用 Region 把它的标签条裁掉）
            _leftFrame.Controls.Add(tabControl);
            tabControl.Dock = DockStyle.None;
            ShellTabs.Style(tabControl, _leftFrame);

            // 右栏
            Panel rightHost = new Panel();
            rightHost.Dock = DockStyle.Fill;
            rightHost.BackColor = Color.Transparent;
            rightHost.Padding = new Padding(12, 2, 0, 2);

            _output = new OutputPanel();
            _output.Dock = DockStyle.Fill;
            _output.AbortRequested = ShellAbortRun;
            _output.SaveLogRequested = ShellSaveLog;
            rightHost.Controls.Add(_output);

            _rightTabs = new TabControl();
            _output.AttachTo(_rightTabs);

            middle.Controls.Add(_leftHost, 0, 0);
            middle.Controls.Add(rightHost, 1, 0);

            Controls.Add(middle);
            Controls.Add(_topBar);
            Controls.Add(_statusBar);
        }

        private void MountPages()
        {
            _allPages = new TabPage[]
            {
                VideoTab, AudioTab, MiscTab, MuxTab, ExtractTab, AVSTab,
                MediaInfoTab, SetupTabPage, HelpTab
            };

            // 顶栏导航顺序：视频 / 音频 / 封装 / 抽取 / AVS / 常用
            _leftPages = new TabPage[] { VideoTab, AudioTab, MuxTab, ExtractTab, AVSTab, MiscTab };
            _rightPages = new TabPage[] { MediaInfoTab, SetupTabPage, HelpTab };

            // 低频域搬到右栏（TabPage 只能在 TabControl 之间移动，这是合法的）
            for (int i = 0; i < _rightPages.Length; i++)
            {
                TabPage p = _rightPages[i];
                if (p.Parent != null)
                    p.Parent.Controls.Remove(p);
                _rightTabs.TabPages.Add(p);
            }

            Theme.ApplySkin(_leftFrame);
            Theme.ApplySkin(_rightTabs);
        }

        #endregion

        #region 导航

        private void RefreshShellTexts()
        {
            if (_topBar == null || _leftPages == null)
                return;

            // 品牌名：从窗口标题里去掉尾部版本号
            string title = Text;
            if (!string.IsNullOrEmpty(title))
            {
                int sp = title.LastIndexOf(' ');
                _topBar.SetBrand(sp > 0 ? title.Substring(0, sp) : title);
            }

            string[] titles = new string[_leftPages.Length];
            for (int i = 0; i < _leftPages.Length; i++)
                titles[i] = CleanTitle(_leftPages[i].Text);
            _topBar.BuildNav(titles);

            // 顶栏右侧放低频域入口（媒体信息 / 设置 / 帮助），标题取自 resx，自动多语言
            if (_rightPages != null)
            {
                string[] actions = new string[_rightPages.Length];
                for (int i = 0; i < _rightPages.Length; i++)
                    actions[i] = CleanTitle(_rightPages[i].Text);
                _topBar.BuildActions(actions);
            }

            LocalizeShell();

            if (_output != null)
                _output.RefreshTitles();
        }

        private static string CleanTitle(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Replace("&", "");
        }

        /// <summary>显示左栏（功能区）某一页。</summary>
        private void ShowLeftPage(TabPage page)
        {
            if (page == null || _leftPages == null)
                return;

            tabControl.SelectedTab = page;

            for (int i = 0; i < _leftPages.Length; i++)
            {
                if (ReferenceEquals(_leftPages[i], page))
                {
                    _topBar.SetSelected(i);
                    break;
                }
            }
        }

        /// <summary>按原 TabControl 的序号导航（Ctrl+1..9 沿用旧习惯）。</summary>
        private void NavigateTo(int index)
        {
            if (_allPages == null || index < 0 || index >= _allPages.Length)
                return;

            TabPage p = _allPages[index];
            if (Array.IndexOf(_rightPages, p) >= 0)
                _output.ShowPage(p);
            else
                ShowLeftPage(p);
        }

        /// <summary>保留原「拖到标签即切换页面」的行为，改为拖到顶栏导航项上切换。</summary>
        private void ShellNav_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.All
                : DragDropEffects.None;
        }

        private void ShellNav_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.All;
            if (_topBar == null || _leftPages == null)
                return;

            Point pt = _topBar.PointToClient(new Point(e.X + 2, e.Y + 2));
            int hit = _topBar.HitTest(pt);
            if (hit >= 0 && hit < _leftPages.Length)
                ShowLeftPage(_leftPages[hit]);
        }

        #endregion

        #region 运行期页面文案（这些页面是代码创建的，没有 resx）

        private void LocalizeShell()
        {
            int lang = 0;
            try { lang = languageComboBox.SelectedIndex; }
            catch { }

            string tLog, tTask, tPreview;
            string sAbort, sSave, sCopy, sClear, sAuto;
            string sReady, sRunning, sEmpty, sEmptyHint, sPreviewHint, sLogEmpty;

            switch (lang)
            {
                case 1: // zh-TW
                    tLog = "日誌"; tTask = "任務"; tPreview = "預覽";
                    sAbort = "中止執行"; sSave = "儲存日誌"; sCopy = "複製"; sClear = "清空"; sAuto = "自動捲動";
                    sReady = "就緒"; sRunning = "壓制中";
                    sEmpty = "任務佇列為空"; sEmptyHint = "在左側開始壓制後，任務會顯示在這裡";
                    sPreviewHint = "AVS 預覽畫布\r\n\r\n目前預覽仍以獨立視窗彈出，後續會嵌入到這裡";
                    sLogEmpty = "執行日誌會顯示在這裡";
                    break;

                case 2: // en-US
                    tLog = "Log"; tTask = "Tasks"; tPreview = "Preview";
                    sAbort = "Abort"; sSave = "Save log"; sCopy = "Copy"; sClear = "Clear"; sAuto = "Auto scroll";
                    sReady = "Ready"; sRunning = "Running";
                    sEmpty = "Task queue is empty"; sEmptyHint = "Start a job on the left, tasks will appear here";
                    sPreviewHint = "AVS preview canvas\r\n\r\nPreview still opens in a separate window for now";
                    sLogEmpty = "Runtime log will appear here";
                    break;

                case 3: // ja-JP
                    tLog = "ログ"; tTask = "タスク"; tPreview = "プレビュー";
                    sAbort = "中止"; sSave = "ログを保存"; sCopy = "コピー"; sClear = "クリア"; sAuto = "自動スクロール";
                    sReady = "待機中"; sRunning = "処理中";
                    sEmpty = "タスクはありません"; sEmptyHint = "左側で開始するとタスクが表示されます";
                    sPreviewHint = "AVS プレビュー\r\n\r\nプレビューは現在別ウィンドウで開きます";
                    sLogEmpty = "実行ログがここに表示されます";
                    break;

                default: // zh-CN
                    tLog = "日志"; tTask = "任务"; tPreview = "预览";
                    sAbort = "中止运行"; sSave = "保存日志"; sCopy = "复制"; sClear = "清空"; sAuto = "自动滚动";
                    sReady = "就绪"; sRunning = "压制中";
                    sEmpty = "任务队列为空"; sEmptyHint = "在左侧选择功能并开始压制后，任务会显示在这里";
                    sPreviewHint = "AVS 预览画布\r\n\r\n目前预览仍以独立窗口弹出，后续会嵌入到这里";
                    sLogEmpty = "运行日志会显示在这里";
                    break;
            }

            if (_output != null)
                _output.ApplyLocalizedTexts(tLog, tTask, tPreview,
                    sAbort, sSave, sCopy, sClear, sAuto, sEmpty, sEmptyHint, sPreviewHint, sLogEmpty);

            if (_statusBar != null)
            {
                _statusBar.SetReadyText(sReady);
                _statusBar.SetRunningText(sRunning);
                _statusBar.SetStandby();
            }
        }

        #endregion

        #region 右栏动作

        private void ShellAbortRun()
        {
            // 运行控制器（下一阶段接入）会在这里中止当前进程
        }

        private void ShellSaveLog()
        {
            if (_output == null || _output.LogBox == null || _output.LogBox.TextLength == 0)
                return;

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.FileName = "LogFile-" + DateTime.Now.ToString("yyyy'-'MM'-'dd'_'HH'-'mm'-'ss") + ".log";
                dlg.Filter = "Log files|*.log|All files|*.*";
                dlg.RestoreDirectory = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(dlg.FileName, _output.LogBox.Text, new UTF8Encoding(false));
                    }
                    catch
                    {
                    }
                }
            }
        }

        #endregion

        #region resx 保护

        /// <summary>
        /// 每次切换语言后调用。
        /// resources.ApplyResources(this, "$this") 会把窗口 ClientSize 打回 574×609，
        /// 这里把它纠回来；顺带重算两只内嵌 TabControl 的显示区。
        /// </summary>
        private void RestoreShellLayout()
        {
            if (!_shellReady)
                return;

            if (WindowState == FormWindowState.Normal && Visible && ClientSize.Width < MinClientWidth)
                ApplyWindowSize(PickDefaultSize());

            ShellTabs.Refit(tabControl, _leftFrame);
            if (_rightTabs != null)
                ShellTabs.Refit(_rightTabs, _output.Host);

            Font = Theme.FormFont;

            // resx 会把标签的 AutoSize / 位置重新写回去，所以这里要重新排一次
            Theme.TidyLayout(_leftFrame);
            if (_rightTabs != null)
                Theme.TidyLayout(_rightTabs);

            // 整套 UI 走一遍文字适配：任何放不下的文字会自动缩字号，
            // 这样 4 套语言（英文/日文更长）都不会出现显示不全。
            Theme.FitTexts(_leftFrame);
            if (_rightTabs != null)
                Theme.FitTexts(_rightTabs);

            RefreshShellTexts();
            BackColor = Theme.PageBg;
            PerformLayout();
        }

        /// <summary>默认窗口尺寸：夹取到当前屏幕工作区，避免在高 DPI 下超出屏幕。</summary>
        private static Size PickDefaultSize()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(MaxClientWidth, Math.Max(MinClientWidth, wa.Width - 300));
            int h = Math.Min(MaxClientHeight, Math.Max(MinClientHeight, wa.Height - 160));
            return new Size(w, h);
        }

        /// <summary>
        /// 设置窗体尺寸（按客户区像素算）。
        ///
        /// 关键实测结论：这套壳里的控件是按「1 像素 = 1 物理像素」摆的
        /// （窗体没有做 AutoScale，字号已用 Theme.FormFont 折算过 125%），
        /// 所以这里**不能**再乘 DPI 比例，否则窗口会凭空大 25%。
        /// </summary>
        private void ApplyWindowSize(Size wantClient)
        {
            _winScale = 1.0;

            // 先放开最小尺寸限制，否则缩小会被拦
            MinimumSize = new Size(200, 150);
            ClientSize = wantClient;

            MinimumSize = new Size(MinClientWidth, MinClientHeight);
        }

        /// <summary>屏幕缩放比例（1.0 = 100%，1.25 = 125%）。</summary>
        private double ScreenScale
        {
            get
            {
                try
                {
                    using (Graphics g = CreateGraphics())
                    {
                        if (g.DpiX >= 48)
                            return g.DpiX / 96.0;
                    }
                }
                catch
                {
                }
                return 1.0;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT r);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

        /// <summary>取真实客户区尺寸（像素）。ClientSize 属性在这种情形下不可信。</summary>
        private Size RealClientSize()
        {
            try
            {
                if (IsHandleCreated)
                {
                    RECT r;
                    if (GetClientRect(Handle, out r))
                        return new Size(r.Right - r.Left, r.Bottom - r.Top);
                }
            }
            catch
            {
            }
            return ClientSize;
        }

        /// <summary>
        /// 窗体背景：ShiorikoTrans 同款（纵向浅蓝灰渐变 + 左上浅蓝光晕 + 右上浅紫光晕）。
        /// 卡片所在的容器都是透明背景，所以光晕能透上来。
        /// 另外这是无边框窗口，顺手画一圈 1px 描边把窗口边界交代清楚。
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Theme.PaintBackdrop(e.Graphics, ClientSize);

            Rectangle r = ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            using (Pen p = new Pen(Theme.WindowBorder))
                e.Graphics.DrawRectangle(p, r);
        }

        /// <summary>把窗体铺满的浅色背景重绘一次（尺寸变化时用）。</summary>
        private void RefreshBackdrop()
        {
            Invalidate(true);
        }

        #endregion

        #region 自定义窗口边框（无系统标题栏）

        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        /// <summary>用窗体自身那圈 Padding 当作可拖拽的缩放边框。</summary>
        private const int ResizeBorder = 6;

        private void HandleWindowCommand(int cmd)
        {
            switch (cmd)
            {
                case 0:
                    WindowState = FormWindowState.Minimized;
                    break;
                case 1:
                    WindowState = (WindowState == FormWindowState.Maximized)
                        ? FormWindowState.Normal
                        : FormWindowState.Maximized;
                    break;
                case 2:
                    Close();
                    break;
            }
            SyncWindowChrome();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SyncWindowChrome();
        }

        /// <summary>最大化时去掉外圈边距，还原时加回来。</summary>
        private void SyncWindowChrome()
        {
            bool maximized = WindowState == FormWindowState.Maximized;

            Padding padding = maximized ? new Padding(0) : new Padding(ResizeBorder);
            if (Padding != padding)
                Padding = padding;

            try
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
            }
            catch
            {
            }

            if (_topBar != null)
                _topBar.SetMaximized(maximized);
        }

        /// <summary>窗体四周 6px 交给系统做缩放（无系统标题栏时必需）。</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if ((int)m.Result != HTCLIENT)
                    return;

                int lp = m.LParam.ToInt32();
                int sx = unchecked((short)(lp & 0xFFFF));
                int sy = unchecked((short)((lp >> 16) & 0xFFFF));
                Point p = PointToClient(new Point(sx, sy));

                int w = ClientSize.Width;
                int h = ClientSize.Height;
                bool left = p.X <= ResizeBorder;
                bool right = p.X >= w - ResizeBorder;
                bool top = p.Y <= ResizeBorder;
                bool bottom = p.Y >= h - ResizeBorder;

                if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                return;
            }

            base.WndProc(ref m);
        }

        #endregion

        #region 开发期文本裁切审计

#if DEBUG
        private static void WalkCombos(Control root, System.Text.StringBuilder sb, string path)
        {
            for (int i = 0; i < root.Controls.Count; i++)
            {
                Control c = root.Controls[i];
                if (c is ComboBox)
                {
                    sb.AppendLine(path + "/" + c.Name
                        + " parent=" + root.GetType().Name
                        + " bounds=" + c.Bounds
                        + " vis=" + c.Visible
                        + " txt=" + c.Text);
                }
                if (c is ComboArrow)
                {
                    sb.AppendLine(path + "[叠加箭头] parent=" + root.GetType().Name
                        + " bounds=" + c.Bounds + " vis=" + c.Visible);
                }
                WalkCombos(c, sb, path + "/" + c.GetType().Name);
            }
        }
#endif

#if DEBUG
        private void DumpTopBar()
        {
            try
            {
                if (_topBar == null) return;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("TOPBAR bounds=" + _topBar.Bounds
                    + " visible=" + _topBar.Visible
                    + " children=" + _topBar.Controls.Count);
                foreach (Control c in _topBar.Controls)
                {
                    sb.AppendLine("   " + c.GetType().Name
                        + " bounds=" + c.Bounds
                        + " vis=" + c.Visible
                        + " text=" + c.Text);
                }

                sb.AppendLine();
                sb.AppendLine("=== ComboBox / 叠加箭头 ===");
                WalkCombos(this, sb, "");
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lanzhu_topbar.txt"),
                    sb.ToString());
            }
            catch
            {
            }
        }
#endif

#if DEBUG
        /// <summary>
        /// 把所有「文字放不下」的控件列出来，写到 %TEMP%\lanzhu_text_clip.txt。
        /// 只用于开发期间定位显示不全的问题，Release 不编译。
        /// </summary>
        private void AuditTextClipping()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 文字显示审计");
                sb.AppendLine("# need=文字实际尺寸  avail=控件真正可见的可用尺寸（已扣掉勾选框/下拉箭头/按钮图标占位）");
                sb.AppendLine();
                AuditWalk(this, sb, "");
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "lanzhu_text_clip.txt"),
                    sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        /// <summary>控件在屏幕坐标下真正可见的区域（会被所有祖先的客户区裁掉）。</summary>
        private static Rectangle AuditVisibleRect(Control c)
        {
            Rectangle r = c.RectangleToScreen(c.ClientRectangle);
            if (r.Width <= 0 || r.Height <= 0)
                return Rectangle.Empty;

            Control p = c.Parent;
            while (p != null)
            {
                Rectangle cr = p.ClientRectangle;
                if (cr.Width <= 0 || cr.Height <= 0)
                    return Rectangle.Empty;

                r = Rectangle.Intersect(r, p.RectangleToScreen(cr));
                if (r.Width <= 0 || r.Height <= 0)
                    return Rectangle.Empty;

                p = p.Parent;
            }
            return r;
        }

        private static void AuditWalk(Control root, StringBuilder sb, string path)
        {
            foreach (Control c in root.Controls)
            {
                string tag = path + "/" + c.Name + "[" + c.GetType().Name + "]";
                string t = c.Text;

                if (!string.IsNullOrEmpty(t) && t.IndexOf('\n') < 0 && t.IndexOf('\r') < 0)
                {
                    // 多行文本框不参与：文字本来就会折行/滚动，横向超宽不是“显示不全”
                    bool multiLine = (c is TextBoxBase) && ((TextBoxBase)c).Multiline;

                    bool textCtl = !multiLine && (c is Label || c is CheckBox || c is RadioButton
                        || c is Button || c is ComboBox || c is NumericUpDown
                        || c is TextBoxBase || c is GroupBox);

                    if (textCtl)
                    {
                        // 隐藏页（未选中的 TabPage）拿不到屏幕坐标，退化成用自身客户区判断
                        bool hidden = !c.Visible;
                        Rectangle vis = hidden
                            ? new Rectangle(Point.Empty, c.ClientSize)
                            : AuditVisibleRect(c);

                        int reserve = (c is CheckBox || c is RadioButton) ? 18 : (c is ComboBox ? 20 : 0);
                        int imgPad = 0;
                        Button btn = c as Button;
                        if (btn != null && btn.Image != null)
                            imgPad = btn.Image.Width + 10;   // 有图标时文字被挤到一边

                        int availW = vis.Width - reserve - imgPad;
                        int availH = vis.Height;

                        Size need = TextRenderer.MeasureText(t, c.Font);
                        int needW = need.Width;
                        int needH = need.Height;
                        if (c is TextBoxBase || c is ComboBox || c is NumericUpDown)
                            needH -= 2;   // 输入类控件允许比字高低 2px

                        if (vis.Width <= 0 || vis.Height <= 0 || availW <= 0
                            || needW > availW || needH > availH)
                        {
                            sb.AppendLine(needH > availH ? "[高度不足]" : "[宽度不足]");
                            sb.AppendLine("  " + tag + (hidden ? "  (隐藏页)" : ""));
                            sb.AppendLine("     text  = \"" + t + "\"");
                            sb.AppendLine("     need  = " + needW + "x" + needH
                                + "   avail = " + availW + "x" + availH);
                            sb.AppendLine("     ctrl  = " + c.Bounds
                                + "   dock=" + c.Dock
                                + "   autosize=" + c.AutoSize
                                + "   imgPad=" + imgPad);
                            sb.AppendLine("     font  = " + c.Font.Name + " "
                                + c.Font.SizeInPoints.ToString("0.##") + "pt");
                        }
                    }
                }

                // 列表里的长路径 / 长文件名也是「显示不全」的重灾区
                ListBox lb = c as ListBox;
                if (lb != null && lb.ClientSize.Width > 0)
                {
                    int over = 0;
                    for (int i = 0; i < lb.Items.Count && i < 300; i++)
                    {
                        int w = TextRenderer.MeasureText(
                            Convert.ToString(lb.Items[i]), lb.Font).Width;
                        if (w > lb.ClientSize.Width - 6)
                            over++;
                    }
                    if (over > 0)
                    {
                        sb.AppendLine("[列表项超宽]");
                        sb.AppendLine("  " + tag + "  超宽 " + over + "/" + lb.Items.Count
                            + "  控件宽=" + lb.ClientSize.Width);
                    }
                }

                AuditWalk(c, sb, path + "/" + c.Name);
            }
        }
#endif

        #endregion
    }
}
