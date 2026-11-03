// Minimal PDF writer (one page, JPEG image) + the --selftest entry point.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MathSnip
{
    static class Pdf
    {
        public static ImageCodecInfo JpegCodec()
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
            throw new InvalidOperationException("No JPEG encoder available");
        }

        public static void Save(Bitmap bmp, string path)
        {
            byte[] jpg;
            using (var rgb = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(rgb)) { g.Clear(Color.White); g.DrawImageUnscaled(bmp, 0, 0); }
                using (var ms = new MemoryStream())
                using (var ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                    rgb.Save(ms, JpegCodec(), ep);
                    jpg = ms.ToArray();
                }
            }

            // 96 dpi pixels -> 72 dpi points; PDF pages max out at 14400 pt.
            float scale = 0.75f;
            float maxSide = Math.Max(bmp.Width, bmp.Height) * scale;
            if (maxSide > 14000f) scale *= 14000f / maxSide;
            var ci = CultureInfo.InvariantCulture;
            string w = (bmp.Width * scale).ToString("0.##", ci), h = (bmp.Height * scale).ToString("0.##", ci);
            string content = "q " + w + " 0 0 " + h + " 0 0 cm /Im0 Do Q";

            using (var fs = File.Create(path))
            {
                var offsets = new List<long>();
                Action<string> W = s => { var b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); };
                W("%PDF-1.4\n");
                fs.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6);
                offsets.Add(fs.Position); W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                offsets.Add(fs.Position); W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                offsets.Add(fs.Position); W("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + w + " " + h +
                                            "] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");
                offsets.Add(fs.Position); W("4 0 obj\n<< /Type /XObject /Subtype /Image /Width " + bmp.Width + " /Height " + bmp.Height +
                                            " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpg.Length + " >>\nstream\n");
                fs.Write(jpg, 0, jpg.Length);
                W("\nendstream\nendobj\n");
                offsets.Add(fs.Position); W("5 0 obj\n<< /Length " + content.Length + " >>\nstream\n" + content + "\nendstream\nendobj\n");
                long xref = fs.Position;
                W("xref\n0 6\n0000000000 65535 f \n");
                foreach (var off in offsets) W(off.ToString("D10", ci) + " 00000 n \n");
                W("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + xref.ToString(ci) + "\n%%EOF\n");
            }
        }
    }

    static class SelfTest
    {
        public static int Run(string dir)
        {
            var sample = new Bitmap(620, 170);
            using (var g = Graphics.FromImage(sample))
            {
                g.Clear(Color.White);
                using (var f = new Font("Segoe UI", 16f))
                {
                    g.DrawString("Q7. f(x) = 2x² − 8x + 3", f, Brushes.Black, 12, 14);
                    g.DrawString("(a) Find the vertex of the graph of f.  [3]", f, Brushes.Black, 12, 64);
                    g.DrawString("(b) Solve f(x) = 0 to 3 s.f.  [3]", f, Brushes.Black, 12, 114);
                }
            }
            var form = new EditorForm(sample, 1) { WindowState = FormWindowState.Normal, Size = new Size(1300, 900) };
            int code = 1;
            form.Shown += delegate
            {
                form.BeginInvoke(new Action(() =>
                {
                    try { form.RunSelfTest(dir); code = 0; Console.WriteLine("SELFTEST OK"); }
                    catch (Exception ex) { Console.WriteLine("SELFTEST FAILED: " + ex); }
                    Application.Exit();
                }));
            };
            Application.Run(form);
            return code;
        }
    }
}
