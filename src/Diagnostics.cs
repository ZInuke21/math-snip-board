// `--gdi-probe out.txt`: stress-tests Windows' GDI+ with the kind of microscopic curves a pen tablet produces.
// Part 1 calls DrawBezier directly (how v2.1.0 drew ink) and counts failures.
// Part 2 draws the same input through StrokeItem (the real ink code) and must never throw.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace MathSnip
{
    static class GdiProbe
    {
        static readonly float[] Scales = { 1f, 1.25f, 0.5f, 2f, 3.7f };
        static readonly float[] Spreads = { 0f, 0.0001f, 0.001f, 0.01f, 0.05f, 0.2f, 0.5f, 1f, 3f };
        const int PerCase = 3000;

        static string F(float v) { return v.ToString("0.#####", CultureInfo.InvariantCulture); }

        public static int Run(string outFile)
        {
            var sb = new StringBuilder();
            int rawFails = 0, inkErrors = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (var bmp = new Bitmap(1400, 1400, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var pen = Geo.InkPen(Color.Black, 2f))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                sb.AppendLine("== Part 1: raw DrawBezier (v2.1.0 behaviour)");
                foreach (var scale in Scales)
                    foreach (var spread in Spreads)
                    {
                        var rnd = new Random(1000 + (int)(scale * 100) + (int)(spread * 100000));
                        Prep(g, scale);
                        int fails = 0;
                        string first = null;
                        for (int i = 0; i < PerCase; i++)
                        {
                            PointF a, b, c;
                            Jitter(rnd, spread, out a, out b, out c);
                            PointF s = Geo.Mid(a, b), e = Geo.Mid(b, c);
                            pen.Width = 0.5f + (float)rnd.NextDouble() * 9f;
                            try { g.DrawBezier(pen, s, Geo.Lerp(s, b, 2f / 3f), Geo.Lerp(e, b, 2f / 3f), e); }
                            catch (Exception ex)
                            {
                                fails++;
                                if (first == null)
                                    first = ex.GetType().Name + " width=" + F(pen.Width) + " s=(" + F(s.X) + "," + F(s.Y) + ") c=(" + F(b.X) + "," + F(b.Y) + ") e=(" + F(e.X) + "," + F(e.Y) + ")";
                            }
                        }
                        rawFails += fails;
                        sb.AppendLine("scale " + F(scale) + "  spread " + F(spread) + "px:  " + fails + "/" + PerCase + " failed" + (first != null ? "   first: " + first : ""));
                    }

                sb.AppendLine();
                sb.AppendLine("== Part 2: same input through StrokeItem (the fixed ink code)");
                StrokeItem.Fallbacks = 0;
                foreach (var scale in Scales)
                    foreach (var spread in Spreads)
                    {
                        var rnd = new Random(2000 + (int)(scale * 100) + (int)(spread * 100000));
                        Prep(g, scale);
                        var st = new StrokeItem(Color.Black, false);
                        var p = new PointF(200 + (float)rnd.NextDouble() * 300, 200 + (float)rnd.NextDouble() * 300);
                        for (int i = 0; i < PerCase; i++)
                        {
                            p = new PointF(p.X + (float)(rnd.NextDouble() - 0.5) * 2 * spread, p.Y + (float)(rnd.NextDouble() - 0.5) * 2 * spread);
                            st.Add(p, 0.5f + (float)rnd.NextDouble() * 9f);
                            try
                            {
                                if (st.Count >= 2)
                                    using (var ip = Geo.InkPen(Color.Black, 1f)) st.DrawSegment(g, ip, st.Count - 2, StrokeItem.TinyFor(g));
                            }
                            catch (Exception ex) { inkErrors++; if (inkErrors < 5) sb.AppendLine("  live error: " + ex.GetType().Name); }
                        }
                        try { st.Draw(g); }
                        catch (Exception ex) { inkErrors++; if (inkErrors < 5) sb.AppendLine("  draw error: " + ex.GetType().Name); }
                        try { new StrokeItem(Color.FromArgb(105, 255, 221, 0), true).Draw(g); } catch { }
                        var hl = new StrokeItem(Color.FromArgb(105, 255, 221, 0), true);
                        foreach (var q in st.Pts) hl.Add(q, 22f);
                        try { hl.Draw(g); }
                        catch (Exception ex) { inkErrors++; if (inkErrors < 5) sb.AppendLine("  highlight error: " + ex.GetType().Name); }
                    }
                sb.AppendLine("errors reaching the app: " + inkErrors + "   (internal fallbacks used: " + StrokeItem.Fallbacks + ")");
            }
            sb.AppendLine();
            sb.AppendLine("RAW_FAILS=" + rawFails);
            sb.AppendLine("INK_ERRORS=" + inkErrors);
            sb.AppendLine("time " + watch.ElapsedMilliseconds + " ms");
            File.WriteAllText(outFile, sb.ToString());
            return inkErrors == 0 ? 0 : 3;
        }

        static void Prep(Graphics g, float scale)
        {
            g.ResetTransform();
            g.TranslateTransform(-37.3f, -11.9f);   // like the board's cache offset
            g.ScaleTransform(scale, scale);         // like zoom
        }

        // Three consecutive pen samples, `spread` pixels apart (with sub-pixel noise), like a fast digitizer.
        static void Jitter(Random rnd, float spread, out PointF a, out PointF b, out PointF c)
        {
            a = new PointF(100 + (float)rnd.NextDouble() * 900, 100 + (float)rnd.NextDouble() * 900);
            b = new PointF(a.X + (float)(rnd.NextDouble() - 0.5) * 2 * spread, a.Y + (float)(rnd.NextDouble() - 0.5) * 2 * spread);
            c = new PointF(b.X + (float)(rnd.NextDouble() - 0.5) * 2 * spread, b.Y + (float)(rnd.NextDouble() - 0.5) * 2 * spread);
        }
    }
}
