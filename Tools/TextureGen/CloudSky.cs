using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TextureGen
{
    // Paints the night cloud layer (Assets/Art/Textures/Sky/SkyClouds.png) as a vertical-strip
    // cubemap (+X, -X, +Y, -Y, +Z, -Z top to bottom): top-lit cumulus heaps from the horizon to
    // straight overhead (so they pass over the moon), clear well below the horizon. Noise is sampled by view direction, so every face meets its
    // neighbours seamlessly. Colour = the cloud's light (black = clear), alpha = how thick it is.
    public static class CloudSky
    {
        // Noise: features round the sky, vertical squash, warp, billow share, octaves.
        public static double Scale = 1.25, Flatten = 1.0, Warp = 0.25, BillowMix = 0.5, Crease = 0.25;
        public static int Octaves = 6, ShadeOct = 4;
        // Coverage: how much is cloud, how soft its edges.
        public static double Cover = 0.4, Soft = 0.06;
        // The cover: its bottom below the horizon (direction y), and how hard it cuts cloud below
        // that. Above it clouds reach all the way overhead.
        public static double BandLow0 = -1.1, BandLow1 = -0.72, BandCut = 0.5;
        // Shading: lit from above, darker below the horizon.
        public static double LightStep = 0.1, LightGain = 5, DetailGain = 0.7, ShadeGain = 1.0, Ambient = 0.4;
        public static double NadirDark0 = -0.4, NadirDark1 = -1.0, NadirLevel = 0.6;
        // Colour: warm where lit, cool grey in shade, overall gain.
        public static double WarmR = 1.0, WarmG = 0.94, WarmB = 0.9, CoolR = 0.48, CoolG = 0.48, CoolB = 0.53, Gain = 0.85;

        static readonly int[] Perm = BuildPerm(4242);

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
        /// Perlin noise, -1..1.
        /// </summary>
        static double Noise(double x, double y, double z)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
            double xf = x - xi, yf = y - yi, zf = z - zi;
            int X = xi & 255, Y = yi & 255, Z = zi & 255;
            double u = Fade(xf), v = Fade(yf), w = Fade(zf);
            int a = Perm[X] + Y, aa = Perm[a] + Z, ab = Perm[a + 1] + Z;
            int b = Perm[X + 1] + Y, ba = Perm[b] + Z, bb = Perm[b + 1] + Z;
            return Lerp(Lerp(Lerp(Grad(Perm[aa], xf, yf, zf), Grad(Perm[ba], xf - 1, yf, zf), u),
                             Lerp(Grad(Perm[ab], xf, yf - 1, zf), Grad(Perm[bb], xf - 1, yf - 1, zf), u), v),
                        Lerp(Lerp(Grad(Perm[aa + 1], xf, yf, zf - 1), Grad(Perm[ba + 1], xf - 1, yf, zf - 1), u),
                             Lerp(Grad(Perm[ab + 1], xf, yf - 1, zf - 1), Grad(Perm[bb + 1], xf - 1, yf - 1, zf - 1), u), v), w);
        }

        /// <summary>
        /// Fractal noise about 0..1, blended towards billow (rounded lobes, creases softened
        /// by Crease) by BillowMix.
        /// </summary>
        static double Fbm(double x, double y, double z, int octaves)
        {
            double sum = 0, bil = 0, amp = 0.5, norm = 0, f = 1;
            for (int o = 0; o < octaves; o++) {
                double n = Noise(x * f + o * 17.3, y * f + o * 9.1, z * f + o * 4.7);
                sum += amp * n; bil += amp * Math.Sqrt(n * n + Crease * Crease);
                norm += amp; amp *= 0.5; f *= 2;
            }
            double fb = 0.5 + 0.5 * sum / norm * 1.6;
            return Lerp(fb, Math.Min(1, bil / norm * 2.2), BillowMix);
        }

        /// <summary>
        /// Thickness and brightness, 0..1, of the cloud seen along a unit direction.
        /// </summary>
        static void Sample(double dx, double dy, double dz, out double thick, out double light)
        {
            double px = dx * Scale, py = dy * Scale * Flatten, pz = dz * Scale;
            double wx = Fbm(px + 11.3, py, pz, 3) - 0.5, wy = Fbm(px, py + 5.1, pz, 3) - 0.5, wz = Fbm(px, py, pz + 7.9, 3) - 0.5;
            px += wx * Warp; py += wy * Warp; pz += wz * Warp;
            double n = Fbm(px, py, pz, Octaves);
            // Cloud from a little below the horizon to straight overhead; clear below that.
            double band = Smooth(BandLow0, BandLow1, dy);
            double d = n + (band - 1) * BandCut;
            thick = Smooth(1 - Cover - Soft, 1 - Cover + Soft, d);
            // Lit from above on a smoother field (rounded lit tops, dark undersides).
            double s = Fbm(px, py, pz, ShadeOct);
            double sUp = Fbm(px, py + LightStep, pz, ShadeOct);
            light = 0.5 + 0.5 * Math.Tanh(2 * (Ambient - 0.5 + (s - sUp) * LightGain + (n - s) * DetailGain + (s - 0.5) * ShadeGain));
            if (dy < 0) light *= Lerp(1, NadirLevel, Smooth(NadirDark0, NadirDark1, dy));
        }

        /// <summary>
        /// Paints the strip, `face` pixels per face, to a PNG.
        /// </summary>
        public static void Run(string path, int face)
        {
            var px = new byte[face * face * 6 * 4];
            Parallel.For(0, face * 6, row => {
                int f = row / face, y = row % face;
                for (int x = 0; x < face; x++) {
                    // Face texel to direction (the usual cubemap layout).
                    double s = (x + 0.5) / face * 2 - 1, t = (y + 0.5) / face * 2 - 1, dx, dy, dz;
                    switch (f) {
                        case 0: dx = 1; dy = -t; dz = -s; break;
                        case 1: dx = -1; dy = -t; dz = s; break;
                        case 2: dx = s; dy = 1; dz = t; break;
                        case 3: dx = s; dy = -1; dz = -t; break;
                        case 4: dx = s; dy = -t; dz = 1; break;
                        default: dx = -s; dy = -t; dz = -1; break;
                    }
                    double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double thick, light;
                    Sample(dx / len, dy / len, dz / len, out thick, out light);
                    double v = thick * Gain * Lerp(0.45, 1, light);
                    int o = (row * face + x) * 4;
                    px[o] = (byte)Math.Min(255, v * Lerp(CoolB, WarmB, light) * 255);
                    px[o + 1] = (byte)Math.Min(255, v * Lerp(CoolG, WarmG, light) * 255);
                    px[o + 2] = (byte)Math.Min(255, v * Lerp(CoolR, WarmR, light) * 255);
                    // Alpha = thickness, so the sky shader hides what's behind by how thick the
                    // cloud is, not how bright (dark undersides are as thick as lit tops).
                    px[o + 3] = (byte)Math.Round(thick * 255);
                }
            });
            var bmp = new Bitmap(face, face * 6, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, face, face * 6), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < face * 6; y++) Marshal.Copy(px, y * face * 4, data.Scan0 + y * data.Stride, face * 4);
            bmp.UnlockBits(data);
            bmp.Save(path, ImageFormat.Png);
            bmp.Dispose();
        }
    }
}
