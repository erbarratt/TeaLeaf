using Core;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The size and place of one piece of a hand's collision shape (the palm,
    /// a finger, a carried prop's collider) - just the numbers, see
    /// HandCollisionShape for the piece itself. Positions are relative to the
    /// hand visual's root, in the CONTROLLER's axes (like everything
    /// HandPhysicalFollow measures), so one rotation - the controller's -
    /// turns them into the world, and the right hand's mirrored visual needs
    /// no special handling.
    ///
    /// A capsule lies along its own Z, HalfLength each side of Centre (the
    /// centres of its two round ends, not its tips). A box's HalfExtents are
    /// along its own X, Y, Z.
    /// </summary>
    public readonly struct HandShapeGeometry
    {
        public enum ShapeKind
        {
            Box,
            Sphere,
            Capsule
        }

        public ShapeKind Kind { get; }
        public Vector3 Centre { get; }
        public Quaternion Rotation { get; }
        public Vector3 HalfExtents { get; }
        public float Radius { get; }
        public float HalfLength { get; }

        private HandShapeGeometry(ShapeKind kind, Vector3 centre, Quaternion rotation, Vector3 halfExtents, float radius, float halfLength)
        {
            Kind = kind;
            Centre = centre;
            Rotation = rotation;
            HalfExtents = halfExtents;
            Radius = radius;
            HalfLength = halfLength;
        }

        public static HandShapeGeometry Box(Vector3 centre, Quaternion rotation, Vector3 halfExtents)
        {
            return new HandShapeGeometry(ShapeKind.Box, centre, rotation, halfExtents, 0f, 0f);
        }

        public static HandShapeGeometry Sphere(Vector3 centre, float radius)
        {
            return new HandShapeGeometry(ShapeKind.Sphere, centre, Quaternion.identity, Vector3.zero, radius, 0f);
        }

        public static HandShapeGeometry Capsule(Vector3 centre, Quaternion rotation, float radius, float halfLength)
        {
            return new HandShapeGeometry(ShapeKind.Capsule, centre, rotation, Vector3.zero, radius, halfLength);
        }

        /// <summary>
        /// A capsule whose round ends are centred on a and b.
        /// </summary>
        public static HandShapeGeometry CapsuleBetween(Vector3 a, Vector3 b, float radius)
        {
            Vector3 axis = b - a;
            float length = axis.magnitude;

            // Two points on top of each other leave no line to lie along:
            // any rotation will do, it's a ball.
            Quaternion rotation = length > 0.0001f ? Quaternion.LookRotation(axis / length) : Quaternion.identity;
            return Capsule((a + b) * 0.5f, rotation, radius, length * 0.5f);
        }

        /// <summary>
        /// True if other is exactly the same shape - used to resize colliders
        /// only when something actually changed.
        /// </summary>
        public bool Matches(in HandShapeGeometry other)
        {
            return Kind == other.Kind
                && Centre == other.Centre
                && Rotation == other.Rotation
                && HalfExtents == other.HalfExtents
                && Mathf.Approximately(Radius, other.Radius)
                && Mathf.Approximately(HalfLength, other.HalfLength);
        }

        /// <summary>
        /// Draws the shape as wire lines for a hand whose visual's root is at
        /// root and whose controller is turned to controllerRotation.
        /// </summary>
        public void Draw(DebugLines lines, Vector3 root, Quaternion controllerRotation)
        {
            Vector3 centre = root + controllerRotation * Centre;
            Quaternion rotation = controllerRotation * Rotation;

            switch (Kind) {
                case ShapeKind.Box:
                    // WireCube is drawn in the lines' matrix space - turn the
                    // matrix to the box for it, then put it back.
                    Matrix4x4 previous = lines.Matrix;
                    lines.Matrix = previous * Matrix4x4.TRS(centre, rotation, Vector3.one);
                    lines.WireCube(Vector3.zero, HalfExtents * 2f);
                    lines.Matrix = previous;
                    break;
                case ShapeKind.Sphere:
                    lines.WireSphere(centre, Radius);
                    break;
                default:
                    Vector3 half = rotation * Vector3.forward * HalfLength;
                    lines.WireCapsule(centre - half, centre + half, Radius);
                    break;
            }
        }
    }
}
