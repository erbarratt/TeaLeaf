using System;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The sizes HandShapeFitter can't read from the hand's bones, for the
    /// six pieces of each hand's collision shape. Set on PlayerHandVisuals;
    /// read once, at load. Defaults measured from the gauntlet mesh
    /// (hand.fbx): the middle of its surface's distance from each finger's
    /// bones, and the palm's thickness between its back plates and palm.
    /// </summary>
    [Serializable]
    public class HandShapeSizes
    {
        [Tooltip("Radius of the bare index and middle fingers' capsules, in metres.")]
        public float fingerRadius = 0.017f;

        [Tooltip("Radius of the armoured ring and little fingers' capsules, in metres.")]
        public float armouredFingerRadius = 0.019f;

        [Tooltip("Radius of the thumb's capsule, in metres.")]
        public float thumbRadius = 0.018f;

        [Tooltip("Thickness of the palm's box, back of the hand to palm, in metres. Its length runs wrist to knuckles and its width across the knuckles, both from the bones.")]
        public float palmThickness = 0.08f;

        [Tooltip("How far the palm box's middle sits towards the back of the hand from the bones' line, in metres. Negative moves it towards the palm.")]
        public float palmOffset = -0.01f;
    }
}
