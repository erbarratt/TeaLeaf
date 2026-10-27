using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TextureGen
{
    // Paints the cobble layer of TeaLeaf/GroundBlend (Assets/Art/Textures/Ground/Cobbles_*.png):
    // albedo, normal, height, occlusion, smoothness, 2048, tiling. Irregular rounded stones
    // (a wrapping power diagram, no rows or grid), cool grey under a patchy brown grime film,
    // worn pale patches and fine cracks, light on the worn edges, gritty sandy joints. All five
    // maps come from one height field, stone tops high and joints low, which the shader's
    // height blend relies on. Everything wraps at the tile edge.
    public static class Cobbles
    {
        const int Size = 2048;

        // Layout: Cells x Cells stones per tile, how far each centre strays, size variation.
        public static int Cells = 11;
        public static double Jitter = 0.85, SizeVar = 0.3;
        // Shape (tile units): joint width, extra per-stone inset, shoulder width, corner
        // rounding, layout wobble, ragged outline, dome steepness.
        public static double GapWidth = 0.002, Inset = 0.005, Bevel = 0.012, CornerRound = 0.007;
        public static double Warp = 0.006, RowWobble = 0.004, OutlineWarp = 0.004, DomePow = 2.2;
        // Height (0..1): stone tops, joints, broad bumps, chips, cracks.
        public static double StoneTop = 0.46, MortarHeight = 0.14, StoneBump = 0.06, ChipDepth = 0.03, VeinDepth = 0.02;
        // Stone colour (0..255) and its variation.
        public static double StoneR = 64, StoneG = 67, StoneB = 71, StoneVar = 0.3, StoneBlue = 0.03, Mottle = 0.2;
        public static double EdgeDark = 0.25, TopLight = 0.18, RimLight = 0.2, WornShare = 1, WornLight = 0.22;
        public static double ColourEdge = 0.0015, VeinWidth = 9, VeinLight = 0.2;
        public static double RustShare = 0.03, RustStrength = 0.45;
        // Joint colour: dark dirt with sandy patches, grime in the joints and over the stones.
        public static double MortarR = 52, MortarG = 44, MortarB = 39, SandShare = 0.55, SandR = 92, SandG = 78, SandB = 64;
        public static double Grime = 0.4, StoneGrime = 0.8;
        // Derived maps.
        public static double NormalStrength = 10, AoStrength = 3, SmoothStone = 0.72, SmoothMortar = 0.42;
        // Chamfered tops (0 = rounded dome): chamfer width (tile units), share of dome kept.
        public static double Chamfer = 0, DomeMix = 0.35;
        // Damage: share of stones pitted and chipped, how deep and dark the pits, how much of
        // the face is pitted (lower = more), how far chips bite into the edge (tile units).
        public static double DamageShare = 0, PitDepth = 0.12, PitDark = 0.45, PitLevel = 0.38, ChipBite = 0.006;
        // Light chipping on every stone's edge (tile units; 0 = none).
        public static double GeneralChip = 0;
        // Facets: flat, slightly tilted faces across the tops (cells per tile, 0 = none), how
        // steep (height per tile unit), how much each face's tilt lights or darkens it.
        public static int FacetCells = 0;
        public static double FacetTilt = 0, FacetLight = 0;
        // Per-stone hue shift: some warmer and browner, some cooler and bluer.
        public static double HueVar = 0;
        // Courses layout (setts in running bond) instead of irregular stones: courses per tile,
        // stones per course, how much stone lengths and course heights vary, and how far each
        // side of a stone may sit back from its neighbours (share of the stone's size).
        public static bool Courses = false;
        public static int Rows = 14, StonesPerRow = 13;
        public static double LengthVar = 0.3, RowVar = 0.15, SideInset = 0.06, JointLean = 0.3;

        static readonly int[] Perm = BuildPerm(7);
        [ThreadStatic] static double _half;
        static double[][] _cuts;
        static double[] _rowEdge;

        /// <summary>
        /// The setts layout's settings (the irregular cobbles' are the field defaults).
        /// </summary>
        public static void UseSetts()
        {
            Courses = true;
            Rows = 14; StonesPerRow = 13; LengthVar = 0.3; RowVar = 0.15; SideInset = 0.035; JointLean = 0.18;
            GapWidth = 0.0009; Inset = 0.0006; CornerRound = 0.0035; Bevel = 0.009;
            Warp = 0.004; RowWobble = 0.008; OutlineWarp = 0.002; DomePow = 3.5;
            // Stones the stand-in's cool teal-grey.
            StoneR = 61; StoneG = 66; StoneB = 70;
            // Deep relief: height uses nearly the whole range (parallax works off height - 0.5),
            // steep normals, occlusion eased to suit the deeper joints.
            StoneTop = 0.78; MortarHeight = 0.04; StoneBump = 0.1; ChipDepth = 0.05; VeinDepth = 0.035;
            NormalStrength = 20; AoStrength = 1.0;
            // Sharp, defined edges: flat tops over a narrow chamfer, a thin dark edge, a crisp
            // light line on the chamfer, straighter outlines.
            Chamfer = 0.006; DomeMix = 0.35; Bevel = 0.004; ColourEdge = 0.0008; OutlineWarp = 0.0012; CornerRound = 0.0025;
            // Worn and worked: a tenth of stones pitted and chipped deeper, every edge lightly chipped,
            // chisel facets across the tops, more tone and hue from stone to stone.
            DamageShare = 0.1; ChipBite = 0.009; GeneralChip = 0.0025;
            FacetCells = 44; FacetTilt = 4; FacetLight = 3;
            StoneVar = 0.38; Mottle = 0.3; StoneBump = 0.14; HueVar = 0.08;
        }

        /// <summary>
        /// The facet a tile point lies on (nearest of jittered points, wrapping) as its plane:
        /// height offset from the facet's centre, and the plane's slope across and down.
        /// </summary>
        static double Facet(double u, double v, out double sx, out double sy)
        {
            int cells = FacetCells;
            int gx = (int)Math.Floor(u * cells), gy = (int)Math.Floor(v * cells);
            double best = double.MaxValue, ax = 0, ay = 0; int aid = 0;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
                int wx = (((gx + i) % cells) + cells) % cells, wy = (((gy + j) % cells) + cells) % cells;
                double x = (gx + i + 0.1 + 0.8 * Hash(wx, wy, 61)) / cells, y = (gy + j + 0.1 + 0.8 * Hash(wx, wy, 62)) / cells;
                double d = (u - x) * (u - x) + (v - y) * (v - y);
                if (d < best) { best = d; ax = x; ay = y; aid = wy * cells + wx; }
            }
            sx = (Hash(aid, 63, 1) - 0.5) * 2 * FacetTilt; sy = (Hash(aid, 64, 1) - 0.5) * 2 * FacetTilt;
            return sx * (u - ax) + sy * (v - ay);
        }

        /// <summary>
        /// Each course's height and joints: random course heights and stone lengths scaled to
        /// fill the tile exactly, each course slid by a random amount so its joints never line
        /// up with the course below.
        /// </summary>
        static void BuildCourses()
        {
            var r = new Random(91);
            _rowEdge = new double[Rows + 1];
            var rh = new double[Rows]; double rs = 0;
            for (int i = 0; i < Rows; i++) { rh[i] = 1 + (r.NextDouble() - 0.5) * 2 * RowVar; rs += rh[i]; }
            for (int i = 0; i < Rows; i++) _rowEdge[i + 1] = _rowEdge[i] + rh[i] / rs;
            _cuts = new double[Rows][];
            for (int row = 0; row < Rows; row++) {
                int count = StonesPerRow + r.Next(-1, 2);
                var len = new double[count]; double sum = 0;
                for (int i = 0; i < count; i++) { len[i] = 1 + (r.NextDouble() - 0.5) * 2 * LengthVar; sum += len[i]; }
                var cuts = new double[count + 1]; double at = r.NextDouble();
                for (int i = 0; i <= count; i++) { cuts[i] = at; if (i < count) at += len[i] / sum; }
                _cuts[row] = cuts;
            }
        }

        /// <summary>
        /// The sett a tile point lies on. Each side of each stone sits back by its own random
        /// amount, so joints wander in width and courses aren't ruled lines. Returns depth inside
        /// the stone (tile units, negative in the joint), its id and the point's position across it.
        /// </summary>
        static double Sett(double u, double v, out int id, out double along, out double across)
        {
            int row = 0;
            while (row < Rows - 1 && v >= _rowEdge[row + 1]) row++;
            double rowH = _rowEdge[row + 1] - _rowEdge[row];
            double t = (v - _rowEdge[row]) / rowH;
            double[] cuts = _cuts[row];
            // Cuts run once round the tile from a random start, so a point before it is a lap on.
            double uu = u < cuts[0] ? u + 1 : u;
            int seg = 0;
            while (seg < cuts.Length - 2 && uu >= cuts[seg + 1]) seg++;
            // Each joint leans its own way a little; a lean can move the point to the next stone.
            int last = cuts.Length - 1;
            Func<int, double> cut = c => cuts[c] + (Hash(row, c == last ? 0 : c, 41) - 0.5) * JointLean * rowH * (t - 0.5);
            if (uu < cut(seg) && seg > 0) seg--;
            else if (uu >= cut(seg + 1) && seg < last - 1) seg++;
            double a = cut(seg), b = cut(seg + 1), w = b - a;
            id = row * 1009 + seg;
            double inL = SideInset * w * Hash(id, 31, 1), inR = SideInset * w * Hash(id, 32, 1);
            double inT = SideInset * rowH * Hash(id, 33, 1), inB = SideInset * rowH * Hash(id, 34, 1);
            double ex = Math.Min(uu - a - inL, b - uu - inR);
            double ey = Math.Min(t * rowH - inT, (1 - t) * rowH - inB);
            double k = CornerRound * (0.6 + 0.9 * Hash(id, 9, 9));
            double m = -k * Math.Log(Math.Exp(-ex / k) + Math.Exp(-ey / k));
            along = (uu - a) / w; across = t;
            _half = Math.Min(w, rowH) * 0.5;
            return m - GapWidth * 0.5 - Inset * Hash(id, 7, 3);
        }

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
        /// 2D Perlin noise, about -1..1, wrapping every `period` lattice cells.
        /// </summary>
        static double Noise(double x, double y, int period)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double xf = x - xi, yf = y - yi;
            int x0 = ((xi % period) + period) % period, y0 = ((yi % period) + period) % period;
            int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            double u = Fade(xf), v = Fade(yf);
            double a = Grad2(Perm[(Perm[x0 & 255] + y0) & 511], xf, yf), b = Grad2(Perm[(Perm[x1 & 255] + y0) & 511], xf - 1, yf);
            double c = Grad2(Perm[(Perm[x0 & 255] + y1) & 511], xf, yf - 1), d = Grad2(Perm[(Perm[x1 & 255] + y1) & 511], xf - 1, yf - 1);
            return Lerp(Lerp(a, b, u), Lerp(c, d, u), v) * 1.4;
        }

        /// <summary>
        /// Fractal noise over a tile coordinate (0..1, wraps), base frequency `freq` cells per tile.
        /// </summary>
        static double Fbm(double u, double v, int freq, int octaves, double gain = 0.5, double offset = 0)
        {
            double sum = 0, amp = 1, norm = 0; int f = freq;
            for (int o = 0; o < octaves; o++) {
                sum += amp * Noise(u * f + offset + o * 31, v * f + offset * 0.7 + o * 17, f);
                norm += amp; amp *= gain; f *= 2;
            }
            return sum / norm;
        }

        /// <summary>
        /// A random number 0..1 from three integers.
        /// </summary>
        static double Hash(int a, int b, int c)
        {
            unchecked {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0x1000000;
            }
        }

        /// <summary>
        /// A stone's centre: one jittered point per cell of a Cells x Cells grid, wrapping, with
        /// a power-diagram weight (bigger stones push their borders out).
        /// </summary>
        static void StonePoint(int cx, int cy, out double px, out double py, out double weight, out int id)
        {
            int wx = ((cx % Cells) + Cells) % Cells, wy = ((cy % Cells) + Cells) % Cells;
            id = wy * Cells + wx;
            px = (cx + 0.5 + (Hash(wx, wy, 21) - 0.5) * Jitter) / Cells;
            py = (cy + 0.5 + (Hash(wx, wy, 22) - 0.5) * Jitter) / Cells;
            weight = (Hash(wx, wy, 23) - 0.5) * SizeVar / (Cells * Cells);
        }

        /// <summary>
        /// The stone a tile point lies on (5 x 5 search so no border is missed). Returns depth
        /// inside the stone (tile units, negative in the joint), the stone's id and the point's
        /// position across it (about 0..1 each way).
        /// </summary>
        static double Stone(double u, double v, out int id, out double along, out double across)
        {
            int gx = (int)Math.Floor(u * Cells), gy = (int)Math.Floor(v * Cells);
            double bestD = double.MaxValue, ax = 0, ay = 0, aw = 0; id = 0;
            for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) {
                double px, py, w; int pid;
                StonePoint(gx + i, gy + j, out px, out py, out w, out pid);
                double dx = u - px, dy = v - py, d2 = dx * dx + dy * dy - w;
                if (d2 < bestD) { bestD = d2; ax = px; ay = py; aw = w; id = pid; }
            }
            // Distance to the border: how far the point lies inside the bisector with each other
            // point (shifted by their weights), the borders joined by a smooth minimum so each
            // stone gets its own rounded corners.
            double k = CornerRound * (0.6 + 0.9 * Hash(id, 9, 9));
            double edge = double.MaxValue;
            for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) {
                double bx, by, bw; int bid;
                StonePoint(gx + i, gy + j, out bx, out by, out bw, out bid);
                double ex = bx - ax, ey = by - ay, len = Math.Sqrt(ex * ex + ey * ey);
                if (len < 1e-9) continue;
                double mx = (ax + bx) * 0.5 + ex / len * (aw - bw) / (2 * len), my = (ay + by) * 0.5 + ey / len * (aw - bw) / (2 * len);
                double dist = ((mx - u) * ex + (my - v) * ey) / len;
                edge = edge == double.MaxValue ? dist : -k * Math.Log(Math.Exp(-edge / k) + Math.Exp(-dist / k));
            }
            along = 0.5 + (u - ax) * Cells; across = 0.5 + (v - ay) * Cells;
            // How far in from the edge a stone's middle is, to shape its dome.
            _half = 0.42 / Cells;
            return edge - GapWidth * 0.5 - Inset * Hash(id, 7, 3);
        }

        /// <summary>
        /// Paints the five maps into a folder as &lt;name&gt;_&lt;Map&gt;.png.
        /// </summary>
        public static void Run(string dir, string name)
        {
            int n = Size;
            var height = new double[n * n];
            var albedo = new double[n * n * 3];
            var smooth = new double[n * n];
            if (Courses) BuildCourses();
            Parallel.For(0, n, y => {
                for (int x = 0; x < n; x++) {
                    double u = (x + 0.5) / n, v = (y + 0.5) / n;
                    // Wobble the layout so stones aren't drawn with a ruler; the warped point is
                    // taken back into the tile so it still wraps.
                    double wu = u + Warp * Fbm(u, v, 6, 3, 0.5, 3.3), wv = v + Warp * Fbm(u, v, 6, 3, 0.5, 9.1) + RowWobble * Fbm(u, 0.5, 4, 2, 0.5, 1.7);
                    wu -= Math.Floor(wu); wv -= Math.Floor(wv);
                    int id; double along, across;
                    double d = Courses ? Sett(wu, wv, out id, out along, out across) : Stone(wu, wv, out id, out along, out across);
                    // Ragged outlines: the edge pushed in and out by fine noise.
                    d += OutlineWarp * Fbm(u, v, 24, 3, 0.5, 12.7);
                    // Damaged stones: edges chipped (bitten in where a noise rises), face pitted.
                    bool damaged = Hash(id, 51, 1) < DamageShare;
                    if (damaged) d -= ChipBite * Smooth(0.0, 0.35, Fbm(u, v, 36, 3, 0.5, 77.7));
                    if (GeneralChip > 0) d -= GeneralChip * Smooth(0.15, 0.45, Fbm(u, v, 48, 3, 0.5, 97.3));
                    // Chisel facets across the tops: a plane per facet, its tilt lighting it.
                    double fsx = 0, fsy = 0, facet = FacetCells > 0 ? Facet(u, v, out fsx, out fsy) : 0;
                    double facetLight = FacetCells > 0 ? (-fsx * 0.6 - fsy * 0.8) / Math.Max(1e-6, FacetTilt) * 0.5 : 0;
                    double rnd = Hash(id, 1, 2), rnd2 = Hash(id, 3, 4), rnd3 = Hash(id, 5, 6);
                    double fine = Fbm(u, v, 64, 3);
                    double grain = Fbm(u, v, 256, 2, 0.5, 2.9);
                    double mid = Fbm(u, v, 16, 3, 0.5, 5.5);
                    double stone = Smooth(0, Bevel, d);
                    // A crisp edge for colour (the height keeps its rounded shoulder).
                    double face = Smooth(0, ColourEdge, d);
                    // Pits: small holes where a fine noise peaks, gathered in clusters.
                    double pit = damaged ? Smooth(PitLevel, PitLevel + 0.08, Fbm(u, v, 220, 2, 0.5, 88.8)) * Smooth(-0.05, 0.35, Fbm(u, v, 20, 2, 0.5, 64.4)) * face : 0;
                    // Pale cracks: thin warped lines where a noise crosses zero, in places.
                    double vwx = 0.02 * Fbm(u, v, 32, 3, 0.6, 81.3), vwy = 0.02 * Fbm(u, v, 32, 3, 0.6, 91.9);
                    double vn = Fbm(u + vwx, v + vwy, 14, 5, 0.65, 61.7);
                    double vein = Math.Pow(Clamp01(1 - Math.Abs(vn) * VeinWidth), 4) * Smooth(0.1, 0.6, Fbm(u, v, 8, 2, 0.5, 71.1) + 0.5);
                    // Pale worn patches where the tops are rubbed smooth.
                    double worn = Smooth(0.15, 0.35, Fbm(u, v, 28, 4, 0.6, 51.9)) * WornShare;
                    // Height: worn into a dome, each stone a little higher or lower, tilted a
                    // touch, chipped by noise; joints low and gritty.
                    double dn = Clamp01(d / Math.Max(1e-4, _half * 0.9));
                    double dome = 1 - Math.Pow(1 - dn, DomePow);
                    // Chamfered: a flat top reached over a narrow chamfer, a little dome kept.
                    if (Chamfer > 0) dome = Lerp(Smooth(0, Chamfer, d), dome, DomeMix);
                    double tilt = (Hash(id, 11, 1) - 0.5) * 0.2 * (along - 0.5) + (Hash(id, 12, 1) - 0.5) * 0.2 * (across - 0.5);
                    double top = StoneTop * (0.85 + 0.3 * rnd) + StoneBump * mid + ChipDepth * fine + 0.006 * grain - VeinDepth * vein + tilt - PitDepth * pit + facet;
                    double hStone = Lerp(MortarHeight, top, dome);
                    double hMortar = MortarHeight + 0.04 * fine + 0.03 * mid;
                    height[y * n + x] = Clamp01(d > 0 ? hStone : hMortar);
                    // Stone colour: a cool grey range, mottled, darker down its shoulder.
                    double mottle = Fbm(u, v, 40, 3, 0.55, 23.1);
                    double b = 1 + (rnd - 0.5) * 2 * StoneVar + 0.08 * fine + 0.1 * mid + Mottle * mottle + 0.06 * grain + VeinLight * vein;
                    // Facets catch the painted light by their tilt (light from the upper left).
                    b += FacetLight * 0.1 * facetLight * face;
                    double hue = (Hash(id, 71, 1) - 0.5) * 2 * HueVar;
                    double sr = StoneR * b * (1 + hue), sg = StoneG * b * (1 + hue * 0.3), sb = StoneB * b * (1 + StoneBlue * (rnd2 - 0.3)) * (1 - hue);
                    double shoulder = 1 - EdgeDark * (1 - stone) + TopLight * Smooth(0.5, 1, stone) * (0.5 + mid);
                    // Light painted onto the worn edge, then the worn patches on top.
                    // A chamfered stone's light line sits on the chamfer itself, crisp.
                    double rim = Chamfer > 0 ? Smooth(0, Chamfer * 0.25, d) * Smooth(Chamfer * 1.1, Chamfer * 0.5, d) : Smooth(0.04, 0.2, dn) * Smooth(0.55, 0.25, dn);
                    shoulder += RimLight * rim + WornLight * worn * dome;
                    shoulder *= 1 - PitDark * pit;
                    sr *= shoulder; sg *= shoulder; sb *= shoulder;
                    // A few stones rust-stained.
                    if (rnd3 < RustShare) {
                        double rs = (0.5 + 0.5 * Smooth(-0.2, 0.4, mid)) * RustStrength;
                        sr = Lerp(sr, sr * 1.35, rs); sg = Lerp(sg, sg * 1.05, rs); sb = Lerp(sb, sb * 0.8, rs);
                    }
                    // Joints: dark dirt with paler sandy patches and grit.
                    double sand = Smooth(-0.1, 0.4, Fbm(u, v, 24, 3, 0.5, 33.3)) * SandShare;
                    double mk = 1 + 0.3 * fine + 0.15 * mid + 0.12 * grain;
                    double mr = Lerp(MortarR, SandR, sand) * mk, mg = Lerp(MortarG, SandG, sand) * mk, mb = Lerp(MortarB, SandB, sand) * mk;
                    // Grime: thick in the joints, a patchy brown film over the stones.
                    double film = Smooth(-0.1, 0.5, Fbm(u, v, 10, 4, 0.55, 41.3));
                    double g = Grime * Smooth(0.1, -0.4, mid) * (1 - stone) + StoneGrime * film * face * (1.2 - dome);
                    double tr = Lerp(mr, sr, face), tg = Lerp(mg, sg, face), tb = Lerp(mb, sb, face);
                    int o = (y * n + x) * 3;
                    albedo[o] = Lerp(tr, MortarR * 0.8, g); albedo[o + 1] = Lerp(tg, MortarG * 0.8, g); albedo[o + 2] = Lerp(tb, MortarB * 0.8, g);
                    smooth[y * n + x] = Lerp(SmoothMortar, SmoothStone * (0.85 + 0.3 * rnd2), stone) + 0.06 * fine - 0.3 * pit;
                }
            });
            WriteSet(dir, name, n, height, albedo, smooth);
        }

        /// <summary>
        /// Normal from the height's slope (wrapping), occlusion from how far a point sits below
        /// its surroundings, then all five maps saved.
        /// </summary>
        static void WriteSet(string dir, string name, int n, double[] height, double[] albedo, double[] smooth)
        {
            var normal = new byte[n * n * 3];
            var ao = new double[n * n];
            var blur = BoxBlur(height, n, 24);
            var blur2 = BoxBlur(height, n, 6);
            Parallel.For(0, n, y => {
                for (int x = 0; x < n; x++) {
                    int xl = (x + n - 1) % n, xr = (x + 1) % n, yu = (y + n - 1) % n, yd = (y + 1) % n;
                    double dhx = (height[y * n + xr] - height[y * n + xl]) * 0.5 * NormalStrength * n / 256.0;
                    double dhy = (height[yd * n + x] - height[yu * n + x]) * 0.5 * NormalStrength * n / 256.0;
                    // Tangent space, +Y up the texture (OpenGL, as Unity; image rows run down).
                    double nx = -dhx, ny = dhy, nz = 1, l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    int o = (y * n + x) * 3;
                    normal[o] = (byte)Math.Round((nx / l * 0.5 + 0.5) * 255);
                    normal[o + 1] = (byte)Math.Round((ny / l * 0.5 + 0.5) * 255);
                    normal[o + 2] = (byte)Math.Round((nz / l * 0.5 + 0.5) * 255);
                    double cav = (blur[y * n + x] - height[y * n + x]) + 0.5 * (blur2[y * n + x] - height[y * n + x]);
                    ao[y * n + x] = Clamp01(1 - Math.Max(0, cav) * AoStrength * 4);
                }
            });
            SaveRgb(dir + "\\" + name + "_Albedo.png", n, albedo);
            SaveBytes(dir + "\\" + name + "_Normal.png", n, normal);
            SaveGrey(dir + "\\" + name + "_Height.png", n, height);
            SaveGrey(dir + "\\" + name + "_Occlusion.png", n, ao);
            SaveGrey(dir + "\\" + name + "_Smoothness.png", n, smooth);
        }

        /// <summary>
        /// Box blur that wraps at the tile edge.
        /// </summary>
        static double[] BoxBlur(double[] src, int n, int radius)
        {
            var tmp = new double[n * n]; var dst = new double[n * n];
            Parallel.For(0, n, y => {
                double s = 0;
                for (int k = -radius; k <= radius; k++) s += src[y * n + ((k % n) + n) % n];
                for (int x = 0; x < n; x++) {
                    tmp[y * n + x] = s / (2 * radius + 1);
                    s += src[y * n + (x + radius + 1) % n] - src[y * n + ((x - radius) % n + n) % n];
                }
            });
            Parallel.For(0, n, x => {
                double s = 0;
                for (int k = -radius; k <= radius; k++) s += tmp[(((k % n) + n) % n) * n + x];
                for (int y = 0; y < n; y++) {
                    dst[y * n + x] = s / (2 * radius + 1);
                    s += tmp[((y + radius + 1) % n) * n + x] - tmp[(((y - radius) % n + n) % n) * n + x];
                }
            });
            return dst;
        }

        static void SaveRgb(string path, int n, double[] rgb)
        {
            var b = new byte[n * n * 3];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(rgb[i])));
            SaveBytes(path, n, b);
        }

        static void SaveGrey(string path, int n, double[] g)
        {
            var b = new byte[n * n * 3];
            for (int i = 0; i < n * n; i++) b[i * 3] = b[i * 3 + 1] = b[i * 3 + 2] = (byte)Math.Round(Clamp01(g[i]) * 255);
            SaveBytes(path, n, b);
        }

        static void SaveBytes(string path, int n, byte[] rgb)
        {
            var bmp = new Bitmap(n, n, PixelFormat.Format24bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var row = new byte[data.Stride];
            for (int y = 0; y < n; y++) {
                for (int x = 0; x < n; x++) { int s = (y * n + x) * 3; row[x * 3] = rgb[s + 2]; row[x * 3 + 1] = rgb[s + 1]; row[x * 3 + 2] = rgb[s]; }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
            }
            bmp.UnlockBits(data);
            bmp.Save(path, ImageFormat.Png);
            bmp.Dispose();
        }
    }
}
