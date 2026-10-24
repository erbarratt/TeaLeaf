using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Puts one hand visual's fingers into a HandSnapProfile's finger pose,
    /// blended over whatever the Animator gave them (the input curl) by a
    /// weight. Plain class, one per hand, owned by PlayerHandAnimation.
    ///
    /// The Animator writes the bones it animates after every Update() and
    /// before LateUpdate(), so the pose is laid on top in LateUpdate()
    /// (Apply()). Bones it doesn't animate (the thumb) would keep last
    /// frame's posed rotation and never come back, so Prepare(), in
    /// Update(), first puts every bone posed last frame back to its rest
    /// rotation: the Animator then overwrites the ones it drives, and the
    /// rest blend from rest.
    /// </summary>
    public class HandFingerPoser
    {
        private readonly Transform[] _bones;
        private readonly Quaternion[] _restRotations;

        // This frame's pose and how much of it shows, from Prepare().
        private HandSnapProfile _profile;
        private float _weight;

        // True once Apply() has moved the bones, until Prepare() puts
        // them back - so a hand at rest costs nothing.
        private bool _isPosed;

        /// <summary>
        /// Finds the hand visual's finger bones and remembers their rest
        /// rotations. Call at load, before anything animates the hand.
        /// </summary>
        public HandFingerPoser(Transform handVisual)
        {
            _bones = HandSnapProfile.FindFingerBones(handVisual);
            _restRotations = new Quaternion[_bones.Length];

            for (int i = 0; i < _bones.Length; i++) {
                if (_bones[i] == null) {
                    Debug.LogWarning($"HandFingerPoser: no bone \"{HandSnapProfile.FingerBoneNames[i]}\" under {handVisual.name} - it won't be posed.", handVisual);
                    continue;
                }

                _restRotations[i] = _bones[i].localRotation;
            }
        }

        /// <summary>
        /// Called in Update(): this frame's pose (null or without a finger
        /// pose: none) and weight (0-1), and the bones posed last frame
        /// back at rest for the Animator.
        /// </summary>
        public void Prepare(HandSnapProfile profile, float weight)
        {
            _profile = profile;
            _weight = weight;

            if (!_isPosed) {
                return;
            }

            for (int i = 0; i < _bones.Length; i++) {
                if (_bones[i] != null) {
                    _bones[i].localRotation = _restRotations[i];
                }
            }

            _isPosed = false;
        }

        /// <summary>
        /// Called in LateUpdate(), after the Animator: blends each finger
        /// bone from where the Animator left it towards the pose.
        /// </summary>
        public void Apply()
        {
            if (_weight <= 0f || _profile == null || !_profile.HasFingerPose) {
                return;
            }

            for (int i = 0; i < _bones.Length; i++) {
                if (_bones[i] != null) {
                    _bones[i].localRotation = Quaternion.Slerp(_bones[i].localRotation, _profile.FingerRotation(i), _weight);
                }
            }

            _isPosed = true;
        }
    }
}
