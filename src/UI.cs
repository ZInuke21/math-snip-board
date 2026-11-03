// Theme, vector icons and custom-drawn controls.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MathSnip
{
    static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentSoft = Color.FromArgb(225, 235, 255);
        public static readonly Color AccentSoftHover = Color.FromArgb(209, 224, 254);
        public static readonly Color Text = Color.FromArgb(31, 35, 41);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Disabled = Color.FromArgb(192, 197, 204);
        public static readonly Color Surface = Color.White;
        public static readonly Color Hover = Color.FromArgb(240, 242, 245);
        public static readonly Color Pressed = Color.FromArgb(226, 230, 235);
        public static readonly Color Border = Color.FromArgb(224, 228, 233);
        public static readonly Color Window = Color.FromArgb(246, 247, 249);

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font Bold = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        public static readonly Font BigBold = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        public static readonly Font Title = new Font("Segoe UI", 13f, FontStyle.Bold);

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static bool IsLight(Color c) { return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 200; }
    }

    // Line icons drawn on a 24x24 grid, so they stay crisp at any DPI.
    static class Icons
    {
        public static void Draw(Graphics g, string name, RectangleF r, Color c)
        {
            if (string.IsNullOrEmpty(name)) return;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (var p = new Pen(c, 1.9f))
            using (var b = new SolidBrush(c))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                Paint(g, name, p, b);
            }
            g.Restore(state);
        }

        static void L(Graphics g, Pen p, params float[] xy)
        {
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[2 * i], xy[2 * i + 1]);
            if (pts.Length == 2) g.DrawLine(p, pts[0], pts[1]); else g.DrawLines(p, pts);
        }

        static void Poly(Graphics g, Pen p, params float[] xy)
        {
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[2 * i], xy[2 * i + 1]);
            g.DrawPolygon(p, pts);
        }

        static void Dot(Graphics g, Brush b, float x, float y, float r) { g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r); }

        static void RoundRect(Graphics g, Pen p, float x, float y, float w, float h, float r)
        {
            using (var path = Theme.Round(new RectangleF(x, y, w, h), r)) g.DrawPath(p, path);
        }

        static void Undo(Graphics g, Pen p)
        {
            L(g, p, 9, 14, 4, 9, 9, 4);
            using (var path = new GraphicsPath())
            {
                path.AddLine(4, 9, 14.5f, 9);
                path.AddArc(9, 9, 11, 11, -90, 180);
                path.AddLine(14.5f, 20, 11, 20);
                g.DrawPath(p, path);
            }
        }

        static void Paint(Graphics g, string n, Pen p, Brush b)
        {
            switch (n)
            {
                case "snip":
                    g.DrawEllipse(p, 3, 3, 6, 6);
                    g.DrawEllipse(p, 3, 15, 6, 6);
                    L(g, p, 8.12f, 8.12f, 12, 12);
                    L(g, p, 20, 4, 8.12f, 15.88f);
                    L(g, p, 14.8f, 14.8f, 20, 20);
                    break;
                case "select":
                    Poly(g, p, 4.5f, 4.5f, 20, 10.8f, 13.6f, 13.1f, 11.2f, 19.6f);
                    break;
                case "pen":
                    Poly(g, p, 16.6f, 3.4f, 20.6f, 7.4f, 7.6f, 20.4f, 3, 21, 3.6f, 16.4f);
                    L(g, p, 14.5f, 5.5f, 18.5f, 9.5f);
                    break;
                case "highlighter":
                    Poly(g, p, 15, 3, 21, 9, 12.5f, 17.5f, 6.5f, 11.5f);
                    Poly(g, p, 6.5f, 11.5f, 12.5f, 17.5f, 9.5f, 20.5f, 3.5f, 20.5f, 3.5f, 14.5f);
                    L(g, p, 14, 21, 21, 21);
                    break;
                case "line":
                    L(g, p, 5.5f, 18.5f, 18.5f, 5.5f);
                    Dot(g, b, 5.5f, 18.5f, 2.3f);
                    Dot(g, b, 18.5f, 5.5f, 2.3f);
                    break;
                case "arrow":
                    L(g, p, 5, 19, 19, 5);
                    L(g, p, 10, 5, 19, 5, 19, 14);
                    break;
                case "rect":
                    RoundRect(g, p, 3.5f, 5.5f, 17, 13, 2.5f);
                    break;
                case "ellipse":
                    g.DrawEllipse(p, 3, 4.5f, 18, 15);
                    break;
                case "text":
                    L(g, p, 4, 7, 4, 4, 20, 4, 20, 7);
                    L(g, p, 12, 4, 12, 20);
                    L(g, p, 9, 20, 15, 20);
                    break;
                case "eraser":
                    L(g, p, 7, 21, 2.9f, 16.9f, 2.9f, 13.3f, 12.3f, 3.9f, 15.7f, 3.9f, 20.6f, 8.8f, 20.6f, 12.2f, 11.8f, 21);
                    L(g, p, 7, 21, 21, 21);
                    L(g, p, 5, 11, 14, 20);
                    break;
                case "laser":
                    Dot(g, b, 15, 9, 2.6f);
                    g.DrawEllipse(p, 9.5f, 3.5f, 11, 11);
                    L(g, p, 11, 13, 4, 20);
                    break;
                case "undo":
                    Undo(g, p);
                    break;
                case "redo":
                    {
                        var st = g.Save();
                        g.TranslateTransform(24, 0);
                        g.ScaleTransform(-1, 1);
                        Undo(g, p);
                        g.Restore(st);
                    }
                    break;
                case "trash":
                    L(g, p, 3, 6, 21, 6);
                    L(g, p, 5, 6, 5, 19.5f, 7, 21.5f, 17, 21.5f, 19, 19.5f, 19, 6);
                    L(g, p, 8.5f, 6, 8.5f, 3.8f, 10, 2.5f, 14, 2.5f, 15.5f, 3.8f, 15.5f, 6);
                    L(g, p, 10, 11, 10, 17);
                    L(g, p, 14, 11, 14, 17);
                    break;
                case "grid":
                    RoundRect(g, p, 3, 3, 18, 18, 2.5f);
                    L(g, p, 9, 3, 9, 21); L(g, p, 15, 3, 15, 21);
                    L(g, p, 3, 9, 21, 9); L(g, p, 3, 15, 21, 15);
                    break;
                case "lines":
                    RoundRect(g, p, 3, 3, 18, 18, 2.5f);
                    L(g, p, 3, 9, 21, 9); L(g, p, 3, 15, 21, 15);
                    break;
                case "dots":
                    RoundRect(g, p, 3, 3, 18, 18, 2.5f);
                    for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) Dot(g, b, 7.5f + i * 4.5f, 7.5f + j * 4.5f, 1.1f);
                    break;
                case "plain":
                    RoundRect(g, p, 3, 3, 18, 18, 2.5f);
                    break;
                case "extend":
                    L(g, p, 4, 11, 4, 5, 6, 3, 18, 3, 20, 5, 20, 11);
                    L(g, p, 12, 8, 12, 21);
                    L(g, p, 8, 17, 12, 21, 16, 17);
                    break;
                case "board":
                    RoundRect(g, p, 3, 4, 18, 16, 2.5f);
                    L(g, p, 12, 8.5f, 12, 15.5f);
                    L(g, p, 8.5f, 12, 15.5f, 12);
                    break;
                case "zoomin":
                    g.DrawEllipse(p, 4, 4, 14, 14);
                    L(g, p, 16, 16, 20.5f, 20.5f);
                    L(g, p, 11, 8, 11, 14);
                    L(g, p, 8, 11, 14, 11);
                    break;
                case "zoomout":
                    g.DrawEllipse(p, 4, 4, 14, 14);
                    L(g, p, 16, 16, 20.5f, 20.5f);
                    L(g, p, 8, 11, 14, 11);
                    break;
                case "copy":
                    RoundRect(g, p, 8, 8, 13, 13, 2.2f);
                    L(g, p, 4.5f, 16, 3, 14.5f, 3, 4.5f, 4.5f, 3, 14.5f, 3, 16, 4.5f);
                    break;
                case "save":
                    L(g, p, 21, 15, 21, 19, 19, 21, 5, 21, 3, 19, 3, 15);
                    L(g, p, 7, 10, 12, 15, 17, 10);
                    L(g, p, 12, 15, 12, 3);
                    break;
                case "help":
                    g.DrawEllipse(p, 2.5f, 2.5f, 19, 19);
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(9, 6.3f, 6, 6, 195, 255);
                        path.AddLine(12, 12.3f, 12, 13.8f);
                        g.DrawPath(p, path);
                    }
                    Dot(g, b, 12, 17.2f, 1.25f);
                    break;
                case "fullscreen":
                    L(g, p, 8, 3, 5, 3, 3, 5, 3, 8);
                    L(g, p, 16, 3, 19, 3, 21, 5, 21, 8);
                    L(g, p, 3, 16, 3, 19, 5, 21, 8, 21);
                    L(g, p, 21, 16, 21, 19, 19, 21, 16, 21);
                    break;
                case "exitfull":
                    L(g, p, 8, 3, 8, 6, 6, 8, 3, 8);
                    L(g, p, 21, 8, 18, 8, 16, 6, 16, 3);
                    L(g, p, 3, 16, 6, 16, 8, 18, 8, 21);
                    L(g, p, 16, 21, 16, 18, 18, 16, 21, 16);
                    break;
                case "pressure":
                    {
                        var up = new List<PointF>();
                        var dn = new List<PointF>();
                        for (int i = 0; i <= 24; i++)
                        {
                            float t = i / 24f;
                            float x = 3 + 18 * t;
                            float y = 12 + 4.5f * (float)Math.Sin(2 * Math.PI * t + 0.6);
                            float w = 0.6f + 4.2f * t;
                            up.Add(new PointF(x, y - w / 2));
                            dn.Insert(0, new PointF(x, y + w / 2));
                        }
                        up.AddRange(dn);
                        g.FillPolygon(b, up.ToArray());
                    }
                    break;
                case "plus":
                    L(g, p, 12, 5, 12, 19);
                    L(g, p, 5, 12, 19, 12);
                    break;
                case "check":
                    L(g, p, 5, 12.5f, 10, 17.5f, 19.5f, 7);
                    break;
            }
        }
    }

    // Flat rounded button with an icon, optional caption, hover/pressed/checked states.
    class ToolButton : Control
    {
        public string IconName;
        public bool Primary, Outline, DropArrow, Center;
        public int FixedWidth, FixedHeight;
        public float IconSize = 19f;
        public float DotSize;           // >0: draws a dot of this diameter instead of an icon (size picker)
        bool checkedState, hover, down;

        public ToolButton(string icon, string caption = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            IconName = icon;
            Font = Theme.Body;
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Margin = new Padding(Program.S(1), Program.S(2), Program.S(1), Program.S(2));
            base.Text = caption ?? "";
            AutoFit();
        }

        public bool Checked
        {
            get { return checkedState; }
            set { if (value != checkedState) { checkedState = value; Invalidate(); } }
        }

        public void SetIcon(string icon) { if (icon != IconName) { IconName = icon; Invalidate(); } }

        public void AutoFit()
        {
            int h = FixedHeight > 0 ? FixedHeight : Program.S(36);
            int w;
            if (FixedWidth > 0) w = FixedWidth;
            else if (Text.Length > 0)
            {
                int tw = TextRenderer.MeasureText(Text, Font).Width;
                w = Program.S(12) + (IconName != null ? Program.S(IconSize + 8) : 0) + tw + Program.S(12) + (DropArrow ? Program.S(14) : 0);
            }
            else w = h + (DropArrow ? Program.S(12) : 0);
            Size = new Size(w, h);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AutoFit(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); AutoFit(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg = Color.Empty, fg = Theme.Text, border = Color.Empty;
            if (!Enabled) fg = Theme.Disabled;
            else if (Primary) { bg = (down || hover) ? Theme.AccentDark : Theme.Accent; fg = Color.White; }
            else if (Checked) { bg = hover ? Theme.AccentSoftHover : Theme.AccentSoft; fg = Theme.Accent; }
            else if (down) bg = Theme.Pressed;
            else if (hover) bg = Theme.Hover;
            if (Outline && !Primary) border = hover ? Color.FromArgb(200, 206, 214) : Theme.Border;

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Program.S(8);
            if (bg != Color.Empty)
                using (var path = Theme.Round(r, rad)) using (var br = new SolidBrush(bg)) g.FillPath(br, path);
            if (border != Color.Empty)
                using (var path = Theme.Round(r, rad)) using (var pen = new Pen(border, Math.Max(1f, Program.Dpi))) g.DrawPath(pen, path);

            float isz = Program.S(IconSize);
            float arrowW = DropArrow ? Program.S(12) : 0;
            if (Text.Length == 0)
            {
                float cx = (Width - arrowW) / 2f, cy = Height / 2f;
                if (DotSize > 0)
                {
                    float d = Program.S(DotSize);
                    using (var br = new SolidBrush(fg)) g.FillEllipse(br, cx - d / 2, cy - d / 2, d, d);
                }
                else Icons.Draw(g, IconName, new RectangleF(cx - isz / 2, cy - isz / 2, isz, isz), fg);
            }
            else
            {
                Size ts = TextRenderer.MeasureText(Text, Font);
                float content = (IconName != null ? isz + Program.S(8) : 0) + ts.Width;
                float x = Center ? (Width - arrowW - content) / 2f : Program.S(12);
                if (IconName != null)
                {
                    Icons.Draw(g, IconName, new RectangleF(x, (Height - isz) / 2f, isz, isz), fg);
                    x += isz + Program.S(8);
                }
                TextRenderer.DrawText(g, Text, Font, new Rectangle((int)x, 0, Width - (int)x, Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
            if (DropArrow)
            {
                float ax = Width - arrowW / 2f - Program.S(4), ay = Height / 2f;
                float s = Program.S(3);
                using (var pen = new Pen(fg, Math.Max(1.2f, 1.6f * Program.Dpi)))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new[] { new PointF(ax - s, ay - s / 2), new PointF(ax, ay + s / 2), new PointF(ax + s, ay - s / 2) });
                }
            }
        }
    }

    // Round colour swatch. IsCustom = the "more colours" button.
    class SwatchButton : Control
    {
        Color color;
        bool selected, hover;
        public bool IsCustom;

        public SwatchButton(Color c)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            color = c;
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Size = new Size(Program.S(28), Program.S(36));
            Margin = new Padding(0, Program.S(2), 0, Program.S(2));
        }

        public Color Color { get { return color; } set { if (value.ToArgb() != color.ToArgb()) { color = value; Invalidate(); } } }
        public bool Selected { get { return selected; } set { if (value != selected) { selected = value; Invalidate(); } } }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = Width / 2f, cy = Height / 2f;
            float d = Program.S(hover ? 22 : 20);
            var r = new RectangleF(cx - d / 2, cy - d / 2, d, d);

            if (IsCustom)
            {
                Color[] hues = { Color.FromArgb(239, 68, 68), Color.FromArgb(245, 158, 11), Color.FromArgb(234, 179, 8),
                                 Color.FromArgb(34, 197, 94), Color.FromArgb(59, 130, 246), Color.FromArgb(168, 85, 247) };
                for (int i = 0; i < hues.Length; i++)
                    using (var br = new SolidBrush(hues[i])) g.FillPie(br, r.X, r.Y, r.Width, r.Height, i * 60 - 90, 61);
                float inner = d * 0.56f;
                var ir = new RectangleF(cx - inner / 2, cy - inner / 2, inner, inner);
                using (var br = new SolidBrush(color.A == 0 ? Color.White : color)) g.FillEllipse(br, ir);
                if (color.A == 0)
                    using (var pen = new Pen(Theme.Text, Math.Max(1.2f, 1.5f * Program.Dpi)))
                    {
                        float s = inner * 0.28f;
                        g.DrawLine(pen, cx - s, cy, cx + s, cy);
                        g.DrawLine(pen, cx, cy - s, cx, cy + s);
                    }
            }
            else
            {
                using (var br = new SolidBrush(color)) g.FillEllipse(br, r);
                if (Theme.IsLight(color))
                    using (var pen = new Pen(Color.FromArgb(60, 0, 0, 0), Math.Max(1f, Program.Dpi * 0.8f))) g.DrawEllipse(pen, r);
            }

            if (selected)
            {
                float rd = Program.S(27);
                using (var pen = new Pen(Theme.Accent, Math.Max(2f, 2f * Program.Dpi)))
                    g.DrawEllipse(pen, cx - rd / 2, cy - rd / 2, rd, rd);
            }
        }
    }

    class Divider : Control
    {
        public Divider()
        {
            SetStyle(ControlStyles.Selectable, false);
            Size = new Size(Math.Max(1, Program.S(1)), Program.S(24));
            Margin = new Padding(Program.S(5), Program.S(8), Program.S(5), Program.S(8));
            BackColor = Theme.Border;
        }
    }

    // Small dark pill that pops up near the bottom of a window and fades away.
    class Toast : Control
    {
        readonly Timer timer = new Timer();

        public Toast()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.FromArgb(32, 35, 40);
            ForeColor = Color.White;
            Font = Theme.Body;
            Visible = false;
            timer.Tick += delegate { timer.Stop(); Visible = false; };
        }

        public void Show(string msg, int ms = 2800)
        {
            Text = msg;
            var sz = TextRenderer.MeasureText(msg, Font);
            Size = new Size(sz.Width + Program.S(36), sz.Height + Program.S(20));
            using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), Program.S(10))) Region = new Region(path);
            Reposition();
            Visible = true;
            BringToFront();
            timer.Stop();
            timer.Interval = ms;
            timer.Start();
        }

        public void Reposition()
        {
            if (Parent == null) return;
            Left = (Parent.ClientSize.Width - Width) / 2;
            Top = Parent.ClientSize.Height - Height - Program.S(32);
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Visible = false; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // The app's logo: blue rounded square with white scissors.
    class LogoMark : Control
    {
        public LogoMark()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Surface;
            Size = new Size(Program.S(40), Program.S(40));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Width * 0.26f))
            using (var br = new LinearGradientBrush(r, Color.FromArgb(59, 130, 246), Theme.AccentDark, 90f))
                g.FillPath(br, path);
            float s = Width * 0.56f;
            Icons.Draw(g, "snip", new RectangleF((Width - s) / 2, (Height - s) / 2, s, s), Color.White);
        }
    }

    // Scroll container that doesn't jump when a child (like the text box) gets focus.
    class ScrollPanel : Panel
    {
        public ScrollPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
    }
}
