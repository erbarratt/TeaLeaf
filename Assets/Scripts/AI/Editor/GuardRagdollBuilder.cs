using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AI
{
    /// <summary>
    /// TeaLeaf > Modify Prefab > Build Guard Ragdoll: puts the ragdoll on
    /// the guard prefab's bones - a Rigidbody, a collider and (but for
    /// the hips) a CharacterJoint on each of eleven bones, all on the
    /// Ragdoll layer - and a GuardRagdoll on the root that knows them.
    /// Run again after the model changes: it clears what it made before.
    ///
    /// The colliders are fitted to the skinned mesh, not guessed from
    /// the bones: every vertex goes to the ragdoll bone it is skinned
    /// to most (or the nearest ragdoll bone above that one), and each
    /// shape is sized round that bone's vertices in the bind pose.
    /// Limbs are capsules along the bone, the hips and chest boxes, the
    /// head a sphere - the shapes Unity's own ragdoll wizard uses. They
    /// are the guard's hit boxes too, so a close fit matters.
    ///
    /// Joint limits are first guesses at a person's range, measured from
    /// the prefab's standing pose: a knee or elbow bends one way only.
    /// </summary>
    public static class GuardRagdollBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/Characters/Guard.prefab";
        private const string LayerName = "Ragdoll";

        // The share of a limb's vertices its capsule's radius takes in,
        // and of a box's vertices on each side its edges leave out:
        // loose bits (a sleeve's edge, a belt buckle) don't swell it.
        private const float RadiusPercentile = 0.8f;
        private const float BoxTrim = 0.03f;

        // The lower arm's capsule carries on past the wrist over the
        // hand, as long as this share of the forearm.
        private const float HandShare = 0.7f;

        private enum Shape { Box, Capsule, Sphere }

        /// <summary>
        /// One ragdoll bone: its name, parent body (by index; -1 for
        /// the hips), shape, mass in kg, the next bone along it (for a
        /// limb's length), and the joint's limits in degrees: bending
        /// about the body's right (low, high), sideways (swing) and
        /// twisting along the bone (twist).
        /// </summary>
        private readonly struct Part
        {
            public readonly string Bone;
            public readonly int Parent;
            public readonly Shape Shape;
            public readonly float Mass;
            public readonly string Next;
            public readonly float BendLow;
            public readonly float BendHigh;
            public readonly float Swing;
            public readonly float Twist;

            public Part(string bone, int parent, Shape shape, float mass, string next, float bendLow, float bendHigh, float swing, float twist)
            {
                Bone = bone;
                Parent = parent;
                Shape = shape;
                Mass = mass;
                Next = next;
                BendLow = bendLow;
                BendHigh = bendHigh;
                Swing = swing;
                Twist = twist;
            }
        }

        // Bend is about the body's right: positive tips the far end
        // backwards (a knee bending, a leg kicked back, the head tipped
        // back). Hips first; parents before children.
        private static readonly Part[] _parts =
        {
            new("Hips", -1, Shape.Box, 12f, null, 0f, 0f, 0f, 0f),
            new("Chest", 0, Shape.Box, 18f, null, -30f, 20f, 15f, 20f),
            new("Head", 1, Shape.Sphere, 5f, null, -40f, 40f, 30f, 40f),
            new("Left_UpperLeg", 0, Shape.Capsule, 8f, "Left_LowerLeg", -100f, 20f, 30f, 20f),
            new("Left_LowerLeg", 3, Shape.Capsule, 5f, "Left_Foot", 0f, 130f, 0f, 5f),
            new("Right_UpperLeg", 0, Shape.Capsule, 8f, "Right_LowerLeg", -100f, 20f, 30f, 20f),
            new("Right_LowerLeg", 5, Shape.Capsule, 5f, "Right_Foot", 0f, 130f, 0f, 5f),
            new("Left_UpperArm", 1, Shape.Capsule, 3f, "Left_LowerArm", -120f, 40f, 70f, 30f),
            new("Left_LowerArm", 7, Shape.Capsule, 2f, "Left_Hand", -130f, 0f, 0f, 20f),
            new("Right_UpperArm", 1, Shape.Capsule, 3f, "Right_LowerArm", -120f, 40f, 70f, 30f),
            new("Right_LowerArm", 9, Shape.Capsule, 2f, "Right_Hand", -130f, 0f, 0f, 20f),
        };

        [MenuItem("TeaLeaf/Modify Prefab/Build Guard Ragdoll")]
        public static void Build()
        {
            int layer = LayerMask.NameToLayer(LayerName);

            if (layer < 0) {
                Debug.LogError($"Build Guard Ragdoll: no '{LayerName}' layer. Add it in Project Settings > Tags and Layers.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

            try {
                if (BuildOn(root, layer)) {
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"Build Guard Ragdoll: {_parts.Length} bones on {PrefabPath}.");
                }
            } finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Builds the ragdoll on a loaded guard. Returns false (having
        /// logged why) if a bone or the skinned mesh is missing.
        /// </summary>
        private static bool BuildOn(GameObject root, int layer)
        {
            SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>();

            if (skin == null) {
                Debug.LogError("Build Guard Ragdoll: no SkinnedMeshRenderer on the guard.");
                return false;
            }

            Transform[] bones = new Transform[_parts.Length];

            for (int i = 0; i < _parts.Length; i++) {
                bones[i] = FindBone(skin.bones, _parts[i].Bone);

                if (bones[i] == null) {
                    Debug.LogError($"Build Guard Ragdoll: no bone '{_parts[i].Bone}' in the skin.");
                    return false;
                }
            }

            // Whatever an earlier build left goes first: joints before the
            // bodies they hang on.
            foreach (Transform bone in bones) {
                foreach (CharacterJoint joint in bone.GetComponents<CharacterJoint>()) {
                    Object.DestroyImmediate(joint);
                }
            }

            foreach (Transform bone in bones) {
                foreach (Collider collider in bone.GetComponents<Collider>()) {
                    Object.DestroyImmediate(collider);
                }

                foreach (Rigidbody body in bone.GetComponents<Rigidbody>()) {
                    Object.DestroyImmediate(body);
                }
            }

            List<Vector3>[] points = GatherVertices(skin, bones);

            // The body's own axes in the prefab's pose: the root stands
            // upright, facing +Z.
            Vector3 right = root.transform.right;
            Vector3 forward = root.transform.forward;

            Rigidbody[] bodies = new Rigidbody[_parts.Length];
            Collider[] hitboxes = new Collider[_parts.Length];

            for (int i = 0; i < _parts.Length; i++) {
                Part part = _parts[i];
                Transform bone = bones[i];
                bone.gameObject.layer = layer;

                hitboxes[i] = part.Shape switch {
                    Shape.Box => AddBox(bone, points[i]),
                    Shape.Sphere => AddSphere(bone, points[i]),
                    _ => AddCapsule(bone, points[i], FindBone(skin.bones, part.Next), part.Bone.EndsWith("LowerArm")),
                };

                Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
                body.mass = part.Mass;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
                bodies[i] = body;

                if (part.Parent >= 0) {
                    AddJoint(bone, bodies[part.Parent], part, right, forward);
                }
            }

            GuardRagdoll ragdoll = root.GetComponent<GuardRagdoll>();

            if (ragdoll == null) {
                ragdoll = root.AddComponent<GuardRagdoll>();
            }

            SerializedObject serialized = new(ragdoll);
            SetArray(serialized.FindProperty("bodies"), bodies);
            SetArray(serialized.FindProperty("hitboxes"), hitboxes);
            serialized.FindProperty("headHitbox").objectReferenceValue = hitboxes[2];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static Transform FindBone(Transform[] bones, string name)
        {
            if (name == null) {
                return null;
            }

            foreach (Transform bone in bones) {
                if (bone != null && bone.name == name) {
                    return bone;
                }
            }

            return null;
        }

        /// <summary>
        /// Each ragdoll bone's vertices, in that bone's own space in the
        /// bind pose. A vertex goes to the bone it is skinned to most; a
        /// bone that isn't a ragdoll bone hands its vertices to the
        /// nearest one above it (a foot's to the lower leg, the spine's
        /// to the hips).
        /// </summary>
        private static List<Vector3>[] GatherVertices(SkinnedMeshRenderer skin, Transform[] ragdollBones)
        {
            Transform[] skinBones = skin.bones;
            Mesh mesh = skin.sharedMesh;
            Matrix4x4[] bindposes = mesh.bindposes;

            // Which ragdoll part each skin bone's vertices go to (-1:
            // none, above the hips), and where each ragdoll bone is in
            // the skin's list, for its bind pose.
            int[] partOf = new int[skinBones.Length];
            int[] skinIndexOf = new int[ragdollBones.Length];

            for (int s = 0; s < skinBones.Length; s++) {
                partOf[s] = -1;

                for (Transform t = skinBones[s]; t != null && partOf[s] < 0; t = t.parent) {
                    partOf[s] = System.Array.IndexOf(ragdollBones, t);
                }

                int own = System.Array.IndexOf(ragdollBones, skinBones[s]);

                if (own >= 0) {
                    skinIndexOf[own] = s;
                }
            }

            List<Vector3>[] points = new List<Vector3>[ragdollBones.Length];

            for (int i = 0; i < points.Length; i++) {
                points[i] = new List<Vector3>();
            }

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;

            for (int v = 0; v < vertices.Length; v++) {
                int part = partOf[weights[v].boneIndex0];

                if (part >= 0) {
                    points[part].Add(bindposes[skinIndexOf[part]].MultiplyPoint3x4(vertices[v]));
                }
            }

            return points;
        }

        /// <summary>
        /// A box round the middle of the bone's vertices: on each of its
        /// own axes, from the BoxTrim share in from either side.
        /// </summary>
        private static Collider AddBox(Transform bone, List<Vector3> points)
        {
            Vector3 min = default;
            Vector3 max = default;

            for (int axis = 0; axis < 3; axis++) {
                List<float> values = new(points.Count);

                foreach (Vector3 p in points) {
                    values.Add(p[axis]);
                }

                values.Sort();
                min[axis] = values[(int)(BoxTrim * (values.Count - 1))];
                max[axis] = values[(int)((1f - BoxTrim) * (values.Count - 1))];
            }

            BoxCollider box = bone.gameObject.AddComponent<BoxCollider>();
            box.center = (min + max) * 0.5f;
            box.size = max - min;
            return box;
        }

        /// <summary>
        /// A sphere at the middle of the bone's vertices, as big as the
        /// average of their trimmed half-widths.
        /// </summary>
        private static Collider AddSphere(Transform bone, List<Vector3> points)
        {
            BoxCollider measure = (BoxCollider)AddBox(bone, points);
            Vector3 centre = measure.center;
            Vector3 size = measure.size;
            Object.DestroyImmediate(measure);

            SphereCollider sphere = bone.gameObject.AddComponent<SphereCollider>();
            sphere.center = centre;
            sphere.radius = (size.x + size.y + size.z) / 6f;
            return sphere;
        }

        /// <summary>
        /// A capsule along the bone (its Y axis) from its joint to the
        /// next bone's (over the hand too, for a lower arm), centred on
        /// the vertices round it and as thick as RadiusPercentile of
        /// them. Vertices beyond the next joint (a foot, a hand) don't
        /// count towards the thickness.
        /// </summary>
        private static Collider AddCapsule(Transform bone, List<Vector3> points, Transform next, bool coversHand)
        {
            float length = next != null ? next.localPosition.magnitude : 0.3f;

            Vector2 middle = Vector2.zero;
            int count = 0;

            foreach (Vector3 p in points) {
                if (p.y >= 0f && p.y <= length) {
                    middle += new Vector2(p.x, p.z);
                    count++;
                }
            }

            middle = count > 0 ? middle / count : Vector2.zero;

            List<float> distances = new(count);

            foreach (Vector3 p in points) {
                if (p.y >= 0f && p.y <= length) {
                    distances.Add(Vector2.Distance(new Vector2(p.x, p.z), middle));
                }
            }

            distances.Sort();
            float radius = distances.Count > 0 ? distances[(int)(RadiusPercentile * (distances.Count - 1))] : 0.05f;

            float end = coversHand ? length * (1f + HandShare) : length;

            CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.height = Mathf.Max(end, radius * 2f);
            capsule.center = new Vector3(middle.x, end * 0.5f, middle.y);
            return capsule;
        }

        /// <summary>
        /// Joins a bone's body to its parent's. The joint's main axis is
        /// the body's left (bending forward and back: a joint's angle
        /// about its axis is positive the other way round from Unity's
        /// rotations, so about the left, positive tips the far end
        /// backwards, as the Part limits are written), its swing axis the
        /// body's forward (bending sideways); twist along the bone is the
        /// third.
        /// </summary>
        private static void AddJoint(Transform bone, Rigidbody parent, Part part, Vector3 right, Vector3 forward)
        {
            CharacterJoint joint = bone.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            joint.autoConfigureConnectedAnchor = true;
            joint.anchor = Vector3.zero;
            joint.axis = bone.InverseTransformDirection(-right);
            joint.swingAxis = bone.InverseTransformDirection(forward);
            joint.enablePreprocessing = false;
            joint.enableProjection = true;

            joint.lowTwistLimit = new SoftJointLimit { limit = part.BendLow };
            joint.highTwistLimit = new SoftJointLimit { limit = part.BendHigh };
            joint.swing1Limit = new SoftJointLimit { limit = part.Swing };
            joint.swing2Limit = new SoftJointLimit { limit = part.Twist };
        }

        private static void SetArray<T>(SerializedProperty property, T[] values) where T : Object
        {
            property.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++) {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
