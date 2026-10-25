using UnityEngine;

namespace Player
{
    /// <summary>
    /// Works out the sizes and places of a hand's collision pieces - all at
    /// load, nothing per frame. Two kinds:
    /// - The hand's own six (FitHand()): a box for the palm, a capsule for
    ///   the thumb and one for each finger, from the hand model's bones, in
    ///   two poses: open (the model's rest pose, the relaxed hand) and closed
    ///   (the fist the hand Animator's "Closed" clips hold).
    /// - A carried prop's (FitCollider()): one piece per collider, the same
    ///   shape as the collider.
    ///
    /// Every piece is measured relative to the hand visual's root in the
    /// CONTROLLER's axes (see HandShapeGeometry), so the right hand's mirrored
    /// visual needs nothing special: its bones are already mirrored there.
    /// </summary>
    public static class HandShapeFitter
    {
        /// How many pieces a hand has, and which is which.
        public const int ShapeCount = 6;
        public const int PalmShape = 0;
        public const int ThumbShape = 1;
        public const int IndexShape = 2;

        // Bone names in hand.fbx - the right hand is the same model mirrored,
        // so its bones have the same (Left) names. A digit's bones are the
        // prefix, its name and 1-4: 1 starts at the knuckle (the thumb's at
        // its base), 4 is the short end bone at the tip.
        private const string BonePrefix = "J_Left_Hand";
        private static readonly string[] _digitNames = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        private const int BonesPerDigit = 4;

        // The hand Animator's clips whose name has this in are the closed
        // ends of its blend trees (Hand_L_Grip_Closed, Hand_L_Index_Closed):
        // together, the fist.
        private const string ClosedClipMarker = "Closed";

        /// <summary>
        /// Fits the hand's six pieces in both poses into open and closed
        /// (each ShapeCount long). The visual must be on its controller at
        /// its rest pose with the fingers at rest - true in Awake(). The
        /// closed clips are sampled onto the bones and the bones put back
        /// after, so nothing is left changed. False, with an error, if a bone
        /// is missing (then nothing is filled in).
        /// </summary>
        public static bool FitHand(Transform visual, HandShapeSizes sizes, HandShapeGeometry[] open, HandShapeGeometry[] closed)
        {
            if (!TryFindBones(visual, out Transform wrist, out Transform[] digitBones)) {
                return false;
            }

            FitPose(visual, wrist, digitBones, sizes, open);

            Animator animator = visual.GetComponent<Animator>();

            if (animator == null || animator.runtimeAnimatorController == null) {
                Debug.LogWarning($"HandShapeFitter: {visual.name} has no Animator controller - its closed hand collides like the open one.", visual);
                System.Array.Copy(open, closed, ShapeCount);
                return true;
            }

            // Every transform's local pose, to put back once the fist has
            // been measured.
            Transform[] all = visual.GetComponentsInChildren<Transform>(true);
            var positions = new Vector3[all.Length];
            var rotations = new Quaternion[all.Length];

            for (int i = 0; i < all.Length; i++) {
                all[i].GetLocalPositionAndRotation(out positions[i], out rotations[i]);
            }

            // Each Closed clip keys only its own fingers (one index, one the
            // other three), so sampling both gives the whole fist.
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips) {
                if (clip.name.Contains(ClosedClipMarker)) {
                    clip.SampleAnimation(visual.gameObject, 0f);
                }
            }

            FitPose(visual, wrist, digitBones, sizes, closed);

            for (int i = 0; i < all.Length; i++) {
                all[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
            }

            return true;
        }

        /// <summary>
        /// Fits the hand's six pieces for the open pose only, from wherever
        /// the bones are now - for the Edit Mode gizmo. False if a bone is
        /// missing (no error: it's called while drawing).
        /// </summary>
        public static bool FitOpen(Transform visual, HandShapeSizes sizes, HandShapeGeometry[] open)
        {
            if (!TryFindBones(visual, out Transform wrist, out Transform[] digitBones, false)) {
                return false;
            }

            FitPose(visual, wrist, digitBones, sizes, open);
            return true;
        }

