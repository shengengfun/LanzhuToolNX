// ------------------------------------------------------------------
// Copyright (C) 2011-2016 Maruko Toolbox Project
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
using System.Collections.Generic;

using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ControlExs
{

    /****************************************************************
    * 
    *           Author：苦笑
    *             Blog: http://www.cnblogs.com/Keep-Silence-/
    *             Date: 2013-2-22
    *
    *****************************************************************/


    /// <summary>
    /// 仿QQ效果的Button
    /// </summary>
    public class QQButton : Button
    {
        #region Field

        private Image _normalImg = RenderHelper.GetImageFormResourceStream("ControlExs.QQControls.QQButton.Image.qqbtn_normal.png");
        private Image _highlightImg = RenderHelper.GetImageFormResourceStream("ControlExs.QQControls.QQButton.Image.qqbtn_highlight.png");
        private Image _focusImg = RenderHelper.GetImageFormResourceStream("ControlExs.QQControls.QQButton.Image.qqbtn_focus.png");
        private Image _downImg = RenderHelper.GetImageFormResourceStream("ControlExs.QQControls.QQButton.Image.qqbtn_down.png");

        private QQControlState _state = QQControlState.Normal;
        private Font _defaultFont = new Font("微软雅黑", 9);

        #region 现代扁平主题

        /// <summary>
        /// 由宿主程序打开：把「仿 QQ 贴图」渲染切换成现代扁平渲染。
        /// 不开则完全保持原样，对其它引用 ControlExs 的项目零影响。
        /// </summary>
        public static bool FlatTheme = false;

        /// <summary>
        /// 按钮变体。对应参考项目 buttonVariants 里的 variant 维度：
        /// 区别一律只能从这几个里选，不允许在调用处现写颜色 —— 这是视觉一致性的根本保证。
        /// </summary>
        public enum ButtonKind
        {
            Outline = 0,       // 默认：白底 + 细边框
            Secondary = 1,     // 浅灰实心
            Ghost = 2,         // 无底无框，仅悬停/按下有底色
            Destructive = 3,   // 危险操作：红底白字
            Accent = 4         // 主操作：强调色实心
        }

        /// <summary>当前变体。旧的 Accent 布尔仍兼容，会被当作 Accent 变体。</summary>
        public ButtonKind Kind = ButtonKind.Outline;

        /// <summary>强调色按钮（主操作，例如「压制」「开始」）。</summary>
        public bool Accent = false;

        public static Color FlatBack = Color.White;
        public static Color FlatBorder = ColorTranslator.FromHtml("#CAD4DD");
        public static Color FlatHover = ColorTranslator.FromHtml("#F2F5F9");
        public static Color FlatDown = ColorTranslator.FromHtml("#E7EDF3");
        public static Color FlatText = ColorTranslator.FromHtml("#222A35");
        public static Color FlatDisabledBack = ColorTranslator.FromHtml("#F5F7FA");
        public static Color FlatDisabledText = ColorTranslator.FromHtml("#9AA4B2");
        public static Color FlatAccent = ColorTranslator.FromHtml("#34B282");
        public static Color FlatAccentHover = ColorTranslator.FromHtml("#2EA674");
        public static Color FlatAccentDown = ColorTranslator.FromHtml("#28996B");
        public static Color FlatAccentText = Color.White;

        // variant 矩阵里其余几种变体的配色（由 Theme 注入，也可直接用这里的默认值）
        public static Color FlatSecondary = ColorTranslator.FromHtml("#E2EAF1");
        public static Color FlatSecondaryHover = ColorTranslator.FromHtml("#D6E1EA");
        public static Color FlatSecondaryDown = ColorTranslator.FromHtml("#CAD8E4");
        public static Color FlatDestructive = ColorTranslator.FromHtml("#E04C4C");
        public static Color FlatDestructiveHover = ColorTranslator.FromHtml("#CF4040");

        public static int FlatRadius = 8;

        private void DrawFlat(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle r = ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            if (r.Width <= 2 || r.Height <= 2)
                return;

            if (!Enabled)
                _state = QQControlState.Disabled;

            bool accent = (Accent || Kind == ButtonKind.Accent) && Enabled;
            Color fill;
            Color border = Color.Empty;
            Color fore;

            if (!Enabled)
            {
                fill = FlatDisabledBack;
                fore = FlatDisabledText;
            }
            else if (accent)
            {
                switch (_state)
                {
                    case QQControlState.Highlight: fill = FlatAccentHover; break;
                    case QQControlState.Down: fill = FlatAccentDown; break;
                    default: fill = FlatAccent; break;
                }
                fore = FlatAccentText;
            }
            else if (Kind == ButtonKind.Secondary)
            {
                switch (_state)
                {
                    case QQControlState.Highlight: fill = FlatSecondaryHover; break;
                    case QQControlState.Down: fill = FlatSecondaryDown; break;
                    default: fill = FlatSecondary; break;
                }
                fore = FlatText;
            }
            else if (Kind == ButtonKind.Ghost)
            {
                // 无底无框：只有悬停 / 按下 / 聚焦时才出现底色
                switch (_state)
                {
                    case QQControlState.Highlight: fill = FlatHover; break;
                    case QQControlState.Down: fill = FlatDown; break;
                    case QQControlState.Focus: fill = FlatHover; break;
                    default: fill = Color.Empty; break;
                }
                fore = FlatText;
            }
            else if (Kind == ButtonKind.Destructive)
            {
                fill = _state == QQControlState.Highlight ? FlatDestructiveHover : FlatDestructive;
                fore = Color.White;
            }
            else
            {
                switch (_state)
                {
                    case QQControlState.Highlight: fill = FlatHover; border = FlatBorder; break;
                    case QQControlState.Down: fill = FlatDown; border = FlatBorder; break;
                    case QQControlState.Focus: fill = FlatBack; border = FlatAccent; break;
                    default: fill = FlatBack; border = FlatBorder; break;
                }
                fore = ForeColor == Color.Empty ? FlatText : ForeColor;
                if (fore == Color.Empty || fore.A == 0) fore = FlatText;
            }

            int d = FlatRadius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            // 半径必须按高度等比推算并夹紧（参考项目里 h-9 配 rounded-lg、h-11 配 rounded-xl，约 22%~27%）。
            // 之前是固定半径：矮按钮会被同心弧包成胶囊/椭圆，同一屏里和方形控件混在一起非常难看。
            int radius = (int)Math.Round(r.Height * 0.25);
            if (radius > FlatRadius) radius = FlatRadius;
            if (radius < 3) radius = 3;
            if (radius * 2 > r.Height) radius = Math.Max(2, r.Height / 2 - 1);
            if (radius * 2 > r.Width) radius = Math.Max(2, r.Width / 2 - 1);
            d = radius * 2;
            using (GraphicsPath path = new GraphicsPath())
            {
                if (d >= 4)
                {
                    path.AddArc(r.X, r.Y, d, d, 180, 90);
                    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                }
                else
                {
                    path.AddRectangle(r);
                }

                using (SolidBrush b = new SolidBrush(fill))
                    g.FillPath(b, path);

                if (border != Color.Empty)
                {
                    using (Pen p = new Pen(border))
                        g.DrawPath(p, path);
                }
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                r,
                fore,
                GetTextFormatFlags(TextAlign, RightToLeft == RightToLeft.Yes));
        }

        #endregion

        #endregion

        #region Constructor

        public QQButton()
        {
            SetStyles();
            this.Font = _defaultFont;
            this.Size = new Size(68, 23);
            this.Cursor = Cursors.Hand;
        }

        #endregion

        #region Properites

        private int ImageWidth
        {
            get
            {
                if (Image == null)
                {
                    return 16;
                }
                else
                {
                    return Image.Width;
                }
            }

        }

        #endregion

        #region Override

        protected override void OnMouseEnter(EventArgs e)
        {
            _state = QQControlState.Highlight;
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_state == QQControlState.Highlight && Focused)
            {
                _state = QQControlState.Focus;
            }
            else if (_state == QQControlState.Focus)
            {
                _state = QQControlState.Focus;
            }
            else
            {
                _state = QQControlState.Normal;
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _state = QQControlState.Down;
            }
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                if (ClientRectangle.Contains(mevent.Location))
                {
                    _state = QQControlState.Highlight;
                }
                else
                {
                    _state = QQControlState.Focus;
                }
            }
            base.OnMouseUp(mevent);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            _state = QQControlState.Normal;
            base.OnLostFocus(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (Enabled)
            {
                _state = QQControlState.Normal;
            }
            else
            {
                _state = QQControlState.Disabled;
            }
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);

            if (FlatTheme)
            {
                DrawFlat(pevent.Graphics);
                return;
            }

            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;

            Rectangle imageRect, textRect;
            CalculateRect(out imageRect, out textRect);

            if (!Enabled)
            {
                _state = QQControlState.Disabled;
            }
            switch (_state)
            {
                case QQControlState.Normal:

                    RenderHelper.DrawImageWithNineRect(
                        g, _normalImg,
                        ClientRectangle,
                        new Rectangle(0, 0, _normalImg.Width, _normalImg.Height));
                    break;
                case QQControlState.Highlight:

                    RenderHelper.DrawImageWithNineRect(
                        g, _highlightImg,
                        ClientRectangle,
                        new Rectangle(0, 0, _highlightImg.Width, _highlightImg.Height));
                    break;
                case QQControlState.Focus:

                    RenderHelper.DrawImageWithNineRect(
                        g, _focusImg,
                        ClientRectangle,
                        new Rectangle(0, 0, _focusImg.Width, _focusImg.Height));
                    break;
                case QQControlState.Down:
                    RenderHelper.DrawImageWithNineRect(
                       g, _downImg,
                       ClientRectangle,
                       new Rectangle(0, 0, _downImg.Width, _downImg.Height));
                    break;
                case QQControlState.Disabled:
                    DrawDisabledButton(g);
                    break;
                default:
                    break;
            }

            if (Image != null)
            {
                g.DrawImage(Image, imageRect, 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel);
            }

            Color textColor = Enabled ? ForeColor : SystemColors.GrayText;
            TextRenderer.DrawText(
                  g,
                  Text,
                  Font,
                  textRect,
                  textColor,
                  GetTextFormatFlags(TextAlign, RightToLeft == RightToLeft.Yes));

        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_normalImg != null) { _normalImg.Dispose(); }
                if (_highlightImg != null) { _highlightImg.Dispose(); }
                if (_downImg != null) { _downImg.Dispose(); }
                if (_focusImg != null) { _focusImg.Dispose(); }
                if (_defaultFont != null) { _defaultFont.Dispose(); }
            }

            _normalImg = null;
            _highlightImg = null;
            _focusImg = null;
            _downImg = null;
            _defaultFont = null;
            base.Dispose(disposing);
        }

        #endregion

        #region Private

        private void SetStyles()
        {
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            UpdateStyles();
        }

        private void CalculateRect(out Rectangle imageRect, out Rectangle textRect)
        {
            imageRect = Rectangle.Empty;
            textRect = Rectangle.Empty;
            if (Image == null)
            {
                textRect = new Rectangle(
                   3,
                   0,
                   Width - 6,
                   Height);
                return;
            }
            switch (TextImageRelation)
            {
                case TextImageRelation.Overlay:
                    imageRect = new Rectangle(
                        3,
                        (Height - ImageWidth) / 2,
                        ImageWidth,
                        ImageWidth);
                    textRect = new Rectangle(
                        3,
                        0,
                        Width - 6,
                        Height);
                    break;
                case TextImageRelation.ImageAboveText:
                    imageRect = new Rectangle(
                        (Width - ImageWidth) / 2,
                        3,
                        ImageWidth,
                        ImageWidth);
                    textRect = new Rectangle(
                        3,
                        imageRect.Bottom,
                        Width - 6,
                        Height - imageRect.Bottom - 2);
                    break;
                case TextImageRelation.ImageBeforeText:
                    imageRect = new Rectangle(
                        3,
                        (Height - ImageWidth) / 2,
                        ImageWidth,
                        ImageWidth);
                    textRect = new Rectangle(
                        imageRect.Right + 3,
                        0,
                        Width - imageRect.Right - 6,
                        Height);
                    break;
                case TextImageRelation.TextAboveImage:
                    imageRect = new Rectangle(
                        (Width - ImageWidth) / 2,
                        Height - ImageWidth - 3,
                        ImageWidth,
                        ImageWidth);
                    textRect = new Rectangle(
                        0,
                        3,
                        Width,
                        Height - imageRect.Y - 3);
                    break;
                case TextImageRelation.TextBeforeImage:
                    imageRect = new Rectangle(
                        Width - ImageWidth - 6,
                        (Height - ImageWidth) / 2,
                        ImageWidth,
                        ImageWidth);
                    textRect = new Rectangle(
                        3,
                        0,
                        imageRect.X - 3,
                        Height);
                    break;
            }

            if (RightToLeft == RightToLeft.Yes)
            {
                imageRect.X = Width - imageRect.Right;
                textRect.X = Width - textRect.Right;
            }
        }

        private void DrawDisabledButton(Graphics g)
        {
            int radius = 4;
            //此处让其宽度减1，让其由Normal态平滑自然的过渡到Disabled态，保持按钮高度一致。
            using (GraphicsPath borderPath = RenderHelper.CreateRoundPath(new Rectangle(ClientRectangle.X, ClientRectangle.Y, ClientRectangle.Width, ClientRectangle.Height -1), radius))
            {
                using (Pen disalbedPen = new Pen(Color.FromArgb(156, 165, 177)))
                {
                    g.DrawPath(disalbedPen, borderPath);
                }

                //背景层渐变,向内缩小1个像素
                Rectangle backRect = new Rectangle(ClientRectangle.X + 1, ClientRectangle.Y + 1, ClientRectangle.Width - 2, ClientRectangle.Height - 2 -1);
                using (GraphicsPath innerPath = RenderHelper.CreateRoundPath(backRect, radius))
                {
                    using (LinearGradientBrush lBrush = new LinearGradientBrush(backRect, Color.FromArgb(247, 252, 254), Color.FromArgb(230, 240, 243), LinearGradientMode.Vertical))
                    {
                        g.FillPath(lBrush, innerPath);
                    }
                }
            }
        }

        internal static TextFormatFlags GetTextFormatFlags(ContentAlignment alignment, bool rightToleft)
        {
            TextFormatFlags flags = TextFormatFlags.WordBreak |
                TextFormatFlags.SingleLine;
            if (rightToleft)
            {
                flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            }

            switch (alignment)
            {
                case ContentAlignment.BottomCenter:
                    flags |= TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter;
                    break;
                case ContentAlignment.BottomLeft:
                    flags |= TextFormatFlags.Bottom | TextFormatFlags.Left;
                    break;
                case ContentAlignment.BottomRight:
                    flags |= TextFormatFlags.Bottom | TextFormatFlags.Right;
                    break;
                case ContentAlignment.MiddleCenter:
                    flags |= TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter;
                    break;
                case ContentAlignment.MiddleLeft:
                    flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
                    break;
                case ContentAlignment.MiddleRight:
                    flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
                    break;
                case ContentAlignment.TopCenter:
                    flags |= TextFormatFlags.Top | TextFormatFlags.HorizontalCenter;
                    break;
                case ContentAlignment.TopLeft:
                    flags |= TextFormatFlags.Top | TextFormatFlags.Left;
                    break;
                case ContentAlignment.TopRight:
                    flags |= TextFormatFlags.Top | TextFormatFlags.Right;
                    break;
            }
            return flags;
        }

        #endregion
    }
}
