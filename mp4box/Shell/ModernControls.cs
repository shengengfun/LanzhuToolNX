// ------------------------------------------------------------------
// 岚珠工具箱 —— 现代 UI 外壳：自绘控件
//
// CardPanel   圆角卡片（带柔和投影，支持透明背景）
// NavChip     顶栏胶囊导航项
// TopBar      顶栏
// StatusBarEx 底栏（状态点 / 任务 / 进度 / CPU / MEM / 版本）
// TabStrip    右栏标签条
// DropZone    虚线拖放区
// Stepper     NumericUpDown 的现代上下箭头（替掉系统灰方块）
// ShellTabs   把 TabControl 变成纯页面宿主
// Ui          轻量控件工厂
// ------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace mp4box.Shell
{
    #region CardPanel

    /// <summary>圆角卡片：投影 + 白底 + 细边框。子控件请按 <see cref="Pad"/> 内缩。</summary>
    internal class CardPanel : Panel
    {
        /// <summary>投影需要的留白，内容请从这里开始摆放。</summary>
        public const int Pad = 6;

        private int _radius = Theme.CardRadius;
        private bool _showBorder = true;
        private Bitmap _cache;

        public int Radius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
        }

        public bool ShowBorder
        {
            get { return _showBorder; }
            set { _showBorder = value; Invalidate(); }
        }

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_cache == null || _cache.Width != Width || _cache.Height != Height)
                BuildCache();

            if (_cache != null)
                e.Graphics.DrawImageUnscaled(_cache, 0, 0);
        }

        private void BuildCache()
        {
            if (_cache != null)
            {
                _cache.Dispose();
                _cache = null;
            }

            int w = Width;
            int h = Height;
            if (w <= Pad * 2 + 4 || h <= Pad * 2 + 4)
                return;

            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                Rectangle card = new Rectangle(Pad, Pad, w - Pad * 2 - 1, h - Pad * 2 - 1);

                Theme.DrawSoftShadow(g, card, _radius, Pad, Color.FromArgb(70, 34, 42, 53));

                using (GraphicsPath path = Theme.Rounded(card, _radius))
                {
                    using (SolidBrush b = new SolidBrush(Theme.CardBg))
                        g.FillPath(b, path);
                    if (_showBorder)
                    {
                        using (Pen p = new Pen(Theme.CardBorder))
                            g.DrawPath(p, path);
                    }
                }
            }
            _cache = bmp;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _cache != null)
            {
                _cache.Dispose();
                _cache = null;
            }
            base.Dispose(disposing);
        }
    }

    #endregion

    #region NavChip

    /// <summary>顶栏胶囊导航项。</summary>
    internal sealed class NavChip : Control
    {
        private bool _hover;
        private bool _selected;

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        public NavChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Font(9.25f);
            Size = new Size(76, 30);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle r = ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;

            if (_selected)
                Theme.FillRounded(g, r, Theme.ChipRadius, Theme.AccentSoft);
            else if (_hover)
                Theme.FillRounded(g, r, Theme.ChipRadius, Theme.HoverBg);

            Color fore = _selected ? Theme.AccentDeep : Theme.TextMuted;
            Font f = _selected ? Theme.Font(9.25f, FontStyle.Bold) : Font;

            using (SolidBrush b = new SolidBrush(fore))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(Text, f, b, new RectangleF(0, 0, Width, Height), sf);
            }
        }
    }

    #endregion

    #region Stepper

    /// <summary>
    /// 盖在 NumericUpDown 右端的小箭头，用来替掉系统那个灰色上下方块。
    /// 只负责「点一下加减一个 Increment」，数值仍在原控件里，逻辑零改动。
    /// </summary>
    internal sealed class Stepper : Control
    {
        public const int Width_ = 15;

        private readonly NumericUpDown _owner;
        private int _hot = -1;   // -1 无 / 0 上 / 1 下

        private Stepper(NumericUpDown owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            // 必须跟输入框同底色，否则在浅灰底上会糊出一块白色方块（典型“狗皮膏药”）
            BackColor = Theme.InputBg;
            Cursor = Cursors.Hand;
        }

        private static readonly HashSet<NumericUpDown> _done = new HashSet<NumericUpDown>();

        /// <summary>幂等：同一个 NumericUpDown 只挂一次。</summary>
        public static void Attach(NumericUpDown num)
        {
            if (num == null || !_done.Add(num))
                return;

            for (int i = 0; i < num.Controls.Count; i++)
            {
                Control kid = num.Controls[i];
                if (kid.GetType().Name.IndexOf("UpDownButtons", StringComparison.Ordinal) >= 0)
                    kid.Visible = false;
            }

            Stepper s = new Stepper(num);
            s.Size = new Size(Width_, Math.Max(14, num.ClientSize.Height));
            s.Location = new Point(Math.Max(0, num.ClientSize.Width - Width_), 0);
            num.Controls.Add(s);
            s.BringToFront();

            s.Visible = num.Visible;
            num.VisibleChanged += delegate { s.Visible = num.Visible; };
        }

        private void Step(int dir)
        {
            try
            {
                decimal step = _owner.Increment <= 0m ? 1m : _owner.Increment;
                decimal v = _owner.Value + (dir > 0 ? step : -step);
                if (v > _owner.Maximum) v = _owner.Maximum;
                if (v < _owner.Minimum) v = _owner.Minimum;
                _owner.Value = v;
            }
            catch
            {
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Step(e.Y < Height / 2 ? 1 : -1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hot = e.Y < Height / 2 ? 0 : 1;
            if (hot != _hot)
            {
                _hot = hot;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != -1)
            {
                _hot = -1;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            float cx = Width / 2f;
            int half = Height / 2;

            using (Pen up = new Pen(_hot == 0 ? Theme.Accent : Theme.TextFaint, 1.3f))
            using (Pen dn = new Pen(_hot == 1 ? Theme.Accent : Theme.TextFaint, 1.3f))
            {
                float uy = half / 2f;
                g.DrawLine(up, cx - 3.5f, uy + 1.5f, cx, uy - 1.5f);
                g.DrawLine(up, cx, uy - 1.5f, cx + 3.5f, uy + 1.5f);

                float dy = half + half / 2f;
                g.DrawLine(dn, cx - 3.5f, dy - 1.5f, cx, dy + 1.5f);
                g.DrawLine(dn, cx, dy + 1.5f, cx + 3.5f, dy - 1.5f);
            }
        }
    }

    #endregion

    #region ComboArrow

    /// <summary>
    /// 盖在 ComboBox 原生下拉箭头上的自绘三角，替掉系统那个灰色方块。
    /// ComboBox 不能有子控件，所以它是挂在 ComboBox 的父容器上的。
    /// </summary>
    internal sealed class ComboArrow : Control
    {
        public const int ArrowWidth = 17;

        private readonly ComboBox _owner;
        private bool _hover;

        private ComboArrow(ComboBox owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.InputBg;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        private static readonly HashSet<ComboBox> _done = new HashSet<ComboBox>();

        public static void Attach(ComboBox combo)
        {
            if (combo == null || !_done.Add(combo))
                return;

            Control parent = combo.Parent;
            if (parent == null)
                return;

            ComboArrow a = new ComboArrow(combo);
            a.Size = new Size(ArrowWidth, Math.Max(12, combo.Height - 2));
            a.Location = new Point(
                combo.Left + Math.Max(0, combo.Width - ArrowWidth - 1),
                combo.Top + 1);

            parent.Controls.Add(a);
            a.BringToFront();

            // 箭头必须和输入框同底色、同圆角，否则会在圆角处露出一块白方角（“狗皮膏药”）
            Theme.RoundCorners(a, Theme.ControlRadius(a.Height));
            // 原控件被隐藏时必须跟着藏，否则会留下一个孤儿三角
            a.Visible = combo.Visible;
            combo.VisibleChanged += delegate { a.Visible = combo.Visible; };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            try
            {
                _owner.Focus();
                _owner.DroppedDown = !_owner.DroppedDown;
            }
            catch
            {
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            if (_hover)
            {
                Rectangle r = new Rectangle(1, 2, Width - 2, Height - 4);
                Theme.FillRounded(g, r, 5, Theme.HoverBg);
            }

            float cx = Width / 2f;
            float cy = Height / 2f;
            using (SolidBrush b = new SolidBrush(_hover ? Theme.AccentDeep : Theme.TextMuted))
            {
                PointF[] tri = new PointF[3];
                tri[0] = new PointF(cx - 3.6f, cy - 1.8f);
                tri[1] = new PointF(cx + 3.6f, cy - 1.8f);
                tri[2] = new PointF(cx, cy + 2.2f);
                g.FillPolygon(b, tri);
            }
        }
    }

    #endregion

    #region WindowButton

    /// <summary>自绘标题栏的窗口按钮（最小化 / 最大化 / 关闭）。</summary>
    internal sealed class WindowButton : Control
    {
        /// <summary>0=最小化 1=最大化/还原 2=关闭</summary>
        public int Kind;

        private bool _hover;
        private bool _maximized;

        public WindowButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.CardBg;
            Size = new Size(42, 28);
            Cursor = Cursors.Hand;
        }

        public bool Maximized
        {
            get { return _maximized; }
            set
            {
                if (_maximized == value) return;
                _maximized = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            Color fore = Theme.TextMuted;
            if (_hover)
            {
                Color hoverBack = (Kind == 2) ? Theme.StateError : Theme.HoverBg;
                Theme.FillRounded(g, new Rectangle(2, 2, Width - 4, Height - 4), 6, hoverBack);
                if (Kind == 2) fore = Color.White;
            }

            float cx = Width / 2f;
            float cy = Height / 2f;

            using (Pen p = new Pen(fore, 1.4f))
            {
                if (Kind == 0)
                {
                    g.DrawLine(p, cx - 5f, cy, cx + 5f, cy);
                }
                else if (Kind == 1)
                {
                    if (_maximized)
                    {
                        // 还原：两个叠起来的小方框
                        g.DrawRectangle(p, cx - 5f, cy - 2f, 7f, 7f);
                        g.DrawLine(p, cx - 2f, cy - 5f, cx + 6f, cy - 5f);
                        g.DrawLine(p, cx + 6f, cy - 5f, cx + 6f, cy + 2f);
                    }
                    else
                    {
                        g.DrawRectangle(p, cx - 5f, cy - 5f, 10f, 10f);
                    }
                }
                else
                {
                    g.DrawLine(p, cx - 5f, cy - 5f, cx + 5f, cy + 5f);
                    g.DrawLine(p, cx + 5f, cy - 5f, cx - 5f, cy + 5f);
                }
            }
        }
    }

    #endregion

    #region TopBar

    /// <summary>顶栏：品牌 + 胶囊导航。</summary>
    internal sealed class TopBar : Panel
    {
        private readonly List<NavChip> _chips = new List<NavChip>();
        private readonly Label _brand;
        private readonly Panel _divider;

        public Action<int> NavSelected;

        private int _selectedIndex = -1;

        /// <summary>窗口按钮回调：0=最小化 1=最大化/还原 2=关闭。</summary>
        public Action<int> WindowCommand;

        private readonly List<WindowButton> _winBtns = new List<WindowButton>();
        private WindowButton _btnMin;
        private WindowButton _btnMax;
        private WindowButton _btnClose;

        private bool _dragging;
        private Point _dragAnchor;   // 按下时鼠标的屏幕坐标
        private Point _dragOrigin;   // 按下时窗体左上角的屏幕坐标

        /// <summary>
        /// 从顶栏空白处拖动整个窗口。
        ///
        /// 这里刻意不用两个"经典"写法：
        ///   1) WM_NCHITTEST → HTCAPTION：会把整条顶栏（含最小化/最大化/关闭按钮）
        ///      都伪装成标题栏，导致点"关闭"没反应，正是之前的 bug。
        ///   2) ReleaseCapture + WM_NCLBUTTONDOWN(HTCAPTION)：依赖系统拖拽循环，
        ///      在 FormBorderStyle.None 的窗体上时灵时不灵。
        ///
        /// 改为直接跟随鼠标改 Location：行为完全确定，也绝不干扰任何子控件的点击。
        /// </summary>
        private void TopBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            Form form = FindForm();
            if (form == null || form.WindowState == FormWindowState.Maximized)
                return;

            _dragging = true;
            _dragAnchor = Cursor.Position;
            _dragOrigin = form.Location;

            Control src = sender as Control;
            if (src != null)
                src.Capture = true;
        }

        private void TopBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging)
                return;

            Form form = FindForm();
            if (form == null)
            {
                _dragging = false;
                return;
            }

            Point cur = Cursor.Position;
            form.Location = new Point(
                _dragOrigin.X + (cur.X - _dragAnchor.X),
                _dragOrigin.Y + (cur.Y - _dragAnchor.Y));
        }

        private void TopBar_MouseUp(object sender, MouseEventArgs e)
        {
            if (!_dragging)
                return;

            _dragging = false;

            Control src = sender as Control;
            if (src != null)
                src.Capture = false;
        }

        private void TopBar_MouseCaptureChanged(object sender, EventArgs e)
        {
            Control src = sender as Control;
            if (src != null && !src.Capture)
                _dragging = false;
        }

        private void TopBar_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && WindowCommand != null)
                WindowCommand(1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            TopBar_MouseDown(this, e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            TopBar_MouseMove(this, e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            TopBar_MouseUp(this, e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            TopBar_MouseDoubleClick(this, e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            TopBar_MouseCaptureChanged(this, e);
        }

        public void SetMaximized(bool maximized)
        {
            if (_btnMax != null)
                _btnMax.Maximized = maximized;
        }

        private const int ChipLeft = 146;
        private const int ChipTop = 8;

        public TopBar()
        {
            Dock = DockStyle.Top;
            Height = Theme.TopBarHeight;
            BackColor = Theme.CardBg;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _brand = new Label();
            _brand.AutoSize = false;
            _brand.Text = "岚珠工具箱";
            _brand.Font = Theme.H1;
            _brand.ForeColor = Theme.TextPrimary;
            _brand.TextAlign = ContentAlignment.MiddleLeft;
            _brand.Location = new Point(16, 8);
            _brand.Size = new Size(140, 30);
            _brand.BackColor = Color.Transparent;
            // 品牌名区域也要能拖动窗口（Label 会吃掉鼠标事件，不会冒泡给父控件）
            _brand.MouseDown += TopBar_MouseDown;
            _brand.MouseMove += TopBar_MouseMove;
            _brand.MouseUp += TopBar_MouseUp;
            _brand.MouseCaptureChanged += TopBar_MouseCaptureChanged;
            _brand.MouseDoubleClick += TopBar_MouseDoubleClick;
            Controls.Add(_brand);

            _divider = new Panel();
            _divider.Dock = DockStyle.Bottom;
            _divider.Height = 1;
            _divider.BackColor = Theme.Divider;
            Controls.Add(_divider);

            _btnMin = MakeWindowButton(0);
            _btnMax = MakeWindowButton(1);
            _btnClose = MakeWindowButton(2);

            Resize += delegate { LayoutRight(); };
        }

        private WindowButton MakeWindowButton(int kind)
        {
            WindowButton b = new WindowButton();
            b.Kind = kind;
            b.Click += delegate
            {
                if (WindowCommand != null)
                    WindowCommand(kind);
            };
            Controls.Add(b);
            _winBtns.Add(b);
            return b;
        }

        private void LayoutRight()
        {
            int right = Width - 6;
            for (int i = _winBtns.Count - 1; i >= 0; i--)
            {
                WindowButton b = _winBtns[i];
                b.Top = 4;
                b.Height = Math.Max(22, Height - 8);
                b.Location = new Point(right - b.Width, b.Top);
                right -= b.Width;
            }

            right -= 10;
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                NavChip c = _actions[i];
                c.Location = new Point(Math.Max(ChipLeft, right - c.Width), (Height - c.Height) / 2);
                right -= c.Width + ActionGap;
            }
        }

        public void SetBrand(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_brand.Text != text)
            {
                _brand.Text = text;
                _brand.Width = Math.Max(140, TextRenderer.MeasureText(text, _brand.Font).Width + 10);
            }
        }

        public void BuildNav(string[] titles)
        {
            for (int i = 0; i < _chips.Count; i++)
                Controls.Remove(_chips[i]);
            _chips.Clear();

            int x = ChipLeft;
            for (int i = 0; i < titles.Length; i++)
            {
                NavChip chip = new NavChip();
                chip.Text = titles[i];
                chip.Size = new Size(TextRenderer.MeasureText(titles[i], chip.Font).Width + 30, 30);
                chip.Location = new Point(x, ChipTop);

                int index = i;
                chip.Click += delegate
                {
                    if (NavSelected != null)
                        NavSelected(index);
                };

                Controls.Add(chip);
                _chips.Add(chip);
                x += chip.Width + 4;
            }

            ApplySelection();
        }

        public void SetSelected(int index)
        {
            _selectedIndex = index;
            ApplySelection();
        }

        private void ApplySelection()
        {
            for (int i = 0; i < _chips.Count; i++)
                _chips[i].Selected = (i == _selectedIndex);
        }

        /// <summary>顶栏右侧的快捷入口（媒体信息 / 设置 / 帮助），右对齐。</summary>
        public Action<int> ActionSelected;

        private readonly List<NavChip> _actions = new List<NavChip>();
        private const int ActionRight = 16;
        private const int ActionGap = 4;

        public void BuildActions(string[] titles)
        {
            for (int i = 0; i < _actions.Count; i++)
                Controls.Remove(_actions[i]);
            _actions.Clear();

            for (int i = 0; i < titles.Length; i++)
            {
                NavChip chip = new NavChip();
                chip.Text = titles[i];
                chip.Size = new Size(TextRenderer.MeasureText(titles[i], chip.Font).Width + 26, 30);

                int index = i;
                chip.Click += delegate
                {
                    if (ActionSelected != null)
                        ActionSelected(index);
                };

                Controls.Add(chip);
                _actions.Add(chip);
            }

            LayoutRight();
        }

        public int HitTest(Point p)
        {
            for (int i = 0; i < _chips.Count; i++)
                if (_chips[i].Bounds.Contains(p))
                    return i;
            return -1;
        }
    }

    #endregion

    #region StatusBarEx

    /// <summary>底栏：透明底，只画状态点和文字（对齐参考图的 Ready / CPU / MEM 行）。</summary>
    internal sealed class StatusBarEx : Panel
    {
        private readonly Timer _timer;
        private string _state = "就绪";
        private Color _stateColor = Theme.StateIdle;
        private string _task = "";
        private string _right = "";
        private string _sys = "";
        private int _progress = -1;
        private bool _polling;
        private bool _busy;
        private string _readyText = "就绪";
        private string _runningText = "压制中";

        public bool ShowSystemStats = true;

        public StatusBarEx()
        {
            Dock = DockStyle.Bottom;
            Height = Theme.StatusBarHeight;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _timer = new Timer();
            _timer.Interval = 3000;
            _timer.Tick += delegate { Poll(); };
            _timer.Enabled = true;
        }

        public void SetState(string text, Color color)
        {
            _state = string.IsNullOrEmpty(text) ? _readyText : text;
            _stateColor = color;
            Invalidate();
        }

        public void SetReady() { _busy = false; SetState(_readyText, Theme.StateIdle); }

        public void SetRunning(string task)
        {
            _busy = true;
            _task = task == null ? "" : task;
            SetState(_runningText, Theme.StateBusy);
        }

        public void SetReadyText(string s) { if (!string.IsNullOrEmpty(s)) _readyText = s; }
        public void SetRunningText(string s) { if (!string.IsNullOrEmpty(s)) _runningText = s; }
        public void SetStandby() { if (!_busy) SetState(_readyText, Theme.StateIdle); }

        public void SetTask(string task)
        {
            _task = task == null ? "" : task;
            Invalidate();
        }

        /// <summary>进度 0..100；传 -1 隐藏进度条。</summary>
        public void SetProgress(int percent)
        {
            _progress = percent;
            if (percent < 0) _progress = -1;
            else if (percent > 100) _progress = 100;
            Invalidate();
        }

        public void SetRight(string text)
        {
            _right = text == null ? "" : text;
            Invalidate();
        }

        private void Poll()
        {
            if (!ShowSystemStats || _polling || IsDisposed || !IsHandleCreated)
                return;

            _polling = true;
            Task.Run(new Action(delegate
            {
                string text = "";
                try
                {
                    text = "CPU " + QueryCpu() + "   MEM " + QueryMem();
                }
                catch
                {
                    text = "";
                }

                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(delegate
                        {
                            _sys = text;
                            Invalidate();
                        }));
                    }
                }
                catch
                {
                }

                _polling = false;
            }));
        }

        private static string QueryCpu()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT LoadPercentage FROM Win32_Processor"))
                {
                    int sum = 0;
                    int count = 0;
                    foreach (ManagementBaseObject mo in searcher.Get())
                    {
                        object v = mo["LoadPercentage"];
                        if (v != null) { sum += Convert.ToInt32(v); count++; }
                    }
                    if (count > 0) return (sum / count).ToString() + "%";
                }
            }
            catch
            {
            }
            return "--";
        }

        private static string QueryMem()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementBaseObject mo in searcher.Get())
                    {
                        double total = Convert.ToDouble(mo["TotalVisibleMemorySize"]);
                        double free = Convert.ToDouble(mo["FreePhysicalMemory"]);
                        if (total > 0)
                            return ((int)Math.Round((total - free) * 100.0 / total)).ToString() + "%";
                    }
                }
            }
            catch
            {
            }
            return "--";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null)
            {
                _timer.Enabled = false;
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Font fSmall = Theme.Small;
            int cy = Height / 2;
            int y;

            // 状态点
            using (SolidBrush b = new SolidBrush(_stateColor))
                g.FillEllipse(b, 14, cy - 3.5f, 7, 7);

            int x = 27;
            Size st = TextRenderer.MeasureText(_state, fSmall);
            y = cy - st.Height / 2;
            TextRenderer.DrawText(g, _state, fSmall, new Point(x, y), Theme.TextMuted);
            x += st.Width + 14;

            if (!string.IsNullOrEmpty(_task))
            {
                Size ts = TextRenderer.MeasureText(_task, fSmall);
                y = cy - ts.Height / 2;
                TextRenderer.DrawText(g, _task, fSmall, new Point(x, y), Theme.TextFaint);
                x += ts.Width + 14;
            }

            if (_progress >= 0)
            {
                int barW = 150;
                int barH = 5;
                Rectangle track = new Rectangle(x, cy - barH / 2 + 1, barW, barH);
                Theme.FillRounded(g, track, 3, Theme.TrackBg);

                int fillW = (int)Math.Round(barW * (_progress / 100.0));
                if (fillW > 0)
                {
                    Rectangle fill = new Rectangle(x, cy - barH / 2 + 1, fillW, barH);
                    Theme.FillRounded(g, fill, 3, Theme.Accent);
                }
                x += barW + 8;

                string ps = _progress.ToString() + "%";
                y = cy - TextRenderer.MeasureText(ps, fSmall).Height / 2;
                TextRenderer.DrawText(g, ps, fSmall, new Point(x, y), Theme.TextFaint);
            }

            string right = _right;
            if (!string.IsNullOrEmpty(_sys))
                right = string.IsNullOrEmpty(right) ? _sys : right + "   " + _sys;
            if (!string.IsNullOrEmpty(right))
            {
                Size sz = TextRenderer.MeasureText(right, fSmall);
                y = cy - sz.Height / 2;
                TextRenderer.DrawText(g, right, fSmall, new Point(Width - sz.Width - 16, y), Theme.TextFaint);
            }
        }
    }

    #endregion

    #region TabStrip

    /// <summary>右栏标签条（胶囊风格）。</summary>
    internal sealed class TabStrip : Control
    {
        private readonly List<string> _titles = new List<string>();
        private readonly List<int> _widths = new List<int>();
        private int _selected;
        private int _hover = -1;

        public Action<int> SelectedChanged;

        private const int PadX = 13;
        private const int ItemGap = 5;
        private const int ItemTop = 7;
        private const int ItemHeight = 26;

        public TabStrip()
        {
            Dock = DockStyle.Top;
            Height = 40;
            Font = Theme.Font(9f);
            Cursor = Cursors.Hand;
            BackColor = Theme.CardBg;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (_titles.Count == 0) return;
                int v = value;
                if (v < 0) v = 0;
                if (v >= _titles.Count) v = _titles.Count - 1;
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(_selected);
            }
        }

        public void SetTitles(IList<string> titles)
        {
            _titles.Clear();
            for (int i = 0; i < titles.Count; i++)
                _titles.Add(titles[i]);

            Measure();
            if (_selected >= _titles.Count) _selected = Math.Max(0, _titles.Count - 1);
            Invalidate();
        }

        private void Measure()
        {
            _widths.Clear();
            for (int i = 0; i < _titles.Count; i++)
                _widths.Add(TextRenderer.MeasureText(_titles[i], Font).Width + PadX * 2);
        }

        private int HitTest(int x)
        {
            int cur = 2;
            for (int i = 0; i < _widths.Count; i++)
            {
                if (x >= cur && x < cur + _widths[i]) return i;
                cur += _widths[i] + ItemGap;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.X);
            if (h != _hover)
            {
                _hover = h;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.X);
            if (h >= 0) SelectedIndex = h;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            int x = 2;
            for (int i = 0; i < _titles.Count; i++)
            {
                Rectangle r = new Rectangle(x, ItemTop, _widths[i], ItemHeight);
                bool sel = (i == _selected);

                if (sel)
                    Theme.FillRounded(g, r, Theme.ChipRadius, Theme.AccentSoft);
                else if (i == _hover)
                    Theme.FillRounded(g, r, Theme.ChipRadius, Theme.HoverBg);

                Color fore = sel ? Theme.AccentDeep : Theme.TextMuted;
                Font f = sel ? Theme.Font(9f, FontStyle.Bold) : Font;

                using (SolidBrush b = new SolidBrush(fore))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags |= StringFormatFlags.NoWrap;
                    g.DrawString(_titles[i], f, b, r, sf);
                }

                x += _widths[i] + ItemGap;
            }

            using (Pen p = new Pen(Theme.Divider))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    #endregion

    #region DropZone

    /// <summary>虚线拖放区（视觉提示用）。</summary>
    internal sealed class DropZone : Control
    {
        public string Caption = "把文件拖到这里";
        public string Hint = "";
        public bool Hot = false;

        public DropZone()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.CardBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            Rectangle r = ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            if (r.Width <= 4 || r.Height <= 4)
                return;

            Theme.FillRounded(g, r, Theme.CardRadius, Hot ? Theme.AccentSoft : Theme.LogBg);

            using (GraphicsPath path = Theme.Rounded(r, Theme.CardRadius))
            using (Pen p = new Pen(Hot ? Theme.Accent : Theme.InputBorder, 1))
            {
                p.DashStyle = DashStyle.Dash;
                p.DashPattern = new float[] { 4f, 3f };
                g.DrawPath(p, path);
            }

            Font f1 = Theme.Font(9.5f, FontStyle.Bold);
            Size s1 = TextRenderer.MeasureText(Caption, f1);
            int cy = Height / 2;

            TextRenderer.DrawText(g, Caption, f1,
                new Point((Width - s1.Width) / 2, cy - s1.Height - 1),
                Hot ? Theme.AccentDeep : Theme.TextMuted);

            if (!string.IsNullOrEmpty(Hint))
            {
                Size s2 = TextRenderer.MeasureText(Hint, Theme.Small);
                TextRenderer.DrawText(g, Hint, Theme.Small,
                    new Point((Width - s2.Width) / 2, cy + 3),
                    Theme.TextFaint);
            }
        }
    }

    #endregion

    #region ShellTabs

    /// <summary>
    /// 把 TabControl 变成「纯粹的页面宿主」：裁掉它自带的标签条。
    /// 这样 9 个功能页可以一个都不搬家，继续吃本地化 resx。
    /// </summary>
    internal static class ShellTabs
    {
        public static void Style(TabControl tabs, Control frame)
        {
            tabs.Appearance = TabAppearance.FlatButtons;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(0, 1);
            tabs.Multiline = false;
            tabs.ShowToolTips = false;
            tabs.HotTrack = false;
            tabs.Padding = new Point(0, 0);

            // 尺寸完全交给 Dock，只额外把标签条那一条用 Region 裁掉。
            // Region 只影响绘制，不影响布局，所以不会踩到定位的坑。
            tabs.Dock = DockStyle.Fill;

            Clip(tabs);

            tabs.HandleCreated += delegate { Clip(tabs); };
            tabs.SizeChanged += delegate { Clip(tabs); };
            tabs.SelectedIndexChanged += delegate { Clip(tabs); };
            if (frame != null)
                frame.SizeChanged += delegate { Clip(tabs); };
        }

        public static void Refit(TabControl tabs, Control frame)
        {
            Clip(tabs);
        }

        public static void Clip(TabControl tabs)
        {
            if (tabs == null)
                return;

            try
            {
                if (tabs.TabPages.Count == 0)
                    return;

                Rectangle d = tabs.DisplayRectangle;
                if (d.Width <= 0 || d.Height <= 0 || d.Y <= 0)
                    return;

                Region old = tabs.Region;
                tabs.Region = new Region(d);
                if (old != null)
                    old.Dispose();
            }
            catch
            {
            }
        }
    }

    #endregion

    #region SlimScrollBar

    /// <summary>
    /// 自绘细滚动条（6px），盖在原生滚动条上面，靠 WM_VSCROLL / GetScrollInfo 双向同步。
    ///
    /// 为什么不直接把原生滚动条换掉：滚动、键盘、惯性、富文本重排全在原生控件内部，
    /// 自己重写风险极高。这里只做「外观替换」——把系统那条厚重的滚动条盖住，
    /// 拖我们自己画的那条，其余一切照旧。对应参考项目的 scroll-area 原语。
    /// </summary>
    internal sealed class SlimScrollBar : Control
    {
        public const int Thickness = 6;

        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public int nPage;
            public int nPos;
            public int nTrackPos;
        }

        private const int SB_VERT = 1;
        private const int SIF_ALL = 0x17;
        private const int WM_VSCROLL = 0x0115;
        private const int SB_THUMBTRACK = 5;

        [DllImport("user32.dll")]
        private static extern bool GetScrollInfo(IntPtr hWnd, int bar, ref SCROLLINFO si);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly Control _target;
        private readonly int _cover;
        private readonly Color _backdrop;
        private int _min, _max;
        private int _page = 1;
        private int _pos;
        private bool _dragging;
        private bool _hover;
        private int _grabOffset;

        private static readonly List<SlimScrollBar> _bars = new List<SlimScrollBar>();
        private static readonly HashSet<Control> _done = new HashSet<Control>();
        private static Timer _pump;

        private SlimScrollBar(Control target, int cover, Color backdrop)
        {
            _target = target;
            _cover = Math.Max(Thickness + 4, cover);
            _backdrop = backdrop;
            SetStyle(ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Width = _cover;
        }

        /// <summary>
        /// 幂等：给一个可滚动控件套上自绘滚动条。必须在它有 Parent 之后调用。
        /// </summary>
        /// <param name="cover">要盖住的原生滚动条宽度（系统默认 17px）。
        /// 整条盖住是必要的：盖不住的话原生那条会从旁边露出来，反而更脏。</param>
        /// <param name="backdrop">遮盖用的底色，必须和被盖住的周围颜色一致才能无缝。</param>
        public static void Attach(Control target, int cover, Color backdrop)
        {
            if (target == null || target.Parent == null)
                return;
            if (!_done.Add(target))
                return;

            try
            {
                SlimScrollBar bar = new SlimScrollBar(target, cover, backdrop);
                target.Parent.Controls.Add(bar);
                bar.BringToFront();
                bar.SyncBounds();
                bar.SyncFromTarget();
                bar.Visible = false;   // 没有溢出就先不显示

                _bars.Add(bar);
                EnsurePump();

                target.LocationChanged += delegate { bar.SyncBounds(); };
                target.SizeChanged += delegate { bar.SyncBounds(); };
                target.VisibleChanged += delegate { if (!target.Visible) bar.Visible = false; };
                target.Disposed += delegate
                {
                    _bars.Remove(bar);
                    bar.Dispose();
                };
            }
            catch
            {
                // 滚动条只是外观，任何异常都不该影响功能
            }
        }

        /// <summary>
        /// 用一只共享定时器轮询所有已挂载的滚动条。
        /// 比去监听每个控件的 MouseWheel / KeyUp / TextChanged 可靠得多，
        /// 开销也极小（150ms 一次 GetScrollInfo）。
        /// </summary>
        private static void EnsurePump()
        {
            if (_pump != null)
                return;

            _pump = new Timer();
            _pump.Interval = 150;
            _pump.Tick += delegate
            {
                for (int i = _bars.Count - 1; i >= 0; i--)
                {
                    SlimScrollBar b = _bars[i];
                    if (b.IsDisposed)
                    {
                        _bars.RemoveAt(i);
                        continue;
                    }
                    b.SyncFromTarget();
                }
            };
            _pump.Start();
        }

        private void SyncBounds()
        {
            if (_target == null || _target.IsDisposed)
                return;

            // 盖住目标控件右侧那一条（就是原生滚动条所在的位置）
            Bounds = new Rectangle(
                _target.Right - _cover,
                _target.Top,
                _cover,
                Math.Max(24, _target.Height));
        }

        private void SyncFromTarget()
        {
            if (_dragging || _target == null || !_target.IsHandleCreated)
                return;

            SCROLLINFO si = new SCROLLINFO();
            si.cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO));
            si.fMask = SIF_ALL;
            if (!GetScrollInfo(_target.Handle, SB_VERT, ref si))
                return;

            _min = si.nMin;
            _max = si.nMax;
            _page = Math.Max(1, si.nPage);
            _pos = si.nPos;

            bool need = _max - _min + 1 > _page + 1;
            if (Visible != need)
                Visible = need;

            Invalidate();
        }

        /// <summary>滑块矩形。内容不足时不画滑块。</summary>
        private Rectangle ThumbRect()
        {
            int total = _max - _min + 1;
            if (total <= _page)
                return Rectangle.Empty;

            int track = Height;
            int h = (int)((long)track * _page / total);
            if (h < 24) h = 24;
            if (h > track) h = track;

            int span = track - h;
            int range = Math.Max(1, total - _page);
            int top = (int)((long)span * (_pos - _min) / range);
            if (top < 0) top = 0;
            if (top > span) top = span;

            return new Rectangle(0, top, Width, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;

            // 先把整条盖住（否则原生滚动条会从旁边露出来）
            using (SolidBrush bg = new SolidBrush(_backdrop))
                g.FillRectangle(bg, ClientRectangle);

            Rectangle t = ThumbRect();
            if (t.IsEmpty || t.Height <= 2)
                return;

            // 滑块只有 6px，居中浮在这条遮盖带里
            t = new Rectangle((Width - Thickness) / 2, t.Top + 1, Thickness, Math.Max(20, t.Height - 2));

            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color c = _dragging || _hover ? Theme.TextMuted : Theme.CardBorder;
            using (GraphicsPath path = Theme.Rounded(t, Thickness / 2))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, path);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            Rectangle t = ThumbRect();
            if (t.IsEmpty)
                return;

            if (t.Contains(e.Location))
            {
                _dragging = true;
                _grabOffset = e.Y - t.Top;
                Capture = true;
            }
            else
            {
                // 点空白：翻一页
                int dir = e.Y < t.Top ? -1 : 1;
                ScrollToNative(_pos + dir * _page);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
                return;

            Rectangle t = ThumbRect();
            if (t.IsEmpty)
                return;

            int total = _max - _min + 1;
            int range = Math.Max(1, total - _page);
            int span = Math.Max(1, Height - t.Height);

            int top = e.Y - _grabOffset;
            if (top < 0) top = 0;
            if (top > span) top = span;

            ScrollToNative(_min + (int)((long)range * top / span));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
                return;

            _dragging = false;
            Capture = false;
            Invalidate();
        }

        private void ScrollToNative(int pos)
        {
            if (_target == null || !_target.IsHandleCreated)
                return;

            if (pos < _min) pos = _min;
            if (pos > _max) pos = _max;

            _pos = pos;
            SendMessage(_target.Handle, WM_VSCROLL,
                (IntPtr)((pos << 16) | SB_THUMBTRACK), IntPtr.Zero);
            Invalidate();
        }
    }

    #endregion

    #region FieldFrame

    /// <summary>
    /// 组合式输入框外框。
    ///
    /// 这是参考项目「shadcn = Radix 无头原语 + Tailwind 外观」在 WinForms 里的等价写法：
    /// 原生 TextBox / ComboBox / NumericUpDown 仍然留在**原位原尺寸**，只负责
    /// 光标、输入法、下拉、步进这些「行为」；外圈那层圆角描边 100% 由本控件画。
    ///
    /// 为什么不像以前那样直接改原生控件的 BorderStyle / Region？
    /// 因为 ComboBox 这类控件的边框是系统自己画的，我没有 API 能改掉，
    /// 只能"绕开"——这就是之前那些"狗皮膏药"的根源。组合才是干净做法。
    ///
    /// 实现关键：本控件的 Region 被挖成**只有 2px 的圆角环**，中间是真正的洞。
    /// 洞既不会盖住里面的原生控件，鼠标也能直接穿透过去点原生控件。
    /// </summary>
    internal sealed class FieldFrame : Control
    {
        private readonly Control _inner;
        private readonly int _radius;
        private bool _hasFocus;

        private FieldFrame(Control inner)
        {
            _inner = inner;
            _radius = Theme.ControlRadius(inner.Height + 2);

            SetStyle(ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
        }

        private static readonly HashSet<Control> _framed = new HashSet<Control>();

        /// <summary>幂等：同一个控件只套一次框；已经套过的只做一次位置同步。</summary>
        public static void Attach(Control inner)
        {
            if (inner == null)
                return;

            try
            {
                if (_framed.Contains(inner))
                {
                    Reposition(inner);
                    return;
                }

                Control parent = inner.Parent;
                if (parent == null || parent is FieldFrame)
                    return;

                FieldFrame f = new FieldFrame(inner);
                _framed.Add(inner);

                parent.Controls.Add(f);
                f.BringToFront();
                f.SyncBounds();
                f.Visible = inner.Visible;

                // 原生控件自己动的时候外框要跟着走（语言切换、resx 重排都会触发）
                inner.LocationChanged += delegate { f.SyncBounds(); };
                inner.SizeChanged += delegate { f.SyncBounds(); };
                inner.VisibleChanged += delegate { f.Visible = inner.Visible; };
                inner.GotFocus += delegate { f.Highlight(true); };
                inner.LostFocus += delegate { f.Highlight(false); };
            }
            catch
            {
                // 外框只是外观，任何异常都不该影响功能
            }
        }

        private static void Reposition(Control inner)
        {
            Control parent = inner.Parent;
            if (parent == null)
                return;
            foreach (Control s in parent.Controls)
            {
                FieldFrame f = s as FieldFrame;
                if (f != null && ReferenceEquals(f._inner, inner))
                    f.SyncBounds();
            }
        }

        private void Highlight(bool on)
        {
            if (_hasFocus == on)
                return;
            _hasFocus = on;
            Invalidate();
        }

        private void SyncBounds()
        {
            if (_inner == null)
                return;

            // 比原生控件大 1px：环形描边画在这 1px 上，绝不占用原生控件自己的区域
            Bounds = new Rectangle(
                _inner.Left - 1, _inner.Top - 1,
                _inner.Width + 2, _inner.Height + 2);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRingRegion();
        }

        /// <summary>
        /// 把自身裁成「只有 2px 的圆角环」，中间挖空。
        /// 洞的形状和原生控件的可见区域严丝合缝，所以既无缝、又不挡鼠标。
        /// </summary>
        private void ApplyRingRegion()
        {
            if (Width <= 6 || Height <= 6)
                return;

            using (GraphicsPath outer = Theme.Rounded(new Rectangle(0, 0, Width, Height), _radius))
            {
                Region ring = new Region(outer);
                ring.Exclude(new Rectangle(2, 2, Width - 4, Height - 4));

                Region old = Region;
                Region = ring;
                if (old != null)
                    old.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 和参考项目一样：常态 1px 淡边框，聚焦时换成强调色并把线加粗一点
            Color border = _hasFocus ? Theme.Accent : Theme.InputBorder;
            float width = _hasFocus ? 1.5f : 1f;

            using (GraphicsPath path = Theme.Rounded(
                new Rectangle(0, 0, Width - 1, Height - 1), _radius))
            using (Pen p = new Pen(border, width))
                g.DrawPath(p, path);
        }
    }

    #endregion

    #region Ui 控件工厂

    /// <summary>按钮变体，对应参考项目 buttonVariants 的 variant 维度。</summary>
    internal enum BtnVariant
    {
        Outline,        // 默认：白底 + 细边框（border border-input/75 bg-card）
        Secondary,      // 浅灰实心（bg-secondary）
        Ghost,          // 无底无框，仅悬停有底（hover:bg-accent/65）
        Destructive,    // 危险操作（bg-destructive）
        Accent          // 主操作（bg-primary）
    }

    /// <summary>按钮尺寸，对应参考项目 buttonVariants 的 size 维度（h-9 / h-11 / h-12）。</summary>
    internal enum BtnSize
    {
        Sm,             // h-9  → 本项目紧凑排版下映射为 24px 高
        Md,             // h-11 → 30px
        Lg,             // h-12 → 36px
        Icon            // 正方形图标按钮
    }

    internal static class Ui
    {
        /// <summary>
        /// 按钮工厂 —— 等价于参考项目的 <c>buttonVariants({ variant, size })</c>。
        /// 全项目所有按钮都必须从这里生成，不允许在调用处现写颜色和圆角。
        /// </summary>
        public static ControlExs.QQButton Button(
            string text, BtnVariant variant = BtnVariant.Outline, BtnSize size = BtnSize.Sm)
        {
            ControlExs.QQButton b = new ControlExs.QQButton();
            b.Text = text;
            b.AutoSize = false;
            b.Kind = MapKind(variant);

            int h;
            int padX;
            switch (size)
            {
                case BtnSize.Md: h = 30; padX = 14; b.Font = Theme.Font(9f); break;
                case BtnSize.Lg: h = 36; padX = 18; b.Font = Theme.Font(10f); break;
                case BtnSize.Icon: h = 30; padX = 0; b.Font = Theme.Font(9f); break;
                default: h = 24; padX = 11; b.Font = Theme.Font(8.5f); break;
            }

            int textW = TextRenderer.MeasureText(text, b.Font).Width;
            b.Size = size == BtnSize.Icon
                ? new Size(h, h)
                : new Size(textW + padX * 2, h);

            return b;
        }

        private static ControlExs.QQButton.ButtonKind MapKind(BtnVariant v)
        {
            switch (v)
            {
                case BtnVariant.Secondary: return ControlExs.QQButton.ButtonKind.Secondary;
                case BtnVariant.Ghost: return ControlExs.QQButton.ButtonKind.Ghost;
                case BtnVariant.Destructive: return ControlExs.QQButton.ButtonKind.Destructive;
                case BtnVariant.Accent: return ControlExs.QQButton.ButtonKind.Accent;
                default: return ControlExs.QQButton.ButtonKind.Outline;
            }
        }

        /// <summary>向后兼容：原来的无参扁平按钮 = Outline/Sm。</summary>
        public static ControlExs.QQButton Flat(string text)
        {
            return Button(text, BtnVariant.Outline, BtnSize.Sm);
        }

        /// <summary>
        /// 给原生输入类控件套上统一样式的外框（组合，不是重写）。
        /// 返回 false 表示没有套上（例如已经被套过），调用方无需关心。
        /// </summary>
        public static void Field(Control inner)
        {
            FieldFrame.Attach(inner);
        }
    }

    #endregion
}
