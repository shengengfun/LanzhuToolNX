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
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
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
    /// 仿QQ效果的CheckBox
    /// </summary>
    public class QQCheckBox : CheckBox
    {
        #region Field

        private QQControlState _state = QQControlState.Normal;
        private Image _checkImg = RenderHelper.GetImageFormResourceStream("ControlExs.QQControls.QQCheckBox.Image.check.png");
        private Font _defaultFont = new Font("微软雅黑", 9);

        private static readonly ContentAlignment RightAlignment = ContentAlignment.TopRight | ContentAlignment.BottomRight | ContentAlignment.MiddleRight;
        private static readonly ContentAlignment LeftAlignment = ContentAlignment.TopLeft | ContentAlignment.BottomLeft | ContentAlignment.MiddleLeft;

        #endregion

        #region Constructor

        public QQCheckBox()
        {
            SetStyles();
            this.BackColor = Color.Transparent;
            this.Font = _defaultFont;
        }

        #endregion

        #region Properites

        [Description("获取QQCheckBox左边正方形的宽度")]
        protected virtual int CheckRectWidth
        {
            get { return 13; }
        }

        #endregion

        #region 扁平现代化皮肤（由宿主 Theme 打开）

        /// <summary>
        /// 打开后改用扁平圆角风格自绘：不再使用旧版 QQ 的勾选位图，
        /// 也不绘制系统原生方块，视觉上和整体 UI 统一。
        /// </summary>
        public static bool FlatTheme = false;

        public static Color FlatAccent = Color.FromArgb(0x34, 0xB2, 0x82);
        public static Color FlatBorder = Color.FromArgb(0xCA, 0xD4, 0xDD);
        public static Color FlatText = Color.FromArgb(0x22, 0x2A, 0x35);
        public static Color FlatDisabledBorder = Color.FromArgb(0xD8, 0xDF, 0xE7);
        public static Color FlatDisabledBack = Color.FromArgb(0xF0, 0xF3, 0xF7);
        public static Color FlatDisabledCheck = Color.FromArgb(0xC3, 0xCD, 0xD8);

        private static GraphicsPath FlatRoundRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) d = 1;

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void DrawFlat(Graphics g)
        {
            Rectangle checkRect, textRect;
            CalculateRect(out checkRect, out textRect);

            bool hot = (_state == QQControlState.Highlight || _state == QQControlState.Down);

            Rectangle box = new Rectangle(
                checkRect.X, checkRect.Y, CheckRectWidth, CheckRectWidth);

            Color border = Enabled
                ? (hot ? FlatAccent : FlatBorder)
                : FlatDisabledBorder;

            Color back;
            if (!Enabled)
                back = Checked ? FlatDisabledCheck : FlatDisabledBack;
            else
                back = Checked ? FlatAccent : Color.White;

            using (GraphicsPath path = FlatRoundRect(box, 4))
            {
                using (SolidBrush brush = new SolidBrush(back))
                    g.FillPath(brush, path);

                // 勾选时不用描边，未勾选时才需要保留轮廓
                if (!Checked)
                {
                    using (Pen pen = new Pen(border, 1.4f))
                        g.DrawPath(pen, path);
                }
            }

            if (Checked)
            {
                float x = box.X, y = box.Y, w = box.Width, h = box.Height;
                using (Pen pen = new Pen(Color.White, 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(x + w * 0.24f, y + h * 0.52f),
                        new PointF(x + w * 0.43f, y + h * 0.72f),
                        new PointF(x + w * 0.78f, y + h * 0.29f)
                    });
                }
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                textRect,
                Enabled ? FlatText : SystemColors.GrayText,
                GetTextFormatFlags(TextAlign, RightToLeft == RightToLeft.Yes));
        }

        #endregion

        #region Override

        protected override void OnMouseEnter(EventArgs e)
        {
            _state = QQControlState.Highlight;
            base.OnMouseEnter(e);
            if (FlatTheme) Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _state = QQControlState.Normal;
            base.OnMouseLeave(e);
            if (FlatTheme) Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _state = QQControlState.Down;
            }
            base.OnMouseDown(mevent);
            if (FlatTheme) Invalidate();
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
                    _state = QQControlState.Normal;
                }
            }
            base.OnMouseUp(mevent);
            if (FlatTheme) Invalidate();
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
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (Enabled == false)
            {
                _state = QQControlState.Disabled;
            }

            if (FlatTheme)
            {
                // 扁平皮肤完全自绘：不调用 base.OnPaint，
                // 否则系统会先把原生方块画出来，看起来就很"违和"。
                base.OnPaintBackground(pevent);
                DrawFlat(g);
                return;
            }

            base.OnPaint(pevent);
            base.OnPaintBackground(pevent);

            Rectangle checkRect, textRect;
            CalculateRect(out checkRect, out textRect);

            switch (_state)
            {
                case QQControlState.Highlight:
                case QQControlState.Down:
                    DrawHighLightCheckRect(g, checkRect);
                    break;
                case QQControlState.Disabled:
                    DrawDisabledCheckRect(g, checkRect);
                    break;
                default:
                    DrawNormalCheckRect(g, checkRect);
                    break;
            }

            Color textColor = (Enabled == true) ? ForeColor : SystemColors.GrayText;
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
                if (_checkImg != null)
                {
                    _checkImg.Dispose();
                }
                if (_defaultFont != null)
                {
                    _defaultFont.Dispose();
                }
            }

            _checkImg = null;
            _defaultFont = null;
            base.Dispose(disposing);
        }

        #endregion

        #region Private

        private void DrawNormalCheckRect(Graphics g, Rectangle checkRect)
        {
            g.FillRectangle(Brushes.White, checkRect);
            using (Pen borderPen = new Pen(ColorTable.QQBorderColor))
            {
                g.DrawRectangle(borderPen, checkRect);
            }
            if (Checked)
            {
                g.DrawImage(_checkImg,checkRect, 0,0,_checkImg.Width, _checkImg.Height,GraphicsUnit.Pixel);
            }
        }

        private void DrawHighLightCheckRect(Graphics g, Rectangle checkRect)
        {
            DrawNormalCheckRect(g, checkRect);
            using (Pen p = new Pen(ColorTable.QQHighLightInnerColor))
            {
                g.DrawRectangle(p, checkRect);

                checkRect.Inflate(1, 1);
                p.Color = ColorTable.QQHighLightColor;

                g.DrawRectangle(p, checkRect);
            }


        }

        private void DrawDisabledCheckRect(Graphics g, Rectangle checkRect)
        {
            g.DrawRectangle(SystemPens.ControlDark, checkRect);
            if (Checked)
            {
                g.DrawImage(_checkImg,checkRect,0,0, _checkImg.Width,_checkImg.Height,GraphicsUnit.Pixel);
            }
        }

        private void SetStyles()
        {
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            UpdateStyles();
        }

        private void CalculateRect(out Rectangle checkRect, out Rectangle textRect)
        {
            checkRect = new Rectangle(
                0, 0, CheckRectWidth, CheckRectWidth);
            textRect = Rectangle.Empty;
            bool bCheckAlignLeft = (int)(LeftAlignment & CheckAlign) != 0;
            bool bCheckAlignRight = (int)(RightAlignment & CheckAlign) != 0;
            bool bRightToLeft = (RightToLeft == RightToLeft.Yes);

            if ((bCheckAlignLeft && !bRightToLeft) ||
                (bCheckAlignRight && bRightToLeft))
            {
                switch (CheckAlign)
                {
                    case ContentAlignment.TopRight:
                    case ContentAlignment.TopLeft:
                        checkRect.Y = 2;
                        break;
                    case ContentAlignment.MiddleRight:
                    case ContentAlignment.MiddleLeft:
                        checkRect.Y = (Height - CheckRectWidth) / 2;
                        break;
                    case ContentAlignment.BottomRight:
                    case ContentAlignment.BottomLeft:
                        checkRect.Y = Height - CheckRectWidth - 2;
                        break;
                }

                checkRect.X = 1;

                textRect = new Rectangle(
                    checkRect.Right + 2,
                    0,
                    Width - checkRect.Right - 4,
                    Height);
            }
            else if ((bCheckAlignRight && !bRightToLeft)
                || (bCheckAlignLeft && bRightToLeft))
            {
                switch (CheckAlign)
                {
                    case ContentAlignment.TopLeft:
                    case ContentAlignment.TopRight:
                        checkRect.Y = 2;
                        break;
                    case ContentAlignment.MiddleLeft:
                    case ContentAlignment.MiddleRight:
                        checkRect.Y = (Height - CheckRectWidth) / 2;
                        break;
                    case ContentAlignment.BottomLeft:
                    case ContentAlignment.BottomRight:
                        checkRect.Y = Height - CheckRectWidth - 2;
                        break;
                }

                checkRect.X = Width - CheckRectWidth - 1;

                textRect = new Rectangle(
                    2, 0, Width - CheckRectWidth - 6, Height);
            }
            else
            {
                switch (CheckAlign)
                {
                    case ContentAlignment.TopCenter:
                        checkRect.Y = 2;
                        textRect.Y = checkRect.Bottom + 2;
                        textRect.Height = Height - CheckRectWidth - 6;
                        break;
                    case ContentAlignment.MiddleCenter:
                        checkRect.Y = (Height - CheckRectWidth) / 2;
                        textRect.Y = 0;
                        textRect.Height = Height;
                        break;
                    case ContentAlignment.BottomCenter:
                        checkRect.Y = Height - CheckRectWidth - 2;
                        textRect.Y = 0;
                        textRect.Height = Height - CheckRectWidth - 6;
                        break;
                }

                checkRect.X = (Width - CheckRectWidth) / 2;

                textRect.X = 2;
                textRect.Width = Width - 4;
            }
        }

        private static TextFormatFlags GetTextFormatFlags(ContentAlignment alignment, bool rightToleft)
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
