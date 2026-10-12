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
    /// The look is painted, not photographed: every surface is built up
    /// from flat "dabs" of colour like the marks of a broad brush (see
    /// Dabs()), in a grimy, washed-out palette whose shadows are cool
    /// (blue-green greys) and whose lights are warm (ochres) - see
    /// Tone() - and the top edge of each stone, tile and plank is painted
    /// lighter and its bottom darker, as if lit from above.
    ///
    /// Assets are only made if missing, so anything retouched by hand (or
    /// a material retuned in the Inspector) is kept. Delete a file to have
    /// it made again, or use TeaLeaf > Repaint Smithy Textures to paint
    /// all five textures again over the old ones (the materials are
    /// kept). Materials use URP's Simple Lit shader with the shine
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

        // How strongly each normal map's bumps stand out (0 = the
        // texture has no normal map).
        private const float StoneBump = 5f;
        private const float TilesBump = 6f;
        private const float PlanksBump = 4f;

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
                Stone = GetMaterial("Stone", PaintStone, StoneBump),
                Tiles = GetMaterial("RoofTiles", PaintTiles, TilesBump),
                Planks = GetMaterial("Planks", PaintPlanks, PlanksBump),
                Iron = GetIronMaterial()
            };

            _cracks = null;
            return materials;
        }

        /// <summary>
        /// Menu command: paints all five textures again, over the PNG
        /// files already there. The materials are untouched and keep
        /// pointing at the same files, so the building in the scene
        /// changes at once.
        /// </summary>
        [MenuItem("TeaLeaf/Repaint Smithy Textures")]
        private static void Repaint()
        {
            if (!EditorUtility.DisplayDialog(
                "Smithy textures",
                $"Paint the smithy's five textures again, overwriting the PNG files in {TextureFolder}? Any retouching done by hand is lost. The materials are kept.",
                "Repaint",
                "Cancel")) {
                return;
            }

            EnsureFolder(TextureFolder);

            _cracks = DrawCracks();

            PaintTexture("Plaster", PaintPlaster, 0f);
            PaintTexture("Timber", PaintTimber, 0f);
            PaintTexture("Stone", PaintStone, StoneBump);
            PaintTexture("RoofTiles", PaintTiles, TilesBump);
            PaintTexture("Planks", PaintPlanks, PlanksBump);

            _cracks = null;
        }

        /// <summary>
        /// Where a texture's colour picture is saved.
        /// </summary>
        private static string ColourPath(string name)
        {
            return $"{TextureFolder}/Smithy{name}.png";
        }

        /// <summary>
        /// Where a texture's normal map is saved.
        /// </summary>
        private static string NormalPath(string name)
        {
            return $"{TextureFolder}/Smithy{name}_Normal.png";
        }

        /// <summary>
        /// Paints one texture's colour picture and (if bumpStrength is
        /// above 0) its normal map, and saves them.
        /// </summary>
        private static void PaintTexture(string name, Painter painter, float bumpStrength)
        {
            Paint(painter, bumpStrength, ColourPath(name), bumpStrength > 0f ? NormalPath(name) : null);
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

            string colourPath = ColourPath(name);
            string normalPath = NormalPath(name);
            bool hasNormal = bumpStrength > 0f;

            if (!File.Exists(colourPath) || (hasNormal && !File.Exists(normalPath))) {
                PaintTexture(name, painter, bumpStrength);
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

        // ---- Brushwork ----

        /// <summary>
        /// Brush dabs: the texture is cut into irregular patches, each
        /// one flat tone (0-1), like marks left by a loaded brush. One
        /// random point is dropped in every square of a grid cellsU
        /// across and cellsV up, and a pixel belongs to whichever point
        /// is nearest - so the patches are the areas nearest each point
        /// (a Voronoi pattern). Different numbers across and up stretch
        /// the dabs into strokes. The grid wraps, so the texture tiles.
        /// </summary>
        private static float Dabs(float u, float v, int cellsU, int cellsV, int seed)
        {
            float x = u * cellsU;
            float y = v * cellsV;
            int cellX = Mathf.FloorToInt(x);
            int cellY = Mathf.FloorToInt(y);

            float nearest = float.MaxValue;
            float tone = 0f;

            // The nearest point can only be in this square or one of the
            // eight round it.
            for (int dy = -1; dy <= 1; dy++) {
                for (int dx = -1; dx <= 1; dx++) {
                    int gridX = cellX + dx;
                    int gridY = cellY + dy;
                    int wrappedX = Wrap(gridX, cellsU);
                    int wrappedY = Wrap(gridY, cellsV);

                    float pointX = gridX + Hash(wrappedX, wrappedY, seed);
                    float pointY = gridY + Hash(wrappedX, wrappedY, seed + 1);
                    float distance = (pointX - x) * (pointX - x) + (pointY - y) * (pointY - y);

                    if (distance < nearest) {
                        nearest = distance;
                        tone = Hash(wrappedX, wrappedY, seed + 2);
                    }
                }
            }

            return tone;
        }

        /// <summary>
        /// A brushed surface, 0-1: broad dabs with smaller ones worked
        /// over them. The place each pixel looks the dabs up from is
        /// nudged by smooth noise first, which bends the dabs' straight
        /// edges into something a hand might have made.
        /// </summary>
        private static float Brushed(float u, float v, int cellsU, int cellsV, int seed)
        {
            float nudgedU = u + (ValueNoise(u, v, cellsU * 2, cellsV * 2, seed + 40) - 0.5f) * 0.6f / cellsU;
            float nudgedV = v + (ValueNoise(u, v, cellsU * 2, cellsV * 2, seed + 41) - 0.5f) * 0.6f / cellsV;

            float broad = Dabs(nudgedU, nudgedV, cellsU, cellsV, seed);
            float fine = Dabs(nudgedU, nudgedV, cellsU * 3, cellsV * 3, seed + 7);
            return broad * 0.65f + fine * 0.35f;
        }

        /// <summary>
        /// A colour for a tone (0 = darkest, 1 = lightest) from three
        /// paints: shadow up to middle over the first half, middle up to
        /// light over the second. Giving the shadow a cool colour and the
        /// light a warm one - rather than one colour made darker and
        /// lighter - is what makes a surface look painted.
        /// </summary>
        private static Color Tone(Color shadow, Color middle, Color light, float tone)
        {
            tone = Mathf.Clamp01(tone);

            return tone < 0.5f
                ? Color.Lerp(shadow, middle, tone * 2f)
                : Color.Lerp(middle, light, tone * 2f - 1f);
        }

        // ---- Painters ----

        /// <summary>
        /// Old plaster: a dirty cream brushed on in broad dabs, greenish
        /// grey in its shadows, with brown grime in patches and a few
        /// hairline cracks.
        /// </summary>
        private static void PaintPlaster(float u, float v, out Color color, out float height)
        {
            float brush = Brushed(u, v, 5, 5, 11);
            float blotch = Layered(u, v, 3, 3, 3, 17);
            float grime = Mathf.SmoothStep(0.5f, 0.8f, Layered(u, v, 3, 3, 3, 37));

            color = Tone(
                new Color(0.33f, 0.36f, 0.33f),
                new Color(0.60f, 0.58f, 0.49f),
                new Color(0.79f, 0.75f, 0.63f),
                0.25f + 0.45f * brush + 0.3f * blotch);

            color = Color.Lerp(color, new Color(0.30f, 0.27f, 0.20f), grime * 0.5f);

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
        /// Weathered oak beams: long brush strokes and grain running
        /// along u (the beam's length), near black in the shadows and a
        /// worn warm brown in the lights, with a few darker streaks.
        /// </summary>
        private static void PaintTimber(float u, float v, out Color color, out float height)
        {
            float brush = Brushed(u, v, 2, 10, 13);
            float grain = Layered(u, v, 3, 40, 3, 5);
            float streak = ValueNoise(u, v, 2, 90, 9);
            streak = streak * streak * streak;

            color = Tone(
                new Color(0.07f, 0.065f, 0.06f),
                new Color(0.21f, 0.16f, 0.12f),
                new Color(0.40f, 0.31f, 0.21f),
                0.15f + 0.5f * brush + 0.35f * grain);

            color *= 1f - 0.4f * streak;
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
            float brush = Brushed(u, v, 8, 10, 73);
            float moss = Mathf.SmoothStep(0.58f, 0.75f, Layered(u, v, 3, 3, 4, 83)) * (0.4f + 0.6f * mottle);

            // Each block its own tone, brushed: blue-grey in the
            // shadows, a warm grey in the lights; the odd block ochre.
            Color stone = Tone(
                new Color(0.16f, 0.19f, 0.21f),
                new Color(0.36f, 0.38f, 0.37f),
                new Color(0.60f, 0.58f, 0.51f),
                0.15f + 0.45f * tone + 0.4f * brush);

            stone = Color.Lerp(stone, new Color(0.45f, 0.36f, 0.24f), warmth > 0.75f ? 0.5f : 0f);
            stone = Color.Lerp(stone, new Color(0.20f, 0.27f, 0.14f), moss * 0.5f);

            // Painted light: v runs up the wall, so the top of a block
            // is where inRow nears 1. Lighter there, darker at its foot.
            float lit = Mathf.SmoothStep(0.72f, 0.95f, inRow);
            float shaded = 1f - Mathf.SmoothStep(0.05f, 0.3f, inRow);
            stone *= 1f + 0.25f * lit - 0.25f * shaded;

            Color mortar = new Color(0.11f, 0.11f, 0.10f) * (0.8f + 0.4f * mottle);

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
            float mottle = Layered(u, v, 32, 32, 3, 101);
            float brush = Brushed(u, v, 10, 8, 103);

            // Each tile its own tone, brushed: a dull rust red, nearly
            // black in the shadows. About one tile in seven is a
            // replacement in grey slate.
            Color tile = Tone(
                new Color(0.17f, 0.10f, 0.10f),
                new Color(0.40f, 0.19f, 0.14f),
                new Color(0.62f, 0.34f, 0.22f),
                0.1f + 0.5f * tone + 0.4f * brush);

            if (Hash(row, column, 97) < 0.15f) {
                tile = Color.Lerp(tile, new Color(0.27f, 0.26f, 0.30f), 0.7f);
            }

            // Painted light: a lighter band just above the tile's thick
            // bottom edge, which catches the light.
            float lip = Mathf.SmoothStep(0.76f, 0.88f, inRow) * (1f - Mathf.SmoothStep(0.9f, 0.96f, inRow));
            tile *= 1f + 0.3f * lip;

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

            // Long strokes down each plank, from a different place in
            // each as the grain is.
            float brush = Brushed(u, v + Hash(plank, 2, 111), 12, 3, 117);

            Color wood = Tone(
                new Color(0.10f, 0.085f, 0.07f),
                new Color(0.27f, 0.20f, 0.14f),
                new Color(0.46f, 0.36f, 0.24f),
                0.1f + 0.3f * tone + 0.3f * brush + 0.3f * grain);

            // Painted light: one edge of every plank a little lighter.
            float edgeLight = Mathf.SmoothStep(0.05f, 0.12f, inPlank) * (1f - Mathf.SmoothStep(0.12f, 0.3f, inPlank));
            wood *= 1f + 0.2f * edgeLight;

            color = wood * Mathf.Lerp(0.3f, 1f, groove);
            height = groove * 0.8f + grain * 0.2f;
        }
    }
}
