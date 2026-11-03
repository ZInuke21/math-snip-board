// Full-screen snipping overlay (all monitors): dimmed screenshot, crosshair, live size readout.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MathSnip
{
    class SnipOverlay : Form
    {
        readonly Bitmap shot, bright, dimmed;
        readonly Rectangle virt;
        Point start, cur;
        Rectangle sel;
        bool dragging, haveCursor;
        Rectangle lastLabel, lastHint;
        const string HintText = "Drag to snip     ·     Enter = whole screen     ·     Esc = cancel";
        public Bitmap Result;

        public SnipOverlay(Bitmap shot, Rectangle vs)
        {
            this.shot = shot;
            virt = vs;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = vs;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            Text = "Snip";

            bright = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppPArgb);
            dimmed = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bright)) g.DrawImage(shot, new Rectangle(0, 0, shot.Width, shot.Height));
            using (var g = Graphics.FromImage(dimmed))
            {
                g.DrawImage(shot, new Rectangle(0, 0, shot.Width, shot.Height));
                using (var b = new SolidBrush(Color.FromArgb(125, 15, 23, 42))) g.FillRectangle(b, 0, 0, shot.Width, shot.Height);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PenApi.QuietPen(Handle);   // pen press-and-hold must not turn into a right-click (= cancel)
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            cur = PointToClient(Cursor.Position);
            haveCursor = true;
            Invalidate();
        }

        Rectangle HintRect()
        {
            var scr = Screen.FromPoint(new Point(cur.X + virt.X, cur.Y + virt.Y)).Bounds;
            using (var g = CreateGraphics())
            {
                var sz = TextRenderer.MeasureText(g, HintText, Theme.Bold);
                int w = sz.Width + Program.S(40), h = sz.Height + Program.S(22);
                return new Rectangle(scr.X - virt.X + (scr.Width - w) / 2, scr.Y - virt.Y + Program.S(28), w, h);
            }
        }

        Rectangle LabelRect()
        {
            if (sel.Width <= 0 || sel.Height <= 0) return Rectangle.Empty;
            string t = sel.Width + " × " + sel.Height;
            var sz = TextRenderer.MeasureText(t, Theme.Small);
            int w = sz.Width + Program.S(16), h = sz.Height + Program.S(8);
            int x = sel.Left, y = sel.Bottom + Program.S(8);
            if (y + h > Height) y = sel.Top - h - Program.S(8);
            if (y < 0) y = sel.Top + Program.S(8);
            return new Rectangle(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = e.ClipRectangle;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(dimmed, clip, clip, GraphicsUnit.Pixel);
            if (sel.Width > 0 && sel.Height > 0)
            {
                var c = Rectangle.Intersect(sel, clip);
                if (c.Width > 0 && c.Height > 0) g.DrawImage(bright, c, c, GraphicsUnit.Pixel);
            }

            if (!dragging && haveCursor)
                using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f))
                {
                    g.DrawLine(pen, 0, cur.Y, Width, cur.Y);
                    g.DrawLine(pen, cur.X, 0, cur.X, Height);
                }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (sel.Width > 0 && sel.Height > 0)
            {
                using (var pen = new Pen(Color.FromArgb(96, 165, 250), Math.Max(2f, 2f * Program.Dpi)) { Alignment = PenAlignment.Inset })
                    g.DrawRectangle(pen, sel);
                var lr = LabelRect();
                using (var path = Theme.Round(lr, Program.S(6)))
                using (var b = new SolidBrush(Color.FromArgb(225, 17, 24, 39)))
                    g.FillPath(b, path);
                TextRenderer.DrawText(g, sel.Width + " × " + sel.Height, Theme.Small, lr, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                lastLabel = lr;
            }

            if (!dragging)
            {
                var hr = HintRect();
                using (var path = Theme.Round(hr, hr.Height / 2f))
                using (var b = new SolidBrush(Color.FromArgb(235, 37, 99, 235)))
                    g.FillPath(b, path);
                TextRenderer.DrawText(g, HintText, Theme.Bold, hr, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                lastHint = hr;
            }
        }

        void InvalidateCross(Point p)
        {
            Invalidate(new Rectangle(0, p.Y - 2, Width, 5));
            Invalidate(new Rectangle(p.X - 2, 0, 5, Height));
        }

        void InvalidateSel(Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            r.Inflate(Program.S(4), Program.S(4));
            Invalidate(r);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            InvalidateCross(cur);
            if (!lastHint.IsEmpty) Invalidate(lastHint);
            start = e.Location;
            cur = e.Location;
            sel = new Rectangle(start, Size.Empty);
            dragging = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                var old = sel;
                var oldLabel = lastLabel;
                sel = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y),
                                         Math.Max(start.X, e.X) + 1, Math.Max(start.Y, e.Y) + 1);
                sel.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
                InvalidateSel(old);
                InvalidateSel(sel);
                if (!oldLabel.IsEmpty) Invalidate(oldLabel);
                Invalidate(LabelRect());
            }
            else
            {
                InvalidateCross(cur);
                var oldHint = lastHint;
                cur = e.Location;
                haveCursor = true;
                InvalidateCross(cur);
                var nh = HintRect();
                if (nh != oldHint) { if (!oldHint.IsEmpty) Invalidate(oldHint); Invalidate(nh); }
            }
            cur = e.Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            if (sel.Width < 6 || sel.Height < 6)
            {
                sel = Rectangle.Empty;
                Invalidate();
                return;
            }
            Finish(sel);
        }

        void Finish(Rectangle r)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.DrawImage(shot, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
            Result = bmp;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Enter)
            {
                var scr = Screen.FromPoint(new Point(cur.X + virt.X, cur.Y + virt.Y)).Bounds;
                var r = new Rectangle(scr.X - virt.X, scr.Y - virt.Y, scr.Width, scr.Height);
                r.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
                if (r.Width > 0 && r.Height > 0) Finish(r);
            }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            bright.Dispose();
            dimmed.Dispose();
            base.OnFormClosed(e);
        }
    }
}
