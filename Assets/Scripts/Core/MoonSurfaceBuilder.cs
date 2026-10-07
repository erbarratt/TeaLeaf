using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Core
{
    /// <summary>
    /// Paints the moon's face: a small greyscale picture of dark seas,
    /// craters of many sizes, ray streaks and fine mottling, made once when
    /// the level loads from a MoonSurfaceSettings. The sky shader multiplies
    /// the moon's colour by it. The picture is the moon as seen from here:
    /// a ball viewed straight on. Every pixel inside the disc is first
    /// turned into the point on the ball's surface it shows, and all the
    /// features are worked out on the ball - so craters near the edge are
    /// squashed the way they are on the real moon, with no extra work.
    /// Painted ahead of time rather than worked out in the shader for every
    /// pixel every frame, because a texture comes with smaller copies of
    /// itself (mipmaps) that the graphics card switches to when the moon is
    /// small on screen - without them, craters finer than a screen pixel
    /// would flicker as the head moves. The work is done off the main
    /// thread (Unity's job system) and compiled by Burst: a few
    /// milliseconds.
    /// </summary>
    public static class MoonSurfaceBuilder
    {
        // A crater changes the surface out to this many of its radii: the
        // rim sits at 1, and its outer slope fades out beyond.
        private const float CraterReach = 1.4f;

        // Craters are also placed a little way round the back of the ball
        // (this far below the edge), so some poke over the edge.
        private const float BehindEdge = -0.15f;

        // Craters with rays are kept this far in from the edge, where their
        // streaks can be seen.
        private const float RayCraterMinFacing = 0.35f;

        // One crater, ready for the job to paint.
        private struct Crater
        {
            // Its centre on the unit ball (z towards the viewer).
            public float3 Centre;

            // As a fraction of the moon's radius.
            public float Radius;

            // 1 if it throws out rays.
            public int HasRays;

            // A different number per ray crater, so each has its own
            // pattern of streaks.
            public float RayPattern;
        }

        /// <summary>
        /// Generates the moon's face. The caller owns the texture and must
        /// destroy it.
        /// </summary>
        public static Texture2D Build(MoonSurfaceSettings settings)
        {
            int size = settings.TextureSize;
            int pixelCount = size * size;

            // With mipmaps (the "true"), blended between when the moon's
            // size on screen falls between two of them (Trilinear).
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) {
                name = "Moon Surface",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };

            Random random = Random.CreateFromIndex((uint)settings.Seed);

            // The seas and the mottling are patterns filling all of space,
            // read where the ball's surface passes through them. A random
            // offset per seed moves the ball to a different part.
            float3 seaOffset = random.NextFloat3(-100f, 100f);
            float3 roughOffset = random.NextFloat3(-100f, 100f);

            NativeArray<Crater> craters = BuildCraters(settings, ref random);
            var brightness = new NativeArray<float>(pixelCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var pixels = new NativeArray<Color32>(pixelCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var groundJob = new GroundJob {
                Size = size,
                SeaThreshold = math.lerp(0.8f, 0.3f, settings.SeaAmount),
                SeaDarkness = settings.SeaDarkness,
                SeaScale = settings.SeaScale,
                SeaOffset = seaOffset,
                Roughness = settings.Roughness,
                RoughOffset = roughOffset,
                Brightness = brightness
            };

            var cratersJob = new CratersJob {
                Size = size,
                Contrast = settings.CraterContrast,
                RayBrightness = settings.RayBrightness,
                RayLength = settings.RayLength,
                EdgeDarkening = settings.EdgeDarkening,

                // The direction light seems to come from (up and to the
                // left), used only to brighten one side of each rim and
                // darken the other, which makes a ring read as a hollow.
                ReliefLight = math.normalize(new float3(-0.6f, 0.7f, 0f)),
                Craters = craters,
                Brightness = brightness,
                Pixels = pixels
            };

            // The ground first, its rows split across every core; then the
            // craters on one thread (they overlap, so two threads could
            // write the same pixel).
            JobHandle handle = groundJob.Schedule(pixelCount, size);
            handle = cratersJob.Schedule(handle);
            handle.Complete();

            texture.SetPixelData(pixels, 0);

            craters.Dispose();
            brightness.Dispose();
            pixels.Dispose();

            // Makes the mipmaps, sends it all to the graphics card and
            // drops the copy in main memory.
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>
        /// Picks every crater's place and size. Ordered largest first, so
        /// that small craters are painted over big ones (as on the real
        /// moon, where the big ones are the oldest), with the ray craters
        /// last of all, their streaks lying over everything.
        /// </summary>
        private static NativeArray<Crater> BuildCraters(MoonSurfaceSettings settings, ref Random random)
        {
            var craters = new Crater[settings.CraterCount + settings.RayCraters];

            for (int i = 0; i < settings.CraterCount; i++) {
                // Raising a 0-1 random number to a power pushes most
                // results towards 0: small.
                float sizeRoll = math.pow(random.NextFloat(), settings.CraterSmallBias);

                craters[i] = new Crater {
                    Centre = RandomPointOnBall(ref random, BehindEdge),
                    Radius = math.lerp(settings.CraterSize.x, settings.CraterSize.y, sizeRoll)
                };
            }

            Array.Sort(craters, 0, settings.CraterCount, Comparer<Crater>.Create((a, b) => b.Radius.CompareTo(a.Radius)));

            for (int i = 0; i < settings.RayCraters; i++) {
                // Middling in size: a ray crater is a young one, and the
                // very large craters are the old ones.
                craters[settings.CraterCount + i] = new Crater {
                    Centre = RandomPointOnBall(ref random, RayCraterMinFacing),
                    Radius = math.lerp(settings.CraterSize.x, settings.CraterSize.y, random.NextFloat(0.3f, 0.55f)),
                    HasRays = 1,
                    RayPattern = random.NextFloat(0f, 100f)
                };
            }

            return new NativeArray<Crater>(craters, Allocator.TempJob);
        }

        /// <summary>
        /// A random point on the unit ball, equally likely anywhere that
        /// faces the viewer by at least minFacing (1 = the middle of the
        /// disc, 0 = its edge, below 0 = round the back).
        /// </summary>
        private static float3 RandomPointOnBall(ref Random random, float minFacing)
        {
            float facing = random.NextFloat(minFacing, 1f);
            float angle = random.NextFloat(0f, 2f * math.PI);
            float ring = math.sqrt(1f - facing * facing);
            return new float3(ring * math.cos(angle), ring * math.sin(angle), facing);
        }

        /// <summary>
        /// The point on the unit ball that a pixel of the picture shows.
        /// The disc fills the picture; a pixel outside the disc (the
        /// picture's corners) gets the nearest point on the ball's edge,
        /// so the corners are filled with edge colour rather than left
        /// black to bleed into the smaller mipmaps.
        /// </summary>
        private static float3 PointOnBall(int x, int y, int size)
        {
            float2 flat = (new float2(x, y) + 0.5f) / size * 2f - 1f;
            float distanceSquared = math.lengthsq(flat);

            if (distanceSquared >= 1f) {
                return new float3(flat / math.sqrt(distanceSquared), 0f);
            }

            // Pythagoras: x, y and z of a point on a unit ball square and
            // add to 1, so z is whatever is left.
            return new float3(flat, math.sqrt(1f - distanceSquared));
        }

        /// <summary>
        /// Layers of simplex noise (a smooth random pattern through 3D
        /// space), each with features half the size and half the strength
        /// of the last: big shapes with finer detail on top. About -1 to 1.
        /// </summary>
        private static float LayeredNoise(float3 point, int layers)
        {
            float sum = 0f;
            float total = 0f;
            float strength = 0.5f;

            for (int i = 0; i < layers; i++) {
                sum += strength * noise.snoise(point);
                total += strength;
                strength *= 0.5f;
                point *= 2.03f;
            }

            return sum / total;
        }

        /// <summary>
        /// The surface before any craters: bright highlands, the dark seas,
        /// and the mottling. One pixel per call; 1 = full brightness.
        /// </summary>
        [BurstCompile(CompileSynchronously = true)]
        private struct GroundJob : IJobParallelFor
        {
            public int Size;
            public float SeaThreshold;
            public float SeaDarkness;
            public float SeaScale;
            public float3 SeaOffset;
            public float Roughness;
            public float3 RoughOffset;
            [WriteOnly] public NativeArray<float> Brightness;

            public void Execute(int index)
            {
                float3 point = PointOnBall(index % Size, index / Size, Size);

                // Sea wherever the large pattern rises above the threshold,
                // with a soft shore.
                float seaPattern = LayeredNoise(point * SeaScale + SeaOffset, 4) * 0.5f + 0.5f;
                float sea = math.smoothstep(SeaThreshold, SeaThreshold + 0.12f, seaPattern);
                float value = math.lerp(0.92f, 0.92f * (1f - SeaDarkness), sea);

                // Mottling: a much finer pattern, a few percent either way.
                value *= 1f + Roughness * 0.2f * LayeredNoise(point * 9f + RoughOffset, 4);

                Brightness[index] = value;
            }
        }

        /// <summary>
        /// Paints every crater over the ground, then finishes the picture:
        /// darkens the edge and stores each pixel as an 8-bit grey.
        /// </summary>
        [BurstCompile(CompileSynchronously = true)]
        private struct CratersJob : IJob
        {
            public int Size;
            public float Contrast;
            public float RayBrightness;
            public float RayLength;
            public float EdgeDarkening;
            public float3 ReliefLight;
            [ReadOnly] public NativeArray<Crater> Craters;
            public NativeArray<float> Brightness;
            [WriteOnly] public NativeArray<Color32> Pixels;

            public void Execute()
            {
                for (int i = 0; i < Craters.Length; i++) {
                    Crater crater = Craters[i];

                    if (crater.HasRays != 0) {
                        PaintRays(crater);
                    }

                    PaintCrater(crater);
                }

                for (int i = 0; i < Brightness.Length; i++) {
                    // z is 1 in the middle of the disc and 0 at its edge.
                    float facing = PointOnBall(i % Size, i / Size, Size).z;
                    float value = Brightness[i] * math.lerp(1f - EdgeDarkening, 1f, math.sqrt(facing));
                    byte grey = (byte)math.round(math.saturate(value) * 255f);
                    Pixels[i] = new Color32(grey, grey, grey, 255);
                }
            }

            /// <summary>
            /// Works out the box of pixels that could be within reach of a
            /// point on the ball. Seen flat, a patch of the ball is never
            /// bigger than it really is, so a box of the reach itself
            /// always covers it. False if the box misses the picture.
            /// </summary>
            private bool TryGetBox(float3 centre, float reach, out int4 box)
            {
                int minX = (int)math.floor((centre.x - reach + 1f) * 0.5f * Size);
                int maxX = (int)math.floor((centre.x + reach + 1f) * 0.5f * Size);
                int minY = (int)math.floor((centre.y - reach + 1f) * 0.5f * Size);
                int maxY = (int)math.floor((centre.y + reach + 1f) * 0.5f * Size);

                box = new int4(math.max(minX, 0), math.max(minY, 0), math.min(maxX, Size - 1), math.min(maxY, Size - 1));
                return box.x <= box.z && box.y <= box.w;
            }

            /// <summary>
            /// One crater: a darker floor inside, a bright ring at its
            /// radius, lit on one side and shaded on the other.
            /// </summary>
            private void PaintCrater(Crater crater)
            {
                if (!TryGetBox(crater.Centre, crater.Radius * CraterReach, out int4 box)) {
                    return;
                }

                for (int y = box.y; y <= box.w; y++) {
                    for (int x = box.x; x <= box.z; x++) {
                        float3 offset = PointOnBall(x, y, Size) - crater.Centre;
                        float distance = math.length(offset);

                        // 0 at the crater's centre, 1 at its rim.
                        float t = distance / crater.Radius;

                        if (t >= CraterReach) {
                            continue;
                        }

                        float floor = 1f - math.smoothstep(0.75f, 1f, t);

                        // A bell curve centred on the rim.
                        float rimOffset = (t - 1f) / 0.15f;
                        float rim = math.exp(-rimOffset * rimOffset);

                        // Which side of the crater this pixel is on,
                        // relative to the light: -1 to 1.
                        float side = distance > 0.00001f ? math.dot(offset / distance, ReliefLight) : 0f;

                        int index = y * Size + x;
                        float value = Brightness[index];
                        value = math.lerp(value, value * (1f - 0.5f * Contrast), floor);
                        value += Contrast * rim * (0.16f + 0.14f * side);
                        Brightness[index] = value;
                    }
                }
            }

            /// <summary>
            /// The bright streaks round a ray crater: strongest near it,
            /// fading to nothing at their full length, and only along some
            /// directions - picked by a noise pattern read round a circle,
            /// so the pattern joins up with itself.
            /// </summary>
            private void PaintRays(Crater crater)
            {
                float reach = crater.Radius * RayLength;

                if (RayBrightness <= 0f || !TryGetBox(crater.Centre, reach, out int4 box)) {
                    return;
                }

                // Two directions lying flat on the ball's surface at the
                // crater, to measure "which way round the crater" with.
                float3 reference = math.abs(crater.Centre.y) > 0.99f ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f);
                float3 across = math.normalize(math.cross(reference, crater.Centre));
                float3 along = math.cross(crater.Centre, across);

                for (int y = box.y; y <= box.w; y++) {
                    for (int x = box.x; x <= box.z; x++) {
                        float3 offset = PointOnBall(x, y, Size) - crater.Centre;
                        float distance = math.length(offset);

                        if (distance >= reach || distance < crater.Radius) {
                            continue;
                        }

                        // The direction from the crater, as a point on a
                        // circle; the noise there says how bright a streak
                        // runs out that way. Cubed, so most directions get
                        // almost nothing and a few get a strong streak.
                        float2 around = math.normalize(new float2(math.dot(offset, across), math.dot(offset, along)));
                        float streak = noise.snoise(new float3(around * 3.5f, crater.RayPattern)) * 0.5f + 0.5f;
                        streak = streak * streak * streak;

                        float fade = 1f - distance / reach;
                        Brightness[y * Size + x] += RayBrightness * 2f * streak * fade * fade;
                    }
                }
            }
        }
    }
}
