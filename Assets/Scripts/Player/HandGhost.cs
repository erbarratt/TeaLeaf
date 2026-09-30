using UnityEngine;
using UnityEngine.Rendering;

namespace Player
{
    /// <summary>
    /// A faint "ghost" copy of one hand that always sits exactly on the real
    /// controller, shown only while a surface holds the hand visual away from
    /// it (as Half-Life: Alyx does) - so the player can see where their real
    /// hand is when the visible one has stopped at a wall.
    ///
    /// A plain C# class, like HandPhysicalFollow: PlayerHandVisuals owns one
    /// per hand and ticks it after the physical follow. The ghost is a copy
    /// of the hand visual made once at startup and parented to the
    /// controller at the visual's rest pose, so it follows tracking by
    /// itself - including the Tracked Pose Driver's before-render update -
    /// with no code moving it. It has no Animator: while shown, its finger
    /// bones copy the visual's, so it makes the same fist.
    /// </summary>
    public class HandGhost
    {
        private readonly Transform _visual;
        private readonly Transform _ghost;
        private readonly SkinnedMeshRenderer _renderer;

        // The visual's bones and the ghost's matching ones, in the same
        // order (the ghost is a copy). Fetched once: SkinnedMeshRenderer.bones
        // allocates a new array on every read.
        private readonly Transform[] _visualBones;
        private readonly Transform[] _ghostBones;

        private bool _isVisible;

        /// <summary>
        /// Copies visual (at startup, while it's still on its controller) as
        /// a hidden ghost beside it, drawn with material.
        /// </summary>
        public HandGhost(Transform visual, Material material)
        {
            _visual = visual;

            // Same parent, so the copy keeps the visual's local pose -
            // including the right hand's mirrored (-1) x scale.
            _ghost = Object.Instantiate(visual, visual.parent);
            _ghost.name = visual.name + " Ghost";

            // Its Animator would pose the fingers itself and overwrite the
            // copied bones. Destroy() happens at the end of this frame, before
            // the ghost is ever shown.
            if (_ghost.TryGetComponent(out Animator animator)) {
                Object.Destroy(animator);
            }

            SkinnedMeshRenderer visualRenderer = visual.GetComponentInChildren<SkinnedMeshRenderer>();
            _renderer = _ghost.GetComponentInChildren<SkinnedMeshRenderer>();
            _visualBones = visualRenderer.bones;
            _ghostBones = _renderer.bones;

            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        /// <summary>
        /// Shows the ghost while the visual is more than showDistance from
        /// the controller, and hides it again once it's back within half that
        /// (the gap stops it flickering on and off right at the threshold).
        /// canShow false (snapped, or ghosts switched off) hides it. The
        /// renderer is only switched when that changes, and bones are only
        /// copied while it's shown, so a hidden ghost costs one distance
        /// check per frame.
        /// </summary>
        public void Tick(bool canShow, float showDistance)
        {
            bool visible = false;

            if (canShow) {
                // The ghost sits where the visual would be if it simply
                // followed the controller, so this is how far a surface is
                // holding the visual away.
                float separation = Vector3.Distance(_ghost.position, _visual.position);
                visible = _isVisible ? separation > showDistance * 0.5f : separation > showDistance;
            }

            if (visible != _isVisible) {
                _isVisible = visible;
                _renderer.enabled = visible;
            }

            if (!_isVisible) {
                return;
            }

            // Match the visual's fingers. This runs before the Animators do,
            // so it's last frame's pose - a frame behind is invisible on a
            // ghost. Only rotations: the Animator only animates those.
            for (int i = 0; i < _ghostBones.Length; i++) {
                _ghostBones[i].localRotation = _visualBones[i].localRotation;
            }
        }
    }
}
