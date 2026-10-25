using System.Collections.Generic;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Held tools burning away when let go of, and burning back into being
    /// when taken: the mesh is eaten by spreading holes with a charred,
    /// glowing edge (shader TeaLeaf/Burn) over a tunable time.
    ///
    /// - Burning away: a copy of the tool is left hanging where it was let
    ///   go of, in the world (not on the hand, which moves on), and burns
    ///   away there; the tool itself is put away at once as before, so
    ///   nothing in the game waits on the effect. The copy is made once per
    ///   tool at load and reused.
    /// - Burning in: the tool itself wears burn materials, running the burn
    ///   backwards, then its own materials go back on.
    /// - A tool taken again while its copy is still burning away picks up
    ///   from the copy's burn (the copy goes): it forms again from as much
    ///   as was left. One let go of while burning in leaves a copy burning
    ///   on from there.
    ///
    /// The burn materials copy each tool material's URP Lit textures and
    /// numbers, so the swap shows no change; they're worn only while a
    /// burn plays (touched on state changes and, during a burn, its one
    /// number a frame).
    ///
    /// Each tool is registered once (Register()), and its system asks for
    /// BurnAway() or BurnIn() at the moment it lets go of it or takes it.
    /// Lives on the Hands object. No Update(): PlayerController calls Tick()
    /// on every path.
    /// </summary>
    public class ToolBurn : MonoBehaviour
    {
        // How long a burn takes, either way. 0 = none: tools vanish and
        // appear at once, as with no ToolBurn at all.
        [Tooltip("Seconds a tool takes to burn away when let go of, or back in when taken (0-2). 0 = no burn: tools vanish and appear at once.")]
        [SerializeField, Range(0f, 2f)] private float duration = 0.8f;

        [Header("Look")]

        // How many noise blobs to a metre: larger = smaller holes.
        [Tooltip("Noise blobs per metre the holes open from: larger = smaller, more holes.")]
        [SerializeField] private float noiseScale = 12f;

        // How far ahead of a hole's edge the surface chars, and glows, in
        // noise (0-1). The glow is never wider than the char.
        [Tooltip("How far ahead of a hole's edge the surface chars dark, in noise 0-1.")]
        [SerializeField, Range(0.001f, 0.5f)] private float charWidth = 0.12f;
        [Tooltip("How far ahead of a hole's edge the surface glows, in noise 0-1. No wider than Char Width.")]
        [SerializeField, Range(0.001f, 0.5f)] private float glowWidth = 0.04f;

        [SerializeField] private Color charColor = new(0.05f, 0.03f, 0.02f, 1f);

        // The glow fading back from the edge, and right at it. Bright
        // (over 1) so it reads as burning in a dark level.
        [ColorUsage(false, true)]
        [SerializeField] private Color glowColor = new(3f, 0.6f, 0.05f, 1f);
        [ColorUsage(false, true)]
        [SerializeField] private Color edgeColor = new(6f, 3.5f, 0.8f, 1f);

        /// <summary>
        /// One tool that burns: its renderer, its own materials, the burn
        /// materials it wears while burning in, and the copy left burning
        /// away (with its own burn materials, so both can play at once).
        /// Made by Register(); the tool's system keeps it and hands it back.
        /// </summary>
        public sealed class Target
        {
            internal Renderer Source;
            internal SkinnedMeshRenderer SourceSkin;
            internal MeshFilter SourceFilter;
            internal Material[] SourceMaterials;
            internal Material[] InMaterials;

            internal GameObject Copy;
            internal SkinnedMeshRenderer CopySkin;
            internal MeshFilter CopyFilter;
            internal Material[] OutMaterials;

            // The burn (0 whole, 1 gone) while burning in, and the copy's
            // while burning away. Below 0 = not playing.
            internal float InBurn = -1f;
            internal float OutBurn = -1f;
        }

        // The shader, found by name (it's in a Resources folder, so builds
        // have it too).
        private const string ShaderName = "TeaLeaf/Burn";

        private static readonly int _burnId = Shader.PropertyToID("_Burn");
        private static readonly int _noiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int _charWidthId = Shader.PropertyToID("_CharWidth");
        private static readonly int _glowWidthId = Shader.PropertyToID("_GlowWidth");
        private static readonly int _charColorId = Shader.PropertyToID("_CharColor");
        private static readonly int _glowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int _edgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int _bumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int _bumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int _metallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int _metallicId = Shader.PropertyToID("_Metallic");
        private static readonly int _smoothnessId = Shader.PropertyToID("_Smoothness");

        private Shader _shader;

        // Every registered tool, and the scene-root object the copies hang
        // under (in the world: a copy stays where it was let go of as the
        // player moves on).
        private readonly List<Target> _targets = new();
        private Transform _copiesRoot;

        private void Awake()
        {
            _shader = Shader.Find(ShaderName);

            if (_shader == null) {
                Debug.LogWarning($"ToolBurn: shader '{ShaderName}' not found, so tools won't burn.", this);
            }
        }

        private void OnDisable()
        {
            // Nothing is left half burnt: tools back in their own
            // materials, copies gone.
            for (int i = 0; i < _targets.Count; i++) {
                EndBurnIn(_targets[i]);
                EndBurnAway(_targets[i]);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _targets.Count; i++) {
                DestroyMaterials(_targets[i].InMaterials);
                DestroyMaterials(_targets[i].OutMaterials);
            }

            if (_copiesRoot != null) {
                Destroy(_copiesRoot.gameObject);
            }
        }

        /// <summary>
        /// Sets up a tool to burn, once, at load: its burn materials, and the
        /// copy that burns away (switched off). Null if the shader is
        /// missing; BurnAway() and BurnIn() take null and do nothing.
        /// </summary>
        public Target Register(Renderer source)
        {
            if (_shader == null || source == null) {
                return null;
            }

            if (_copiesRoot == null) {
                _copiesRoot = new GameObject("Tool Burns").transform;
            }

            Target target = new() {
                Source = source,
                SourceSkin = source as SkinnedMeshRenderer,
                SourceMaterials = source.sharedMaterials
            };

            target.InMaterials = MakeBurnMaterials(target.SourceMaterials);
            target.OutMaterials = MakeBurnMaterials(target.SourceMaterials);

            GameObject copy = new(source.name + " Burn") {
                layer = source.gameObject.layer
            };
            copy.transform.SetParent(_copiesRoot, false);

            Renderer copyRenderer;

            // A skinned tool (the bow, bent by its blend shape) needs a
            // skinned copy to show the same bend.
            if (target.SourceSkin != null) {
                target.CopySkin = copy.AddComponent<SkinnedMeshRenderer>();
                target.CopySkin.sharedMesh = target.SourceSkin.sharedMesh;
                target.CopySkin.updateWhenOffscreen = false;
                copyRenderer = target.CopySkin;
            } else {
                target.SourceFilter = source.GetComponent<MeshFilter>();
                target.CopyFilter = copy.AddComponent<MeshFilter>();
                copyRenderer = copy.AddComponent<MeshRenderer>();
            }

            copyRenderer.sharedMaterials = target.OutMaterials;
            copyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copyRenderer.receiveShadows = source.receiveShadows;

            copy.SetActive(false);
            target.Copy = copy;
            _targets.Add(target);
            return target;
        }

        /// <summary>
        /// The tool is being let go of: a copy is left where it is now,
        /// burning away. Call before the tool is put away (switched off or
        /// moved), while it's still where the player let go of it. A tool
        /// still burning in burns away from as far as it had come.
        /// </summary>
        public void BurnAway(Target target)
        {
            if (target == null) {
                return;
            }

            float start = 0f;

            if (target.InBurn >= 0f) {
                start = target.InBurn;
                EndBurnIn(target);
            }

            // Not showing (switched off with its object), or no burn time:
            // nothing to leave behind.
            if (duration <= 0f || target.Source == null || !target.Source.enabled || !target.Source.gameObject.activeInHierarchy) {
                return;
            }

            Transform from = target.Source.transform;
            Transform copy = target.Copy.transform;
            copy.SetPositionAndRotation(from.position, from.rotation);
            copy.localScale = from.lossyScale;

            if (target.CopySkin != null) {
                // The same bend, and the same bounds round it.
                int shapes = target.SourceSkin.sharedMesh != null ? target.SourceSkin.sharedMesh.blendShapeCount : 0;

                for (int i = 0; i < shapes; i++) {
                    target.CopySkin.SetBlendShapeWeight(i, target.SourceSkin.GetBlendShapeWeight(i));
                }

                target.CopySkin.localBounds = target.SourceSkin.localBounds;
            } else if (target.SourceFilter != null) {
                // The mesh shown now (the held arrow swaps its mesh by kind).
                target.CopyFilter.sharedMesh = target.SourceFilter.sharedMesh;
            }

            ApplyLook(target.OutMaterials);
            SetBurn(target.OutMaterials, start);
            target.OutBurn = start;
            target.Copy.SetActive(true);
        }

        /// <summary>
        /// The tool has just been taken (and is showing): it burns into
        /// being on the hand. A copy of it still burning away goes, and the
        /// tool forms from as much as the copy had left - unless
        /// keepCopy, for a tool that burns away in one place and back in
        /// another (the lockpicks let go of, back on the wrist): then the
        /// copy burns on and the tool forms from nothing.
        /// </summary>
        public void BurnIn(Target target, bool keepCopy = false)
        {
            if (target == null || duration <= 0f || target.Source == null) {
                return;
            }

            float start = 1f;

            if (target.OutBurn >= 0f && !keepCopy) {
                start = target.OutBurn;
                EndBurnAway(target);
            }

            if (target.InBurn < 0f) {
                target.Source.sharedMaterials = target.InMaterials;
            }

            ApplyLook(target.InMaterials);
            SetBurn(target.InMaterials, start);
            target.InBurn = start;
        }

        /// <summary>
        /// Moves every burn playing on by this frame's share of the
        /// duration. Called by PlayerController on every path.
        /// </summary>
        public void Tick()
        {
            float step = duration > 0f ? Time.deltaTime / duration : 1f;

            for (int i = 0; i < _targets.Count; i++) {
                Target target = _targets[i];

                if (target.InBurn >= 0f) {
                    // Put away by other means (its system switched off)
                    // mid-burn: back in its own materials.
                    if (target.Source == null || !target.Source.gameObject.activeInHierarchy) {
                        EndBurnIn(target);
                    } else {
                        target.InBurn -= step;

                        if (target.InBurn <= 0f) {
                            EndBurnIn(target);
                        } else {
                            SetBurn(target.InMaterials, target.InBurn);
                        }
                    }
                }

                if (target.OutBurn >= 0f) {
                    target.OutBurn += step;

                    if (target.OutBurn >= 1f) {
                        EndBurnAway(target);
                    } else {
                        SetBurn(target.OutMaterials, target.OutBurn);
                    }
                }
            }
        }

        /// <summary>
        /// Burning in finished (or stopped): the tool's own materials back
        /// on.
        /// </summary>
        private static void EndBurnIn(Target target)
        {
            if (target.InBurn < 0f) {
                return;
            }

            target.InBurn = -1f;

            if (target.Source != null) {
                target.Source.sharedMaterials = target.SourceMaterials;
            }
        }

        /// <summary>
        /// Burning away finished (or stopped): the copy switched off.
        /// </summary>
        private static void EndBurnAway(Target target)
        {
            if (target.OutBurn < 0f) {
                return;
            }

            target.OutBurn = -1f;

            if (target.Copy != null) {
                target.Copy.SetActive(false);
            }
        }

        /// <summary>
        /// One burn material per tool material, copying URP Lit's textures
        /// and numbers so it looks the same. URP Lit with a metallic map
        /// takes metallic from the map alone; this shader multiplies map by
        /// number, so the number is 1 then.
        /// </summary>
        private Material[] MakeBurnMaterials(Material[] sources)
        {
            Material[] burns = new Material[sources.Length];

            for (int i = 0; i < sources.Length; i++) {
                Material source = sources[i];

                if (source == null) {
                    continue;
                }

                Material burn = new(_shader) { name = source.name + " (Burn)" };

                if (source.HasProperty(_baseMapId)) {
                    burn.SetTexture(_baseMapId, source.GetTexture(_baseMapId));
                    burn.SetTextureScale(_baseMapId, source.GetTextureScale(_baseMapId));
                    burn.SetTextureOffset(_baseMapId, source.GetTextureOffset(_baseMapId));
                }

                if (source.HasProperty(_baseColorId)) {
                    burn.SetColor(_baseColorId, source.GetColor(_baseColorId));
                }

                if (source.HasProperty(_bumpMapId) && source.GetTexture(_bumpMapId) != null) {
                    burn.SetTexture(_bumpMapId, source.GetTexture(_bumpMapId));
                    burn.SetFloat(_bumpScaleId, source.HasProperty(_bumpScaleId) ? source.GetFloat(_bumpScaleId) : 1f);
                }

                Texture metallicMap = source.HasProperty(_metallicGlossMapId) ? source.GetTexture(_metallicGlossMapId) : null;

                if (metallicMap != null) {
                    burn.SetTexture(_metallicGlossMapId, metallicMap);
                    burn.SetFloat(_metallicId, 1f);
                } else if (source.HasProperty(_metallicId)) {
                    burn.SetFloat(_metallicId, source.GetFloat(_metallicId));
                }

                if (source.HasProperty(_smoothnessId)) {
                    burn.SetFloat(_smoothnessId, source.GetFloat(_smoothnessId));
                }

                burns[i] = burn;
            }

            return burns;
        }

        /// <summary>
        /// The look settings onto a set of burn materials, at the start of
        /// each burn (so they can be tuned in Play Mode).
        /// </summary>
        private void ApplyLook(Material[] materials)
        {
            float glow = Mathf.Min(glowWidth, charWidth);

            for (int i = 0; i < materials.Length; i++) {
                Material material = materials[i];

                if (material == null) {
                    continue;
                }

                material.SetFloat(_noiseScaleId, noiseScale);
                material.SetFloat(_charWidthId, charWidth);
                material.SetFloat(_glowWidthId, glow);
                material.SetColor(_charColorId, charColor);
                material.SetColor(_glowColorId, glowColor);
                material.SetColor(_edgeColorId, edgeColor);
            }
        }

        private static void SetBurn(Material[] materials, float burn)
        {
            for (int i = 0; i < materials.Length; i++) {
                if (materials[i] != null) {
                    materials[i].SetFloat(_burnId, burn);
                }
            }
        }

        private static void DestroyMaterials(Material[] materials)
        {
            if (materials == null) {
                return;
            }

            for (int i = 0; i < materials.Length; i++) {
                if (materials[i] != null) {
                    Destroy(materials[i]);
                }
            }
        }
    }
}
