using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TextureGen
{
    // Paints the smoke flipbook (Assets/Art/Textures/SmokeFlipbook.png): a 6 x 6 sheet of 36
    // looping frames, frame 0 top-left, each a soft column of smoke low in its cell. Alpha holds
    // the smoke, colour is near-white grey (the material tints it). The frames loop because the
    // noise wraps in time after the last frame.
    public static class SmokeSheet
    {
        // Envelope: the column's width at its base and how much it spreads (negative narrows) to
        // the top, and where across the cell it stands.
        public static double WidthBase = 0.34, WidthTop = -0.04, CentreU = 0.465, EnvPow = 2;
        // Vertical fades: in at the base (cell height 0 = top, 1 = bottom), out towards the top.
        public static double BaseFade0 = 1.03, BaseFade1 = 0.86, TopFade0 = -0.45, TopFade1 = 0.7, TopPow = 1;
        // Noise: features per cell, how far the sample point is warped, octaves, share of billow
        // (rounded lobes) noise mixed in.
        public static double Scale = 3.0, Warp = 0.4, BillowMix = 0.3;
        public static int Octaves = 6, ShadeOct = 3;
        // Density to alpha: density = envelope x (DensBase + DensGain x noise), then a soft step.
        public static double DensBase = 0.0, DensGain = 1.75, Edge0 = 0.34, Edge1 = 0.62, AlphaMax = 0.78;
        // Shading: lit from above on a smooth field, plus the fine detail's own light and shade.
        public static double LightStep = 0.12, ShadeBase = 0.62, ShadeGain = 2.0, LightGain = 2.0, DetailGain = 2.2, LumLow = 120;

        static readonly int[] Perm = BuildPerm(1337);

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

        static double Smooth(double e0, double e1, double x)
        {
            double t = Math.Max(0, Math.Min(1, (x - e0) / (e1 - e0)));
            return t * t * (3 - 2 * t);
        }

        static double Grad(int h, double x, double y, double z)
        {
            int k = h & 15;
            double u = k < 8 ? x : y, v = k < 4 ? y : (k == 12 || k == 14 ? x : z);
            return ((k & 1) == 0 ? u : -u) + ((k & 2) == 0 ? v : -v);
        }

        /// <summary>
        /// Perlin noise, -1..1, wrapping in z (time) every zPeriod lattice cells.
        /// </summary>
        static double Noise(double x, double y, double z, int zPeriod)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
            double xf = x - xi, yf = y - yi, zf = z - zi;
            int X = xi & 255, Y = yi & 255;
            int Z0 = ((zi % zPeriod) + zPeriod) % zPeriod, Z1 = (Z0 + 1) % zPeriod;
            double u = Fade(xf), v = Fade(yf), w = Fade(zf);
            int a = Perm[X] + Y, b = Perm[X + 1] + Y;
            int aa = Perm[a], ab = Perm[a + 1], ba = Perm[b], bb = Perm[b + 1];
            double x1 = Lerp(Grad(Perm[aa + Z0], xf, yf, zf), Grad(Perm[ba + Z0], xf - 1, yf, zf), u);
            double x2 = Lerp(Grad(Perm[ab + Z0], xf, yf - 1, zf), Grad(Perm[bb + Z0], xf - 1, yf - 1, zf), u);
            double y1 = Lerp(x1, x2, v);
            x1 = Lerp(Grad(Perm[aa + Z1], xf, yf, zf - 1), Grad(Perm[ba + Z1], xf - 1, yf, zf - 1), u);
            x2 = Lerp(Grad(Perm[ab + Z1], xf, yf - 1, zf - 1), Grad(Perm[bb + Z1], xf - 1, yf - 1, zf - 1), u);
            return Lerp(y1, Lerp(x1, x2, v), w);
        }

        /// <summary>
        /// Fractal noise, about 0..1. Octave o runs time 2^o times faster with a period 2^o
        /// times longer, so every octave loops on the same frame.
        /// </summary>
        static double Fbm(double x, double y, double t, int octaves, int period)
        {
            double sum = 0, amp = 0.5, norm = 0, f = 1;
            for (int o = 0; o < octaves; o++) {
                sum += amp * Noise(x * f + o * 17.3, y * f + o * 9.1, t * f, period * (int)f);
                norm += amp; amp *= 0.5; f *= 2;
            }
            return 0.5 + 0.5 * sum / norm * 1.6;
        }

        /// <summary>
        /// Billow noise, about 0..1: absolute value per octave gives rounded lobes.
        /// </summary>
        static double Billow(double x, double y, double t, int octaves, int period)
        {
            double sum = 0, amp = 0.5, norm = 0, f = 1;
            for (int o = 0; o < octaves; o++) {
                sum += amp * Math.Abs(Noise(x * f + o * 5.9, y * f + o * 13.7, t * f + 0.37, period * (int)f));
                norm += amp; amp *= 0.5; f *= 2;
            }
            return Math.Min(1, sum / norm * 2.2);
        }

        /// <summary>
        /// Paints the 2048 sheet to a PNG.
        /// </summary>
        public static void Run(string path)
        {
            const int size = 2048, tiles = 6, frames = 36, period = 3;
            var px = new byte[size * size * 4];
            Parallel.For(0, size, y => {
                double gy = (y + 0.5) * tiles / size;
                int row = (int)gy; double v = gy - row;
                for (int x = 0; x < size; x++) {
                    double gx = (x + 0.5) * tiles / size;
                    int col = (int)gx; double u = gx - col;
                    int frame = row * tiles + col;
                    double t = (double)frame / frames * period;
                    // Column of smoke: low in the cell, gone well before the cell's edges.
                    double width = WidthBase + WidthTop * (1 - v);
                    double dx = (u - CentreU) / width;
                    double env = Math.Exp(-Math.Pow(Math.Abs(dx), EnvPow)) * Smooth(BaseFade0, BaseFade1, v) * Math.Pow(Smooth(TopFade0, TopFade1, v), TopPow);
                    env *= Smooth(0, 0.05, u) * Smooth(1, 0.95, u) * Smooth(0, 0.02, v);
                    int o = (y * size + x) * 4;
                    // Clear pixels keep the smoke's grey so filtering never pulls in a dark fringe.
                    if (env < 0.005) { px[o] = px[o + 1] = px[o + 2] = 200; px[o + 3] = 0; continue; }
                    double sx = u * Scale, sy = v * Scale;
                    double wx = Fbm(sx + 3.1, sy, t, 2, period) - 0.5;
                    double wy = Fbm(sx, sy + 7.7, t, 2, period) - 0.5;
                    double qx = sx + wx * Warp, qy = sy + wy * Warp;
                    double n = Lerp(Fbm(qx, qy, t, Octaves, period), Billow(qx, qy, t, Octaves, period), BillowMix);
                    double d = env * (DensBase + DensGain * n);
                    double a = Smooth(Edge0, Edge1, d) * AlphaMax;
                    double s = Fbm(qx, qy, t, ShadeOct, period);
                    double sUp = Fbm(qx, qy - LightStep, t, ShadeOct, period);
                    double lit = 0.5 + 0.5 * Math.Tanh(2 * (ShadeBase - 0.5 + (s - 0.5) * ShadeGain + (s - sUp) * LightGain + (n - s) * DetailGain));
                    byte l = (byte)Math.Min(255, LumLow + (255 - LumLow) * lit);
                    px[o] = px[o + 1] = px[o + 2] = l;
                    px[o + 3] = (byte)Math.Round(a * 255);
                }
            });
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < size; y++) Marshal.Copy(px, y * size * 4, data.Scan0 + y * data.Stride, size * 4);
            bmp.UnlockBits(data);
            bmp.Save(path, ImageFormat.Png);
            bmp.Dispose();
        }
    }
}
