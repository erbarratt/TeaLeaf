using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TextureGen
{
    // Turns a Poly Haven texture set (CC0; diff, nor_gl, disp, rough, ao PNGs at 2k) into a
    // TeaLeaf/GroundBlend layer: each map matched to a target mean and spread, so a new scan
    // drops into the palette, relief and wetness the ground blend is tuned for.
    public static class PolyHavenGround
    {
        // Targets per layer: albedo R, G, B mean and spread (0..255); height mean and spread
        // (spread below 0: flat at the mean); occlusion; smoothness (from 1 - roughness);
        // normal strength (1 keeps the scan's, lower flattens).
        public static readonly double[] Dirt = { 65, 22, 60, 19, 55, 16, 133, 34, 216, 40, 123, 26, 0.68 };
        public static readonly double[] WetDirt = { 57, 15, 55, 13, 51, 11, 0, -1, 216, 13, 225, 34, 0.28 };

        static double[] Load(string path, out int n)
        {
            var src = new Bitmap(path); n = src.Width;
            var b = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) g.DrawImage(src, 0, 0, n, n);
            src.Dispose();
            var d = b.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[n * n * 4];
            for (int y = 0; y < n; y++) Marshal.Copy(d.Scan0 + y * d.Stride, buf, y * n * 4, n * 4);
            b.UnlockBits(d); b.Dispose();
            var o = new double[n * n * 3];
            for (int i = 0; i < n * n; i++) { o[i * 3] = buf[i * 4 + 2]; o[i * 3 + 1] = buf[i * 4 + 1]; o[i * 3 + 2] = buf[i * 4]; }
            return o;
        }

        static void Save(string path, int n, double[] rgb)
        {
            var bmp = new Bitmap(n, n, PixelFormat.Format24bppRgb);
            var d = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var row = new byte[d.Stride];
            for (int y = 0; y < n; y++) {
                for (int x = 0; x < n; x++) for (int c = 0; c < 3; c++) row[x * 3 + 2 - c] = (byte)Math.Max(0, Math.Min(255, Math.Round(rgb[(y * n + x) * 3 + c])));
                Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, d.Stride);
            }
            bmp.UnlockBits(d); bmp.Save(path, ImageFormat.Png); bmp.Dispose();
        }

        /// <summary>
        /// Shifts and scales channel c to a target mean and spread.
        /// </summary>
        static void Match(double[] p, int c, double mean, double sd)
        {
            int n = p.Length / 3; double s = 0, q = 0;
            for (int i = 0; i < n; i++) { s += p[i * 3 + c]; q += p[i * 3 + c] * p[i * 3 + c]; }
            double m = s / n, v = Math.Sqrt(Math.Max(1e-6, q / n - m * m));
            for (int i = 0; i < n; i++) p[i * 3 + c] = (p[i * 3 + c] - m) / v * sd + mean;
        }

        /// <summary>
        /// A greyscale map (from its red channel, inverted if asked) matched to a target.
        /// </summary>
        static void Grey(double[] p, double mean, double sd, bool invert)
        {
            int n = p.Length / 3;
            for (int i = 0; i < n; i++) if (invert) p[i * 3] = 255 - p[i * 3];
            Match(p, 0, mean, sd);
            for (int i = 0; i < n; i++) p[i * 3 + 1] = p[i * 3 + 2] = p[i * 3];
        }

        /// <summary>
        /// Converts the set in `src` into `dst` as &lt;name&gt;_&lt;Map&gt;.png.
        /// </summary>
        public static void Run(string src, string dst, string name, double[] t)
        {
            int n;
            var a = Load(src + "\\diff.png", out n);
            for (int c = 0; c < 3; c++) Match(a, c, t[c * 2], t[c * 2 + 1]);
            Save(dst + "\\" + name + "_Albedo.png", n, a);
            var h = Load(src + "\\disp.png", out n);
            if (t[7] < 0) { for (int i = 0; i < h.Length; i++) h[i] = t[6]; } else Grey(h, t[6], t[7], false);
            Save(dst + "\\" + name + "_Height.png", n, h);
            var o = Load(src + "\\ao.png", out n); Grey(o, t[8], t[9], false);
            Save(dst + "\\" + name + "_Occlusion.png", n, o);
            var s = Load(src + "\\rough.png", out n); Grey(s, t[10], t[11], true);
            Save(dst + "\\" + name + "_Smoothness.png", n, s);
            // Normal: OpenGL already (as Unity); tilt scaled by the strength, renormalised.
            var nm = Load(src + "\\nor_gl.png", out n);
            double k = t[12];
            for (int i = 0; i < n * n; i++) {
                double x = (nm[i * 3] / 127.5 - 1) * k, y = (nm[i * 3 + 1] / 127.5 - 1) * k, z = nm[i * 3 + 2] / 127.5 - 1;
                double l = Math.Sqrt(x * x + y * y + z * z);
                nm[i * 3] = (x / l * 0.5 + 0.5) * 255; nm[i * 3 + 1] = (y / l * 0.5 + 0.5) * 255; nm[i * 3 + 2] = (z / l * 0.5 + 0.5) * 255;
            }
            Save(dst + "\\" + name + "_Normal.png", n, nm);
        }
    }
}
