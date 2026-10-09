using System.IO;
using UnityEditor;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Makes the textures and materials for SmithyBuilding: plaster,
    /// timber, stone, roof tiles, planks and iron. Every texture is painted
    /// here, pixel by pixel, from a few layers of random noise and simple
    /// patterns (rows of stones, rows of tiles), then saved as an ordinary
    /// PNG - so the game just reads textures, the cheapest thing a shader
    /// can do, and the pictures can be opened and repainted by hand.
    ///
    /// Every texture tiles: its left edge matches its right and its top
    /// matches its bottom, so it can repeat across a wall with no seam.
    /// That's why all the noise here "wraps" - see ValueNoise().
    ///
    /// Stone, tiles and planks also get a normal map (fine bumps for the
    /// lighting: mortar lines, tile edges, plank gaps), worked out from a
    /// height painted alongside the colour. Plaster and timber don't: the
    /// beams are real geometry, and an extra texture read on every pixel
    /// of the biggest surfaces isn't worth it on Quest.
    ///
    /// Assets are only made if missing, so anything retouched by hand (or
    /// a material retuned in the Inspector) is kept. Delete a file to have
    /// it made again. Materials use URP's Simple Lit shader with the shine
    /// switched off (iron apart): the cheapest lit shader that reads a
    /// texture.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class SmithyTextures
    {
        /// <summary>
        /// The six materials, in the order SmithyBuilding uses them.
        /// </summary>
        public struct Materials
        {
            public Material Plaster;
            public Material Timber;
            public Material Stone;
            public Material Tiles;
            public Material Planks;
            public Material Iron;
        }

        // How many metres one repeat of each texture covers on the
        // building (the patterns below are painted to suit: 6 courses of
        // stone in 1.5m, 8 rows of tiles in 2m, 6 planks in 1.2m).
        public const float PlasterTileSize = 2f;
        public const float TimberTileSize = 1f;
        public const float StoneTileSize = 1.5f;
        public const float TilesTileSize = 2f;
        public const float PlanksTileSize = 1.2f;

        private const string TextureFolder = "Assets/Art/Textures/Smithy";
        private const string MaterialFolder = "Assets/Art/Materials/Smithy";
        private const string ShaderName = "Universal Render Pipeline/Simple Lit";

        private const int Size = 512;

        // Paints one pixel: u and v run 0-1 across and up the texture.
        // height (0-1) is only used by textures with a normal map.
        private delegate void Painter(float u, float v, out Color color, out float height);

        // The plaster's cracks, drawn once before the plaster is painted:
        // how dark each pixel's crack is, 0-1.
        private static float[] _cracks;

        /// <summary>
        /// Returns the materials, making any that are missing (and their
        /// textures).
        /// </summary>
        public static Materials GetMaterials()
        {
            EnsureFolder(TextureFolder);
            EnsureFolder(MaterialFolder);

            _cracks = DrawCracks();

            Materials materials = new() {
                Plaster = GetMaterial("Plaster", PaintPlaster, 0f),
                Timber = GetMaterial("Timber", PaintTimber, 0f),
                Stone = GetMaterial("Stone", PaintStone, 5f),
                Tiles = GetMaterial("RoofTiles", PaintTiles, 6f),
                Planks = GetMaterial("Planks", PaintPlanks, 4f),
                Iron = GetIronMaterial()
            };

            _cracks = null;
            return materials;
        }

        /// <summary>
        /// One textured material: loaded if it exists, otherwise made, with
        /// its colour texture and (if bumpStrength is above 0) a normal map.
        /// </summary>
        private static Material GetMaterial(string name, Painter painter, float bumpStrength)
        {
            string materialPath = $"{MaterialFolder}/Smithy{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material != null) {
                return material;
            }

            string colourPath = $"{TextureFolder}/Smithy{name}.png";
            string normalPath = $"{TextureFolder}/Smithy{name}_Normal.png";
            bool hasNormal = bumpStrength > 0f;

            if (!File.Exists(colourPath) || (hasNormal && !File.Exists(normalPath))) {
                Paint(painter, bumpStrength, colourPath, hasNormal ? normalPath : null);
            }

            material = new Material(Shader.Find(ShaderName)) { name = $"Smithy{name}" };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(colourPath));
            material.SetColor("_BaseColor", Color.white);

            if (hasNormal) {
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                material.EnableKeyword("_NORMALMAP");
            }

            TurnShineOff(material);
            AssetDatabase.CreateAsset(material, materialPath);
            return material;
        }

        /// <summary>
        /// Iron needs no texture: a dark colour with a dull shine.
        /// </summary>
        private static Material GetIronMaterial()
        {
            string materialPath = $"{MaterialFolder}/SmithyIron.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material != null) {
                return material;
            }

            material = new Material(Shader.Find(ShaderName)) { name = "SmithyIron" };
            material.SetColor("_BaseColor", new Color(0.1f, 0.1f, 0.11f));
            material.SetColor("_SpecColor", new Color(0.25f, 0.25f, 0.25f));
            material.SetFloat("_Smoothness", 0.45f);
            AssetDatabase.CreateAsset(material, materialPath);
            return material;
        }

        /// <summary>
        /// Switches a Simple Lit material's highlights off: plaster, wood
        /// and stone don't shine, and it saves the shader the work.
        /// </summary>
        private static void TurnShineOff(Material material)
        {
            material.SetFloat("_SpecularHighlights", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.SetFloat("_Smoothness", 0f);
        }

        /// <summary>
        /// Paints a texture with painter and saves it as a PNG; and, given
        /// a normalPath, saves the normal map made from the painted height.
        /// </summary>
        private static void Paint(Painter painter, float bumpStrength, string colourPath, string normalPath)
        {
            Color[] colours = new Color[Size * Size];
            float[] heights = new float[Size * Size];

            for (int y = 0; y < Size; y++) {
                for (int x = 0; x < Size; x++) {
                    // The middle of the pixel, so 0 and 1 (the same place
                    // on a tiling texture) are never both painted.
                    painter((x + 0.5f) / Size, (y + 0.5f) / Size, out Color colour, out float height);
                    colour.a = 1f;
                    colours[y * Size + x] = colour;
                    heights[y * Size + x] = height;
                }
            }

            Save(colours, colourPath, false);

            if (normalPath == null) {
                return;
            }

            // A normal map stores, per pixel, which way the surface
            // leans: found from how the height changes to the pixels
            // either side and above and below (wrapping round the edges,
            // since the texture tiles). Leaning is stored as a colour:
            // red for left-right, green for up-down, blue for straight
            // out, each squeezed from -1..1 into 0..1.
            Color[] normals = new Color[Size * Size];

            for (int y = 0; y < Size; y++) {
                for (int x = 0; x < Size; x++) {
                    float left = heights[y * Size + (x + Size - 1) % Size];
                    float right = heights[y * Size + (x + 1) % Size];
                    float below = heights[(y + Size - 1) % Size * Size + x];
                    float above = heights[(y + 1) % Size * Size + x];

                    Vector3 normal = new Vector3((left - right) * bumpStrength, (below - above) * bumpStrength, 1f).normalized;
                    normals[y * Size + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
                }
            }

            Save(normals, normalPath, true);
        }

        /// <summary>
        /// Writes pixels to a PNG and sets how Unity imports it: repeating,
        /// with mipmaps (smaller copies used at a distance, which stop it
        /// shimmering), and as a normal map or a colour picture.
        /// </summary>
        private static void Save(Color[] pixels, string path, bool isNormalMap)
        {
            Texture2D texture = new(Size, Size, TextureFormat.RGBA32, false, isNormalMap);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 2;

            if (!isNormalMap) {
                importer.sRGBTexture = true;
            }

            importer.SaveAndReimport();
        }

        /// <summary>
        /// Makes a folder under Assets if it isn't there, and any folders
        /// above it.
        /// </summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---- Noise ----

        /// <summary>
        /// A random number 0-1 from two whole numbers and a seed: the same
        /// three always give the same answer. (Multiply by big odd numbers
        /// and fold the bits over each other until no pattern is left.)
        /// </summary>
        private static float Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }

        /// <summary>
        /// Smooth noise, 0-1, that wraps: a random number at every corner
        /// of a grid periodU squares across and periodV up, blended
        /// smoothly across each square - and the grid's last column and
        /// row use the first's numbers again, so the texture's edges
        /// match. Different periods across and up stretch the blobs (wood
        /// grain).
        /// </summary>
        private static float ValueNoise(float u, float v, int periodU, int periodV, int seed)
        {
            float x = u * periodU;
            float y = v * periodV;
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;

            // Eases the blend so the grid's lines don't show.
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            int xa = Wrap(x0, periodU);
            int xb = Wrap(x0 + 1, periodU);
            int ya = Wrap(y0, periodV);
            int yb = Wrap(y0 + 1, periodV);

            float bottom = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx);
            float top = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx);
            return Mathf.Lerp(bottom, top, fy);
        }

        /// <summary>
        /// Several layers of ValueNoise(), each twice as fine and half as
        /// strong as the last: big soft patches with smaller detail on
        /// top. 0-1.
        /// </summary>
        private static float Layered(float u, float v, int periodU, int periodV, int layers, int seed)
        {
            float sum = 0f;
            float strength = 0.5f;
            float total = 0f;

            for (int i = 0; i < layers; i++) {
                sum += ValueNoise(u, v, periodU << i, periodV << i, seed + i * 17) * strength;
                total += strength;
                strength *= 0.5f;
            }

            return sum / total;
        }

        /// <summary>
        /// value brought into 0..period-1, going round like a clock (also
        /// for negative values, which % alone gets wrong).
        /// </summary>
        private static int Wrap(int value, int period)
        {
            return (value % period + period) % period;
        }

        // ---- Painters ----

        /// <summary>
        /// Limewashed plaster: off-white, with large soft blotches, a fine
        /// grain, brownish stains in patches and a few hairline cracks.
        /// </summary>
        private static void PaintPlaster(float u, float v, out Color color, out float height)
        {
            float blotch = Layered(u, v, 4, 4, 4, 11);
            float grain = Layered(u, v, 48, 48, 2, 23);
            float stain = Mathf.SmoothStep(0.55f, 0.8f, Layered(u, v, 3, 3, 3, 37));

            Color clean = new(0.80f, 0.77f, 0.70f);
            Color stained = new(0.58f, 0.52f, 0.42f);

            color = Color.Lerp(clean, stained, stain * 0.45f);
            color *= 0.86f + 0.2f * blotch + 0.08f * (grain - 0.5f);

            int x = Mathf.Min((int)(u * Size), Size - 1);
            int y = Mathf.Min((int)(v * Size), Size - 1);
            color *= 1f - 0.55f * _cracks[y * Size + x];

            height = 0f;
        }

        /// <summary>
        /// Draws the plaster's cracks into a grid the size of the texture:
        /// each crack is a walk of one-pixel steps that mostly keeps its
        /// direction and wanders a little, wrapping round the edges.
        /// </summary>
        private static float[] DrawCracks()
        {
            float[] cracks = new float[Size * Size];
            System.Random random = new(7);

            for (int crack = 0; crack < 9; crack++) {
                float x = (float)random.NextDouble() * Size;
                float y = (float)random.NextDouble() * Size;
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                int steps = random.Next(80, 260);

                for (int step = 0; step < steps; step++) {
                    angle += ((float)random.NextDouble() - 0.5f) * 0.5f;
                    x += Mathf.Cos(angle);
                    y += Mathf.Sin(angle);

                    int px = Wrap(Mathf.RoundToInt(x), Size);
                    int py = Wrap(Mathf.RoundToInt(y), Size);

                    // Thinner towards its end.
                    float strength = 1f - step / (float)steps * 0.6f;
                    cracks[py * Size + px] = Mathf.Max(cracks[py * Size + px], strength);

                    // A fainter pixel beside it, so it isn't a jagged line.
                    int side = Wrap(px + 1, Size);
                    cracks[py * Size + side] = Mathf.Max(cracks[py * Size + side], strength * 0.35f);
                }
            }

            return cracks;
        }

        /// <summary>
        /// Dark oak beams: grain running along u (the beam's length), from
        /// noise stretched long and thin, with a few darker streaks.
        /// </summary>
        private static void PaintTimber(float u, float v, out Color color, out float height)
        {
            float grain = Layered(u, v, 3, 40, 3, 5);
            float streak = ValueNoise(u, v, 2, 90, 9);
            streak = streak * streak * streak;
            float patch = Layered(u, v, 2, 3, 2, 13);

            Color dark = new(0.13f, 0.085f, 0.055f);
            Color light = new(0.29f, 0.20f, 0.13f);

            color = Color.Lerp(dark, light, grain * 0.7f + patch * 0.3f);
            color *= 1f - 0.45f * streak;
            height = 0f;
        }

        /// <summary>
        /// Coursed rubble stone: six rows, each cut into three to five
        /// blocks of uneven length, grey with the odd brownish block, moss
        /// in patches, and darker mortar between. The height (for the
        /// normal map) is flat on a block's face, rounded off at its edges
        /// and sunk at the mortar.
        /// </summary>
        private static void PaintStone(float u, float v, out Color color, out float height)
        {
            const int rows = 6;

            float rowPosition = v * rows;
            int row = Mathf.FloorToInt(rowPosition);
            float inRow = rowPosition - row;

            // How many blocks this row has, and where along it this pixel
            // is, counted in blocks. Each row starts at a different place.
            int blocks = 3 + (int)(Hash(row, 0, 51) * 2.99f);
            float along = (u + Hash(row, 1, 51)) * blocks;

            // The joint before block k is nudged up to a quarter of a
            // block either way, so the blocks differ in length. Start from
            // the whole number below the pixel's place, then step back or
            // on one block if the nudged joints say it's really in a
            // neighbour.
            int k = Mathf.FloorToInt(along);

            if (along < StoneJoint(row, k, blocks)) {
                k--;
            } else if (along >= StoneJoint(row, k + 1, blocks)) {
                k++;
            }

            float joint = StoneJoint(row, k, blocks);
            float nextJoint = StoneJoint(row, k + 1, blocks);
            int block = Wrap(k, blocks);

            // Distance to the block's nearest edge, in row heights (a row
            // is 1/rows of the texture high; a block is 1/blocks long).
            float toSide = Mathf.Min(along - joint, nextJoint - along) / blocks * rows;
            float toTopOrBottom = Mathf.Min(inRow, 1f - inRow);
            float edge = Mathf.Min(toSide, toTopOrBottom);

            float isStone = Mathf.SmoothStep(0.03f, 0.09f, edge);
            float bevel = Mathf.SmoothStep(0.03f, 0.22f, edge);

            float tone = Hash(row, block, 61);
            float warmth = Hash(row, block, 67);
            float mottle = Layered(u, v, 24, 24, 3, 71);
            float moss = Mathf.SmoothStep(0.58f, 0.75f, Layered(u, v, 3, 3, 4, 83)) * (0.4f + 0.6f * mottle);

            Color stone = Color.Lerp(new Color(0.30f, 0.30f, 0.29f), new Color(0.50f, 0.49f, 0.46f), tone);
            stone = Color.Lerp(stone, new Color(0.44f, 0.37f, 0.28f), warmth > 0.75f ? 0.5f : 0f);
            stone *= 0.8f + 0.4f * mottle;
            stone = Color.Lerp(stone, new Color(0.22f, 0.30f, 0.12f), moss * 0.7f);

            Color mortar = new Color(0.20f, 0.19f, 0.17f) * (0.8f + 0.4f * mottle);

            color = Color.Lerp(mortar, stone, isStone);
            height = isStone * (0.55f + 0.3f * bevel) + mottle * 0.15f;
        }

        /// <summary>
        /// Where the joint before block k of a stone row is, counted in
        /// blocks along the row: k itself, nudged up to a quarter of a
        /// block either way. The row's blocks repeat, so block k and block
        /// k + blocks get the same nudge - which is what lets the texture
        /// tile.
        /// </summary>
        private static float StoneJoint(int row, int k, int blocks)
        {
            return k + (Hash(row, Wrap(k, blocks) + 2, 53) - 0.5f) * 0.5f;
        }

        /// <summary>
        /// Clay roof tiles: eight rows of ten, every other row moved half
        /// a tile along. v runs DOWN the roof (SmithyBuilding lays it that
        /// way), so within a row v goes from the top of the tile, tucked
        /// in shadow under the row above, to its thick bottom edge. The
        /// height rises the same way, which makes each row step up over
        /// the next in the lighting.
        /// </summary>
        private static void PaintTiles(float u, float v, out Color color, out float height)
        {
            const int rows = 8;
            const int columns = 10;

            float rowPosition = v * rows;
            int row = Mathf.FloorToInt(rowPosition);
            float inRow = rowPosition - row;

            float columnPosition = u * columns + (row % 2 == 0 ? 0f : 0.5f);
            int column = Wrap(Mathf.FloorToInt(columnPosition), columns);
            float inColumn = columnPosition - Mathf.Floor(columnPosition);

            // The gap between one tile and the next along the row.
            float gap = Mathf.SmoothStep(0f, 0.07f, Mathf.Min(inColumn, 1f - inColumn));

            // Dark just below the row above, light towards the bottom
            // edge, with the very edge darkened again.
            float shade = Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0f, 0.3f, inRow));
            shade *= Mathf.Lerp(1f, 0.75f, Mathf.SmoothStep(0.93f, 1f, inRow));

            float tone = Hash(row, column, 91);
            float soot = Hash(row, column, 97) < 0.12f ? 0.7f : 1f;
            float mottle = Layered(u, v, 32, 32, 3, 101);

            Color tile = Color.Lerp(new Color(0.42f, 0.13f, 0.09f), new Color(0.60f, 0.23f, 0.14f), tone);
            tile *= soot * (0.82f + 0.36f * mottle);

            color = tile * shade * Mathf.Lerp(0.45f, 1f, gap);
            height = inRow * gap * 0.85f + mottle * 0.1f;
        }

        /// <summary>
        /// Sawn planks: six side by side across u, running along v, each
        /// its own shade, with grain along its length and a dark groove
        /// between one and the next.
        /// </summary>
        private static void PaintPlanks(float u, float v, out Color color, out float height)
        {
            const int planks = 6;

            float position = u * planks;
            int plank = Mathf.FloorToInt(position);
            float inPlank = position - plank;

            float tone = Hash(plank, 0, 111);

            // Each plank reads the grain from a different place along it,
            // so neighbours don't match.
            float grain = Layered(u, v + Hash(plank, 1, 111), 48, 4, 3, 113);
            float groove = Mathf.SmoothStep(0f, 0.05f, Mathf.Min(inPlank, 1f - inPlank));

            Color wood = Color.Lerp(new Color(0.24f, 0.16f, 0.10f), new Color(0.36f, 0.25f, 0.15f), tone);
            wood *= 0.78f + 0.44f * grain;

            color = wood * Mathf.Lerp(0.3f, 1f, groove);
            height = groove * 0.8f + grain * 0.2f;
        }
    }
}
