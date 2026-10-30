using System;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Core
{
    /// <summary>
    /// Bakes the shadow floor into Adaptive Probe Volumes, the probe half of
    /// what ShadowFloor does to the lightmaps, so a guard or small piece lit
    /// by the probes is never darker than the floored wall behind it.
    ///
    /// APV keeps no probe data that can be changed after its bake (the cells
    /// are packed and compressed), so the floor goes in during it: this is
    /// set as APV's lighting baker (AdaptiveProbeVolumes.SetLightingBakerOverride)
    /// at editor load. It runs Unity's own baker unchanged and, once that has
    /// lit every probe, lifts each probe whose darkest of six directions is
    /// below twice Moonlight.shadowFloor, by ShadowFloor.Lift() - the same
    /// curve as the lightmaps. APV then fixes seams, dilates and packs the
    /// floored probes as it would its own. No Moonlight, or a black floor,
    /// leaves the bake as Unity's.
    ///
    /// Unity's baker (DefaultLightTransport) and the bake jobs it needs are
    /// internal to the package, so they are reached by reflection. If a
    /// package upgrade moves them, this logs an error and stands aside:
    /// probes bake without the floor, the bake itself never breaks.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class ProbeVolumeShadowFloor : AdaptiveProbeVolumes.LightingBaker
    {
        // Directions a probe is evaluated in to find its darkest side.
        private static readonly Vector3[] _probeDirections = {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

        // The package internals reached by reflection (see the summary).
        private static readonly Type _defaultBakerType = typeof(AdaptiveProbeVolumes).GetNestedType("DefaultLightTransport", BindingFlags.NonPublic);
        private static readonly Type _bakeJobType = typeof(AdaptiveProbeVolumes).GetNestedType("BakeJob", BindingFlags.NonPublic);
        private static readonly FieldInfo _defaultBakerJobs = _defaultBakerType?.GetField("jobs", Public);
        private static readonly FieldInfo _bakeData = typeof(AdaptiveProbeVolumes).GetField("s_BakeData", PrivateStatic);
        private static readonly FieldInfo _bakeDataJobs = _bakeData?.FieldType.GetField("jobs", Public);
        private static readonly FieldInfo _bakeDataPositions = _bakeData?.FieldType.GetField("sortedPositions", Public);
        private static readonly FieldInfo _bakeJobProbeCount = _bakeJobType?.GetField("probeCount", Public);
        private static readonly MethodInfo _bakeJobCreate = _bakeJobType?.GetMethod("Create", Public, null,
            new[] { typeof(ProbeVolumeBakingSet), typeof(LightingSettings), typeof(bool) }, null);
        private static readonly MethodInfo _bakeJobDispose = _bakeJobType?.GetMethod("Dispose", Public);
        private static readonly MethodInfo _lightingSettings = typeof(Lightmapping).GetMethod("GetLightingSettingsOrDefaultsFallback", PrivateStatic);

        // Unity's own baker for this bake; null between bakes.
        private AdaptiveProbeVolumes.LightingBaker _inner;

        // Jobs this made itself (a bake other than the full one, which owns
        // its jobs): disposed with the baker.
        private Array _ownJobs;

        // The floor for this bake, and whether there is one.
        private Color _floor;
        private bool _hasFloor;

        // Unity's SH evaluation as weights, measured on the main thread at
        // Initialize() (SphericalHarmonicsL2.Evaluate and AddAmbientLight
        // only run there; the bake's Step() runs on a worker): a probe's
        // light towards direction d, per channel, is the sum over k of
        // _directionWeights[d * 9 + k] * coefficient k; adding ambient light
        // a adds a * _ambientWeights[k] to coefficient k.
        private readonly float[] _directionWeights = new float[6 * 9];
        private readonly float[] _ambientWeights = new float[9];

        // Set by the worker once the probes are floored; until then the
        // baker never reports itself finished, so nothing reads the probes
        // half lifted.
        private volatile bool _lifted;

        static ProbeVolumeShadowFloor()
        {
            if (_defaultBakerType == null || _bakeJobType == null || _defaultBakerJobs == null || _bakeDataJobs == null
                || _bakeDataPositions == null || _bakeJobProbeCount == null || _bakeJobCreate == null
                || _bakeJobDispose == null || _lightingSettings == null) {
                Debug.LogError("ProbeVolumeShadowFloor: the Adaptive Probe Volume bake internals it wraps have moved (package upgrade?), so probe volumes bake without the shadow floor. Lightmaps still get it.");
                return;
            }
            AdaptiveProbeVolumes.SetLightingBakerOverride(new ProbeVolumeShadowFloor());
        }

        public override bool isThreadSafe => _inner == null || _inner.isThreadSafe;

        public override ulong currentStep
        {
            get
            {
                if (_inner == null) {
                    return 0;
                }
                ulong step = _inner.currentStep;
                ulong count = _inner.stepCount;
                // Lit but not yet floored: one short of done.
                return !_lifted && count > 0 && step >= count ? count - 1 : step;
            }
        }

        public override ulong stepCount => _inner == null ? 0 : _inner.stepCount;
        public override NativeArray<SphericalHarmonicsL2> irradiance => _inner.irradiance;
        public override NativeArray<float> validity => _inner.validity;
        public override NativeArray<Vector4> occlusion => _inner.occlusion;

        public override void Initialize(bool bakeProbeOcclusion, NativeArray<Vector3> probePositions)
        {
            Initialize(bakeProbeOcclusion, probePositions, default);
        }

        /// <summary>
        /// Starts Unity's baker for this bake, hands it the bake jobs, and
        /// reads the floor and the SH weights (main thread).
        /// </summary>
        public override void Initialize(bool bakeProbeOcclusion, NativeArray<Vector3> probePositions, NativeArray<uint> bakedRenderingLayerMasks)
        {
            DisposeInner();
            _lifted = false;
            _inner = (AdaptiveProbeVolumes.LightingBaker)Activator.CreateInstance(_defaultBakerType, true);
            if (bakedRenderingLayerMasks.IsCreated) {
                _inner.Initialize(bakeProbeOcclusion, probePositions, bakedRenderingLayerMasks);
            } else {
                _inner.Initialize(bakeProbeOcclusion, probePositions);
            }
            _defaultBakerJobs.SetValue(_inner, FindJobs(probePositions));

            Moonlight moon = Object.FindFirstObjectByType<Moonlight>();
            _floor = moon != null ? moon.ShadowFloor : Color.black;
            _hasFloor = _floor.r > 0f || _floor.g > 0f || _floor.b > 0f;
            if (_hasFloor) {
                MeasureWeights();
            }
        }

        /// <summary>
        /// The bake jobs Unity's baker needs: Unity gives them only to its
        /// own baker. For the full bake they are the bake's own (it disposes
        /// them); for a smaller bake (an adjustment volume's, or an
        /// additional request) one job over every probe with the scene's
        /// lighting settings, as Unity makes for an additional request.
        /// </summary>
        private Array FindJobs(NativeArray<Vector3> probePositions)
        {
            object bakeData = _bakeData.GetValue(null);
            var bakePositions = (NativeArray<Vector3>)_bakeDataPositions.GetValue(bakeData);
            if (bakePositions.IsCreated && bakePositions.Equals(probePositions) && _bakeDataJobs.GetValue(bakeData) is Array bakeJobs) {
                return bakeJobs;
            }
            object job = Activator.CreateInstance(_bakeJobType);
            _bakeJobCreate.Invoke(job, new object[] { null, _lightingSettings.Invoke(null, null), false });
            _bakeJobProbeCount.SetValue(job, probePositions.Length);
            _ownJobs = Array.CreateInstance(_bakeJobType, 1);
            _ownJobs.SetValue(job, 0);
            return _ownJobs;
        }

        /// <summary>
        /// Measures Unity's SH evaluation in the six directions and its
        /// ambient term, one coefficient at a time, into the weight tables.
        /// </summary>
        private void MeasureWeights()
        {
            Color[] results = new Color[_probeDirections.Length];
            for (int k = 0; k < 9; k++) {
                SphericalHarmonicsL2 single = default;
                single[0, k] = 1f;
                single.Evaluate(_probeDirections, results);
                for (int d = 0; d < _probeDirections.Length; d++) {
                    _directionWeights[d * 9 + k] = results[d].r;
                }
            }
            SphericalHarmonicsL2 ambient = default;
            ambient.AddAmbientLight(new Color(1f, 0f, 0f));
            for (int k = 0; k < 9; k++) {
                _ambientWeights[k] = ambient[0, k];
            }
        }

        /// <summary>
        /// Runs Unity's baker; once it has lit every probe, floors them
        /// before reporting done. May run on a worker thread.
        /// </summary>
        public override bool Step()
        {
            bool ok = _inner.Step();
            if (!_lifted && _inner.currentStep >= _inner.stepCount) {
                try {
                    if (_hasFloor) {
                        int raised = FloorProbes(_inner.irradiance);
                        Debug.Log($"ProbeVolumeShadowFloor: raised {raised} of {_inner.irradiance.Length} probe(s) onto the floor {_floor} (Moonlight.shadowFloor).");
                    }
                } catch (Exception e) {
                    Debug.LogError($"ProbeVolumeShadowFloor: flooring the probes failed, so they bake without it: {e}");
                } finally {
                    _lifted = true;
                }
            }
            return ok;
        }

        /// <summary>
        /// Adds ambient light to every probe whose darkest direction is below
        /// twice the floor, by what ShadowFloor.Lift() gives that darkest
        /// direction per channel, so its own spread between sides is kept
        /// (as ShadowFloor does to legacy probes). Returns how many were raised.
        /// </summary>
        private int FloorProbes(NativeArray<SphericalHarmonicsL2> probes)
        {
            int raised = 0;
            for (int i = 0; i < probes.Length; i++) {
                SphericalHarmonicsL2 probe = probes[i];
                bool changed = false;
                for (int c = 0; c < 3; c++) {
                    float darkest = float.MaxValue;
                    for (int d = 0; d < _probeDirections.Length; d++) {
                        float light = 0f;
                        for (int k = 0; k < 9; k++) {
                            light += _directionWeights[d * 9 + k] * probe[c, k];
                        }
                        darkest = Mathf.Min(darkest, light);
                    }
                    float lift = ShadowFloor.Lift(darkest, _floor[c]) - darkest;
                    if (lift <= 0f) {
                        continue;
                    }
                    for (int k = 0; k < 9; k++) {
                        probe[c, k] += lift * _ambientWeights[k];
                    }
                    changed = true;
                }
                if (changed) {
                    probes[i] = probe;
                    raised++;
                }
            }
            return raised;
        }

        public override void Dispose()
        {
            DisposeInner();
        }

        /// <summary>
        /// Frees Unity's baker and any jobs this made; the instance stays
        /// set as the override for the next bake.
        /// </summary>
        private void DisposeInner()
        {
            _inner?.Dispose();
            _inner = null;
            if (_ownJobs != null) {
                foreach (object job in _ownJobs) {
                    _bakeJobDispose.Invoke(job, null);
                }
                _ownJobs = null;
            }
        }
    }
}