        /// <summary>
        /// Finds the wrist and every digit's four bones under visual by name:
        /// digitBones holds thumb 1-4, then index, middle, ring, little 1-4.
        /// </summary>
        private static bool TryFindBones(Transform visual, out Transform wrist, out Transform[] digitBones, bool logMissing = true)
        {
            wrist = null;
            digitBones = new Transform[_digitNames.Length * BonesPerDigit];

            foreach (Transform bone in visual.GetComponentsInChildren<Transform>(true)) {
                if (bone.name == BonePrefix) {
                    wrist = bone;
                    continue;
                }

                for (int digit = 0; digit < _digitNames.Length; digit++) {
                    for (int i = 0; i < BonesPerDigit; i++) {
                        if (bone.name == $"{BonePrefix}{_digitNames[digit]}{i + 1}") {
                            digitBones[digit * BonesPerDigit + i] = bone;
                        }
                    }
                }
            }

            bool isComplete = wrist != null && System.Array.IndexOf(digitBones, null) < 0;

            if (!isComplete && logMissing) {
                Debug.LogError($"HandShapeFitter: {visual.name} is missing hand bones ({BonePrefix}...) - that hand won't collide.", visual);
            }

            return isComplete;
        }

        /// <summary>
        /// Measures the six pieces from where the bones are now.
        /// - Palm: a box from the wrist bone to the middle of the four
        ///   knuckles, as wide as the knuckles are apart plus a finger radius
        ///   each side, palmThickness thick, its middle palmOffset towards
        ///   the back of the hand.
        /// - Each digit: a capsule from its first bone to whichever of its
        ///   other bones is furthest from that one - the tip on an open hand,
        ///   a middle joint on a fist (a curled finger's tip folds back in,
        ///   inside the palm's box). Only a tip is pulled in by the radius:
        ///   the tip bone is on the finger's surface, the joints are inside.
        /// </summary>
        private static void FitPose(Transform visual, Transform wrist, Transform[] digitBones, HandShapeSizes sizes, HandShapeGeometry[] into)
        {
            Transform controller = visual.parent;
            Vector3 root = controller.InverseTransformPoint(visual.position);

            Vector3 wristPoint = controller.InverseTransformPoint(wrist.position) - root;
            Vector3 indexKnuckle = Point(controller, root, digitBones, 1, 0);
            Vector3 pinkyKnuckle = Point(controller, root, digitBones, 4, 0);
            Vector3 knuckles = (indexKnuckle
                + Point(controller, root, digitBones, 2, 0)
                + Point(controller, root, digitBones, 3, 0)
                + pinkyKnuckle) * 0.25f;

            Vector3 forward = knuckles - wristPoint;
            float length = forward.magnitude;
            forward /= length;

            // The back of the hand is the visual's -X. TransformVector (not
            // TransformDirection) includes the right visual's -1 scale, so
            // this is the right way for either hand.
            Vector3 back = controller.InverseTransformVector(visual.TransformVector(Vector3.left));
            Vector3 up = Vector3.ProjectOnPlane(back, forward).normalized;
            Vector3 across = Vector3.Cross(up, forward);
            float span = Mathf.Abs(Vector3.Dot(indexKnuckle - pinkyKnuckle, across));

            into[PalmShape] = HandShapeGeometry.Box(
                wristPoint + forward * (length * 0.5f) + up * sizes.palmOffset,
                Quaternion.LookRotation(forward, up),
                new Vector3(span * 0.5f + sizes.fingerRadius, sizes.palmThickness * 0.5f, length * 0.5f));

            into[ThumbShape] = FitDigit(controller, root, digitBones, 0, sizes.thumbRadius);

            for (int finger = 0; finger < 4; finger++) {
                // Index and middle are bare; ring and little armoured.
                float radius = finger < 2 ? sizes.fingerRadius : sizes.armouredFingerRadius;
                into[IndexShape + finger] = FitDigit(controller, root, digitBones, finger + 1, radius);
            }
        }

