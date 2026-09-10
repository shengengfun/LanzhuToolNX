// ------------------------------------------------------------------
// 岚珠工具箱 —— 现代 UI 外壳：右栏（输出区）
//
// 结构：圆角卡片 ─ 自绘标签条 + 内容宿主
// 内容宿主里放的是一整只 TabControl（它自己的标签条被隐藏），所以：
//   · 「日志 / 任务 / 预览」是运行时创建的 TabPage
//   · 「MediaInfo / 设置 / 帮助」是把原来的 TabPage 原样搬进来
// 页面不重建、控件树不动，本地化 resx 继续生效，
// 也不会踩到「TabPage 只能放进 TabControl」这条 WinForms 限制。
// ------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace mp4box.Shell
{
    internal sealed class OutputPanel : CardPanel
    {
        private readonly Panel _inner;
        private readonly TabStrip _strip;
        private readonly Panel _host;

        private readonly List<string> _titles = new List<string>();

        private TabControl _tabs;

        private readonly TabPage _logPage;
        private readonly TabPage _taskPage;
        private readonly TabPage _previewPage;

        private Button _abortButton;
        private Button _saveButton;
        private Button _copyButton;
        private Button _clearButton;
        private CheckBox _autoScroll;

        private Label _statusText;
        private Label _previewHint;
        private Label _logHint;
        private Label _taskHint;
        private Panel _logBar;

        /// <summary>日志面板。</summary>
        public RichTextBox LogBox { get; private set; }
        /// <summary>日志视图底部状态文本。</summary>
        public Label StatusText { get { return _statusText; } }

        /// <summary>内容宿主（右栏 TabControl 的容器）。</summary>
        public Panel Host { get { return _host; } }

        public Action AbortRequested;
        public Action SaveLogRequested;

        public const string TitleLog = "日志";
        public const string TitleTask = "任务";
        public const string TitlePreview = "预览";

        public OutputPanel()
        {
            // 给卡片的投影留出空间：Dock=Fill 的子控件会自动避开 Padding
            Padding = new Padding(CardPanel.Pad);

            _inner = new Panel();
            _inner.Dock = DockStyle.Fill;
            _inner.BackColor = Theme.CardBg;
            _inner.Padding = new Padding(10, 8, 10, 8);
            Controls.Add(_inner);

            // 先加 Fill 的宿主，再加 Top 的标签条：
            // WinForms 的停靠是按 z 序倒序处理的，Fill 必须先于 Top/Bottom 加入。
            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = Theme.CardBg;
            _inner.Controls.Add(_host);

            _strip = new TabStrip();
            _strip.SelectedChanged = delegate(int i)
            {
                if (_tabs != null && i >= 0 && i < _tabs.TabPages.Count)
                    _tabs.SelectedIndex = i;
            };
            _inner.Controls.Add(_strip);

            _logPage = new TabPage(TitleLog);
            _logPage.BackColor = Theme.CardBg;
            _logPage.UseVisualStyleBackColor = false;
            _logPage.Controls.Add(BuildLogView());

            _taskPage = new TabPage(TitleTask);
            _taskPage.BackColor = Theme.CardBg;
            _taskPage.UseVisualStyleBackColor = false;
            _taskPage.Controls.Add(BuildTaskView());

            _previewPage = new TabPage(TitlePreview);
            _previewPage.BackColor = Theme.CardBg;
            _previewPage.UseVisualStyleBackColor = false;
            _previewPage.Controls.Add(BuildPreviewView());
        }

        /// <summary>把右栏 TabControl 接进来（它自己的标签条由本面板自绘替代）。</summary>
        public void AttachTo(TabControl tabs)
        {
            _tabs = tabs;
            _tabs.TabPages.Add(_logPage);
            _tabs.TabPages.Add(_taskPage);
            _tabs.TabPages.Add(_previewPage);
            _tabs.SelectedIndexChanged += delegate { SyncStrip(); };
            _host.Controls.Add(_tabs);
            ShellTabs.Style(_tabs, _host);
            RefreshTitles();
        }

        /// <summary>切到某个已挂载的 TabPage。</summary>
        public void ShowPage(TabPage page)
        {
            if (_tabs == null || page == null)
                return;
            int i = _tabs.TabPages.IndexOf(page);
            if (i >= 0)
                _tabs.SelectedIndex = i;
        }

        public void ShowLog()
        {
            if (_tabs != null)
                _tabs.SelectedIndex = _tabs.TabPages.IndexOf(_logPage);
        }

        public void ShowTasks()
        {
            if (_tabs != null)
                _tabs.SelectedIndex = _tabs.TabPages.IndexOf(_taskPage);
        }

        /// <summary>刷新标签名（页面 Text 由 resx 本地化提供）。</summary>
        public void RefreshTitles()
        {
            _titles.Clear();
            if (_tabs != null)
            {
                for (int i = 0; i < _tabs.TabPages.Count; i++)
                    _titles.Add(Clean(_tabs.TabPages[i].Text));
                _strip.SelectedIndex = _tabs.SelectedIndex;
            }
            _strip.SetTitles(_titles);
        }

        private void SyncStrip()
        {
            if (_tabs == null)
                return;
            _strip.SelectedIndex = _tabs.SelectedIndex;
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Replace("&", "");
        }

        #region 运行期页面的本地化

        /// <summary>由 MainForm 在语言切换时调用，写入运行期页面与工具条的文案。</summary>
        public void ApplyLocalizedTexts(
            string log, string task, string preview,
            string abort, string save, string copy, string clear, string autoScroll,
            string queueEmpty, string queueHint, string previewHint,
            string logEmpty)
        {
            _logPage.Text = log;
            _taskPage.Text = task;
            _previewPage.Text = preview;

            _abortButton.Text = abort;
            _saveButton.Text = save;
            _copyButton.Text = copy;
            _clearButton.Text = clear;
            _autoScroll.Text = autoScroll;
            LayoutLogToolbar();

            if (_taskHint != null)
                _taskHint.Text = string.IsNullOrEmpty(queueHint)
                    ? queueEmpty
                    : queueEmpty + "\r\n" + queueHint;

            _previewHint.Text = previewHint;

            if (_logHint != null)
                _logHint.Text = logEmpty;

            RefreshTitles();
        }

        #endregion

        #region 日志视图

        /// <summary>
        /// 重排日志工具条。右栏一变窄，「自动滚动」就自动换到第二行，
        /// 保证任何窗口尺寸下都不会被卡片边缘吃掉。
        /// </summary>
        private void LayoutLogToolbar()
        {
            if (_logBar == null || _abortButton == null || _saveButton == null
                || _copyButton == null || _clearButton == null || _autoScroll == null)
                return;

            const int Pad = 22;
            const int Gap = 5;
            const int RowY = 2;

            _abortButton.Width = TextRenderer.MeasureText(_abortButton.Text, _abortButton.Font).Width + Pad;
            _saveButton.Width = TextRenderer.MeasureText(_saveButton.Text, _saveButton.Font).Width + Pad;
            _copyButton.Width = TextRenderer.MeasureText(_copyButton.Text, _copyButton.Font).Width + Pad;
            _clearButton.Width = TextRenderer.MeasureText(_clearButton.Text, _clearButton.Font).Width + Pad;

            _abortButton.Location = new Point(0, RowY);
            _saveButton.Location = new Point(_abortButton.Right + Gap, RowY);
            _copyButton.Location = new Point(_saveButton.Right + Gap, RowY);
            _clearButton.Location = new Point(_copyButton.Right + Gap, RowY);

            int toggleWidth = TextRenderer.MeasureText(_autoScroll.Text, _autoScroll.Font).Width + 22;
            int avail = _logBar.ClientSize.Width;

            if (_clearButton.Right + 10 + toggleWidth <= avail)
            {
                _autoScroll.Location = new Point(_clearButton.Right + 10, RowY + 6);
                _logBar.Height = 32;
            }
            else
            {
                _autoScroll.Location = new Point(0, RowY + 26);
                _logBar.Height = 58;
            }
        }

        private Control BuildLogView()
        {
            Panel view = new Panel();
            view.Dock = DockStyle.Fill;
            view.BackColor = Theme.CardBg;

            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 32;
            bar.BackColor = Theme.CardBg;
            bar.SizeChanged += delegate { LayoutLogToolbar(); };
            _logBar = bar;

            _abortButton = Ui.Flat("中止运行");
            _abortButton.Location = new Point(0, 2);
            _abortButton.Enabled = false;
            _abortButton.Click += delegate
            {
                if (AbortRequested != null) AbortRequested();
            };
            bar.Controls.Add(_abortButton);

            _saveButton = Ui.Flat("保存日志");
            _saveButton.Location = new Point(_abortButton.Right + 6, 2);
            _saveButton.Click += delegate
            {
                if (SaveLogRequested != null) SaveLogRequested();
            };
            bar.Controls.Add(_saveButton);

            _copyButton = Ui.Flat("复制");
            _copyButton.Location = new Point(_saveButton.Right + 6, 2);
            _copyButton.Click += delegate
            {
                try
                {
                    if (LogBox != null && LogBox.TextLength > 0)
                        Clipboard.SetText(LogBox.Text);
                }
                catch
                {
                }
            };
            bar.Controls.Add(_copyButton);

            _clearButton = Ui.Flat("清空");
            _clearButton.Location = new Point(_copyButton.Right + 6, 2);
            _clearButton.Click += delegate
            {
                if (LogBox != null) LogBox.Clear();
            };
            bar.Controls.Add(_clearButton);

            _autoScroll = new CheckBox();
            _autoScroll.Text = "自动滚动";
            _autoScroll.Checked = true;
            _autoScroll.AutoSize = true;
            _autoScroll.Font = Theme.Small;
            _autoScroll.ForeColor = Theme.TextMuted;
            _autoScroll.BackColor = Theme.CardBg;
            _autoScroll.Location = new Point(_clearButton.Right + 14, 8);
            bar.Controls.Add(_autoScroll);

            LogBox = new RichTextBox();
            LogBox.Dock = DockStyle.Fill;
            LogBox.BorderStyle = BorderStyle.None;
            LogBox.BackColor = Theme.LogBg;
            LogBox.ForeColor = Theme.TextPrimary;
            LogBox.Font = Theme.Mono(9f);
            LogBox.ReadOnly = true;
            LogBox.WordWrap = false;
            LogBox.HideSelection = false;
            LogBox.ScrollBars = RichTextBoxScrollBars.Both;
            LogBox.DetectUrls = false;

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            // 28 而不是 24：8pt 的“就绪”在 DPI 125% 下实际占 20px，
            // 高度只给 24 再加上 Y=5 就会被底部裁掉 1px（审计工具实测）。
            bottom.Height = 28;
            bottom.BackColor = Theme.CardBg;

            _statusText = new Label();
            _statusText.AutoSize = true;
            _statusText.Font = Theme.Small;
            _statusText.ForeColor = Theme.TextFaint;
            _statusText.Text = "就绪";
            _statusText.Location = new Point(2, 4);
            bottom.Controls.Add(_statusText);

            _logHint = new Label();
            _logHint.Dock = DockStyle.Fill;
            _logHint.TextAlign = ContentAlignment.MiddleCenter;
            _logHint.Font = Theme.Font(10f);
            _logHint.ForeColor = Theme.TextFaint;
            _logHint.BackColor = Theme.LogBg;
            _logHint.Text = "运行日志会显示在这里";

            view.Controls.Add(_logHint);
            view.Controls.Add(LogBox);
            view.Controls.Add(bottom);
            view.Controls.Add(bar);

            // 盖掉系统那条厚重的滚动条，换成 6px 细滑块。
            // 必须整条盖住（系统默认 17px），底色用日志区自己的颜色才能无缝。
            SlimScrollBar.Attach(LogBox, 17, Theme.LogBg);
            return view;
        }

        /// <summary>追加日志。</summary>
        public void AppendLog(string text)
        {
            if (LogBox == null || string.IsNullOrEmpty(text))
                return;
            if (_logHint != null)
                _logHint.Visible = false;
            LogBox.AppendText(text);
            if (_autoScroll == null || _autoScroll.Checked)
            {
                LogBox.SelectionStart = LogBox.TextLength;
                LogBox.ScrollToCaret();
            }
        }

        public void ClearLog()
        {
            if (LogBox != null)
                LogBox.Clear();
            if (_logHint != null)
                _logHint.Visible = true;
        }

        #endregion

        #region 任务视图

        private Control BuildTaskView()
        {
            Panel view = new Panel();
            view.Dock = DockStyle.Fill;
            view.BackColor = Theme.CardBg;

            _taskHint = new Label();
            _taskHint.Dock = DockStyle.Fill;
            _taskHint.TextAlign = ContentAlignment.MiddleCenter;
            _taskHint.Font = Theme.Font(10f);
            _taskHint.ForeColor = Theme.TextFaint;
            _taskHint.BackColor = Theme.CardBg;
            _taskHint.Text = "任务队列为空\r\n在左侧选择功能并开始压制后，任务会显示在这里";

            ListView list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = false;
            list.BorderStyle = BorderStyle.None;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Font = Theme.BaseFont;
            list.BackColor = Theme.CardBg;
            list.Columns.Add("#", 40);
            list.Columns.Add("文件", 300);
            list.Columns.Add("状态", 90);
            list.Columns.Add("进度", 70);

            view.Controls.Add(list);
            view.Controls.Add(_taskHint);   // 后加的在最上层：空状态盖在列表上
            return view;
        }

        #endregion

        #region 预览视图

        private Control BuildPreviewView()
        {
            Panel view = new Panel();
            view.Dock = DockStyle.Fill;
            view.BackColor = Theme.CardBg;

            _previewHint = new Label();
            _previewHint.Dock = DockStyle.Fill;
            _previewHint.TextAlign = ContentAlignment.MiddleCenter;
            _previewHint.Font = Theme.Font(10f);
            _previewHint.ForeColor = Theme.TextFaint;
            _previewHint.BackColor = Theme.CardBg;
            _previewHint.Text = "AVS 预览画布\r\n\r\n目前预览仍以独立窗口弹出，后续会嵌入到这里";
            view.Controls.Add(_previewHint);
            return view;
        }

        #endregion
    }
}
