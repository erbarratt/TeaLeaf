using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGen
{
    /// <summary>
    /// One material for every building proxy of a sector (BuildingProxies):
    /// each plain opaque URP Lit material the proxies draw with gets a tile
    /// in three atlases (colour, normal map, metallic/occlusion/smoothness),
    /// the same tile in all three, and the proxies' triangles drawn with it
    /// have their texture coordinates moved into that tile. A proxy then
    /// draws in one go instead of once per material, and so does its shadow.
    ///
    /// Each tile is shrunk from the material's own textures on the GPU (the
    /// source's mip chain does the filtering), then the material's own
    /// settings - colour tint, normal strength, smoothness, metallic,
    /// occlusion strength - are baked into the tile's pixels, so the atlas
    /// material itself is plain: white, full strength. Round each tile's
    /// picture its edge pixels are repeated outwards (padding), so the
    /// atlas's smaller mip levels don't blend one tile into the next.
    ///
    /// Kept on their own materials: anything else (alpha clipped foliage,
    /// glowing windows, other shaders, tiled or offset textures), and every
    /// triangle whose texture coordinates leave the texture's square (a
    /// repeating texture, which a tile can't repeat).
    /// </summary>
    internal class ProxyAtlas
    {
        private const string LitShader = "Universal Render Pipeline/Lit";

        // How far (in texture coordinates) a corner may stand outside the
        // texture's square and still be drawn from the tile, pulled onto
        // its edge: the pack's coordinates stray a little past 0 and 1.
        private const float SquareTolerance = 0.005f;

        private static readonly int _baseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int _mainTex = Shader.PropertyToID("_MainTex");
        private static readonly int _baseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int _color = Shader.PropertyToID("_Color");
        private static readonly int _bumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int _bumpScale = Shader.PropertyToID("_BumpScale");
        private static readonly int _metallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int _metallic = Shader.PropertyToID("_Metallic");
        private static readonly int _smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int _occlusionMap = Shader.PropertyToID("_OcclusionMap");
        private static readonly int _occlusionStrength = Shader.PropertyToID("_OcclusionStrength");

        /// <summary>
        /// The one material every atlased triangle is drawn with.
        /// </summary>
        public Material Material { get; private set; }

        /// <summary>
        /// Materials in the atlas.
        /// </summary>
        public int Tiles => _cells.Count;

        /// <summary>
        /// Materials the proxies use that stay on their own (not plain
        /// opaque URP Lit, or no tile left).
        /// </summary>
        public int KeptApart { get; private set; }

        // Each atlased material's picture in the atlas: offset and scale
        // from its own 0-1 texture coordinates into the atlas's.
        private readonly Dictionary<Material, Vector4> _cells = new();

        /// <summary>
        /// Builds the sector's atlases and atlas material (saved under
        /// folder, named from prefix) from the materials the proxies will be
        /// drawn with, in the order given. Gives back null when none of them
        /// can go in an atlas. Must run outside AssetDatabase.StartAssetEditing
        /// (the atlases are imported before the material takes them).
        /// </summary>
        internal static ProxyAtlas Build(string folder, string prefix, List<Material> materials, GeneratorSettings settings)
        {
            int size = settings.ProxyAtlasSize;
            int tile = Mathf.Min(settings.ProxyAtlasTile, size);
            int padding = Mathf.Clamp(settings.ProxyAtlasPadding, 0, tile / 4);
            int content = tile - 2 * padding;
            int perRow = size / tile;
            ProxyAtlas atlas = new();
            List<Material> atlased = new();

            foreach (Material material in materials) {
                if (!CanAtlas(material) || atlased.Count == perRow * perRow) {
                    atlas.KeptApart++;
                    continue;
                }

                atlased.Add(material);
            }

            if (atlased.Count == 0) {
                return null;
            }

            Color32[] albedo = new Color32[size * size];
            Color32[] normal = new Color32[size * size];
            Color32[] mrao = new Color32[size * size];

            for (int i = 0; i < atlased.Count; i++) {
                Material material = atlased[i];
                int x = i % perRow * tile;
                int y = i / perRow * tile;
                // Tile picture from 0-1: pixel (x + padding) to (x + padding + content).
                atlas._cells[material] = new Vector4((x + padding) / (float)size, (y + padding) / (float)size, content / (float)size, content / (float)size);

                Color[] colours = AlbedoTile(material, content);
                Color[] normals = NormalTile(material, content);
                Color[] masks = MaskTile(material, content);
                PutTile(albedo, size, x, y, tile, padding, content, colours, true);
                PutTile(normal, size, x, y, tile, padding, content, normals, false);
                PutTile(mrao, size, x, y, tile, padding, content, masks, false);
            }

            string path = $"{folder}/{prefix}Proxy Atlas";
            Texture2D albedoMap = SaveAtlas($"{path} Albedo.png", size, albedo, TextureImporterType.Default, true, false);
            Texture2D normalMap = SaveAtlas($"{path} Normal.png", size, normal, TextureImporterType.NormalMap, false, false);
            Texture2D maskMap = SaveAtlas($"{path} MRAO.png", size, mrao, TextureImporterType.Default, false, true);

            // Plain URP Lit: white, full strength, every map on (the tiles
            // hold each material's own settings).
            Material made = new(Shader.Find(LitShader)) {
                name = Path.GetFileName(path)
            };
            made.SetTexture(_baseMap, albedoMap);
            made.SetTexture(_mainTex, albedoMap);
            made.SetColor(_baseColor, Color.white);
            made.SetColor(_color, Color.white);
            made.SetTexture(_bumpMap, normalMap);
            made.SetFloat(_bumpScale, 1f);
            made.SetTexture(_metallicGlossMap, maskMap);
            made.SetFloat(_metallic, 0f);
            made.SetFloat(_smoothness, 1f);
            made.SetTexture(_occlusionMap, maskMap);
            made.SetFloat(_occlusionStrength, 1f);
            made.EnableKeyword("_NORMALMAP");
            made.EnableKeyword("_METALLICSPECGLOSSMAP");
            made.EnableKeyword("_OCCLUSIONMAP");
            AssetDatabase.CreateAsset(made, $"{path}.mat");
            atlas.Material = made;
            return atlas;
        }

        /// <summary>
        /// Whether a material can be drawn from the atlas: URP Lit, metallic
        /// workflow, opaque, not clipped, not glowing, back faces culled,
        /// smoothness in the metallic map's alpha, highlights and reflections
        /// on, no detail or height map, its colour texture neither tiled nor
        /// offset (the atlas material's own settings, so it looks the same).
        /// </summary>
        private static bool CanAtlas(Material material)
        {
            if (material == null || material.shader == null || material.shader.name != LitShader) {
                return false;
            }

            return Is(material, "_WorkflowMode", 1f) && Is(material, "_Surface", 0f) && Is(material, "_AlphaClip", 0f)
                && Is(material, "_Cull", (float)CullMode.Back) && Is(material, "_SmoothnessTextureChannel", 0f)
                && Is(material, "_SpecularHighlights", 1f) && Is(material, "_EnvironmentReflections", 1f)
                && !material.IsKeywordEnabled("_EMISSION")
                && material.GetTexture("_DetailAlbedoMap") == null && material.GetTexture("_DetailNormalMap") == null
                && material.GetTexture("_ParallaxMap") == null
                && material.GetTextureScale(_baseMap) == Vector2.one && material.GetTextureOffset(_baseMap) == Vector2.zero;
        }

        private static bool Is(Material material, string property, float value)
        {
            return !material.HasProperty(property) || Mathf.Approximately(material.GetFloat(property), value);
        }

        /// <summary>
        /// The material's colour as the shader sees it (linear): its texture
        /// times its tint.
        /// </summary>
        private static Color[] AlbedoTile(Material material, int content)
        {
            Color tint = material.GetColor(_baseColor).linear;
            Texture texture = material.GetTexture(_baseMap);
            Color[] pixels = texture != null ? Sample(texture, content) : Filled(content, Color.white);

            for (int p = 0; p < pixels.Length; p++) {
                pixels[p] = new Color(pixels[p].r * tint.r, pixels[p].g * tint.g, pixels[p].b * tint.b, 1f);
            }

            return pixels;
        }

        /// <summary>
        /// The material's normal map unpacked and scaled by its strength, as
        /// the shader would (stored either as RG or as AG: x is red times
        /// alpha, y green), packed again as plain RGB; flat without one.
        /// </summary>
        private static Color[] NormalTile(Material material, int content)
        {
            Texture texture = material.IsKeywordEnabled("_NORMALMAP") ? material.GetTexture(_bumpMap) : null;

            if (texture == null) {
                return Filled(content, new Color(0.5f, 0.5f, 1f, 1f));
            }

            float scale = material.GetFloat(_bumpScale);
            Color[] pixels = Sample(texture, content);

            for (int p = 0; p < pixels.Length; p++) {
                Vector2 xy = new Vector2(pixels[p].r * pixels[p].a * 2f - 1f, pixels[p].g * 2f - 1f) * scale;
                float z = Mathf.Sqrt(Mathf.Max(0f, 1f - Vector2.Dot(xy, xy)));
                Vector3 n = new Vector3(xy.x, xy.y, z).normalized;
                pixels[p] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }

            return pixels;
        }

        /// <summary>
        /// Metallic (red), occlusion (green) and smoothness (alpha) as the
        /// shader reads them: metallic and smoothness from the metallic map
        /// (smoothness times the material's), else the material's values;
        /// occlusion from the occlusion map eased towards white by its
        /// strength, else white.
        /// </summary>
        private static Color[] MaskTile(Material material, int content)
        {
            Texture metallicMap = material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") ? material.GetTexture(_metallicGlossMap) : null;
            Texture occlusionMap = material.IsKeywordEnabled("_OCCLUSIONMAP") ? material.GetTexture(_occlusionMap) : null;
            float metallic = material.GetFloat(_metallic);
            float smoothness = material.GetFloat(_smoothness);
            float strength = material.GetFloat(_occlusionStrength);
            Color[] metals = metallicMap != null ? Sample(metallicMap, content) : null;
            // The pack's two maps are one texture: sampled once.
            Color[] occlusions = occlusionMap == null ? null : occlusionMap == metallicMap ? metals : Sample(occlusionMap, content);
            Color[] pixels = new Color[content * content];

            for (int p = 0; p < pixels.Length; p++) {
                float m = metals != null ? metals[p].r : metallic;
                float s = metals != null ? metals[p].a * smoothness : smoothness;
                float o = occlusions != null ? Mathf.Lerp(1f, occlusions[p].g, strength) : 1f;
                pixels[p] = new Color(m, o, 0f, s);
            }

            return pixels;
        }

        /// <summary>
        /// The texture drawn at content x content pixels on the GPU, read
        /// back as the shader would sample it (linear; an sRGB texture is
        /// decoded). The GPU picks the mip level nearest the new size.
        /// </summary>
        private static Color[] Sample(Texture texture, int content)
        {
            RenderTexture target = RenderTexture.GetTemporary(content, content, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            Texture2D read = new(content, content, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, content, content), 0, 0, false);
            Color[] pixels = read.GetPixels();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(read);
            return pixels;
        }

        private static Color[] Filled(int content, Color colour)
        {
            Color[] pixels = new Color[content * content];
            System.Array.Fill(pixels, colour);
            return pixels;
        }

        /// <summary>
        /// Writes a tile's picture into the atlas at (x, y), its edge pixels
        /// repeated out across the padding; colour pictures (srgb) encoded
        /// from linear as the sRGB atlas stores them.
        /// </summary>
        private static void PutTile(Color32[] atlas, int size, int x, int y, int tile, int padding, int content, Color[] pixels, bool srgb)
        {
            for (int ty = 0; ty < tile; ty++) {
                int sy = Mathf.Clamp(ty - padding, 0, content - 1);

                for (int tx = 0; tx < tile; tx++) {
                    int sx = Mathf.Clamp(tx - padding, 0, content - 1);
                    Color c = pixels[sy * content + sx];

                    if (srgb) {
                        c = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), c.a);
                    }

                    atlas[(y + ty) * size + x + tx] = c;
                }
            }
        }

        /// <summary>
        /// Saves an atlas as a PNG at path and imports it: mip-mapped,
        /// clamped, at full size, as a normal map or colour (sRGB) or data
        /// (linear), with its alpha only where it holds something.
        /// </summary>
        private static Texture2D SaveAtlas(string path, int size, Color32[] pixels, TextureImporterType type, bool srgb, bool alpha)
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false, !srgb);
            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The merged proxy mesh drawn from the atlas: every triangle of an
        /// atlased material whose corners lie within its texture's square
        /// moves to the first sub-mesh, its texture coordinates into the
        /// material's tile; the rest stay in one sub-mesh per material, as
        /// before. Gives back the new mesh (the old one destroyed), its
        /// materials, and how many triangles of atlased materials stayed
        /// out (outside their square).
        /// </summary>
        internal Mesh Apply(Mesh mesh, Material[] materials, out Material[] drawnWith, out int keptTriangles)
        {
            List<Vector3> positions = new();
            List<Vector3> normals = new();
            List<Vector4> tangents = new();
            List<Vector2> uvs = new();
            List<Color> colors = new();
            mesh.GetVertices(positions);
            mesh.GetNormals(normals);
            mesh.GetTangents(tangents);
            mesh.GetUVs(0, uvs);
            mesh.GetColors(colors);

            List<Vector3> newPositions = new();
            List<Vector3> newNormals = new();
            List<Vector4> newTangents = new();
            List<Vector2> newUVs = new();
            List<Color> newColors = new();
            // Each old vertex's index in the new mesh: one copy for atlased
            // triangles, one for those kept apart (a vertex can serve both).
            int[] inAtlas = new int[positions.Count];
            int[] keptAt = new int[positions.Count];
            System.Array.Fill(inAtlas, -1);
            System.Array.Fill(keptAt, -1);
            List<int> atlasTriangles = new();
            List<(Material material, List<int> triangles)> kept = new();
            keptTriangles = 0;

            for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++) {
                bool atlased = _cells.TryGetValue(materials[s], out Vector4 cell);
                int[] triangles = mesh.GetTriangles(s);
                List<int> keptHere = new();

                for (int t = 0; t < triangles.Length; t += 3) {
                    bool inside = atlased && InSquare(uvs, triangles[t]) && InSquare(uvs, triangles[t + 1]) && InSquare(uvs, triangles[t + 2]);

                    if (atlased && !inside) {
                        keptTriangles++;
                    }

                    for (int c = 0; c < 3; c++) {
                        int vertex = triangles[t + c];
                        int[] map = inside ? inAtlas : keptAt;

                        if (map[vertex] < 0) {
                            map[vertex] = newPositions.Count;
                            newPositions.Add(positions[vertex]);
                            CopyInto(normals, newNormals, vertex);
                            CopyInto(tangents, newTangents, vertex);
                            CopyInto(colors, newColors, vertex);

                            if (uvs.Count > 0) {
                                Vector2 uv = uvs[vertex];
                                newUVs.Add(inside ? new Vector2(cell.x + Mathf.Clamp01(uv.x) * cell.z, cell.y + Mathf.Clamp01(uv.y) * cell.w) : uv);
                            }
                        }

                        (inside ? atlasTriangles : keptHere).Add(map[vertex]);
                    }
                }

                if (keptHere.Count > 0) {
                    kept.Add((materials[s], keptHere));
                }
            }

            Mesh made = new() {
                name = mesh.name,
                indexFormat = newPositions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            made.SetVertices(newPositions);

            // Only the attributes the merged mesh had.
            if (newNormals.Count > 0) {
                made.SetNormals(newNormals);
            }

            if (newTangents.Count > 0) {
                made.SetTangents(newTangents);
            }

            if (newUVs.Count > 0) {
                made.SetUVs(0, newUVs);
            }

            if (newColors.Count > 0) {
                made.SetColors(newColors);
            }

            List<Material> order = new();
            List<List<int>> subMeshes = new();

            if (atlasTriangles.Count > 0) {
                order.Add(Material);
                subMeshes.Add(atlasTriangles);
            }

            foreach ((Material material, List<int> triangles) in kept) {
                order.Add(material);
                subMeshes.Add(triangles);
            }

            made.subMeshCount = subMeshes.Count;

            for (int s = 0; s < subMeshes.Count; s++) {
                made.SetTriangles(subMeshes[s], s);
            }

            made.RecalculateBounds();
            Object.DestroyImmediate(mesh);
            drawnWith = order.ToArray();
            return made;
        }

        private static bool InSquare(List<Vector2> uvs, int vertex)
        {
            if (uvs.Count == 0) {
                return false;
            }

            Vector2 uv = uvs[vertex];
            return uv.x >= -SquareTolerance && uv.x <= 1f + SquareTolerance && uv.y >= -SquareTolerance && uv.y <= 1f + SquareTolerance;
        }

        /// <summary>
        /// Adds the vertex's value to the new attribute list, if the mesh has
        /// that attribute.
        /// </summary>
        private static void CopyInto<T>(List<T> from, List<T> into, int vertex)
        {
            if (from.Count > 0) {
                into.Add(from[vertex]);
            }
        }
    }
}
