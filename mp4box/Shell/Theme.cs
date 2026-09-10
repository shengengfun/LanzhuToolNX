// ------------------------------------------------------------------
// 岚珠工具箱 —— 现代 UI 外壳：全局主题
//
// 设计令牌直接对齐 ShiorikoTrans 的 design tokens（globals.css）：
//   background  hsl(210 34% 96%)   → #F1F5F8
//   foreground  hsl(215 22% 17%)   → #222A35
//   primary     hsl(157 55% 45%)   → #34B282
//   border      hsl(208 22% 83%)   → #CAD4DD
//   muted-fg    hsl(214 12% 44%)   → #636E7E
//   radius      0.75rem            → 12px
//   背景是「浅蓝 + 浅紫径向光晕 + 纵向渐变」，卡片用柔和投影浮起来。
//
// 这一层只负责外观，不改变任何布局坐标，对 4 套本地化 resx 完全安全。
// ------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace mp4box.Shell
{
    internal static class Theme
    {
        #region 颜色（ShiorikoTrans tokens）

        public static readonly Color PageBg = ColorTranslator.FromHtml("#F1F5F8");
        public static readonly Color PageBgTop = ColorTranslator.FromHtml("#F5F8FA");
        public static readonly Color PageBgBottom = ColorTranslator.FromHtml("#EFF3F7");
        public static readonly Color GlowBlue = ColorTranslator.FromHtml("#C5E5FC");
        public static readonly Color GlowViolet = ColorTranslator.FromHtml("#E8CBF6");

        public static readonly Color CardBg = Color.White;
        public static readonly Color CardBorder = ColorTranslator.FromHtml("#CAD4DD");
        public static readonly Color Divider = ColorTranslator.FromHtml("#E1E8EE");
        public static readonly Color TrackBg = ColorTranslator.FromHtml("#E1E8EE");

        public static readonly Color Accent = ColorTranslator.FromHtml("#34B282");
        public static readonly Color AccentSoft = ColorTranslator.FromHtml("#E3F5EE");
        public static readonly Color AccentGlow = ColorTranslator.FromHtml("#9FE0C8");
        public static readonly Color AccentDeep = ColorTranslator.FromHtml("#2A9A70");

        public static readonly Color TextPrimary = ColorTranslator.FromHtml("#222A35");
        public static readonly Color TextMuted = ColorTranslator.FromHtml("#636E7E");
        public static readonly Color TextFaint = ColorTranslator.FromHtml("#93A0AE");

        public static readonly Color HoverBg = ColorTranslator.FromHtml("#E9EEF1");
        public static readonly Color InputBorder = ColorTranslator.FromHtml("#CAD4DD");
        public static readonly Color WindowBorder = ColorTranslator.FromHtml("#C2CDD9");
        /// <summary>输入/输出类区域（日志、列表）的底色，比纯白稍沉一点，避免看起来像“一块空洞”。</summary>
        public static readonly Color LogBg = ColorTranslator.FromHtml("#F5F8FB");

        public static readonly Color StateIdle = ColorTranslator.FromHtml("#B4BFCA");
        public static readonly Color StateOk = Accent;
        public static readonly Color StateBusy = ColorTranslator.FromHtml("#3E8FD6");
        public static readonly Color StateWarn = ColorTranslator.FromHtml("#E0912A");
        public static readonly Color StateError = ColorTranslator.FromHtml("#DC4C4C");

        #endregion

        #region 度量（紧凑化）

        public const int CardRadius = 12;
        public const int ChipRadius = 9;
        public const int Gap = 8;

        public const int TopBarHeight = 46;
        public const int StatusBarHeight = 30;
        public const int CardPad = 8;

        /// <summary>左栏内容框（= 原 TabControl 的 580×613 收窄到刚好放下 572×587 的页面）。</summary>
        public const int LeftFrameWidth = 580;
        public const int LeftFrameHeight = 599;
        /// <summary>左栏卡片（内容框 + 四周内边距）。</summary>
        public const int LeftCardWidth = LeftFrameWidth + CardPad * 2;
        public const int LeftCardHeight = LeftFrameHeight + CardPad * 2;
        public const int LeftColumnWidth = LeftCardWidth;

        public const int RightMinWidth = 380;

        #endregion

        #region 字体

        private static string _uiFamily;
        private static string _monoFamily;
        private static readonly Dictionary<string, Font> _fontCache = new Dictionary<string, Font>();

        public static string UiFamily
        {
            get
            {
                if (_uiFamily == null)
                    _uiFamily = PickFamily(new string[] { "Microsoft YaHei UI", "微软雅黑", "Microsoft YaHei", "Segoe UI" });
                return _uiFamily;
            }
        }

        public static string MonoFamily
        {
            get
            {
                if (_monoFamily == null)
                    _monoFamily = PickFamily(new string[] { "Cascadia Mono", "Consolas", "Courier New" });
                return _monoFamily;
            }
        }

        public static Font Font(float size)
        {
            return Font(size, FontStyle.Regular);
        }

        public static Font Font(float size, FontStyle style)
        {
            string key = size.ToString("0.##") + "|" + ((int)style).ToString();
            Font f;
            if (!_fontCache.TryGetValue(key, out f))
            {
                f = new Font(UiFamily, size, style, GraphicsUnit.Point);
                _fontCache[key] = f;
            }
            return f;
        }

        public static Font Mono(float size)
        {
            string key = "mono|" + size.ToString("0.##");
            Font f;
            if (!_fontCache.TryGetValue(key, out f))
            {
                f = new Font(MonoFamily, size, FontStyle.Regular, GraphicsUnit.Point);
                _fontCache[key] = f;
            }
            return f;
        }

        /// <summary>
        /// 原页面（功能区）统一使用的正文字体。
        ///
        /// 字号必须按 DPI 折算：原布局是按 96dpi / 9pt（em = 12px）设计的，
        /// 而现在进程是 DPI 感知的，在 125%（120dpi）下 9pt 会渲染成 15px，
        /// 宽 25% → 紧挨着的标签就会压到右边的下拉框上（典型症状：
        /// 「H.264」显示成「1.264」、「压制格式」缺字）。
        /// 所以这里取 7.5pt ≈ 12.5px，和原始设计尺寸基本一致，布局不会再打架。
        /// </summary>
        public static Font FormFont { get { return Font(7.5f); } }
        public static Font BaseFont { get { return Font(9f); } }
        public static Font H1 { get { return Font(11.5f, FontStyle.Bold); } }
        public static Font H2 { get { return Font(9.5f, FontStyle.Bold); } }
        public static Font Small { get { return Font(8f); } }

        private static string PickFamily(string[] names)
        {
            try
            {
                using (InstalledFontCollection col = new InstalledFontCollection())
                {
                    HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < col.Families.Length; i++)
                        set.Add(col.Families[i].Name);
                    for (int i = 0; i < names.Length; i++)
                        if (set.Contains(names[i])) return names[i];
                }
            }
            catch
            {
            }
            return "Microsoft Sans Serif";
        }

        #endregion

        #region 绘制工具

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0)
                return path;

            int d = radius * 2;
            if (radius <= 0 || d > r.Width || d > r.Height)
            {
                d = Math.Min(Math.Min(r.Width, r.Height), Math.Max(radius * 2, 0));
                if (d < 2)
                {
                    path.AddRectangle(r);
                    return path;
                }
            }

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Rectangle r, int radius, Color fill)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
        }

        /// <summary>多层递减透明度模拟柔和投影（对齐 ShiorikoTrans 的 --shadow-sm / --shadow）。</summary>
        public static void DrawSoftShadow(Graphics g, Rectangle card, int radius, int spread, Color baseColor)
        {
            for (int i = spread; i >= 1; i--)
            {
                int alpha = (int)Math.Round(baseColor.A * (1.0 - (double)i / (spread + 1)) * 0.34);
                if (alpha <= 0) continue;
                Rectangle r = new Rectangle(card.X - i, card.Y - i + 1, card.Width + i * 2, card.Height + i * 2);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, baseColor)))
                    FillRounded(g, r, radius + i, b.Color);
            }
        }

        #endregion

        #region 背景渐变

        private static Bitmap _backdrop;
        private static Size _backdropSize;

        /// <summary>
        /// ShiorikoTrans 同款背景：纵向浅蓝灰渐变 + 左上浅蓝光晕 + 右上浅紫光晕。
        /// 结果缓存成位图，避免每次重绘都算径向渐变。
        /// </summary>
        public static void PaintBackdrop(Graphics g, Size size)
        {
            if (size.Width <= 0 || size.Height <= 0)
                return;

            if (_backdrop == null || _backdropSize != size)
            {
                if (_backdrop != null) _backdrop.Dispose();
                _backdrop = BuildBackdrop(size);
                _backdropSize = size;
            }

            if (_backdrop != null)
                g.DrawImageUnscaled(_backdrop, 0, 0);
        }

        private static Bitmap BuildBackdrop(Size size)
        {
            Bitmap bmp = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (LinearGradientBrush lg = new LinearGradientBrush(
                    new Rectangle(0, 0, size.Width, size.Height), PageBgTop, PageBgBottom, 90f))
                {
                    g.FillRectangle(lg, 0, 0, size.Width, size.Height);
                }

                DrawGlow(g, size, 0.15f, -0.10f, 1.10f, 0.90f, Color.FromArgb(118, GlowBlue));
                DrawGlow(g, size, 1.00f, 0.00f, 0.90f, 0.75f, Color.FromArgb(74, GlowViolet));
            }
            return bmp;
        }

        private static void DrawGlow(Graphics g, Size size, float cx, float cy, float rw, float rh, Color color)
        {
            float x = size.Width * cx;
            float y = size.Height * cy;
            float rx = size.Width * rw * 0.5f;
            float ry = size.Height * rh * 0.5f;
            if (rx < 8 || ry < 8)
                return;

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(x - rx, y - ry, rx * 2, ry * 2);
                using (PathGradientBrush b = new PathGradientBrush(path))
                {
                    b.CenterColor = color;
                    b.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                    g.FillPath(b, path);
                }
            }
        }

        #endregion

        #region ControlExs 配色注入

        /// <summary>把 ControlExs（仿 QQ 蓝色玻璃贴图）切换为现代扁平渲染：只改渲染，不改行为。</summary>
        public static void ApplyControlExsTheme()
        {
            try
            {
                // 第一个参数是 QQTextBox 常态边框色。全站输入框统一成
                // 「浅灰填充 + 圆角 + 无边框」，这里设成透明把那条不统一的灰线去掉
                // （聚焦时的强调色描边仍然保留，作为唯一的输入焦点提示）。
                ControlExs.ColorTable.SetTheme(Color.Empty, AccentGlow, Accent);
                ControlExs.QQButton.FlatTheme = true;
                ControlExs.QQButton.FlatBack = CardBg;
                ControlExs.QQButton.FlatBorder = CardBorder;
                ControlExs.QQButton.FlatHover = HoverBg;
                ControlExs.QQButton.FlatDown = ColorTranslator.FromHtml("#E7EDF3");
                ControlExs.QQButton.FlatText = TextPrimary;
                ControlExs.QQButton.FlatDisabledBack = ColorTranslator.FromHtml("#F5F7FA");
                ControlExs.QQButton.FlatDisabledText = TextFaint;
                ControlExs.QQButton.FlatAccent = Accent;
                ControlExs.QQButton.FlatAccentHover = AccentDeep;
                ControlExs.QQButton.FlatAccentDown = ColorTranslator.FromHtml("#28996B");
                ControlExs.QQButton.FlatAccentText = Color.White;
                ControlExs.QQButton.FlatRadius = RadiusMd;

                // variant 矩阵里其余几种变体的配色
                ControlExs.QQButton.FlatSecondary = ColorTranslator.FromHtml("#E2EAF1");
                ControlExs.QQButton.FlatSecondaryHover = ColorTranslator.FromHtml("#D6E1EA");
                ControlExs.QQButton.FlatSecondaryDown = ColorTranslator.FromHtml("#CAD8E4");
                ControlExs.QQButton.FlatDestructive = ColorTranslator.FromHtml("#E04C4C");
                ControlExs.QQButton.FlatDestructiveHover = ColorTranslator.FromHtml("#CF4040");

                // 勾选框 / 单选框：默认皮肤画的是旧版 QQ 位图 + 系统原生方块，
                // 和扁平 UI 完全不是一个时代，这里统一切到自绘扁平风。
                ControlExs.QQCheckBox.FlatTheme = true;
                ControlExs.QQCheckBox.FlatAccent = Accent;
                ControlExs.QQCheckBox.FlatBorder = InputBorder;
                ControlExs.QQCheckBox.FlatText = TextPrimary;
                ControlExs.QQCheckBox.FlatDisabledBorder = ColorTranslator.FromHtml("#D8DFE7");
                ControlExs.QQCheckBox.FlatDisabledBack = ColorTranslator.FromHtml("#F0F3F7");
                ControlExs.QQCheckBox.FlatDisabledCheck = ColorTranslator.FromHtml("#C3CDD8");

                ControlExs.QQRadioButton.FlatTheme = true;
                ControlExs.QQRadioButton.FlatAccent = Accent;
                ControlExs.QQRadioButton.FlatBorder = InputBorder;
                ControlExs.QQRadioButton.FlatText = TextPrimary;
                ControlExs.QQRadioButton.FlatDisabledBorder = ColorTranslator.FromHtml("#D8DFE7");
                ControlExs.QQRadioButton.FlatDisabledBack = ColorTranslator.FromHtml("#F0F3F7");
                ControlExs.QQRadioButton.FlatDisabledCheck = ColorTranslator.FromHtml("#C3CDD8");
            }
            catch
            {
            }
        }

        /// <summary>主操作按钮（渲染成强调色实心）。</summary>
        private static readonly string[] PrimaryNames = new string[]
        {
            "x264StartBtn", "btnBatchAuto", "btnaac", "btnmux", "btnBatchMP4",
            "AudioOnePicButton", "AudioJoinButton", "AudioBatchButton",
            "BlackStartButton", "btnAVS9", "ExtractMP4Button"
        };

        private static bool IsPrimaryButton(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            for (int i = 0; i < PrimaryNames.Length; i++)
                if (string.Equals(PrimaryNames[i], name, StringComparison.Ordinal))
                    return true;
            return false;
        }

        #endregion

        #region 皮肤

        /// <summary>
        /// 把现代主题套到原有 WinForms 控件树上。
        /// 只改字体与背景色，绝不改动 Location / Size —— 保证 resx 本地化安全。
        /// </summary>
        public static void ApplySkin(Control root)
        {
            _skinFont = root.Font;
            ApplySkin(root, true);
        }

        /// <summary>ApplySkin 递归期间的有效基准字体（根控件的字体）。</summary>
        private static Font _skinFont;

        private static void ApplySkin(Control c, bool isRoot)
        {
            if (!isRoot)
            {
                if (c is CardPanel || c is NavChip || c is StatusBarEx || c is TopBar || c is TabStrip
                    || c is WindowButton || c is Stepper || c is ComboArrow || c is FieldFrame)
                {
                    // 新壳控件自己管外观
                }
                else if (c is TabPage)
                {
                    TabPage tp = (TabPage)c;
                    tp.UseVisualStyleBackColor = false;
                    tp.BackColor = Color.White;
                }
                else if (c is GroupBox)
                {
                    // 注意：千万不要改 GroupBox 的 ForeColor，
                    // 否则子控件会继承成灰色，看起来发虚、像“模糊”。
                    // 卡片标题的颜色由 GroupBoxCardPaint 单独画。
                    c.BackColor = Color.White;
                    SkinGroupBox((GroupBox)c);
                }
                else if (c is CheckBox || c is RadioButton)
                {
                    if (c.BackColor != Color.Transparent)
                        c.BackColor = Color.White;

                    // 原生勾选框带视觉样式边框，和这套扁平壳不搭，压平
                    CheckBox cb = c as CheckBox;
                    if (cb != null)
                        cb.FlatStyle = FlatStyle.Flat;
                    RadioButton rb = c as RadioButton;
                    if (rb != null)
                        rb.FlatStyle = FlatStyle.Flat;

                    // QQCheckBox / QQRadioButton 在构造函数里把字体写死成 9pt 微软雅黑，
                    // 不会跟随窗体字体，结果比周围文字大一圈、对不齐。统一切回基准字号。
                    if ((c is ControlExs.QQCheckBox || c is ControlExs.QQRadioButton)
                        && _skinFont != null
                        && c.Font.Size != _skinFont.Size)
                    {
                        c.Font = new Font(_skinFont.FontFamily, _skinFont.Size, c.Font.Style);
                    }
                }

                // 主操作按钮渲染成强调色实心
                ControlExs.QQButton qb = c as ControlExs.QQButton;
                if (qb != null)
                    qb.Accent = IsPrimaryButton(qb.Name);

                Flatten(c);
            }

            for (int i = 0; i < c.Controls.Count; i++)
                ApplySkin(c.Controls[i], false);

            if (isRoot)
            {
                // 顺序很重要：先把字段标签对齐成一列，再解决重叠，最后才做字号自适应
                TidyLayout(c);
                ResolveOverlaps(c);
                FitTexts(c);
            }
        }

        #endregion

        #region 标签不许压到右边的控件

        /// <summary>
        /// 换了字体之后，某些定宽标签会变宽，把右边控件的开头盖住
        /// （典型症状：下拉框里的 "H.264" 显示成 "1.264"）。
        /// 这里给每个标签算出「到右边最近控件」的可用宽度，超了就截断 + 让字号自适应。
        /// </summary>
        private static void ResolveOverlaps(Control parent)
        {
            foreach (Control c in parent.Controls)
                ResolveOverlaps(c);

            foreach (Control c in parent.Controls)
            {
                Label lbl = c as Label;
                if (lbl == null || string.IsNullOrEmpty(lbl.Text))
                    continue;
                if (lbl.Text.IndexOf('\n') >= 0 || lbl.Text.IndexOf('\r') >= 0)
                    continue;

                int limit = int.MaxValue;
                foreach (Control other in parent.Controls)
                {
                    if (ReferenceEquals(other, lbl) || other is Label)
                        continue;
                    if (other.Left <= lbl.Left)
                        continue;
                    if (other.Top >= lbl.Bottom || other.Bottom <= lbl.Top)
                        continue;
                    int gap = other.Left - lbl.Left;
                    if (gap > 0 && gap < limit)
                        limit = gap;
                }

                if (limit == int.MaxValue)
                    continue;

                int avail = limit - 3;
                if (avail < 16)
                    continue;

                Font baseFont;
                if (!_baseFonts.TryGetValue(lbl, out baseFont))
                {
                    baseFont = lbl.Font;
                    _baseFonts[lbl] = baseFont;
                }

                // 只缩字号，绝对不动标签宽度 —— 动宽度会把字裁掉
                Size box = new Size(avail, lbl.Height + 6);
                if (TextFits(lbl.Text, baseFont, box))
                    continue;

                for (float s = baseFont.SizeInPoints - 0.25f; s >= 7f; s -= 0.25f)
                {
                    Font probe = Font(s, baseFont.Style);
                    if (TextFits(lbl.Text, probe, box))
                    {
                        lbl.Font = probe;
                        break;
                    }
                }
            }
        }

        #endregion

        #region 字段标签列对齐（根治「对不齐」）

        /// <summary>
        /// 让同一个容器里的「字段标签」右对齐成一列，并统一到右边输入框的间距。
        ///
        /// 原来标签一律左对齐，而各自文字宽度不同 → 右边缘参差不齐，看起来就是"对不齐"。
        /// 这里给同一列标签一个共同的盒子，文字右对齐，盒子右边缘统一停在
        /// 「该列最左边那个输入控件」往左 Theme.Space(2) 的位置。
        ///
        /// 安全保证：
        ///  - 只动标签、只往左移，绝不往右跑，所以不可能盖住任何输入控件；
        ///  - 按「目标控件的 X」分列，同一行里的两个标签不会被拉到同一个盒子里重叠；
        ///  - 左边放不下就整列放弃，不冒险；
        ///  - 不依赖可见性，隐藏页（未选中的 TabPage）一样会排。
        /// </summary>
        public static void TidyLayout(Control root)
        {
            if (root == null)
                return;
            TidyWalk(root);
        }

        private static void TidyWalk(Control parent)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
                TidyWalk(parent.Controls[i]);

            AlignFieldLabels(parent);
        }

        private static void AlignFieldLabels(Control parent)
        {
            const int MaxGap = 100;      // 离右边控件超过这个距离，就不认作它的标签
            const int SameColumn = 4;    // 目标 X 相差在这个范围内视为同一列
            int gap = Space(2);          // 标签与输入框的统一间距 = 8px（4px 栅格的两格）

            List<Label> labels = new List<Label>();
            List<int> targets = new List<int>();

            foreach (Control c in parent.Controls)
            {
                Label lbl = c as Label;
                if (lbl == null || string.IsNullOrEmpty(lbl.Text))
                    continue;
                if (lbl.Text.IndexOf('\n') >= 0 || lbl.Text.IndexOf('\r') >= 0)
                    continue;
                if (lbl.Dock != DockStyle.None)
                    continue;

                int best = int.MaxValue;
                foreach (Control other in parent.Controls)
                {
                    if (ReferenceEquals(other, lbl))
                        continue;
                    if (other is Label || other is FieldFrame)
                        continue;
                    if (other.Left < lbl.Left)
                        continue;
                    if (other.Top >= lbl.Bottom || other.Bottom <= lbl.Top)
                        continue;                       // 不在同一条横带上
                    if (other.Left - lbl.Right > MaxGap)
                        continue;
                    if (other.Left < best)
                        best = other.Left;
                }

                if (best == int.MaxValue)
                    continue;                           // 右边没有对应控件，不是字段标签

                labels.Add(lbl);
                targets.Add(best);
            }

            if (labels.Count < 2)
                return;                                 // 单个标签没有"列"可言

            // 按目标 X 分列：只有同一列的标签才能共用同一个盒子
            List<int> order = new List<int>();
            for (int i = 0; i < labels.Count; i++)
                order.Add(i);
            order.Sort(delegate (int a, int b) { return targets[a].CompareTo(targets[b]); });

            int start = 0;
            while (start < order.Count)
            {
                int end = start;
                while (end + 1 < order.Count
                    && targets[order[end + 1]] - targets[order[start]] <= SameColumn)
                    end++;

                ApplyColumn(labels, targets, order, start, end, gap);
                start = end + 1;
            }
        }

        private static void ApplyColumn(
            List<Label> labels, List<int> targets, List<int> order,
            int from, int to, int gap)
        {
            if (to - from + 1 < 2)
                return;                                 // 这一列只有一个标签，不用对齐

            // 同一列里如果有两个标签在垂直方向重叠（同一行的两个标签），
            // 拉到同一个盒子会互相盖住，整列放弃。
            for (int i = from; i <= to; i++)
            {
                for (int j = i + 1; j <= to; j++)
                {
                    Label a = labels[order[i]];
                    Label b = labels[order[j]];
                    if (a.Top < b.Bottom && b.Top < a.Bottom)
                        return;
                }
            }

            // 右边缘统一停在这里，所有标签的文字右端就会严丝合缝排成一列
            int minTarget = int.MaxValue;
            int minW = int.MaxValue;
            for (int i = from; i <= to; i++)
            {
                if (targets[order[i]] < minTarget)
                    minTarget = targets[order[i]];

                int w = labels[order[i]].Width;
                if (w < minW) minW = w;
            }

            int right = minTarget - gap;
            if (right - minW < Space(1))
                return;                                 // 左边塞不下，不冒险

            for (int i = from; i <= to; i++)
            {
                Label lbl = labels[order[i]];
                if (!lbl.AutoSize && lbl.TextAlign != ContentAlignment.MiddleRight)
                {
                    // 定宽标签：让文字在盒子里右对齐，视觉右端才和整列一致。
                    // 注意这里**不动 AutoSize、不动 Width、不动 Height** ——
                    // 一旦把 AutoSize 冻结成 false，designer 里那个过矮的 Size 就会
                    // 把文字竖向裁掉（审计实测 13 处 need 19px / 实际只有 12px）。
                    lbl.TextAlign = ContentAlignment.MiddleRight;
                }

                lbl.Location = new Point(right - lbl.Width, lbl.Top);
            }
        }

        #endregion

        #region 文字自动适配

        private static readonly Dictionary<Control, Font> _baseFonts = new Dictionary<Control, Font>();

        /// <summary>
        /// 把所有「文字放不下」的 Label / Button / CheckBox / RadioButton 逐档缩小字号直到刚好装下。
        /// 只动字体，**不动任何控件尺寸**，所以对 resx 本地化完全安全（切换语言后重跑一次即可）。
        /// </summary>
        public static void FitTexts(Control root)
        {
            if (root == null)
                return;

            FitTextWalk(root, true);
        }

        private static void FitTextWalk(Control c, bool isRoot)
        {
            if (!isRoot)
                TryFitText(c);

            for (int i = 0; i < c.Controls.Count; i++)
                FitTextWalk(c.Controls[i], false);
        }

        private static void TryFitText(Control c)
        {
            if (c.AutoSize)
                return;

            bool isCombo = c is ComboBox;
            if (!(c is Label || c is Button || c is CheckBox || c is RadioButton || isCombo))
                return;

            string t = c.Text;
            if (string.IsNullOrEmpty(t) || t.IndexOf('\n') >= 0 || t.IndexOf('\r') >= 0)
                return;

            Font baseFont;
            if (!_baseFonts.TryGetValue(c, out baseFont))
            {
                baseFont = c.Font;
                _baseFonts[c] = baseFont;
            }

            // 勾选框要给方框留位；下拉框要给右侧箭头留位（否则 H.264 会被裁成 1.264）
            int reserve = (c is CheckBox || c is RadioButton) ? 18 : (isCombo ? 20 : 0);
            int heightSlack = isCombo || c is TextBoxBase ? 2 : 0;

            Size have = c.ClientSize;
            if (have.Width <= 0 || have.Height <= 0)
                return;

            // ── 先修竖向：designer 里的标签在 96dpi 下定高 12px 就够，
            //    开了 DPI 感知 + 换雅黑之后实际要 19px，文字上下会被硬裁掉。
            //    直接把高度补够（不动上边，也不越过父容器底边）。
            int textH = TextRenderer.MeasureText(t, baseFont).Height;
            if (textH > c.Height)
            {
                int limit = c.Parent != null ? c.Parent.ClientSize.Height : c.Top + textH;
                int want = Math.Min(c.Top + textH, limit);
                if (want > c.Height)
                {
                    c.Height = want;
                    have = c.ClientSize;
                    if (have.Height <= 0)
                        return;
                }
            }

            Size box = new Size(Math.Max(1, have.Width - reserve), have.Height + heightSlack);

            if (TextFits(t, baseFont, box))
            {
                if (c.Font.SizeInPoints != baseFont.SizeInPoints)
                    c.Font = baseFont;   // 语言切换后字号要回弹到基准
                return;
            }

            // ── 第 1 招：先「往右长」而不是缩字。
            //    右边本来就有空位时，吃过来既不影响别人，又能保住正常字号。
            //    （以前只会缩字号，缩到 7pt 还放不下就硬裁，用户看到的就是"显示不全"。）
            int room = FreeSpaceRight(c);
            if (room > box.Width)
            {
                Size need = TextRenderer.MeasureText(t, baseFont);
                int want = Math.Min(room - 2, need.Width + reserve + 4);
                int delta = want - box.Width;
                if (delta > 0)
                {
                    c.Width += delta;
                    box = new Size(Math.Max(1, c.ClientSize.Width - reserve),
                        c.ClientSize.Height + heightSlack);
                    if (TextFits(t, baseFont, box))
                    {
                        if (c.Font.SizeInPoints != baseFont.SizeInPoints)
                            c.Font = baseFont;
                        return;
                    }
                }
            }

            // ── 第 2 招：真没地方长了，才逐档缩字号（最小 7pt）
            for (float s = baseFont.SizeInPoints - 0.25f; s >= 7f; s -= 0.25f)
            {
                Font probe = Font(s, baseFont.Style);
                if (TextFits(t, probe, box))
                {
                    c.Font = probe;
                    return;
                }
            }

            // ── 第 3 招：7pt 还放不下，就改成省略号收尾，
            //    至少不会出现「一句话被硬砍掉一半」的观感。
            c.Font = baseFont;
            Label lbl = c as Label;
            if (lbl != null)
                lbl.AutoEllipsis = true;
        }

        /// <summary>
        /// 控件右侧到「同一条横带上最近的那个非标签控件左边界 / 父容器右边界」的空白宽度。
        /// 用于让文字优先「往右长」而不是缩字号。
        /// </summary>
        private static int FreeSpaceRight(Control c)
        {
            Control parent = c.Parent;
            if (parent == null)
                return c.Right;

            int limit = parent.ClientSize.Width;
            foreach (Control other in parent.Controls)
            {
                if (ReferenceEquals(other, c) || !other.Visible)
                    continue;
                if (other.Left < c.Right - 1)
                    continue;                                   // 在我们左边或与本控件重叠
                if (other.Top >= c.Bottom || other.Bottom <= c.Top)
                    continue;                                   // 不在同一条横带上
                if (other.Left < limit)
                    limit = other.Left;
            }
            return limit;
        }

        private static bool TextFits(string text, Font f, Size box)
        {
            Size need = TextRenderer.MeasureText(text, f);
            return need.Width <= box.Width && need.Height <= box.Height;
        }

        #endregion

        #region 设计令牌（等价于参考项目 globals.css 的 :root 变量）

        // 参考项目把所有颜色 / 圆角 / 阴影声明成 CSS 变量，再由 Tailwind 映射成
        // rounded-xl / shadow-sm 这类工具类。这里是同一套思路的 C# 版本：
        // 全项目只允许从这里取值，不允许在控件里现写字面量。

        /// <summary>栅格单位。对应 Tailwind 的 1 格 = 4px，所有间距都取它的整数倍。</summary>
        public const int Grid = 4;

        public static int Space(int units) { return units * Grid; }

        /// <summary>圆角刻度，对应参考项目的 --radius-sm / --radius-md / --radius-lg。</summary>
        public const int RadiusSm = 6;
        public const int RadiusMd = 8;
        public const int RadiusLg = 12;

        /// <summary>输入类控件的浅灰填充（对应参考项目 input 的 bg-muted/40）。</summary>
        public static readonly Color InputBg = ColorTranslator.FromHtml("#EFF3F7");

        /// <summary>
        /// 按控件高度取圆角。参考项目 h-9 配 rounded-lg(8px)、h-11 配 rounded-xl(12px)，
        /// 比例约 22%~27%；这里统一取 25% 再夹紧。
        /// 关键：**绝不允许半径超过高度的一半**，否则矮控件会被画成胶囊/椭圆，
        /// 同一屏里就会出现「椭圆和长方形混在一起」的观感。
        /// </summary>
        public static int ControlRadius(int height)
        {
            int r = (int)Math.Round(height * 0.25);
            if (r < 3) r = 3;
            if (r > 10) r = 10;
            if (r * 2 > height - 1) r = Math.Max(2, (height - 1) / 2);
            return r;
        }

        /// <summary>用 Region 把控件裁成圆角。只影响绘制，不影响布局。</summary>
        /// <param name="inset">向内收缩的像素。</param>
        public static void RoundCorners(Control c, int radius, int inset = 0)
        {
            if (c == null)
                return;

            Rectangle r = new Rectangle(inset, inset,
                c.Width - inset * 2, c.Height - inset * 2);
            if (r.Width <= 2 || r.Height <= 2)
                return;

            using (GraphicsPath path = Rounded(r, radius))
            {
                Region old = c.Region;
                c.Region = new Region(path);
                if (old != null)
                    old.Dispose();
            }
        }

        #endregion

        #region 压平原生硬边框 + 统一输入控件外观

        /// <summary>
        /// 把 WinForms 原生控件压平，并把所有输入类控件统一成
        /// 「浅灰填充 + 圆角描边」——描边由组合式外框 FieldFrame 画，
        /// 原生控件只负责行为，不去改它的边框（也改不掉）。
        /// 只在开头做一次，不动任何位置与尺寸。
        /// </summary>
        private static void Flatten(Control c)
        {
            // QQTextBox 自带 1px 浅灰边框（QQBorderColor），Theme 已把它设为透明
            ControlExs.QQTextBox qq = c as ControlExs.QQTextBox;
            if (qq != null)
            {
                qq.BorderStyle = BorderStyle.None;
                qq.BackColor = InputBg;
                Ui.Field(qq);
                return;
            }

            ComboBox combo = c as ComboBox;
            if (combo != null)
            {
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = InputBg;
                combo.ForeColor = TextPrimary;
                ComboArrow.Attach(combo);   // 自绘三角盖掉系统灰色箭头
                Ui.Field(combo);            // 外框负责圆角描边（同时盖住系统的深色边）
                return;
            }

            ListBox list = c as ListBox;
            if (list != null)
            {
                list.BorderStyle = BorderStyle.None;
                list.BackColor = LogBg;
                list.ForeColor = TextPrimary;
                return;
            }

            NumericUpDown num = c as NumericUpDown;
            if (num != null)
            {
                num.BorderStyle = BorderStyle.None;
                num.BackColor = InputBg;
                Stepper.Attach(num);   // 自绘箭头替掉系统灰色上下方块
                Ui.Field(num);
                return;
            }

            TextBoxBase tb = c as TextBoxBase;
            if (tb != null && !(tb is RichTextBox))
            {
                tb.BorderStyle = BorderStyle.None;
                tb.BackColor = InputBg;
                Ui.Field(tb);
            }
        }

        #endregion

        #region GroupBox 圆角卡片皮肤

        private static readonly HashSet<GroupBox> _skinnedBoxes = new HashSet<GroupBox>();

        /// <summary>
        /// 把 WinForms 原生的「蚀刻边框 + 标题」GroupBox 改画成圆角卡片。
        /// 只接管绘制，不动任何子控件坐标 —— resx 本地化依然安全。
        /// </summary>
        public static void SkinGroupBox(GroupBox gb)
        {
            if (gb == null || !_skinnedBoxes.Add(gb))
                return;
            gb.Paint += GroupBoxCardPaint;
        }

        private static void GroupBoxCardPaint(object sender, PaintEventArgs e)
        {
            GroupBox gb = sender as GroupBox;
            if (gb == null)
                return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 抹掉原生蚀刻边框与标题（子控件之后才绘制，不会被盖掉）
            using (SolidBrush bg = new SolidBrush(gb.BackColor))
                g.FillRectangle(bg, gb.ClientRectangle);

            Rectangle r = gb.ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            if (r.Width <= 4 || r.Height <= 4)
                return;

            using (GraphicsPath path = Rounded(r, 10))
            using (Pen p = new Pen(CardBorder))
                g.DrawPath(p, path);

            string title = gb.Text;
            if (string.IsNullOrEmpty(title))
                return;

            Font f = Small;
            Size ts = TextRenderer.MeasureText(title, f);
            if (ts.Width <= 0)
                return;

            int tx = 12;
            Rectangle back = new Rectangle(tx - 4, 0, ts.Width + 8, ts.Height);
            using (SolidBrush bg = new SolidBrush(gb.BackColor))
                g.FillRectangle(bg, back);
            TextRenderer.DrawText(g, title, f, new Point(tx, 0), TextMuted);
        }

        #endregion
    }
}
