// Everything that can live on a board. Coordinates are "page units" (= screen pixels at 100% zoom).
// Items are treated as immutable once committed, so undo can keep cheap list snapshots.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MathSnip
{
    static class Geo
    {
        public static float Dist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static float SegDist(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            if (len2 < 1e-6f) return Dist(p, a);
            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0f, Math.Min(1f, t));
            return Dist(p, new PointF(a.X + t * dx, a.Y + t * dy));
        }

        public static PointF Lerp(PointF a, PointF b, float t) { return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
        public static PointF Mid(PointF a, PointF b) { return new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f); }

        public static RectangleF Norm(PointF a, PointF b)
        {
            return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        public static RectangleF Box(PointF a, PointF b, float pad)
        {
            var r = Norm(a, b);
            r.Inflate(pad + 2, pad + 2);
            return r;
        }

        public static RectangleF Union(RectangleF a, RectangleF b)
        {
            if (a.IsEmpty) return b;
            if (b.IsEmpty) return a;
            return RectangleF.Union(a, b);
        }

        public static float ScaleOf(Graphics g)
        {
            using (var m = g.Transform) return Math.Abs(m.Elements[0]);
        }

        public static Pen InkPen(Color c, float w)
        {
            var p = new Pen(c, w);
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }
    }

    abstract class Item
    {
        public abstract void Draw(Graphics g);
        public abstract bool Hit(PointF p, float r);
        public abstract RectangleF Bounds { get; }
        public virtual bool IsInk { get { return true; } }

        // Copy of this item moved/scaled: p' = to + (p - from) * s. Used to carry annotations along with a picture.
        public virtual Item Transformed(PointF from, PointF to, float s) { return this; }

        protected static PointF Map(PointF p, PointF from, PointF to, float s)
        {
            return new PointF(to.X + (p.X - from.X) * s, to.Y + (p.Y - from.Y) * s);
        }
    }

    // Freehand stroke drawn as a smooth curve (midpoint quadratic Beziers), width per point for pressure.
    class StrokeItem : Item
    {
        public readonly List<PointF> Pts = new List<PointF>();
        public readonly List<float> Ws = new List<float>();
        public readonly Color Ink;
        public readonly bool Highlight;
        RectangleF bounds = RectangleF.Empty;

        public StrokeItem(Color ink, bool highlight) { Ink = ink; Highlight = highlight; }

        public int Count { get { return Pts.Count; } }

        public void Add(PointF p, float w)
        {
            Pts.Add(p);
            Ws.Add(w);
            var b = new RectangleF(p.X - w / 2 - 2, p.Y - w / 2 - 2, w + 4, w + 4);
            bounds = Geo.Union(bounds, b);
        }

        static void Seg(Graphics g, Pen pen, PointF a, PointF b)
        {
            if (Geo.Dist(a, b) < 0.01f) return;
            g.DrawLine(pen, a, b);
        }

        // Piece k: 0 = start, n-1 = tail, otherwise the curve through point k.
        public void DrawSegment(Graphics g, Pen pen, int k)
        {
            int n = Pts.Count;
            if (n < 2 || k < 0 || k >= n) return;
            pen.Width = Ws[k];
            if (k == 0) { Seg(g, pen, Pts[0], Geo.Mid(Pts[0], Pts[1])); return; }
            if (k == n - 1) { Seg(g, pen, Geo.Mid(Pts[n - 2], Pts[n - 1]), Pts[n - 1]); return; }
            PointF s = Geo.Mid(Pts[k - 1], Pts[k]), c = Pts[k], e = Geo.Mid(Pts[k], Pts[k + 1]);
            g.DrawBezier(pen, s, Geo.Lerp(s, c, 2f / 3f), Geo.Lerp(e, c, 2f / 3f), e);
        }

        void Dot(Graphics g)
        {
            float w = Math.Max(1f, Ws[0]);
            using (var b = new SolidBrush(Ink)) g.FillEllipse(b, Pts[0].X - w / 2, Pts[0].Y - w / 2, w, w);
        }

        public void DrawFinalized(Graphics g)
        {
            if (Pts.Count < 2) return;
            using (var pen = Geo.InkPen(Ink, 1f))
                for (int k = 0; k < Pts.Count - 1; k++) DrawSegment(g, pen, k);
        }

        public void DrawTail(Graphics g)
        {
            if (Pts.Count == 0) return;
            if (Pts.Count == 1) { Dot(g); return; }
            using (var pen = Geo.InkPen(Ink, 1f)) DrawSegment(g, pen, Pts.Count - 1);
        }

        GraphicsPath BuildPath()
        {
            var path = new GraphicsPath();
            int n = Pts.Count;
            path.AddLine(Pts[0], Geo.Mid(Pts[0], Pts[1]));
            for (int k = 1; k < n - 1; k++)
            {
                PointF s = Geo.Mid(Pts[k - 1], Pts[k]), c = Pts[k], e = Geo.Mid(Pts[k], Pts[k + 1]);
                path.AddBezier(s, Geo.Lerp(s, c, 2f / 3f), Geo.Lerp(e, c, 2f / 3f), e);
            }
            path.AddLine(Geo.Mid(Pts[n - 2], Pts[n - 1]), Pts[n - 1]);
            return path;
        }

        public override void Draw(Graphics g)
        {
            if (Pts.Count == 0) return;
            if (Pts.Count == 1) { Dot(g); return; }
            if (Highlight)
            {
                // One path, so the see-through ink doesn't get darker where pieces overlap.
                using (var path = BuildPath())
                using (var pen = Geo.InkPen(Ink, Ws[0]))
                    g.DrawPath(pen, path);
                return;
            }
            using (var pen = Geo.InkPen(Ink, 1f))
                for (int k = 0; k < Pts.Count; k++) DrawSegment(g, pen, k);
        }

        public override bool Hit(PointF q, float r)
        {
            if (Pts.Count == 1) return Geo.Dist(q, Pts[0]) <= r + Ws[0] / 2;
            for (int i = 1; i < Pts.Count; i++)
                if (Geo.SegDist(q, Pts[i - 1], Pts[i]) <= r + Ws[i] / 2) return true;
            return false;
        }

        public RectangleF RecentBounds(int k)
        {
            var r = RectangleF.Empty;
            for (int i = Math.Max(0, Pts.Count - k); i < Pts.Count; i++)
            {
                float w = Ws[i];
                r = Geo.Union(r, new RectangleF(Pts[i].X - w / 2 - 2, Pts[i].Y - w / 2 - 2, w + 4, w + 4));
            }
            return r;
        }

        public override RectangleF Bounds { get { return bounds; } }

        public override Item Transformed(PointF from, PointF to, float s)
        {
            var c = new StrokeItem(Ink, Highlight);
            for (int i = 0; i < Pts.Count; i++) c.Add(Map(Pts[i], from, to, s), Ws[i] * s);
            return c;
        }

        // Rub out the part of the stroke under the eraser. Returns null when nothing was touched,
        // otherwise the surviving pieces (possibly none).
        public List<StrokeItem> EraseAt(PointF p, float r)
        {
            bool hit = false;
            var pieces = new List<StrokeItem>();
            StrokeItem cur = null;
            for (int i = 0; i < Pts.Count; i++)
            {
                bool inside = Geo.Dist(Pts[i], p) <= r + (Highlight ? Ws[i] * 0.3f : 0);
                bool crosses = !inside && cur != null && i > 0 && Geo.SegDist(p, Pts[i - 1], Pts[i]) <= r;
                if (inside || crosses)
                {
                    hit = true;
                    if (cur != null) pieces.Add(cur);
                    cur = null;
                    if (inside) continue;
                }
                if (cur == null) cur = new StrokeItem(Ink, Highlight);
                cur.Add(Pts[i], Ws[i]);
            }
            if (!hit) return null;
            if (cur != null) pieces.Add(cur);
            if (Pts.Count > 1) pieces.RemoveAll(s => s.Count < 2);
            return pieces;
        }
    }

    enum ShapeKind { Line, Arrow, Rect, Ellipse }

    class ShapeItem : Item
    {
        public readonly ShapeKind Kind;
        public PointF A, B;            // only changed while being drawn
        public readonly Color Ink;
        public readonly float W;

        public ShapeItem(ShapeKind kind, PointF a, PointF b, Color ink, float w) { Kind = kind; A = a; B = b; Ink = ink; W = w; }

        float HeadLen
        {
            get
            {
                float L = W * 2.6f + 10f * Program.Dpi;
                float len = Geo.Dist(A, B);
                return Math.Min(L, len * 0.6f);
            }
        }

        public override void Draw(Graphics g)
        {
            using (var pen = Geo.InkPen(Ink, W))
            {
                switch (Kind)
                {
                    case ShapeKind.Line:
                        if (Geo.Dist(A, B) < 0.01f) return;
                        g.DrawLine(pen, A, B);
                        break;
                    case ShapeKind.Arrow:
                        {
                            float len = Geo.Dist(A, B);
                            if (len < 0.5f) return;
                            float ux = (B.X - A.X) / len, uy = (B.Y - A.Y) / len;
                            float L = HeadLen, half = L * 0.5f;
                            var baseP = new PointF(B.X - ux * L, B.Y - uy * L);
                            g.DrawLine(pen, A, new PointF(baseP.X + ux * W * 0.5f, baseP.Y + uy * W * 0.5f));
                            var tri = new[] { B, new PointF(baseP.X - uy * half, baseP.Y + ux * half), new PointF(baseP.X + uy * half, baseP.Y - ux * half) };
                            using (var br = new SolidBrush(Ink)) g.FillPolygon(br, tri);
                            using (var edge = Geo.InkPen(Ink, Math.Max(1f, W * 0.6f))) g.DrawPolygon(edge, tri);
                        }
                        break;
                    case ShapeKind.Rect:
                        {
                            var r = Geo.Norm(A, B);
                            if (r.Width < 0.5f && r.Height < 0.5f) return;
                            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                        }
                        break;
                    case ShapeKind.Ellipse:
                        {
                            var r = Geo.Norm(A, B);
                            if (r.Width < 0.5f || r.Height < 0.5f) return;
                            g.DrawEllipse(pen, r);
                        }
                        break;
                }
            }
        }

        public override bool Hit(PointF p, float r)
        {
            float rr = r + W / 2;
            switch (Kind)
            {
                case ShapeKind.Line:
                case ShapeKind.Arrow:
                    return Geo.SegDist(p, A, B) <= rr;
                case ShapeKind.Rect:
                    {
                        var q = Geo.Norm(A, B);
                        PointF a = new PointF(q.Left, q.Top), b = new PointF(q.Right, q.Top),
                               c = new PointF(q.Right, q.Bottom), d = new PointF(q.Left, q.Bottom);
                        return Geo.SegDist(p, a, b) <= rr || Geo.SegDist(p, b, c) <= rr ||
                               Geo.SegDist(p, c, d) <= rr || Geo.SegDist(p, d, a) <= rr;
                    }
                default:
                    {
                        var q = Geo.Norm(A, B);
                        float cx = q.X + q.Width / 2, cy = q.Y + q.Height / 2, ax = q.Width / 2, ay = q.Height / 2;
                        PointF prev = new PointF(cx + ax, cy);
                        for (int i = 1; i <= 72; i++)
                        {
                            double t = i * Math.PI * 2 / 72;
                            var cur = new PointF(cx + ax * (float)Math.Cos(t), cy + ay * (float)Math.Sin(t));
                            if (Geo.SegDist(p, prev, cur) <= rr) return true;
                            prev = cur;
                        }
                        return false;
                    }
            }
        }

        public override RectangleF Bounds
        {
            get { return Geo.Box(A, B, W + (Kind == ShapeKind.Arrow ? HeadLen : 0)); }
        }

        public override Item Transformed(PointF from, PointF to, float s)
        {
            return new ShapeItem(Kind, Map(A, from, to, s), Map(B, from, to, s), Ink, W * s);
        }
    }

    class TextItem : Item
    {
        static readonly Bitmap measureBmp = new Bitmap(1, 1);
        static readonly Graphics measure = Graphics.FromImage(measureBmp);

        public readonly string Text;
        public readonly PointF Pos;
        public readonly float SizePx;
        public readonly Color Ink;
        public readonly Font Font;
        readonly SizeF size;

        public TextItem(string text, PointF pos, float sizePx, Color ink)
        {
            Text = text; Pos = pos; SizePx = sizePx; Ink = ink;
            Font = MakeFont(sizePx);
            lock (measure) size = measure.MeasureString(text, Font, PointF.Empty, StringFormat.GenericTypographic);
        }

        public static Font MakeFont(float px) { return new Font("Segoe UI", Math.Max(1f, px), GraphicsUnit.Pixel); }

        public override void Draw(Graphics g)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (var b = new SolidBrush(Ink)) g.DrawString(Text, Font, b, Pos, StringFormat.GenericTypographic);
        }

        public override bool Hit(PointF p, float r)
        {
            var b = new RectangleF(Pos, size);
            b.Inflate(r, r);
            return b.Contains(p);
        }

        public override RectangleF Bounds
        {
            get { var b = new RectangleF(Pos, size); b.Inflate(4, 4); return b; }
        }

        public override Item Transformed(PointF from, PointF to, float s)
        {
            return new TextItem(Text, Map(Pos, from, to, s), SizePx * s, Ink);
        }
    }

    class ImageItem : Item
    {
        public readonly Bitmap Img;
        public readonly RectangleF Rect;

        public ImageItem(Bitmap img, RectangleF rect) { Img = img; Rect = rect; }

        public override bool IsInk { get { return false; } }
        public override void Draw(Graphics g) { DrawAt(g, Img, Rect); }

        public static void DrawAt(Graphics g, Bitmap img, RectangleF r)
        {
            float scale = Geo.ScaleOf(g);
            float px = 1f / Math.Max(0.01f, scale);
            var oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            for (int i = 3; i >= 1; i--)
                using (var b = new SolidBrush(Color.FromArgb(10 + 6 * (3 - i), 15, 23, 42)))
                    g.FillRectangle(b, r.X - i * px, r.Y - i * px + px, r.Width + 2 * i * px, r.Height + 2 * i * px);

            bool exact = Math.Abs(r.Width * scale - img.Width) < 0.5f && Math.Abs(r.Height * scale - img.Height) < 0.5f;
            g.InterpolationMode = exact ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var ia = new System.Drawing.Imaging.ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);   // no grey fringe at the edges when scaled
                g.DrawImage(img, Rectangle.Round(r), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
            }
            g.PixelOffsetMode = PixelOffsetMode.Default;
            using (var pen = new Pen(Color.FromArgb(203, 208, 216), px)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            g.SmoothingMode = oldSmooth;
        }

        public override bool Hit(PointF p, float r) { return Rect.Contains(p); }

        public override RectangleF Bounds
        {
            get { var b = Rect; b.Inflate(16, 16); return b; }   // room for the shadow at any zoom
        }
    }
}
