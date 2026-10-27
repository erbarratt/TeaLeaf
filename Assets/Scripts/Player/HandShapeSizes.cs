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
        [Tooltip("Radius of the bare index and middle fingers' collision capsules, in metres; also padding each side of the palm box's width. e.g. 0.017 = a 3.4cm-thick finger. Higher stops the fingers further from a wall, lower lets them get closer (and through narrower gaps).")]
        public float fingerRadius = 0.017f;

        [Tooltip("Radius of the armoured ring and little fingers' collision capsules, in metres, e.g. 0.019 = a little thicker than the bare fingers for the gauntlet's plates. Higher stops them further from a wall, lower closer.")]
        public float armouredFingerRadius = 0.019f;

        [Tooltip("Radius of the thumb's collision capsule, in metres, e.g. 0.018 = a 3.6cm-thick thumb. Higher stops the thumb further from a wall, lower closer.")]
        public float thumbRadius = 0.018f;

        [Tooltip("Thickness of the palm's box, back of the hand to palm, in metres. Its length runs wrist to knuckles and its width across the knuckles, both from the bones. e.g. 0.08 = an 8cm-thick palm. Higher keeps a flat hand further off a wall, lower lets it lie closer.")]
        public float palmThickness = 0.08f;

        [Tooltip("How far the palm box's middle sits towards the back of the hand from the bones' line, in metres. Negative moves it towards the palm. e.g. -0.01 = the box sits 1cm towards the palm; 0 centres it on the bones.")]
        public float palmOffset = -0.01f;
    }
}
