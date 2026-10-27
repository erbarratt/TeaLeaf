using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TextureGen
{
    // Repaints the pack's pipe atlas (STYLZ_MVillage_Pipes_LD_*, 2048) at 4096 as painted cast
    // iron: an all-over mottle of darker, lighter and browner dabs, dark rust-brown in creases and
    // patches with a few warm specks, faint scratches, and only thin dim warm light on raised
    // edges (no outlined facets: bright outlines read wrong). Works in the atlas's own UV space,
    // starting from the pack's albedo (so the pipes keep their colours) and normal (whose
    // curvature finds the edges and creases). Writes Albedo, Normal (the pack's plus pitting)
    // and Mask (R metallic, G occlusion, A smoothness: URP Lit reads one texture for both slots).
    public static class PipesPainted
    {
        const int Size = 4096;

        // Mottle (painted dabs over the iron): dab frequency (per atlas width), level above which
        // a dab shows (higher = fewer), edge softness, how much darker/lighter/browner the dabs.
        public static double MottleFreq = 55, MottleLevel = 0.0, MottleEdge = 0.012;
        public static double MottleDark = 0.5, MottleLight = 0.35, MottleBrown = 0.4;
        // Dents: small pits in the normal.
        public static double DentStrength = 0.04;
        // Rust: share of surface, colour (0..1), how much it gathers in creases, warm specks.
        public static double RustShare = 0.1, RustR = 0.42, RustG = 0.25, RustB = 0.15, RustCrease = 0.5, SpeckLevel = 0.5;
        // Edge wear: thin, dim, warm light on convex edges.
        public static double EdgeLight = 0.3, EdgeR = 0.55, EdgeG = 0.5, EdgeB = 0.44, EdgeScale = 18;
        // Grime in concave creases and occlusion depth.
        public static double Grime = 0.45, AoStrength = 0.9;
        // Scratches: density and brightness.
        public static double ScratchShare = 0.3, ScratchLight = 0.25;
        // Material values.
        public static double SmoothMetal = 0.3, SmoothEdge = 0.6, SmoothRust = 0.18, MetalScale = 0.35, MetalRust = 0.15;

        static readonly int[] Perm = BuildPerm(23);

        static int[] BuildPerm(int seed)
        {
            var r = new Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); int t = p[i]; p[i] = p[j]; p[j] = t; }
            var pp = new int[512];
            for (int i = 0; i < 512; i++) pp[i] = p[i & 255];
            return pp;
        }

        static double Fade(double t) { return t * t * t * (t * (t * 6 - 15) + 10); }
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double Clamp01(double x) { return x < 0 ? 0 : x > 1 ? 1 : x; }

        static double Smooth(double e0, double e1, double x)
        {
            double t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3 - 2 * t);
        }

        static double Grad2(int h, double x, double y)
        {
            switch (h & 7) {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x; case 5: return -x; case 6: return y; default: return -y;
            }
        }

        /// <summary>
        /// 2D Perlin noise, about -1..1 (atlas islands don't tile, so no wrapping needed).
        /// </summary>
        static double Noise(double x, double y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double xf = x - xi, yf = y - yi;
            int x0 = xi & 255, y0 = yi & 255, x1 = (x0 + 1) & 255, y1 = (y0 + 1) & 255;
            double u = Fade(xf), v = Fade(yf);
            double a = Grad2(Perm[Perm[x0] + y0], xf, yf), b = Grad2(Perm[Perm[x1] + y0], xf - 1, yf);
            double c = Grad2(Perm[Perm[x0] + y1], xf, yf - 1), d = Grad2(Perm[Perm[x1] + y1], xf - 1, yf - 1);
            return Lerp(Lerp(a, b, u), Lerp(c, d, u), v) * 1.4;
        }

        static double Fbm(double u, double v, double freq, int octaves, double gain = 0.5, double offset = 0)
        {
            double sum = 0, amp = 1, norm = 0, f = freq;
            for (int o = 0; o < octaves; o++) {
                sum += amp * Noise(u * f + offset + o * 31.7, v * f + offset * 0.7 + o * 17.3);
                norm += amp; amp *= gain; f *= 2;
            }
            return sum / norm;
        }

        static double Hash(int a, int b, int c)
        {
            unchecked {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0x1000000;
            }
        }


        static double[] Load(string path, int n)
        {
            var src = new Bitmap(path);
            var b = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, n, n));
            }
            src.Dispose();
            var d = b.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[n * n * 4];
            for (int y = 0; y < n; y++) Marshal.Copy(d.Scan0 + y * d.Stride, buf, y * n * 4, n * 4);
            b.UnlockBits(d); b.Dispose();
            var o = new double[n * n * 3];
            for (int i = 0; i < n * n; i++) { o[i * 3] = buf[i * 4 + 2] / 255.0; o[i * 3 + 1] = buf[i * 4 + 1] / 255.0; o[i * 3 + 2] = buf[i * 4] / 255.0; }
            return o;
        }

        static double[] Blur(double[] src, int n, int radius)
        {
            var tmp = new double[n * n]; var dst = new double[n * n];
            Parallel.For(0, n, y => {
                double s = 0; int c = 0;
                for (int k = 0; k <= radius && k < n; k++) { s += src[y * n + k]; c++; }
                for (int x = 0; x < n; x++) {
                    tmp[y * n + x] = s / c;
                    int add = x + radius + 1, rem = x - radius;
                    if (add < n) { s += src[y * n + add]; c++; }
                    if (rem >= 0) { s -= src[y * n + rem]; c--; }
                }
            });
            Parallel.For(0, n, x => {
                double s = 0; int c = 0;
                for (int k = 0; k <= radius && k < n; k++) { s += tmp[k * n + x]; c++; }
                for (int y = 0; y < n; y++) {
                    dst[y * n + x] = s / c;
                    int add = y + radius + 1, rem = y - radius;
                    if (add < n) { s += tmp[add * n + x]; c++; }
                    if (rem >= 0) { s -= tmp[rem * n + x]; c--; }
                }
            });
            return dst;
        }

        /// <summary>
        /// Curvature from a tangent-space normal field: positive on convex edges, negative in
        /// creases (image rows run down, normal +Y up the texture).
        /// </summary>
        static double[] Curvature(double[] nx, double[] ny, int n, int step)
        {
            var c = new double[n * n];
            Parallel.For(0, n, y => {
                for (int x = 0; x < n; x++) {
                    int xl = Math.Max(0, x - step), xr = Math.Min(n - 1, x + step), yu = Math.Max(0, y - step), yd = Math.Min(n - 1, y + step);
                    c[y * n + x] = ((nx[y * n + xr] - nx[y * n + xl]) - (ny[yd * n + x] - ny[yu * n + x])) / (2.0 * step) * n / 256.0;
                }
            });
            return c;
        }

        /// <summary>
        /// Paints the three maps from the pack's atlas folder into `dst`.
        /// </summary>
        public static void Run(string packDir, string dst)
        {
            int n = Size;
            var alb = Load(packDir + "\\STYLZ_MVillage_Pipes_LD_ALBEDO.tif", n);
            var nor = Load(packDir + "\\STYLZ_MVillage_Pipes_LD_NORMAL.tif", n);
            var mrao = Load(packDir + "\\STYLZ_MVillage_Pipes_LD_MRAO.tif", n);
            var nx = new double[n * n]; var ny = new double[n * n];
            for (int i = 0; i < n * n; i++) { nx[i] = nor[i * 3] * 2 - 1; ny[i] = nor[i * 3 + 1] * 2 - 1; }
            // Edges and creases at two scales: fine for painted edge lines, broad for grime/AO.
            var cFine = Curvature(Blur(nx, n, 2), Blur(ny, n, 2), n, 3);
            var cBroad = Curvature(Blur(nx, n, 10), Blur(ny, n, 10), n, 12);

            var outA = new byte[n * n * 3]; var outN = new byte[n * n * 3]; var outM = new byte[n * n * 4];
            Parallel.For(0, n, y => {
                for (int x = 0; x < n; x++) {
                    int i = y * n + x;
                    double u = (x + 0.5) / n, v = (y + 0.5) / n;
                    double r = alb[i * 3], g = alb[i * 3 + 1], b = alb[i * 3 + 2];
                    double metal0 = mrao[i * 3], rough0 = mrao[i * 3 + 1];
                    // Background outside the islands (pure red in the pack's MRAO): leave alone.
                    bool island = !(metal0 > 0.98 && rough0 < 0.02);
                    double edge = Clamp01(cFine[i] * EdgeScale * 0.02), crease = Clamp01(-cBroad[i] * 1.2);
                    double fine = Fbm(u, v, 512, 2, 0.5, 3.3), mid = Fbm(u, v, 64, 3, 0.5, 7.7), broad = Fbm(u, v, 12, 3, 0.55, 1.9);
                    // Mottle: painted dabs at two sizes, each a crisp-edged blob where a warped
                    // noise rises above a level - some darker, some lighter, some browner.
                    double wu = u + 0.003 * mid, wv = v + 0.003 * Fbm(u, v, 64, 2, 0.5, 9.1);
                    double dDark = Smooth(MottleLevel, MottleLevel + MottleEdge, Fbm(wu, wv, MottleFreq, 3, 0.55, 11.1));
                    double dLight = Smooth(MottleLevel, MottleLevel + MottleEdge, Fbm(wu, wv, MottleFreq, 3, 0.55, 23.4));
                    double dBrown = Smooth(MottleLevel, MottleLevel + MottleEdge, Fbm(wu, wv, MottleFreq * 0.7, 3, 0.55, 37.9));
                    double sDark = Smooth(MottleLevel, MottleLevel + MottleEdge, Fbm(wu, wv, MottleFreq * 2.2, 2, 0.5, 51.3));
                    double sLight = Smooth(MottleLevel, MottleLevel + MottleEdge, Fbm(wu, wv, MottleFreq * 2.2, 2, 0.5, 63.6));
                    double tone = 1 - MottleDark * dDark + MottleLight * dLight - MottleDark * 0.6 * sDark + MottleLight * 0.6 * sLight + 0.04 * mid + 0.03 * fine;
                    r *= tone; g *= tone; b *= tone;
                    double lum = (r + g + b) / 3;
                    // Brown dabs: the iron's own tone pulled towards dark rust-brown.
                    double brown = dBrown * MottleBrown;
                    r = Lerp(r, lum * 1.25, brown); g = Lerp(g, lum * 0.95, brown); b = Lerp(b, lum * 0.7, brown);
                    // Rust: dark brown in creases and broad patches, a few warm specks.
                    double rustNoise = broad * 0.6 + mid * 0.4 + RustCrease * crease;
                    double rust = Smooth(1 - RustShare * 2.2, 1 - RustShare * 2.2 + 0.25, rustNoise * 0.5 + 0.5);
                    double speck = Smooth(SpeckLevel, SpeckLevel + 0.04, Fbm(u, v, 260, 2, 0.5, 71.7)) * Smooth(-0.1, 0.3, broad);
                    double rk = 0.55 + 0.9 * lum + 0.15 * fine;
                    r = Lerp(r, RustR * rk, rust); g = Lerp(g, RustG * rk, rust); b = Lerp(b, RustB * rk, rust);
                    r = Lerp(r, RustR * 1.3, speck * 0.8); g = Lerp(g, RustG * 1.2, speck * 0.8); b = Lerp(b, RustB, speck * 0.8);
                    rust = Math.Max(rust, speck);
                    // Creases dark with grime.
                    double dark = Clamp01(crease * Grime);
                    r *= 1 - dark; g *= 1 - dark; b *= 1 - dark;
                    // Faint scratches and hairline cracks, in a few directions.
                    double sa = (Fbm(u, v, 3, 1, 0.5, 81.1) + 1) * Math.PI, ca = Math.Cos(sa), sn = Math.Sin(sa);
                    double sl = Math.Abs(Fbm((u * ca + v * sn) * 0.15 + 3.7, (-u * sn + v * ca) * 2.5, 60, 2, 0.5, 91.3));
                    double scratch = Smooth(0.025, 0.0, sl) * Smooth(ScratchShare, ScratchShare + 0.1, Fbm(u, v, 20, 2, 0.5, 97.7) * 0.5 + 0.5) * (1 - rust);
                    // Thin, dim, warm light on raised edges; scratches a touch lighter.
                    double lit = Clamp01(EdgeLight * edge + ScratchLight * scratch) * (1 - 0.7 * rust);
                    double le = 0.5 + 0.8 * lum;
                    r = Lerp(r, EdgeR * le, lit); g = Lerp(g, EdgeG * le, lit); b = Lerp(b, EdgeB * le, lit);
                    if (!island) { r = alb[i * 3]; g = alb[i * 3 + 1]; b = alb[i * 3 + 2]; }
                    outA[i * 3] = (byte)Math.Round(Clamp01(r) * 255); outA[i * 3 + 1] = (byte)Math.Round(Clamp01(g) * 255); outA[i * 3 + 2] = (byte)Math.Round(Clamp01(b) * 255);

                    // Normal: the pack's, plus the cast surface's pitting and small dents.
                    double dx = nx[i], dy = ny[i];
                    if (island) {
                        double ddx = Fbm(u + 1.0 / n, v, 300, 2, 0.5, 5.1) - Fbm(u - 1.0 / n, v, 300, 2, 0.5, 5.1);
                        double ddy = Fbm(u, v + 1.0 / n, 300, 2, 0.5, 5.1) - Fbm(u, v - 1.0 / n, 300, 2, 0.5, 5.1);
                        dx -= DentStrength * ddx * 6; dy += DentStrength * ddy * 6;
                    }
                    double nz = Math.Sqrt(Math.Max(0.05, 1 - Math.Min(0.95, dx * dx + dy * dy)));
                    double nl = Math.Sqrt(dx * dx + dy * dy + nz * nz);
                    outN[i * 3] = (byte)Math.Round(Clamp01(dx / nl * 0.5 + 0.5) * 255); outN[i * 3 + 1] = (byte)Math.Round(Clamp01(dy / nl * 0.5 + 0.5) * 255); outN[i * 3 + 2] = (byte)Math.Round(Clamp01(nz / nl * 0.5 + 0.5) * 255);

                    // Mask: metallic from the pack (less where rusted), occlusion from creases,
                    // smoothness: worn edges glossiest, rust dullest.
                    double metal = island ? Clamp01(metal0 * MetalScale * Lerp(1, MetalRust, rust) + 0.25 * lit) : 0;
                    double ao = island ? Clamp01(1 - AoStrength * crease * 0.8) : 1;
                    double smooth = island ? Clamp01(Lerp(Lerp(SmoothMetal + 0.08 * mid - 0.25 * (rough0 - 0.75), SmoothEdge, lit), SmoothRust, rust) - 0.2 * dark) : 0;
                    outM[i * 4] = (byte)Math.Round(metal * 255); outM[i * 4 + 1] = (byte)Math.Round(ao * 255); outM[i * 4 + 2] = 0; outM[i * 4 + 3] = (byte)Math.Round(smooth * 255);
                }
            });
            SaveRgb(dst + "\\Pipes_Painted_Albedo.png", n, outA);
            SaveRgb(dst + "\\Pipes_Painted_Normal.png", n, outN);
            SaveRgba(dst + "\\Pipes_Painted_Mask.png", n, outM);
        }

        static void SaveRgb(string path, int n, byte[] rgb)
        {
            var bmp = new Bitmap(n, n, PixelFormat.Format24bppRgb);
            var d = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var row = new byte[d.Stride];
            for (int y = 0; y < n; y++) {
                for (int x = 0; x < n; x++) { int s = (y * n + x) * 3; row[x * 3] = rgb[s + 2]; row[x * 3 + 1] = rgb[s + 1]; row[x * 3 + 2] = rgb[s]; }
                Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, d.Stride);
            }
            bmp.UnlockBits(d); bmp.Save(path, ImageFormat.Png); bmp.Dispose();
        }

        static void SaveRgba(string path, int n, byte[] rgba)
        {
            var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            var d = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var row = new byte[n * 4];
            for (int y = 0; y < n; y++) {
                for (int x = 0; x < n; x++) { int s = (y * n + x) * 4; row[x * 4] = rgba[s + 2]; row[x * 4 + 1] = rgba[s + 1]; row[x * 4 + 2] = rgba[s]; row[x * 4 + 3] = rgba[s + 3]; }
                Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, n * 4);
            }
            bmp.UnlockBits(d); bmp.Save(path, ImageFormat.Png); bmp.Dispose();
        }
    }
}