        /// <summary>
        /// One digit's capsule - see FitPose().
        /// </summary>
        private static HandShapeGeometry FitDigit(Transform controller, Vector3 root, Transform[] digitBones, int digit, float radius)
        {
            Vector3 start = Point(controller, root, digitBones, digit, 0);
            Vector3 end = start;
            int endBone = 0;

            for (int i = 1; i < BonesPerDigit; i++) {
                Vector3 point = Point(controller, root, digitBones, digit, i);

                if ((point - start).sqrMagnitude > (end - start).sqrMagnitude) {
                    end = point;
                    endBone = i;
                }
            }

            if (endBone == BonesPerDigit - 1) {
                Vector3 axis = end - start;
                float length = axis.magnitude;
                end -= axis / length * Mathf.Min(radius, length);
            }

            return HandShapeGeometry.CapsuleBetween(start, end, radius);
        }

        /// <summary>
        /// One bone's position relative to the visual's root, controller axes.
        /// </summary>
        private static Vector3 Point(Transform controller, Vector3 root, Transform[] digitBones, int digit, int bone)
        {
            return controller.InverseTransformPoint(digitBones[digit * BonesPerDigit + bone].position) - root;
        }

        /// <summary>
        /// One carried-prop piece the same shape as collider, for a hand
        /// whose visual will hold the prop from handPosition, turned to
        /// handRotation (the pose the hand is snapping to). toController turns
        /// the hand's axes into the controller's - the visual's rest rotation
        /// under its controller. A mesh collider (or any other kind) becomes
        /// the box round its mesh. False for a trigger or a switched-off
        /// collider, which the hand ignores.
        /// </summary>
        public static bool FitCollider(Collider collider, Vector3 handPosition, Quaternion handRotation, Quaternion toController, out HandShapeGeometry geometry)
        {
            geometry = default;

            if (collider.isTrigger || !collider.enabled) {
                return false;
            }

            Transform transform = collider.transform;
            Vector3 scale = transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Quaternion rotation = transform.rotation;
            Vector3 centre;

            switch (collider) {
                case BoxCollider box:
                    centre = transform.TransformPoint(box.center);
                    geometry = HandShapeGeometry.Box(default, default, Vector3.Scale(box.size, scale) * 0.5f);
                    break;
                case SphereCollider sphere:
                    centre = transform.TransformPoint(sphere.center);
                    geometry = HandShapeGeometry.Sphere(default, sphere.radius * Mathf.Max(scale.x, scale.y, scale.z));
                    break;
                case CapsuleCollider capsule:
                    centre = transform.TransformPoint(capsule.center);

                    // Its axis is local X, Y or Z (direction 0, 1, 2); the
                    // piece's is always its Z, so turn Z onto that axis.
                    int axis = capsule.direction;
                    float along = scale[axis];
                    float acrossScale = Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                    float radius = capsule.radius * acrossScale;
                    float halfLength = Mathf.Max(0f, capsule.height * along * 0.5f - radius);
                    rotation *= axis == 0 ? Quaternion.Euler(0f, 90f, 0f) : axis == 1 ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
                    geometry = HandShapeGeometry.Capsule(default, default, radius, halfLength);
                    break;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    Bounds bounds = mesh.sharedMesh.bounds;
                    centre = transform.TransformPoint(bounds.center);
                    geometry = HandShapeGeometry.Box(default, default, Vector3.Scale(bounds.extents, scale));
                    break;
                default:
                    // The world box round it, as it lies now.
                    Bounds world = collider.bounds;
                    centre = world.center;
                    rotation = Quaternion.identity;
                    geometry = HandShapeGeometry.Box(default, default, world.extents);
                    break;
            }

            // Into the hand's own axes, then the controller's.
            Quaternion fromWorld = toController * Quaternion.Inverse(handRotation);
            Vector3 localCentre = fromWorld * (centre - handPosition);
            Quaternion localRotation = fromWorld * rotation;

            geometry = geometry.Kind switch {
                HandShapeGeometry.ShapeKind.Box => HandShapeGeometry.Box(localCentre, localRotation, geometry.HalfExtents),
                HandShapeGeometry.ShapeKind.Sphere => HandShapeGeometry.Sphere(localCentre, geometry.Radius),
                _ => HandShapeGeometry.Capsule(localCentre, localRotation, geometry.Radius, geometry.HalfLength)
            };

            return true;
        }
    }
}
