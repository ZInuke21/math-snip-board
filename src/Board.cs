// The drawing surface: page model + zoom/scroll view, pen tablet input, all the tools.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MathSnip
{
    enum Tool { Select, Pen, Highlighter, Line, Arrow, Rect, Ellipse, Text, Eraser, Laser }
    enum Backdrop { Plain, Grid, Lines, Dots }

    static class PenApi
    {
        public const int WM_POINTERUPDATE = 0x0245, WM_POINTERDOWN = 0x0246, WM_POINTERUP = 0x0247,
                         WM_POINTERCAPTURECHANGED = 0x024C;
        public const uint PT_PEN = 3;
        public const uint POINTER_FLAG_INCONTACT = 0x4;
        public const uint PEN_FLAG_BARREL = 0x1, PEN_FLAG_INVERTED = 0x2, PEN_FLAG_ERASER = 0x4;
        public const uint PEN_MASK_PRESSURE = 0x1;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_INFO
        {
            public uint pointerType;
            public uint pointerId;
            public uint frameId;
            public uint pointerFlags;
            public IntPtr sourceDevice;
            public IntPtr hwndTarget;
            public POINT ptPixelLocation;
            public POINT ptHimetricLocation;
            public POINT ptPixelLocationRaw;
            public POINT ptHimetricLocationRaw;
            public uint dwTime;
            public uint historyCount;
            public int InputData;
            public uint dwKeyStates;
            public ulong PerformanceCount;
            public int ButtonChangeType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_PEN_INFO
        {
            public POINTER_INFO pointerInfo;
            public uint penFlags;
            public uint penMask;
            public uint pressure;
            public uint rotation;
            public int tiltX;
            public int tiltY;
        }

        [DllImport("user32.dll")] public static extern bool GetPointerType(uint pointerId, out uint pointerType);
        [DllImport("user32.dll")] public static extern bool GetPointerPenInfo(uint pointerId, out POINTER_PEN_INFO penInfo);
        [DllImport("user32.dll")] public static extern bool GetPointerPenInfoHistory(uint pointerId, ref uint entriesCount, [In, Out] POINTER_PEN_INFO[] penInfo);
        [DllImport("user32.dll")] public static extern bool GetPointerDeviceRects(IntPtr device, out RECT pointerDeviceRect, out RECT displayRect);
        [DllImport("user32.dll")] public static extern bool SetWindowFeedbackSetting(IntPtr hwnd, int feedback, uint flags, uint size, ref int configuration);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // No press-and-hold right-click, flicks or tap ripples: ink starts the instant the pen lands.
        public static void QuietPen(IntPtr hwnd)
        {
            try { SetProp(hwnd, "MicrosoftTabletPenServiceProperty", (IntPtr)(0x1 | 0x8 | 0x10 | 0x10000)); } catch { }
            try
            {
                int off = 0;
                for (int f = 1; f <= 11; f++) SetWindowFeedbackSetting(hwnd, f, 0, 4, ref off);
            }
            catch { }
        }

        public static void ZeroTextMargins(TextBox tb)
        {
            try { SendMessage(tb.Handle, 0xD3, (IntPtr)3, IntPtr.Zero); } catch { }   // EM_SETMARGINS
        }
    }

    static class Cursors2
    {
        static Cursor blank, dot;

        static Cursor Make(Action<Graphics> paint)
        {
            using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; paint(g); }
                return new Cursor(bmp.GetHicon());   // icon hotspot = centre (16,16)
            }
        }

        public static Cursor Blank
        {
            get
            {
                if (blank == null) { try { blank = Make(g => { }); } catch { blank = Cursors.Cross; } }
                return blank;
            }
        }

        public static Cursor Dot
        {
            get
            {
                if (dot == null)
                {
                    try
                    {
                        dot = Make(g =>
                        {
                            g.FillEllipse(Brushes.White, 12.5f, 12.5f, 7, 7);
                            g.FillEllipse(new SolidBrush(Color.FromArgb(30, 30, 30)), 14, 14, 4, 4);
                        });
                    }
                    catch { dot = Cursors.Cross; }
                }
                return dot;
            }
        }
    }

    class Board : Control
    {
        static readonly float[] PenW = { 2f, 3.5f, 6.5f };
        static readonly float[] HlW = { 14f, 22f, 34f };
        static readonly float[] TextPx = { 20f, 28f, 42f };
        static readonly float[] EraserR = { 9f, 18f, 34f };
        const float Smooth = 0.4f;
        public const float MinZoom = 0.25f, MaxZoom = 4f;
        const int LaserLife = 900;

        // ---------------------------------------------------------------- model
        public List<Item> Items = new List<Item>();
        readonly Stack<List<Item>> undo = new Stack<List<Item>>();
        readonly Stack<List<Item>> redo = new Stack<List<Item>>();
        float zoom = 1f;
        float pageW, pageH;
        Backdrop backdrop = Backdrop.Plain;

        public Tool CurrentTool = Tool.Pen;
        public Color PenColor { get; set; }
        public Color HlColor { get; set; }
        int level = 1;
        public bool UsePressure = true;
        public bool Dirty;
        public event EventHandler HistoryChanged, ZoomChanged;

        // ---------------------------------------------------------------- render cache
        Bitmap cache;
        Rectangle cacheRect;
        Graphics liveG;

        // ---------------------------------------------------------------- interaction
        enum Mode { None, Ink, Shape, Erase, Pan, Move, Resize, Laser, LineEdit }
        Mode mode;
        bool inputIsPen, penDown, penApiOk = true, spacePan;
        uint penId;
        StrokeItem liveStroke;
        ShapeItem liveShape;
        bool snapped, holdTried;
        int holdSince;
        PointF holdAnchor;
        readonly Timer holdTimer = new Timer { Interval = 50 };
        PointF smoothP, lastRaw;
        float smoothPr = -1f;
        List<Item> eraseSnapshot;
        bool erasedAny;
        PointF? eraseLast;
        Point panStartScreen, panStartScroll;
        Cursor cursorBeforePan;
        ImageItem selected, dragItem;
        int dragIndex;
        List<Item> dragSnapshot, dragAttached;
        RectangleF dragRect, dragStartRect;
        PointF dragOffset;
        bool haveDevRect;

        // end dots on a straight line/arrow (drag a dot to move that end, like the Snipping Tool)
        ShapeItem activeLine, lineOrig;
        int lineGrab, lineIndex;            // grab: 0 = end A, 1 = end B, 2 = whole line (Select tool)
        List<Item> lineSnapshot;
        PointF lineGrabStart;
        bool lineMoved, handleHover;
        PenApi.RECT devRect, dispRect;

        // laser pointer
        class LaserPt { public PointF P; public int T; }
        readonly List<List<LaserPt>> laser = new List<List<LaserPt>>();
        List<LaserPt> laserCur;
        readonly Timer laserTimer = new Timer { Interval = 16 };
        Rectangle laserDirty;

        // pointer visuals
        Point mouse;
        bool mouseIn, hoverEraser;

        // text editing
        TextBox textBox;
        PointF textPos;
        float textPx;
        Color textColor;
        TextItem editOriginal;
        int editIndex;
        bool committingText;

        public Board()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            BackColor = Color.White;
            PenColor = Color.FromArgb(24, 24, 27);
            HlColor = Color.FromArgb(255, 221, 0);
            holdTimer.Tick += HoldTick;
            laserTimer.Tick += LaserTick;
            UpdateCursor();
        }

        // ---------------------------------------------------------------- properties
        public float Zoom { get { return zoom; } }
        public int Level { get { return level; } }
        public Backdrop Backdrop { get { return backdrop; } set { backdrop = value; DropCache(); Invalidate(); } }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public bool IsTyping { get { return textBox != null; } }
        public bool IsBusy { get { return mode != Mode.None; } }
        public bool HasSelection { get { return selected != null || activeLine != null; } }
        public bool HasInk { get { foreach (var it in Items) if (it.IsInk) return true; return false; } }

        float PenWidth { get { return PenW[level] * Program.Dpi; } }
        float HlWidth { get { return HlW[level] * Program.Dpi; } }
        float TextSize { get { return TextPx[level] * Program.Dpi; } }
        float EraseClientR { get { return EraserR[level] * Program.Dpi; } }

        float WidthFor(float pressure)
        {
            if (pressure < 0 || !UsePressure) return PenWidth;
            double p = Math.Max(0.02, Math.Min(1.0, pressure));
            return PenWidth * (0.35f + 1.15f * (float)Math.Pow(p, 0.75));
        }

        public Color ActiveColor { get { return CurrentTool == Tool.Highlighter ? HlColor : PenColor; } }

        public void SetTool(Tool t)
        {
            CommitText();
            CurrentTool = t;
            if (t != Tool.Select && selected != null) { var s = selected; selected = null; InvalidatePage(s.Bounds); }
            if (t != Tool.Select) SetActiveLine(null);
            if (t == Tool.Laser) laserTimer.Start();
            UpdateCursor();
            Invalidate();
        }

        public void SetColor(Color c)
        {
            if (CurrentTool == Tool.Highlighter) HlColor = c;
            else PenColor = c;
            if (textBox != null) { textColor = c; textBox.ForeColor = c; }
        }

        public void SetLevel(int l)
        {
            level = Math.Max(0, Math.Min(2, l));
            if (textBox != null)
            {
                textPx = TextSize;
                textBox.Font = TextItem.MakeFont(textPx * zoom);
                SizeTextBox();
            }
            Invalidate(IndicatorRect());
        }

        // ---------------------------------------------------------------- view helpers
        ScrollableControl Host { get { return Parent as ScrollableControl; } }

        Rectangle VisibleRect()
        {
            var h = Host;
            if (h == null) return ClientRectangle;
            var r = new Rectangle(-Left, -Top, h.ClientSize.Width, h.ClientSize.Height);
            r.Intersect(ClientRectangle);
            return r;
        }

        Point ScrollPos
        {
            get { var h = Host; return h == null ? Point.Empty : new Point(-h.AutoScrollPosition.X, -h.AutoScrollPosition.Y); }
            set { var h = Host; if (h != null) h.AutoScrollPosition = new Point(Math.Max(0, value.X), Math.Max(0, value.Y)); }
        }

        PointF ToPage(PointF c) { return new PointF(c.X / zoom, c.Y / zoom); }

        Rectangle ToClient(RectangleF r)
        {
            return Rectangle.FromLTRB((int)Math.Floor(r.Left * zoom), (int)Math.Floor(r.Top * zoom),
                                      (int)Math.Ceiling(r.Right * zoom), (int)Math.Ceiling(r.Bottom * zoom));
        }

        RectangleF PageRectOf(Rectangle c) { return new RectangleF(c.X / zoom, c.Y / zoom, c.Width / zoom, c.Height / zoom); }

        void InvalidatePage(RectangleF r)
        {
            if (r.IsEmpty) return;
            var c = ToClient(r);
            c.Inflate(3, 3);
            Invalidate(c);
        }

        // Page always covers the viewport, with a little extra height so there is always somewhere to write.
        public void ApplySize()
        {
            var h = Host;
            if (h != null)
            {
                // Width tracks the window (never wider than needed, so no pointless sideways scrollbar);
                // height only ever grows, and always leaves room to keep writing.
                int vw = ViewWidth() - 1;
                var c = ContentBounds();
                pageW = Math.Max(vw / zoom, c.IsEmpty ? 0 : c.Right + 24);
                pageH = Math.Max(pageH, h.ClientSize.Height * 1.25f / zoom);
            }
            var sz = new Size((int)Math.Ceiling(pageW * zoom), (int)Math.Ceiling(pageH * zoom));
            if (sz != Size) Size = sz;
        }

        // Width of the visible area next to the (always present) vertical scrollbar.
        int ViewWidth()
        {
            var h = Host;
            if (h == null) return Width;
            if (h.VerticalScroll.Visible && h.ClientSize.Width < h.Width) return h.ClientSize.Width;   // Windows: exact
            return h.Width - SystemInformation.VerticalScrollBarWidth;
        }

        void Grow(RectangleF b)
        {
            bool changed = false;
            var h = Host;
            float slack = (h != null ? h.ClientSize.Height / zoom : 600) * 0.6f;
            if (b.Bottom > pageH - slack * 0.5f) { pageH = b.Bottom + slack; changed = true; }
            if (b.Right > pageW - 20) { pageW = b.Right + 120; changed = true; }
            if (changed) ApplySize();
        }

        public RectangleF ContentBounds()
        {
            var r = RectangleF.Empty;
            foreach (var it in Items) r = Geo.Union(r, it.Bounds);
            return r;
        }

        // ---------------------------------------------------------------- zoom
        public void SetZoom(float z, Point anchor)
        {
            z = Math.Max(MinZoom, Math.Min(MaxZoom, z));
            if (Math.Abs(z - zoom) < 0.0005f || mode != Mode.None) return;
            CommitText();
            var pageAnchor = new PointF(anchor.X / zoom, anchor.Y / zoom);
            var viewPos = new Point(anchor.X + Left, anchor.Y + Top);
            zoom = z;
            DropCache();
            ApplySize();
            var h = Host;
            if (h != null)
            {
                h.PerformLayout();
                ScrollPos = new Point((int)Math.Round(pageAnchor.X * zoom - viewPos.X), (int)Math.Round(pageAnchor.Y * zoom - viewPos.Y));
            }
            Invalidate();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        Point ViewCenter()
        {
            var v = VisibleRect();
            return new Point(v.X + v.Width / 2, v.Y + v.Height / 2);
        }

        static readonly float[] ZoomSteps = { 0.25f, 0.33f, 0.5f, 0.67f, 0.8f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f, 4f };

        public void ZoomStep(bool zoomIn)
        {
            float next = zoom;
            if (zoomIn) { foreach (var s in ZoomSteps) if (s > zoom + 0.01f) { next = s; break; } }
            else { for (int i = ZoomSteps.Length - 1; i >= 0; i--) if (ZoomSteps[i] < zoom - 0.01f) { next = ZoomSteps[i]; break; } }
            SetZoom(next, ViewCenter());
        }

        public void ZoomTo(float z) { SetZoom(z, ViewCenter()); }

        public void FitWidth()
        {
            var h = Host;
            if (h == null) return;
            var c = ContentBounds();
            if (c.IsEmpty) { ZoomTo(1f); return; }
            float pad = Program.S(24);
            float z = ViewWidth() / (c.Width + 2 * pad);
            z = Math.Max(MinZoom, Math.Min(MaxZoom, z));
            SetZoom(z, ViewCenter());
            ScrollPos = new Point((int)((c.Left - pad) * zoom), (int)((c.Top - pad) * zoom));
        }

        // ---------------------------------------------------------------- rendering
        void DropCache()
        {
            ReleaseLive();
            if (cache != null) { cache.Dispose(); cache = null; }
        }

        void ReleaseLive()
        {
            if (liveG != null) { liveG.Dispose(); liveG = null; }
        }

        void PrepCache(Graphics g)
        {
            g.TranslateTransform(-cacheRect.X, -cacheRect.Y);
            g.ScaleTransform(zoom, zoom);
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        // The cache covers the visible area plus a margin, so scrolling and zooming never need a huge bitmap.
        void EnsureCache(Rectangle need)
        {
            if (cache != null && cacheRect.Contains(need)) return;
            var want = Rectangle.Union(VisibleRect(), need);
            want.Inflate(Program.S(320), Program.S(320));
            want.Intersect(ClientRectangle);
            if (want.Width < 1 || want.Height < 1) want = new Rectangle(0, 0, 1, 1);
            DropCache();
            cache = new Bitmap(want.Width, want.Height, PixelFormat.Format32bppPArgb);
            cacheRect = want;
            using (var g = Graphics.FromImage(cache))
            {
                PrepCache(g);
                PaintPage(g, PageRectOf(want));
                if (liveStroke != null && !liveStroke.Highlight) liveStroke.DrawFinalized(g);
            }
        }

        Graphics LiveGraphics()
        {
            EnsureCache(VisibleRect());
            if (liveG == null)
            {
                liveG = Graphics.FromImage(cache);
                PrepCache(liveG);
            }
            return liveG;
        }

        // Re-render just one area of the cache (eraser, moved pictures, edited text).
        void RefreshCache(RectangleF pageRect)
        {
            if (pageRect.IsEmpty) return;
            if (cache != null)
            {
                var c = ToClient(pageRect);
                c.Inflate(2, 2);
                c.Intersect(cacheRect);
                if (c.Width > 0 && c.Height > 0)
                {
                    ReleaseLive();
                    using (var g = Graphics.FromImage(cache))
                    {
                        PrepCache(g);
                        var pr = PageRectOf(c);
                        g.SetClip(pr);
                        PaintPage(g, pr);
                        if (liveStroke != null && !liveStroke.Highlight) liveStroke.DrawFinalized(g);
                    }
                }
            }
            InvalidatePage(pageRect);
        }

        void PaintPage(Graphics g, RectangleF clip)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.FillRectangle(Brushes.White, clip);
            DrawBackdrop(g, clip);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var it in Items)
                if (it.Bounds.IntersectsWith(clip))
                {
                    try { it.Draw(g); }
                    catch { }   // one bad item must never blank the board
                }
        }

        void DrawBackdrop(Graphics g, RectangleF clip)
        {
            if (backdrop == Backdrop.Plain) return;
            float step = Program.S(backdrop == Backdrop.Lines ? 40 : 32);
            int every = 1;
            while (step * every * zoom < 12) every *= 2;
            float px = 1f / zoom;
            int i0 = (int)Math.Floor(clip.Left / step), i1 = (int)Math.Ceiling(clip.Right / step);
            int j0 = (int)Math.Floor(clip.Top / step), j1 = (int)Math.Ceiling(clip.Bottom / step);

            if (backdrop == Backdrop.Grid)
            {
                using (var minor = new Pen(Color.FromArgb(228, 233, 240), px))
                using (var major = new Pen(Color.FromArgb(203, 212, 225), px))
                {
                    for (int i = i0; i <= i1; i++)
                        if (i % every == 0) g.DrawLine(i % 5 == 0 ? major : minor, i * step, clip.Top, i * step, clip.Bottom);
                    for (int j = j0; j <= j1; j++)
                        if (j % every == 0) g.DrawLine(j % 5 == 0 ? major : minor, clip.Left, j * step, clip.Right, j * step);
                }
            }
            else if (backdrop == Backdrop.Lines)
            {
                using (var pen = new Pen(Color.FromArgb(200, 219, 240), px))
                    for (int j = Math.Max(1, j0); j <= j1; j++)
                        if (j % every == 0) g.DrawLine(pen, clip.Left, j * step, clip.Right, j * step);
            }
            else
            {
                float r = 1.5f * Program.Dpi / zoom;
                using (var b = new SolidBrush(Color.FromArgb(176, 187, 202)))
                    for (int i = i0; i <= i1; i++)
                        if (i % every == 0)
                            for (int j = j0; j <= j1; j++)
                                if (j % every == 0) g.FillRectangle(b, i * step - r / 2, j * step - r / 2, r, r);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = e.ClipRectangle;
            EnsureCache(clip);
            g.DrawImage(cache, clip, new Rectangle(clip.X - cacheRect.X, clip.Y - cacheRect.Y, clip.Width, clip.Height), GraphicsUnit.Pixel);

            var st = g.Save();
            g.ScaleTransform(zoom, zoom);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try   // a drawing hiccup on one frame must never interrupt the lesson
            {
                if (liveStroke != null) { if (liveStroke.Highlight) liveStroke.Draw(g); else liveStroke.DrawTail(g); }
                if (liveShape != null) liveShape.Draw(g);
                if (dragItem != null) DrawDragged(g);
            }
            catch { }
            g.Restore(st);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            var sel = SelectionClientRect();
            if (!sel.IsEmpty)
            {
                using (var pen = new Pen(Theme.Accent, Math.Max(1.5f, 1.5f * Program.Dpi)) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, sel.X - 2, sel.Y - 2, sel.Width + 4, sel.Height + 4);
                var hr = HandleRect(sel);
                g.FillEllipse(Brushes.White, hr);
                using (var pen = new Pen(Theme.Accent, Math.Max(2f, 2f * Program.Dpi))) g.DrawEllipse(pen, hr);
            }
            DrawLineHandles(g);
            if (textBox != null)
            {
                var r = textBox.Bounds;
                r.Inflate(Program.S(6), Program.S(4));
                using (var pen = new Pen(Color.FromArgb(160, Theme.Accent), Math.Max(1f, Program.Dpi)) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, r);
            }
            if (ShowEraserRing)
            {
                float rr = EraseClientR;
                using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillEllipse(b, mouse.X - rr, mouse.Y - rr, 2 * rr, 2 * rr);
                using (var pen = new Pen(Color.FromArgb(150, 60, 64, 72), Math.Max(1.2f, 1.2f * Program.Dpi))) g.DrawEllipse(pen, mouse.X - rr, mouse.Y - rr, 2 * rr, 2 * rr);
            }
            try { DrawLaser(g); } catch { }
        }

        Rectangle SelectionClientRect()
        {
            if (dragItem != null) return ToClient(dragRect);
            if (selected != null && CurrentTool == Tool.Select) return ToClient(selected.Rect);
            return Rectangle.Empty;
        }

        static Rectangle HandleRect(Rectangle sel)
        {
            int h = Program.S(14);
            return new Rectangle(sel.Right + 2 - h / 2, sel.Bottom + 2 - h / 2, h, h);
        }

        bool ShowEraserRing
        {
            get
            {
                if (!mouseIn && mode != Mode.Erase) return false;
                return CurrentTool == Tool.Eraser || mode == Mode.Erase || hoverEraser;
            }
        }

        Rectangle IndicatorRect()
        {
            int r = (int)Math.Max(EraseClientR, Program.S(14)) + Program.S(4);
            return new Rectangle(mouse.X - r, mouse.Y - r, 2 * r, 2 * r);
        }

        void SetMouse(Point p)
        {
            bool show = ShowEraserRing || CurrentTool == Tool.Laser;
            if (show) Invalidate(IndicatorRect());
            mouse = p;
            mouseIn = true;
            if (show) Invalidate(IndicatorRect());
        }

        // ---------------------------------------------------------------- laser pointer
        void LaserAdd(PointF pp)
        {
            if (laserCur == null) { laserCur = new List<LaserPt>(); laser.Add(laserCur); }
            laserCur.Add(new LaserPt { P = pp, T = Environment.TickCount });
            laserTimer.Start();
        }

        void LaserTick(object sender, EventArgs e)
        {
            int now = Environment.TickCount;
            foreach (var l in laser) l.RemoveAll(q => unchecked(now - q.T) > LaserLife);
            laser.RemoveAll(l => l.Count == 0 && l != laserCur);
            var r = LaserBounds();
            var dirty = laserDirty.IsEmpty ? r : (r.IsEmpty ? laserDirty : Rectangle.Union(laserDirty, r));
            if (!dirty.IsEmpty) Invalidate(dirty);
            laserDirty = r;
            if (laser.Count == 0 && laserCur == null && CurrentTool != Tool.Laser) laserTimer.Stop();
        }

        Rectangle LaserBounds()
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var l in laser)
                foreach (var q in l)
                {
                    x0 = Math.Min(x0, q.P.X * zoom); y0 = Math.Min(y0, q.P.Y * zoom);
                    x1 = Math.Max(x1, q.P.X * zoom); y1 = Math.Max(y1, q.P.Y * zoom);
                }
            if (x0 > x1) return Rectangle.Empty;
            int pad = Program.S(16);
            return Rectangle.FromLTRB((int)x0 - pad, (int)y0 - pad, (int)x1 + pad, (int)y1 + pad);
        }

        void DrawLaser(Graphics g)
        {
            int now = Environment.TickCount;
            using (var glow = Geo.InkPen(Color.Red, Program.S(10)))
            using (var core = Geo.InkPen(Color.Red, Math.Max(2.5f, 3.5f * Program.Dpi)))
            {
                foreach (var l in laser)
                    for (int i = 1; i < l.Count; i++)
                    {
                        float a = 1f - unchecked(now - l[i].T) / (float)LaserLife;
                        if (a <= 0) continue;
                        var p0 = new PointF(l[i - 1].P.X * zoom, l[i - 1].P.Y * zoom);
                        var p1 = new PointF(l[i].P.X * zoom, l[i].P.Y * zoom);
                        glow.Color = Color.FromArgb((int)(60 * a), 255, 40, 40);
                        core.Color = Color.FromArgb((int)(235 * a), 255, 45, 45);
                        g.DrawLine(glow, p0, p1);
                        g.DrawLine(core, p0, p1);
                    }
            }
            if (CurrentTool == Tool.Laser && mouseIn)
            {
                float r1 = Program.S(12), r2 = Program.S(7), r3 = Program.S(4.5f);
                using (var b = new SolidBrush(Color.FromArgb(50, 255, 40, 40))) g.FillEllipse(b, mouse.X - r1, mouse.Y - r1, 2 * r1, 2 * r1);
                using (var b = new SolidBrush(Color.FromArgb(110, 255, 40, 40))) g.FillEllipse(b, mouse.X - r2, mouse.Y - r2, 2 * r2, 2 * r2);
                using (var b = new SolidBrush(Color.FromArgb(255, 235, 30, 30))) g.FillEllipse(b, mouse.X - r3, mouse.Y - r3, 2 * r3, 2 * r3);
                float r4 = Program.S(1.6f);
                g.FillEllipse(Brushes.White, mouse.X - r4, mouse.Y - r4, 2 * r4, 2 * r4);
            }
        }

        // ---------------------------------------------------------------- cursor
        void UpdateCursor()
        {
            if (mode == Mode.Pan) return;
            Cursor c;
            if (spacePan) c = Cursors.Hand;
            else if (hoverEraser) c = Cursors2.Blank;
            else switch (CurrentTool)
                {
                    case Tool.Select: c = Cursors.Default; break;
                    case Tool.Text: c = Cursors.IBeam; break;
                    case Tool.Eraser:
                    case Tool.Laser: c = Cursors2.Blank; break;
                    case Tool.Pen:
                    case Tool.Highlighter: c = Cursors2.Dot; break;
                    default: c = Cursors.Cross; break;
                }
            if (Cursor != c) Cursor = c;
        }

        public bool SpacePan
        {
            get { return spacePan; }
            set { if (value != spacePan) { spacePan = value; UpdateCursor(); } }
        }

        void UpdateSelectCursor(Point p)
        {
            if (CurrentTool != Tool.Select || spacePan || mode != Mode.None) return;
            var pp = ToPage(p);
            Cursor c = Cursors.Default;
            if (selected != null && HandleRect(ToClient(selected.Rect)).Contains(p)) c = Cursors.SizeNWSE;
            else if (LineAt(pp) != null || ImageAt(pp) != null) c = Cursors.SizeAll;
            if (Cursor != c) Cursor = c;
        }

        ImageItem ImageAt(PointF pp)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var im = Items[i] as ImageItem;
                if (im != null && im.Rect.Contains(pp)) return im;
            }
            return null;
        }

        // ---------------------------------------------------------------- pen tablet input (Windows Ink)
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PenApi.QuietPen(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (penApiOk && (m.Msg == PenApi.WM_POINTERDOWN || m.Msg == PenApi.WM_POINTERUPDATE ||
                             m.Msg == PenApi.WM_POINTERUP || m.Msg == PenApi.WM_POINTERCAPTURECHANGED))
            {
                bool handled = false;
                try { handled = HandlePointer(m); }
                catch (DllNotFoundException) { penApiOk = false; }
                catch (EntryPointNotFoundException) { penApiOk = false; }
                if (handled) { m.Result = IntPtr.Zero; return; }
            }
            base.WndProc(ref m);
        }

        bool HandlePointer(Message m)
        {
            uint id = (uint)(m.WParam.ToInt64() & 0xFFFF);
            uint type;
            if (!PenApi.GetPointerType(id, out type) || type != PenApi.PT_PEN) return false;

            if (m.Msg == PenApi.WM_POINTERCAPTURECHANGED)
            {
                if (penDown && id == penId) { penDown = false; EndInput(); }
                return true;
            }

            PenApi.POINTER_PEN_INFO info;
            if (!PenApi.GetPointerPenInfo(id, out info)) return false;
            Point origin = PointToScreen(Point.Empty);
            bool eraserEnd = (info.penFlags & (PenApi.PEN_FLAG_ERASER | PenApi.PEN_FLAG_INVERTED)) != 0;
            bool barrel = (info.penFlags & PenApi.PEN_FLAG_BARREL) != 0;

            if (m.Msg == PenApi.WM_POINTERDOWN)
            {
                if (mode != Mode.None) EndInput();
                penId = id;
                penDown = true;
                haveDevRect = PenApi.GetPointerDeviceRects(info.pointerInfo.sourceDevice, out devRect, out dispRect);
                if (!Focused && textBox == null) Focus();
                BeginInput(ToClientPt(ref info.pointerInfo, origin), Pressure(ref info), true, eraserEnd || barrel);
                return true;
            }

            if (m.Msg == PenApi.WM_POINTERUP)
            {
                if (penDown && id == penId) { penDown = false; EndInput(); }
                return true;
            }

            // WM_POINTERUPDATE
            if (!penDown || id != penId)
            {
                // Hovering: show the eraser ring when the pen is flipped over, then let Windows move the cursor.
                if (eraserEnd != hoverEraser) { hoverEraser = eraserEnd; Invalidate(IndicatorRect()); UpdateCursor(); }
                return false;
            }
            if ((info.pointerInfo.pointerFlags & PenApi.POINTER_FLAG_INCONTACT) == 0)
            {
                penDown = false;
                EndInput();
                return true;
            }

            // Pull every sample since the last message so fast curves stay round.
            uint n = info.pointerInfo.historyCount;
            if (n > 1)
            {
                var hist = new PenApi.POINTER_PEN_INFO[n];
                uint cnt = n;
                if (PenApi.GetPointerPenInfoHistory(id, ref cnt, hist))
                {
                    int got = (int)Math.Min(cnt, n);
                    for (int i = got - 1; i >= 0; i--)
                        MoveInput(ToClientPt(ref hist[i].pointerInfo, origin), Pressure(ref hist[i]));
                    return true;
                }
            }
            MoveInput(ToClientPt(ref info.pointerInfo, origin), Pressure(ref info));
            return true;
        }

        static float Pressure(ref PenApi.POINTER_PEN_INFO i)
        {
            if ((i.penMask & PenApi.PEN_MASK_PRESSURE) == 0) return -1f;
            return Math.Min(1f, i.pressure / 1024f);
        }

        // Sub-pixel position from HIMETRIC coordinates when they agree with the pixel position.
        PointF ToClientPt(ref PenApi.POINTER_INFO pi, Point origin)
        {
            float x = pi.ptPixelLocation.X, y = pi.ptPixelLocation.Y;
            if (haveDevRect)
            {
                float dw = devRect.Right - devRect.Left, dh = devRect.Bottom - devRect.Top;
                float sw = dispRect.Right - dispRect.Left, sh = dispRect.Bottom - dispRect.Top;
                if (dw > 0 && dh > 0 && sw > 0 && sh > 0)
                {
                    float hx = dispRect.Left + (pi.ptHimetricLocation.X - devRect.Left) * sw / dw;
                    float hy = dispRect.Top + (pi.ptHimetricLocation.Y - devRect.Top) * sh / dh;
                    if (Math.Abs(hx - x) <= 1.5f && Math.Abs(hy - y) <= 1.5f) { x = hx; y = hy; }
                }
            }
            return new PointF(x - origin.X, y - origin.Y);
        }

        // ---------------------------------------------------------------- mouse / touch
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (textBox == null) Focus();
            if (mode != Mode.None) return;
            if (e.Button == MouseButtons.Middle) { inputIsPen = false; StartPan(e.Location); return; }
            if (e.Button == MouseButtons.Right) { CommitText(); return; }
            if (e.Button != MouseButtons.Left) return;
            BeginInput(e.Location, -1f, false, false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (mode == Mode.None || !inputIsPen) SetMouse(e.Location);
            if (mode != Mode.None && !inputIsPen) MoveInput(e.Location, -1f);
            else if (mode == Mode.None && !UpdateHandleCursor(e.Location)) UpdateSelectCursor(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (mode != Mode.None && !inputIsPen &&
                (e.Button == MouseButtons.Left || (mode == Mode.Pan && e.Button == MouseButtons.Middle)))
                EndInput();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); mouseIn = true; }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Invalidate(IndicatorRect());
            mouseIn = false;
            if (hoverEraser) { hoverEraser = false; UpdateCursor(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
            if ((ModifierKeys & Keys.Control) != 0)
            {
                SetZoom(zoom * (float)Math.Pow(1.1, e.Delta / 120.0), e.Location);
                return;
            }
            int d = (int)(e.Delta * Program.S(100) / 120.0);
            var sp = ScrollPos;
            ScrollPos = (ModifierKeys & Keys.Shift) != 0 ? new Point(sp.X - d, sp.Y) : new Point(sp.X, sp.Y - d);
            base.OnMouseWheel(e);
        }

        // ---------------------------------------------------------------- input pipeline
        void BeginInput(PointF cp, float pressure, bool pen, bool eraserTip)
        {
            if (textBox != null)
            {
                bool wasText = CurrentTool == Tool.Text;
                CommitText();
                if (wasText && !eraserTip && !spacePan) return;
            }
            inputIsPen = pen;
            var pp = ToPage(cp);
            if (spacePan) { StartPan(cp); return; }
            Tool t = eraserTip ? Tool.Eraser : CurrentTool;
            if (t != Tool.Eraser && t != Tool.Laser)
            {
                int end = LineHandleAt(cp);
                if (end >= 0) { BeginLineEdit(activeLine, end, pp); return; }
            }
            SetActiveLine(null);   // touching anywhere else puts the dots away

            switch (t)
            {
                case Tool.Select:
                    BeginSelect(cp, pp);
                    break;
                case Tool.Text:
                    BeginText(pp);
                    break;
                case Tool.Pen:
                case Tool.Highlighter:
                    {
                        bool hl = t == Tool.Highlighter;
                        smoothP = pp; lastRaw = pp; smoothPr = pressure;
                        snapped = false; holdTried = false;
                        holdAnchor = pp; holdSince = Environment.TickCount;
                        liveStroke = new StrokeItem(hl ? Color.FromArgb(105, HlColor) : PenColor, hl);
                        liveStroke.Add(pp, hl ? HlWidth : WidthFor(smoothPr));
                        mode = Mode.Ink;
                        InvalidatePage(liveStroke.Bounds);
                        holdTimer.Start();
                    }
                    break;
                case Tool.Line:
                case Tool.Arrow:
                case Tool.Rect:
                case Tool.Ellipse:
                    {
                        var kind = t == Tool.Line ? ShapeKind.Line : t == Tool.Arrow ? ShapeKind.Arrow : t == Tool.Rect ? ShapeKind.Rect : ShapeKind.Ellipse;
                        liveShape = new ShapeItem(kind, pp, pp, PenColor, PenWidth);
                        mode = Mode.Shape;
                    }
                    break;
                case Tool.Eraser:
                    mode = Mode.Erase;
                    eraseSnapshot = new List<Item>(Items);
                    erasedAny = false;
                    eraseLast = null;
                    SetMouse(Point.Round(cp));
                    EraseAt(pp);
                    break;
                case Tool.Laser:
                    mode = Mode.Laser;
                    laserCur = null;
                    LaserAdd(pp);
                    break;
            }
        }

        void MoveInput(PointF cp, float pressure)
        {
            if (mode == Mode.None) return;
            var pp = ToPage(cp);
            switch (mode)
            {
                case Mode.Pan:
                    {
                        var s = PointToScreen(Point.Round(cp));
                        ScrollPos = new Point(panStartScroll.X - (s.X - panStartScreen.X), panStartScroll.Y - (s.Y - panStartScreen.Y));
                    }
                    break;
                case Mode.Move:
                    {
                        var old = DragArea();
                        dragRect.X = (float)Math.Round(pp.X - dragOffset.X);
                        dragRect.Y = (float)Math.Round(pp.Y - dragOffset.Y);
                        InvalidateDrag(old);
                    }
                    break;
                case Mode.Resize:
                    {
                        var old = DragArea();
                        float sx = (pp.X - dragStartRect.X) / dragStartRect.Width, sy = (pp.Y - dragStartRect.Y) / dragStartRect.Height;
                        float sc = Math.Max(sx, sy);
                        float minW = 40 * Program.Dpi;
                        sc = Math.Max(sc, minW / dragStartRect.Width);
                        dragRect = new RectangleF(dragStartRect.X, dragStartRect.Y,
                            (float)Math.Round(dragStartRect.Width * sc), (float)Math.Round(dragStartRect.Height * sc));
                        InvalidateDrag(old);
                    }
                    break;
                case Mode.Ink:
                    InkMove(pp, pressure);
                    break;
                case Mode.LineEdit:
                    LineEditMove(pp);
                    break;
                case Mode.Shape:
                    {
                        var old = liveShape.Bounds;
                        bool shift = (ModifierKeys & Keys.Shift) != 0;
                        if (liveShape.Kind == ShapeKind.Line || liveShape.Kind == ShapeKind.Arrow)
                            liveShape.B = SnapAngle(liveShape.A, pp, shift);
                        else if (shift)
                        {
                            float dx = pp.X - liveShape.A.X, dy = pp.Y - liveShape.A.Y;
                            float s = Math.Max(Math.Abs(dx), Math.Abs(dy));
                            liveShape.B = new PointF(liveShape.A.X + Math.Sign(dx == 0 ? 1 : dx) * s, liveShape.A.Y + Math.Sign(dy == 0 ? 1 : dy) * s);
                        }
                        else liveShape.B = pp;
                        InvalidatePage(Geo.Union(old, liveShape.Bounds));
                    }
                    break;
                case Mode.Erase:
                    SetMouse(Point.Round(cp));
                    EraseAt(pp);
                    break;
                case Mode.Laser:
                    SetMouse(Point.Round(cp));
                    LaserAdd(pp);
                    break;
            }
        }

        void EndInput()
        {
            if (mode == Mode.None) return;
            var m = mode;
            mode = Mode.None;
            holdTimer.Stop();
            switch (m)
            {
                case Mode.Pan:
                    if (cursorBeforePan != null) Cursor = cursorBeforePan;
                    cursorBeforePan = null;
                    break;
                case Mode.Move:
                case Mode.Resize:
                    EndDrag();
                    break;
                case Mode.Ink:
                    InkEnd();
                    break;
                case Mode.LineEdit:
                    EndLineEdit();
                    break;
                case Mode.Shape:
                    {
                        var s = liveShape;
                        liveShape = null;
                        if (s != null)
                        {
                            if (Geo.Dist(s.A, s.B) * zoom > 3) { CommitItem(s, false); if (IsStraight(s)) SetActiveLine(s); }
                            else InvalidatePage(s.Bounds);
                        }
                    }
                    break;
                case Mode.Erase:
                    if (erasedAny)
                    {
                        undo.Push(eraseSnapshot);
                        redo.Clear();
                        Changed();
                    }
                    eraseSnapshot = null;
                    Invalidate(IndicatorRect());
                    break;
                case Mode.Laser:
                    laserCur = null;
                    break;
            }
            UpdateCursor();
        }

        // ---------------------------------------------------------------- ink + hold-to-perfect
        void InkMove(PointF raw, float pressure)
        {
            lastRaw = raw;
            if (Geo.Dist(raw, holdAnchor) * zoom > 5 * Program.Dpi)
            {
                holdAnchor = raw;
                holdSince = Environment.TickCount;
                holdTried = false;
            }
            if (snapped)
            {
                // After snapping to a line, keep following the pen so the end can be adjusted.
                if (liveShape != null && liveShape.Kind == ShapeKind.Line)
                {
                    var old = liveShape.Bounds;
                    liveShape.B = SnapAngle(liveShape.A, raw, (ModifierKeys & Keys.Shift) != 0);
                    InvalidatePage(Geo.Union(old, liveShape.Bounds));
                }
                return;
            }
            if (liveStroke == null) return;
            // Adaptive smoothing: steadies small wobbles, follows big fast moves closely.
            float d = Geo.Dist(raw, smoothP) * zoom;
            float a = Smooth + (1f - Smooth) * Math.Min(1f, d / (12f * Program.Dpi));
            smoothP = new PointF(smoothP.X + a * (raw.X - smoothP.X), smoothP.Y + a * (raw.Y - smoothP.Y));
            if (pressure >= 0) smoothPr = smoothPr < 0 ? pressure : smoothPr + 0.3f * (pressure - smoothPr);
            AddPoint(smoothP);
        }

        void AddPoint(PointF p)
        {
            var s = liveStroke;
            var last = s.Pts[s.Count - 1];
            if (!StrokeItem.Finite(p) || Geo.Dist(last, p) * zoom < (inputIsPen ? 1.0f : 1.2f)) return;
            s.Add(p, s.Highlight ? HlWidth : WidthFor(smoothPr));
            if (!s.Highlight)
            {
                // The piece before the tail is final now: paint it straight onto the page cache.
                var g = LiveGraphics();
                using (var pen = Geo.InkPen(s.Ink, 1f)) s.DrawSegment(g, pen, s.Count - 2, StrokeItem.TinyForZoom(zoom));
            }
            InvalidatePage(s.RecentBounds(3));
        }

        void InkEnd()
        {
            if (snapped)
            {
                var sh = liveShape;
                liveShape = null;
                snapped = false;
                if (sh != null) { CommitItem(sh, false); if (IsStraight(sh)) SetActiveLine(sh); }
                return;
            }
            var s = liveStroke;
            if (s == null) return;
            // Ease the smoothed line into the real pen tip instead of jumping to it.
            for (int i = 0; i < 8 && Geo.Dist(smoothP, lastRaw) * zoom > 0.5f; i++)
            {
                smoothP = Geo.Lerp(smoothP, lastRaw, Smooth);
                AddPoint(smoothP);
            }
            if (Geo.Dist(s.Pts[s.Count - 1], lastRaw) * zoom > 0.5f) AddPoint(lastRaw);
            liveStroke = null;
            bool drawn = false;
            if (!s.Highlight) { s.DrawTail(LiveGraphics()); drawn = true; }
            ReleaseLive();
            CommitItem(s, drawn);
        }

        void HoldTick(object sender, EventArgs e)
        {
            if (mode != Mode.Ink || snapped || holdTried || liveStroke == null) return;
            if (unchecked(Environment.TickCount - holdSince) < 550) return;
            holdTried = true;
            var shape = TrySnap(liveStroke);
            if (shape == null) return;
            var b = liveStroke.Bounds;
            liveStroke = null;
            liveShape = shape;
            snapped = true;
            RefreshCache(b);
            InvalidatePage(shape.Bounds);
        }

        // Turns a held, roughly straight stroke into a perfect line and a roughly round one into a perfect circle/ellipse.
        ShapeItem TrySnap(StrokeItem s)
        {
            var pts = s.Pts;
            int n = pts.Count;
            if (n < 4) return null;
            float px = Program.Dpi / zoom;   // one screen pixel in page units
            PointF a = pts[0], b = pts[n - 1];
            float chord = Geo.Dist(a, b);
            float path = 0;
            for (int i = 1; i < n; i++) path += Geo.Dist(pts[i - 1], pts[i]);

            if (chord >= 36 * px)
            {
                float maxDev = 0;
                foreach (var p in pts) maxDev = Math.Max(maxDev, Geo.SegDist(p, a, b));
                if (maxDev <= Math.Max(6 * px, chord * 0.06f) && path < chord * 1.25f)
                {
                    var end = SnapAngle(a, b, false);
                    return new ShapeItem(ShapeKind.Line, a, end, s.Ink, s.Highlight ? s.Ws[0] : PenWidth);
                }
            }
            if (s.Highlight) return null;

            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            float rx = (x1 - x0) / 2, ry = (y1 - y0) / 2;
            if (Math.Min(rx, ry) * 2 < 24 * px) return null;
            if (chord > 0.3f * (rx + ry)) return null;                    // must be (nearly) closed
            float cx = x0 + rx, cy = y0 + ry;
            double sum = 0, sum2 = 0;
            var bins = new bool[16];
            foreach (var p in pts)
            {
                double dx = (p.X - cx) / rx, dy = (p.Y - cy) / ry;
                double e = Math.Sqrt(dx * dx + dy * dy);
                sum += e; sum2 += e * e;
                int bin = (int)((Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * 16) % 16;
                bins[bin] = true;
            }
            double mean = sum / n, std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
            int covered = 0;
            foreach (var bb in bins) if (bb) covered++;
            if (covered < 14 || Math.Abs(mean - 1) > 0.15 || std > 0.1) return null;
            double perim = Math.PI * (3 * (rx + ry) - Math.Sqrt((3 * rx + ry) * (rx + 3 * ry)));
            if (path < perim * 0.8 || path > perim * 1.4) return null;
            if (Math.Abs(rx - ry) / Math.Max(rx, ry) < 0.12f) { float r = (rx + ry) / 2; rx = ry = r; }
            return new ShapeItem(ShapeKind.Ellipse, new PointF(cx - rx, cy - ry), new PointF(cx + rx, cy + ry), s.Ink, PenWidth);
        }

        // Lines snap to exactly horizontal/vertical when close; Shift snaps to 45 degree steps.
        static PointF SnapAngle(PointF a, PointF b, bool shift)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return b;
            double ang = Math.Atan2(dy, dx);
            double step = shift ? Math.PI / 4 : Math.PI / 2;
            double snapped = Math.Round(ang / step) * step;
            double tol = shift ? Math.PI : (3.0 * Math.PI / 180.0);
            if (Math.Abs(ang - snapped) <= tol)
                return new PointF(a.X + (float)(len * Math.Cos(snapped)), a.Y + (float)(len * Math.Sin(snapped)));
            return b;
        }

        // ---------------------------------------------------------------- eraser
        void EraseAt(PointF p)
        {
            float r = EraseClientR / zoom;
            PointF from = eraseLast ?? p;
            eraseLast = p;
            float d = Geo.Dist(from, p);
            int steps = Math.Max(1, (int)Math.Ceiling(d / (r * 0.5f)));
            RectangleF dirty = RectangleF.Empty;
            for (int k = 1; k <= steps; k++) dirty = Geo.Union(dirty, EraseOne(Geo.Lerp(from, p, k / (float)steps), r));
            if (!dirty.IsEmpty) { erasedAny = true; RefreshCache(dirty); }
        }

        RectangleF EraseOne(PointF p, float r)
        {
            RectangleF dirty = RectangleF.Empty;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var it = Items[i];
                if (!it.IsInk) continue;
                var b = it.Bounds;
                var bb = b;
                bb.Inflate(r, r);
                if (!bb.Contains(p)) continue;
                var s = it as StrokeItem;
                if (s != null)
                {
                    var pieces = s.EraseAt(p, r);
                    if (pieces == null) continue;
                    Items.RemoveAt(i);
                    Items.InsertRange(i, pieces);
                }
                else if (it.Hit(p, r)) Items.RemoveAt(i);
                else continue;
                dirty = Geo.Union(dirty, b);
            }
            return dirty;
        }

        // ---------------------------------------------------------------- select / move / resize pictures
        void BeginSelect(PointF cp, PointF pp)
        {
            if (selected != null && Items.Contains(selected) && HandleRect(ToClient(selected.Rect)).Contains(Point.Round(cp)))
            {
                BeginDrag(selected, true, pp);
                return;
            }
            var line = LineAt(pp);
            if (line != null)
            {
                if (selected != null) { InvalidatePage(selected.Bounds); selected = null; }
                BeginLineEdit(line, 2, pp);
                return;
            }
            var img = ImageAt(pp);
            if (img != null)
            {
                if (selected != null) InvalidatePage(selected.Bounds);
                selected = img;
                BeginDrag(img, false, pp);
                return;
            }
            if (selected != null) { InvalidatePage(selected.Bounds); selected = null; }
            StartPan(cp);
        }

        void BeginDrag(ImageItem img, bool resize, PointF pp)
        {
            dragSnapshot = new List<Item>(Items);
            dragIndex = Items.IndexOf(img);
            // Writing that sits on the picture travels with it.
            dragAttached = new List<Item>();
            var dirty = img.Bounds;
            for (int i = Items.Count - 1; i > dragIndex; i--)
            {
                var it = Items[i];
                if (!it.IsInk || !IsOnPicture(it.Bounds, img.Rect)) continue;
                dragAttached.Insert(0, it);
                dirty = Geo.Union(dirty, it.Bounds);
                Items.RemoveAt(i);
            }
            Items.RemoveAt(dragIndex);
            dragItem = img;
            dragRect = dragStartRect = img.Rect;
            dragOffset = new PointF(pp.X - img.Rect.X, pp.Y - img.Rect.Y);
            mode = resize ? Mode.Resize : Mode.Move;
            Cursor = resize ? Cursors.SizeNWSE : Cursors.SizeAll;
            RefreshCache(dirty);
        }

        static bool IsOnPicture(RectangleF b, RectangleF pic)
        {
            var c = new PointF(b.X + b.Width / 2, b.Y + b.Height / 2);
            return pic.Contains(c) && b.Width <= pic.Width * 1.25f + 20 && b.Height <= pic.Height * 1.25f + 20;
        }

        float DragScale { get { return dragStartRect.Width > 0 ? dragRect.Width / dragStartRect.Width : 1f; } }

        RectangleF DragMap(RectangleF r)
        {
            float s = DragScale;
            return new RectangleF(dragRect.X + (r.X - dragStartRect.X) * s, dragRect.Y + (r.Y - dragStartRect.Y) * s, r.Width * s, r.Height * s);
        }

        RectangleF DragArea()
        {
            var r = dragRect;
            r.Inflate(16, 16);
            if (dragAttached != null) foreach (var it in dragAttached) r = Geo.Union(r, DragMap(it.Bounds));
            return r;
        }

        void DrawDragged(Graphics g)
        {
            ImageItem.DrawAt(g, dragItem.Img, dragRect);
            if (dragAttached == null || dragAttached.Count == 0) return;
            var st = g.Save();
            float s = DragScale;
            g.TranslateTransform(dragRect.X, dragRect.Y);
            g.ScaleTransform(s, s);
            g.TranslateTransform(-dragStartRect.X, -dragStartRect.Y);
            foreach (var it in dragAttached) it.Draw(g);
            g.Restore(st);
        }

        void InvalidateDrag(RectangleF oldArea)
        {
            var c = ToClient(Geo.Union(oldArea, DragArea()));
            c.Inflate(Program.S(16), Program.S(16));
            Invalidate(c);
        }

        void EndDrag()
        {
            var img = dragItem;
            dragItem = null;
            if (img == null) return;
            var attached = dragAttached ?? new List<Item>();
            dragAttached = null;
            var oldArea = img.Bounds;
            foreach (var it in attached) oldArea = Geo.Union(oldArea, it.Bounds);
            if (dragRect == dragStartRect)
            {
                Items = dragSnapshot;
                selected = img;
                RefreshCache(oldArea);
                return;
            }
            undo.Push(dragSnapshot);
            redo.Clear();
            var moved = new ImageItem(img.Img, dragRect);
            Items.Insert(Math.Min(dragIndex, Items.Count), moved);
            var newArea = moved.Bounds;
            float s = DragScale;
            foreach (var it in attached)
            {
                var t = it.Transformed(dragStartRect.Location, dragRect.Location, s);
                Items.Add(t);
                newArea = Geo.Union(newArea, t.Bounds);
            }
            selected = moved;
            Grow(newArea);
            RefreshCache(Geo.Union(oldArea, newArea));
            Changed();
        }

        public void DeleteSelected()
        {
            if (mode != Mode.None) return;
            if (selected == null && activeLine != null)
            {
                var ln = activeLine;
                SetActiveLine(null);
                int li = Items.IndexOf(ln);
                if (li < 0) return;
                undo.Push(new List<Item>(Items));
                redo.Clear();
                Items.RemoveAt(li);
                RefreshCache(ln.Bounds);
                Changed();
                return;
            }
            if (selected == null) return;
            int i = Items.IndexOf(selected);
            var s = selected;
            selected = null;
            if (i < 0) return;
            undo.Push(new List<Item>(Items));
            redo.Clear();
            Items.RemoveAt(i);
            RefreshCache(s.Bounds);
            Changed();
        }

        public void ClearSelection()
        {
            SetActiveLine(null);
            if (selected == null) return;
            InvalidatePage(selected.Bounds);
            selected = null;
        }

        // ---------------------------------------------------------------- line end dots
        static bool IsStraight(Item it)
        {
            var s = it as ShapeItem;
            return s != null && (s.Kind == ShapeKind.Line || s.Kind == ShapeKind.Arrow);
        }

        // The line whose dots are on screen: the one being dragged, or the active one.
        ShapeItem HandleShape()
        {
            if (mode == Mode.LineEdit) return liveShape;
            if (mode == Mode.None && activeLine != null && Items.Contains(activeLine)) return activeLine;
            return null;
        }

        static float HandleR { get { return 7f * Program.Dpi; } }   // same 14px dot as the picture resize handle

        // Which end dot is under a client point: 0 = A, 1 = B, -1 = none.
        int LineHandleAt(PointF cp)
        {
            if (mode != Mode.None) return -1;
            var s = HandleShape();
            if (s == null) return -1;
            float da = Geo.Dist(cp, new PointF(s.A.X * zoom, s.A.Y * zoom));
            float db = Geo.Dist(cp, new PointF(s.B.X * zoom, s.B.Y * zoom));
            if (Math.Min(da, db) > HandleR + 5f * Program.Dpi) return -1;   // 24px target (WCAG 2.5.8)
            return da <= db ? 0 : 1;
        }

        // Topmost straight line under a page point (a picture on top hides lines under it).
        ShapeItem LineAt(PointF pp)
        {
            float r = 6f * Program.Dpi / zoom;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var it = Items[i];
                if (IsStraight(it) && it.Hit(pp, r)) return (ShapeItem)it;
                var im = it as ImageItem;
                if (im != null && im.Rect.Contains(pp)) return null;
            }
            return null;
        }

        void InvalidateHandles(ShapeItem s)
        {
            if (s == null) return;
            int r = (int)Math.Ceiling(HandleR) + Program.S(4);
            foreach (var p in new[] { s.A, s.B })
            {
                int x = (int)(p.X * zoom), y = (int)(p.Y * zoom);
                Invalidate(new Rectangle(x - r, y - r, 2 * r, 2 * r));
            }
        }

        void SetActiveLine(ShapeItem s)
        {
            if (s == activeLine) return;
            InvalidateHandles(activeLine);
            activeLine = s;
            InvalidateHandles(s);
        }

        void DrawLineHandles(Graphics g)
        {
            var s = HandleShape();
            if (s == null) return;
            float r = HandleR;
            using (var pen = new Pen(Theme.Accent, Math.Max(2f, 2f * Program.Dpi)))
                foreach (var p in new[] { s.A, s.B })
                {
                    float x = p.X * zoom, y = p.Y * zoom;
                    g.FillEllipse(Brushes.White, x - r, y - r, 2 * r, 2 * r);
                    g.DrawEllipse(pen, x - r, y - r, 2 * r, 2 * r);
                }
        }

        bool UpdateHandleCursor(Point p)
        {
            if (!spacePan && LineHandleAt(p) >= 0)
            {
                handleHover = true;
                if (Cursor != Cursors.SizeAll) Cursor = Cursors.SizeAll;
                return true;
            }
            if (handleHover) { handleHover = false; UpdateCursor(); }
            return false;
        }

        // Committed items never change (undo keeps the old list): lift the line out, drag a copy, put the copy back.
        void BeginLineEdit(ShapeItem s, int grab, PointF pp)
        {
            int i = Items.IndexOf(s);
            if (i < 0) return;
            SetActiveLine(s);
            lineSnapshot = new List<Item>(Items);
            lineIndex = i;
            lineOrig = s;
            lineGrab = grab;
            lineGrabStart = pp;
            lineMoved = false;
            Items.RemoveAt(i);
            liveShape = new ShapeItem(s.Kind, s.A, s.B, s.Ink, s.W);
            mode = Mode.LineEdit;
            Cursor = Cursors.SizeAll;
            RefreshCache(s.Bounds);
        }

        void LineEditMove(PointF pp)
        {
            var s = liveShape;
            var o = lineOrig;
            if (s == null || o == null) return;
            float dx = pp.X - lineGrabStart.X, dy = pp.Y - lineGrabStart.Y;
            if (!lineMoved && (float)Math.Sqrt(dx * dx + dy * dy) * zoom < 2f) return;   // a tap is not a drag
            lineMoved = true;
            var old = s.Bounds;
            InvalidateHandles(s);
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            if (lineGrab == 0) s.A = SnapAngle(o.B, new PointF(o.A.X + dx, o.A.Y + dy), shift);
            else if (lineGrab == 1) s.B = SnapAngle(o.A, new PointF(o.B.X + dx, o.B.Y + dy), shift);
            else
            {
                s.A = new PointF(o.A.X + dx, o.A.Y + dy);
                s.B = new PointF(o.B.X + dx, o.B.Y + dy);
            }
            InvalidatePage(Geo.Union(old, s.Bounds));
            InvalidateHandles(s);
        }

        void EndLineEdit()
        {
            var s = liveShape;
            var o = lineOrig;
            liveShape = null;
            lineOrig = null;
            if (s == null || o == null) return;
            InvalidateHandles(s);
            bool changed = lineMoved && Geo.Dist(s.A, s.B) * zoom > 3 && (s.A != o.A || s.B != o.B);
            if (!changed)
            {
                Items = lineSnapshot;
                activeLine = o;
                RefreshCache(Geo.Union(o.Bounds, s.Bounds));
            }
            else
            {
                undo.Push(lineSnapshot);
                redo.Clear();
                Items.Insert(Math.Min(lineIndex, Items.Count), s);
                activeLine = s;
                Grow(s.Bounds);
                RefreshCache(Geo.Union(o.Bounds, s.Bounds));
                Changed();
            }
            lineSnapshot = null;
            InvalidateHandles(activeLine);
        }

        void StartPan(PointF cp)
        {
            mode = Mode.Pan;
            panStartScreen = PointToScreen(Point.Round(cp));
            panStartScroll = ScrollPos;
            cursorBeforePan = Cursor;
            Cursor = Cursors.SizeAll;
        }

        // ---------------------------------------------------------------- text
        void BeginText(PointF pp)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var t = Items[i] as TextItem;
                if (t != null && t.Hit(pp, 3 / zoom))
                {
                    editOriginal = t;
                    editIndex = i;
                    Items.RemoveAt(i);
                    RefreshCache(t.Bounds);
                    OpenTextBox(t.Pos, t.SizePx, t.Ink, t.Text);
                    return;
                }
            }
            float size = TextSize;
            OpenTextBox(new PointF(pp.X, pp.Y - size * 0.62f), size, PenColor, "");
        }

        void OpenTextBox(PointF pos, float sizePx, Color c, string text)
        {
            textPos = pos;
            textPx = sizePx;
            textColor = c;
            var tb = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = false,
                WordWrap = false,
                ScrollBars = ScrollBars.None,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = c,
                Font = TextItem.MakeFont(sizePx * zoom),
                Text = text.Replace("\n", "\r\n"),
                Location = new Point((int)Math.Round(pos.X * zoom), (int)Math.Round(pos.Y * zoom))
            };
            tb.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; CommitText(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CommitText(true); }
            };
            tb.TextChanged += delegate { SizeTextBox(); };
            tb.LostFocus += delegate { CommitText(); };
            textBox = tb;
            Controls.Add(tb);
            PenApi.ZeroTextMargins(tb);
            SizeTextBox();
            tb.Focus();
            tb.SelectionStart = tb.TextLength;
        }

        void SizeTextBox()
        {
            var tb = textBox;
            if (tb == null) return;
            var old = tb.Bounds;
            string[] lines = tb.Text.Replace("\r", "").Split('\n');
            int w = 0;
            foreach (var l in lines) w = Math.Max(w, TextRenderer.MeasureText(l + "WW", tb.Font).Width);
            int lineH = TextRenderer.MeasureText("Ag", tb.Font).Height;
            tb.Size = new Size(Math.Max(Program.S(120), w), lineH * lines.Length + Program.S(4));
            var r = Rectangle.Union(old, tb.Bounds);
            r.Inflate(Program.S(10), Program.S(10));
            Invalidate(r);
        }

        public void CommitText(bool cancel = false)
        {
            if (textBox == null || committingText) return;
            committingText = true;
            var tb = textBox;
            textBox = null;
            string t = tb.Text.Replace("\r", "").TrimEnd();
            var frame = tb.Bounds;
            frame.Inflate(Program.S(10), Program.S(10));
            Invalidate(frame);

            if (editOriginal != null)
            {
                var orig = editOriginal;
                editOriginal = null;
                int idx = Math.Min(editIndex, Items.Count);
                bool same = t == orig.Text && textColor.ToArgb() == orig.Ink.ToArgb() && Math.Abs(textPx - orig.SizePx) < 0.01f;
                if (cancel || same)
                {
                    Items.Insert(idx, orig);
                    RefreshCache(orig.Bounds);
                }
                else
                {
                    var before = new List<Item>(Items);
                    before.Insert(idx, orig);
                    undo.Push(before);
                    redo.Clear();
                    if (t.Length > 0)
                    {
                        var ni = new TextItem(t, orig.Pos, textPx, textColor);
                        Items.Insert(idx, ni);
                        RefreshCache(Geo.Union(orig.Bounds, ni.Bounds));
                    }
                    Changed();
                }
            }
            else if (!cancel && t.Length > 0)
                CommitItem(new TextItem(t, textPos, textPx, textColor), false);

            Controls.Remove(tb);
            tb.Dispose();
            committingText = false;
            if (!IsDisposed) Focus();
        }

        // ---------------------------------------------------------------- history
        void CommitItem(Item it, bool alreadyDrawn)
        {
            undo.Push(new List<Item>(Items));
            redo.Clear();
            Items.Add(it);
            if (!alreadyDrawn && cache != null)
            {
                ReleaseLive();
                using (var g = Graphics.FromImage(cache))
                {
                    PrepCache(g);
                    try { it.Draw(g); } catch { DropCache(); }   // worst case: repaint the page from scratch
                }
            }
            InvalidatePage(it.Bounds);
            Grow(it.Bounds);
            Changed();
        }

        void Changed()
        {
            Dirty = true;
            if (HistoryChanged != null) HistoryChanged(this, EventArgs.Empty);
        }

        void ReplaceAll(List<Item> items)
        {
            Items = items;
            if (selected != null && !Items.Contains(selected)) selected = null;
            if (activeLine != null && !Items.Contains(activeLine)) activeLine = null;
            DropCache();
            Invalidate();
            Changed();
        }

        public void Undo()
        {
            CommitText();
            if (mode != Mode.None || undo.Count == 0) return;
            redo.Push(Items);
            ReplaceAll(undo.Pop());
        }

        public void Redo()
        {
            CommitText();
            if (mode != Mode.None || redo.Count == 0) return;
            undo.Push(Items);
            ReplaceAll(redo.Pop());
        }

        // Wipes the writing but keeps the pictures (undo brings it back).
        public void ClearInk()
        {
            CommitText();
            if (mode != Mode.None || !HasInk) return;
            undo.Push(Items);
            redo.Clear();
            ReplaceAll(Items.FindAll(i => !i.IsInk));
        }

        // ---------------------------------------------------------------- pictures & page
        public void AddImage(Bitmap img, bool record)
        {
            if (img == null) return;
            CommitText();
            var h = Host;
            float viewW = ViewWidth() / zoom;
            float viewH = (h != null ? h.ClientSize.Height : Height) / zoom;
            float margin = Program.S(48);
            float maxW = Math.Max(Program.S(200), viewW - 2 * margin);
            float w = img.Width, ht = img.Height;
            if (w > maxW) { ht = ht * maxW / w; w = maxW; }

            var content = ContentBounds();
            float top = content.IsEmpty ? Program.S(36) / zoom : content.Bottom + Program.S(48);
            var vis = PageRectOf(VisibleRect());
            float left = vis.Left + (Math.Min(vis.Width, viewW) - w) / 2;
            left = Math.Max(Program.S(16), left);

            var item = new ImageItem(img, new RectangleF((float)Math.Round(left), (float)Math.Round(top), (float)Math.Round(w), (float)Math.Round(ht)));
            if (record) { undo.Push(new List<Item>(Items)); redo.Clear(); }
            Items.Add(item);
            pageH = Math.Max(pageH, item.Rect.Bottom + viewH * 0.7f);
            ApplySize();
            RefreshCache(item.Bounds);
            if (record)
            {
                ScrollIntoView(item.Rect);
                Changed();
            }
            else if (HistoryChanged != null) HistoryChanged(this, EventArgs.Empty);
        }

        void ScrollIntoView(RectangleF r)
        {
            var v = VisibleRect();
            var c = ToClient(r);
            if (c.Top < v.Top || c.Bottom > v.Bottom)
            {
                var h = Host;
                if (h != null) h.PerformLayout();
                ScrollPos = new Point(ScrollPos.X, Math.Max(0, c.Top - Program.S(40)));
            }
        }

        public void ExtendPage()
        {
            CommitText();
            var h = Host;
            float add = (h != null ? h.ClientSize.Height : 600) * 0.75f / zoom;
            pageH += add;
            ApplySize();
            if (h != null) h.PerformLayout();
            var sp = ScrollPos;
            ScrollPos = new Point(sp.X, sp.Y + (int)(add * zoom));
        }

        public Bitmap Export()
        {
            CommitText();
            var c = ContentBounds();
            RectangleF r;
            if (c.IsEmpty) r = PageRectOf(VisibleRect());
            else
            {
                r = c;
                r.Inflate(Program.S(24), Program.S(24));
                r.Intersect(new RectangleF(0, 0, Math.Max(pageW, c.Right + 30), Math.Max(pageH, c.Bottom + 30)));
            }
            int w = Math.Max(1, Math.Min(20000, (int)Math.Ceiling(r.Width)));
            int ht = Math.Max(1, Math.Min(20000, (int)Math.Ceiling(r.Height)));
            var bmp = new Bitmap(w, ht, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.TranslateTransform(-(float)Math.Floor(r.X), -(float)Math.Floor(r.Y));
                PaintPage(g, new RectangleF((float)Math.Floor(r.X), (float)Math.Floor(r.Y), w, ht));
            }
            return bmp;
        }

        // ---------------------------------------------------------------- test hooks (used by --selftest)
        public void TestStroke(PointF[] clientPts, bool holdAtEnd, bool pressure = false)
        {
            Func<int, float> pr = i => pressure ? 0.15f + 0.8f * (float)Math.Abs(Math.Sin(i * 0.05)) : -1f;
            BeginInput(clientPts[0], pr(0), true, false);
            for (int i = 1; i < clientPts.Length; i++) MoveInput(clientPts[i], pr(i));
            if (holdAtEnd && mode == Mode.Ink)
            {
                holdSince = Environment.TickCount - 1000;
                HoldTick(this, EventArgs.Empty);
            }
            EndInput();
        }

        public ShapeItem ActiveLine { get { return HandleShape(); } }

        public void TestErase(PointF[] clientPts)
        {
            var t = CurrentTool;
            CurrentTool = Tool.Eraser;
            BeginInput(clientPts[0], -1f, true, false);
            for (int i = 1; i < clientPts.Length; i++) MoveInput(clientPts[i], -1f);
            EndInput();
            CurrentTool = t;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DropCache();
                holdTimer.Dispose();
                laserTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
