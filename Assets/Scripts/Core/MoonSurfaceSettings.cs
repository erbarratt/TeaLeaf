using System;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Everything that decides what the moon's face looks like: the recipe
    /// MoonSurfaceBuilder follows. The same settings and seed always give
    /// the same moon. Its size, colour and place in the sky aren't here -
    /// those are ProceduralSky's.
    /// </summary>
    [Serializable]
    public class MoonSurfaceSettings
    {
        // Width and height of the generated picture, in pixels. The moon
        // only covers about a hundred screen pixels at 4 degrees wide, so
        // this only needs raising for a very large moon.
        [SerializeField, Range(64, 1024)] private int textureSize = 512;

        // Which moon: every seed gives a different face.
        [SerializeField, Min(0)] private int seed = 1;

        [Header("Seas (the large dark patches)")]

        // How much of the face they cover.
        [SerializeField, Range(0f, 1f)] private float seaAmount = 0.45f;

        // How much darker they are than the rest: 0 = invisible.
        [SerializeField, Range(0f, 1f)] private float seaDarkness = 0.35f;

        // Their size: low = one or two big ones, high = many small ones.
        [SerializeField, Range(0.3f, 4f)] private float seaScale = 1.2f;

        [Header("Craters")]
        [SerializeField, Range(0, 2000)] private int craterCount = 350;

        // A crater's radius as a fraction of the moon's, from the smallest
        // (x) to the largest (y).
        [SerializeField] private Vector2 craterSize = new Vector2(0.012f, 0.16f);

        // How rare large craters are. 1 = every size is equally likely;
        // higher = mostly small ones with a few large.
        [SerializeField, Range(1f, 8f)] private float craterSmallBias = 4f;

        // How strongly craters stand out: darker floors, brighter rims.
        [SerializeField, Range(0f, 1f)] private float craterContrast = 0.5f;

        [Header("Rays (bright streaks thrown out by a young crater)")]

        // How many craters have them.
        [SerializeField, Range(0, 6)] private int rayCraters = 2;

        [SerializeField, Range(0f, 1f)] private float rayBrightness = 0.2f;

        // How far the streaks reach, in crater radii.
        [SerializeField, Range(2f, 20f)] private float rayLength = 9f;

        [Header("Finish")]

        // Fine mottling all over the surface.
        [SerializeField, Range(0f, 1f)] private float roughness = 0.4f;

        // How much darker the moon gets towards its edge, which is what
        // makes it look like a ball rather than a plate.
        [SerializeField, Range(0f, 1f)] private float edgeDarkening = 0.3f;

        public int TextureSize => textureSize;
        public int Seed => seed;
        public float SeaAmount => seaAmount;
        public float SeaDarkness => seaDarkness;
        public float SeaScale => seaScale;
        public int CraterCount => craterCount;
        public Vector2 CraterSize => craterSize;
        public float CraterSmallBias => craterSmallBias;
        public float CraterContrast => craterContrast;
        public int RayCraters => rayCraters;
        public float RayBrightness => rayBrightness;
        public float RayLength => rayLength;
        public float Roughness => roughness;
        public float EdgeDarkening => edgeDarkening;
    }
}
