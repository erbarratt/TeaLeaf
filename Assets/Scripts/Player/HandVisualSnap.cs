using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Moves one hand visual between "following the controller" (its normal
    /// local pose under the tracked hand) and a world-space HandSnapPose,
    /// blending over a short time either way so the hand never pops.
    ///
    /// A plain C# class rather than a MonoBehaviour: it has no Update() of
    /// its own. PlayerHandVisuals owns one per hand and ticks it; other
    /// systems (PlayerClimbing now, grabbing and tools later) only call
    /// Snap()/Release(). Only ever touches the cosmetic visual transform,
    /// never the tracked controller.
    ///
    /// While snapped, the visual is detached from the controller entirely
    /// (parented to the scene root). The hands' Tracked Pose Drivers update
    /// in "Update And Before Render" mode: just before rendering they move
    /// the controller once more with fresher tracking data, after all our
    /// Update() code has run. A visual still parented to the controller
    /// would be carried by that last bit of movement every frame and wobble
    /// on the ledge, however late we set its pose. Detached, nothing but
    /// this class can move it. It re-attaches once the release blend ends.
    /// </summary>
    public class HandVisualSnap
    {
        private readonly Transform _visual;

        // Seconds a blend takes unless a Snap()/Release() call gives its
        // own, and the duration of the blend currently running.
        private readonly float _defaultBlendDuration;
        private float _blendDuration;

        // The controller transform the visual normally lives under, and
        // returns to after a release.
        private readonly Transform _restParent;

        // The visual's own local pose under the tracked hand, captured once -
        // "following the controller" means sitting exactly here.
        private readonly Vector3 _restLocalPosition;
        private readonly Quaternion _restLocalRotation;

        // The "from" end of the blend, relative to the controller like the
        // rest pose, so it follows the real hand while blending. Normally the
        // rest pose, but a grab while a surface holds the visual off the
        // controller (the physical hands) starts from where the visual
        // actually is instead - see Snap().
        private Vector3 _fromLocalPosition;
        private Quaternion _fromLocalRotation;

        private HandSnapPose _snapPose;

        // 0 = at rest (following the controller), 1 = fully snapped.
        private float _blend;

        /// True from Snap() until Release().
        public bool IsSnapped { get; private set; }

        /// The pose passed to the last Snap() - e.g. which finger pose to
        /// play. Only meaningful while Weight is above 0 (it stays valid
        /// through the release blend, not just while IsSnapped).
        public HandSnapPose SnapPose => _snapPose;

        /// How far into the snap the hand currently is, eased: 0 = following
        /// the controller, 1 = fully snapped. The same value that places the
        /// visual, so anything else blending with the snap (e.g. the finger
        /// pose layer in PlayerHandAnimation) stays exactly in step with it.
        public float Weight { get; private set; }

        public HandVisualSnap(Transform visual, float blendDuration)
        {
            _visual = visual;
            _defaultBlendDuration = blendDuration;
            _blendDuration = blendDuration;
            _restParent = visual.parent;
            _restLocalPosition = visual.localPosition;
            _restLocalRotation = visual.localRotation;
            _fromLocalPosition = _restLocalPosition;
            _fromLocalRotation = _restLocalRotation;
        }

        /// <summary>
        /// Starts blending the visual towards pose (from wherever it is now,
        /// so snapping mid-release doesn't jump).
        /// </summary>
        public void Snap(HandSnapPose pose)
        {
            Snap(pose, _defaultBlendDuration);
        }

        /// <summary>
        /// Snap() with its own blend time, in seconds - for a hand that
        /// travels further than a grab onto a ledge does (reaching out to
        /// pick up a prop).
        /// </summary>
        public void Snap(HandSnapPose pose, float blendDuration)
        {
            _blendDuration = blendDuration;
            _snapPose = pose;
            IsSnapped = true;

            // Starting from rest: blend from wherever the visual is now,
            // which isn't the rest pose if a surface is holding it off the
            // controller. Blending from the rest pose made the hand jump back
            // to the controller (maybe inside the wall) on the first frame.
            // Stored in the controller's space; the controller is unscaled,
            // so this is the inverse of TransformPoint(). Mid-release, the
            // blend already runs from the rest pose, so it's left alone.
            if (_blend <= 0f) {
                Quaternion toController = Quaternion.Inverse(_restParent.rotation);
                _fromLocalPosition = toController * (_visual.position - _restParent.position);
                _fromLocalRotation = toController * _visual.rotation;
            }

            // Detach so the controller's before-render update can't move it -
            // see the class comment. worldPositionStays keeps it exactly where
            // it is, including the right hand's mirrored (-1) x scale. Only
            // happens on grab, so the hierarchy change isn't a per-frame cost.
            // Skipped if it's still detached from a release blend that hasn't
            // finished yet.
            if (_visual.parent == _restParent) {
                _visual.SetParent(null, true);
            }
        }

        /// <summary>
        /// Shifts the pose the hand is snapped to by movement (world space),
        /// keeping its rotation and finger pose - for a grip that travels,
        /// like a hand sliding along a zip line. Tick() places the visual at
        /// the moved pose as usual.
        /// </summary>
        public void MoveSnapPose(Vector3 movement)
        {
            _snapPose = new HandSnapPose(_snapPose.Position + movement, _snapPose.Rotation, _snapPose.Pose);
        }

        /// <summary>
        /// Starts blending the visual back to following the controller.
        /// </summary>
        public void Release()
        {
            Release(_defaultBlendDuration);
        }

        /// <summary>
        /// Release() with its own blend time, in seconds.
        /// </summary>
        public void Release(float blendDuration)
        {
            _blendDuration = blendDuration;
            IsSnapped = false;

            // Always blend back onto the controller itself, never to where a
            // surface held the hand before the grab. Released mid-snap-blend,
            // this shifts the hand slightly; fully snapped, it can't be seen.
            _fromLocalPosition = _restLocalPosition;
            _fromLocalRotation = _restLocalRotation;
        }

        /// <summary>
        /// Advances the blend and places the visual. Must run after the rig
        /// has finished moving and turning this frame: the visual is a child
        /// of the rig, so any later Move() or turn would drag a world-space
        /// snap pose along with it until next frame.
        /// </summary>
        public void Tick(float deltaTime)
        {
            // Fully at rest: the visual is already back at its local pose and
            // simply rides along with the controller, so skip touching the
            // transform at all - the common case, most frames.
            if (!IsSnapped && _blend <= 0f) {
                return;
            }

            float target = IsSnapped ? 1f : 0f;
            float step = _blendDuration > 0f ? deltaTime / _blendDuration : 1f;
            _blend = Mathf.MoveTowards(_blend, target, step);

            if (_blend <= 0f) {
                // Finished releasing - re-attach to the controller and
                // restore the exact local pose once, so no floating-point
                // error from the world-space blend lingers.
                Weight = 0f;
                _visual.SetParent(_restParent, false);
                _visual.SetLocalPositionAndRotation(_restLocalPosition, _restLocalRotation);
                return;
            }

            // The "from" end of the blend this frame: normally where the
            // visual would be if it were just following the controller (see
            // _fromLocalPosition). Read from _restParent, not _visual.parent,
            // since the visual is detached.
            Vector3 fromPosition = _restParent.TransformPoint(_fromLocalPosition);
            Quaternion fromRotation = _restParent.rotation * _fromLocalRotation;

            // SmoothStep eases in and out, so the hand doesn't start or stop
            // moving abruptly at either end of the blend.
            float t = Mathf.SmoothStep(0f, 1f, _blend);
            Weight = t;

            _visual.SetPositionAndRotation(
                Vector3.Lerp(fromPosition, _snapPose.Position, t),
                Quaternion.Slerp(fromRotation, _snapPose.Rotation, t));
        }
    }
}
