// A cobbled floor made entirely in the shader: no textures. Every pixel works
// out which cobble it's on from its position in the WORLD, so:
// - the cobbles are the same size on any object, however it's scaled, and
//   two floor pieces side by side join up with no seam;
// - the pattern never repeats: each cobble's place, shape and colour come
//   from a random number made from its cell's coordinates.
//
// How the pattern is made (a "Voronoi" pattern): the surface is divided into
// a grid of square cells, each cobbleSize across, and every cell holds one
// point, nudged a random distance off-centre. A pixel belongs to whichever
// point is nearest, which carves the surface into irregular many-sided
// stones. The pixel's distance to the nearest border between two stones
// gives the gap (near the border) and the stone's rounded shoulder (a little
// further in). Blending those border distances smoothly where several meet
// rounds the stones' corners off.
//
// Lighting is URP's simple (Blinn-Phong) model - the cheap one - with the
// surface normal tilted on the stones' shoulders so they catch the light.
// The tilt is worked out from how the height changes between neighbouring
// pixels, so no tangents or normal map are needed.
//
// Cost, for Quest: this is paid per pixel, on a surface that fills the view.
// About 25 random numbers and 24 border distances per pixel (9 and 8 with
// Narrow Search - see Cobbles() for when that's safe), twice that with
// Parallax on (the pattern is worked out a second time at a shifted spot).
// Parallax is a single shift, not parallax occlusion's many samples. The
// Dirt layer adds four smooth noises (16 more random numbers). If
// profiling says it's too much, bake the pattern into textures instead.
//
// Because the pattern is fixed to the world, it slides across anything that
// moves. For floors (and walls - it picks whichever world axis the surface
// faces most), not props.
//
// No Meta pass yet: a lightmap bake won't see the cobbles' colours.
Shader "TeaLeaf/Cobblestone"
{
    Properties
    {
        [Header(Layout)]
        // Metres across one grid cell - roughly one cobble.
        _CobbleSize ("Cobble Size (m)", Range(0.03, 1)) = 0.14
        // 0 = a perfect square grid, 1 = as uneven as it goes.
        _Irregularity ("Irregularity", Range(0, 1)) = 0.7
        // How much the stones' corners are rounded off (also opens the gaps
        // a little where three stones meet).
        _CornerRoundness ("Corner Roundness", Range(0.001, 0.5)) = 0.15
        // 0 = every stone about one cell big. Higher gives each stone a
        // random weight, and heavier ones take ground from lighter ones:
        // a mix of big and small. The gaps stay the same width.
        _SizeVariation ("Size Variation", Range(0, 1)) = 0
        // Off (the default): each pixel looks at the 5 x 5 grid cells round
        // it for stones, which is right for any settings. On: only 3 x 3 -
        // about a third of the work, but only correct with Size Variation
        // at or near 0 and a modest Corner Roundness; otherwise thin
        // straight lines and wrongly-lit wedges appear on the stones.
        [Toggle(_NARROW_SEARCH)] _NarrowSearch ("Narrow Search (cheaper)", Float) = 0
        // Any number: a different one gives a different layout.
        _Seed ("Seed", Float) = 0

        [Header(Shape)]
        // The gap round each stone's ROUNDED outline, as a fraction of the
        // cobble size. Corner Roundness pulls that outline in near the
        // corners, which opens pockets where stones meet; a negative gap
        // grows the outline back out and shrinks them.
        _GapWidth ("Gap Width", Range(-1, 0.4)) = 0.08
        // The line always kept between two neighbouring stones, however
        // far a negative Gap Width grows them - without it they'd fuse
        // along their straight sides. A fraction of the cobble size.
        _MinGap ("Minimum Gap", Range(0, 0.2)) = 0.02
        // How far in from the gap the stone's rounded shoulder runs before
        // its flat top, as a fraction of the cobble size.
        _Bevel ("Shoulder Width", Range(0.01, 0.5)) = 0.16
        // How deep the gaps are, in metres - sets how steep the shoulders
        // look in the lighting.
        _Depth ("Gap Depth (m)", Range(0, 0.1)) = 0.015
        _NormalStrength ("Lighting Strength", Range(0, 4)) = 1

        [Header(Colour)]
        // Each stone takes a colour somewhere along A - B - C.
        _ColorA ("Stone Colour A", Color) = (0.23, 0.10, 0.09, 1)
        _ColorB ("Stone Colour B", Color) = (0.26, 0.18, 0.13, 1)
        _ColorC ("Stone Colour C", Color) = (0.27, 0.26, 0.25, 1)
        _GapColor ("Gap Colour", Color) = (0.07, 0.065, 0.06, 1)
        // How much lighter or darker than its colour a stone can be.
        _BrightnessVariation ("Brightness Variation", Range(0, 1)) = 0.25
        // How much darker a stone is down its shoulder than on top.
        _EdgeDarkening ("Edge Darkening", Range(0, 1)) = 0.35
        // A soft shadow in the crevices, darkest on the line where a stone
        // meets the gap and easing off up the stone and out into the gap:
        // how dark it gets (0 = off), and how far it reaches from that
        // line, as a fraction of the cobble size.
        _OcclusionStrength ("Crevice Shadow", Range(0, 1)) = 0.6
        _OcclusionWidth ("Crevice Shadow Width", Range(0.01, 0.5)) = 0.12

        [Header(Shine)]
        [Toggle(_SPECULAR_COLOR)] _Specular ("Shine", Float) = 0
        _SpecColor ("Shine Colour", Color) = (0.12, 0.12, 0.12, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.4

        [Header(Parallax)]
        // Makes the gaps look sunk below the stones by shifting where the
        // pattern is read, by how far the view is off straight-on. Works
        // the pattern out twice: leave off on Quest unless profiled.
        [Toggle(_PARALLAX)] _Parallax ("Parallax", Float) = 0
        _ParallaxDepth ("Parallax Depth (m)", Range(0, 0.1)) = 0.02

        [Header(Dirt)]
        // A second layer that roughens everything up: grime in patches,
        // mottled stones, gritty gaps, chipped edges and slightly warped
        // shapes. Off, none of it is worked out.
        [Toggle(_DIRT)] _Dirt ("Dirt", Float) = 0
        _DirtColor ("Dirt Colour", Color) = (0.10, 0.085, 0.065, 1)
        // Grime: how strongly it covers, how much of the floor has a patch
        // on it, and how big the patches are.
        _GrimeAmount ("Grime Amount", Range(0, 1)) = 0.6
        _GrimeCoverage ("Grime Coverage", Range(0, 1)) = 0.45
        _GrimeScale ("Grime Patch Size (m)", Range(0.1, 10)) = 1.5
        // How much dirt lies in the gaps and down the stones' shoulders
        // everywhere, patch or not.
        _GrimeLowBias ("Dirt In Low Places", Range(0, 1)) = 0.5
        // Fine noise: lighter and darker flecks on the stones, how big the
        // flecks are, how much they pit the surface for the lighting, and
        // how much they vary the gap colour.
        _MottleAmount ("Stone Mottling", Range(0, 1)) = 0.3
        _MottleScale ("Fleck Size (m)", Range(0.0005, 0.2)) = 0.03
        // Flecks are never drawn smaller than this many pixels across:
        // further away they grow instead (see the fragment shader). Lower
        // keeps them fine for longer, but they start to shimmer as the
        // head moves; higher is steadier and coarser.
        _FleckPixels ("Smallest Fleck (pixels)", Range(1, 8)) = 4
        _MottleBump ("Pitting Depth (m)", Range(0, 0.01)) = 0.002
        _GapNoiseAmount ("Gap Grit", Range(0, 1)) = 0.5
        // How far the same flecks push the stone's outline in and out, as
        // a fraction of the cobble size.
        _EdgeRaggedness ("Edge Raggedness", Range(0, 0.3)) = 0.06
        // Bends the whole pattern, so the stones' sides aren't straight:
        // how far (as a fraction of the cobble size) and over what distance.
        _WarpAmount ("Shape Warp", Range(0, 0.5)) = 0.12
        _WarpScale ("Shape Warp Size (m)", Range(0.05, 5)) = 0.5

        [Header(Distance)]
        // Far away, cobbles get smaller than a pixel and would shimmer, so
        // they fade to one flat colour. Measured in cobbles per pixel: the
        // fade starts at the first value and is complete at the second.
        _FadeStart ("Fade Start (cobbles per pixel)", Range(0.01, 2)) = 0.25
        _FadeEnd ("Fade End (cobbles per pixel)", Range(0.02, 3)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "SimpleLit"
            "Queue" = "Geometry"
        }

        // Shared by every pass. The properties must be declared identically
        // in all of them, inside this CBUFFER, for the SRP Batcher to batch
        // objects using the shader.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _CobbleSize;
            float _Irregularity;
            float _CornerRoundness;
            float _Seed;
            float _GapWidth;
            float _Bevel;
            float _Depth;
            float _NormalStrength;
            half4 _ColorA;
            half4 _ColorB;
            half4 _ColorC;
            half4 _GapColor;
            half _BrightnessVariation;
            half _EdgeDarkening;
            half4 _SpecColor;
            half _Smoothness;
            float _ParallaxDepth;
            float _FadeStart;
            float _FadeEnd;
            half4 _DirtColor;
            half _GrimeAmount;
            half _GrimeCoverage;
            float _GrimeScale;
            half _GrimeLowBias;
            half _MottleAmount;
            float _MottleScale;
            float _MottleBump;
            half _GapNoiseAmount;
            float _EdgeRaggedness;
            float _WarpAmount;
            float _WarpScale;
            float _SizeVariation;
            float _MinGap;
            float _FleckPixels;
            half _OcclusionStrength;
            float _OcclusionWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // This material's own switches. shader_feature: only the
            // combinations some material actually uses end up in a build.
            #pragma shader_feature_local_fragment _SPECULAR_COLOR
            #pragma shader_feature_local_fragment _PARALLAX
            #pragma shader_feature_local_fragment _DIRT
            #pragma shader_feature_local_fragment _NARROW_SEARCH

            // The same lighting options URP's Simple Lit shader compiles
            // for: shadows, extra lights, baked light, fog, foveation.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON

            // Single-pass instanced stereo (each eye is an instance).
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;

                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    half4 fogFactorAndVertexLight : TEXCOORD2;
                #else
                    half fogFactor : TEXCOORD2;
                #endif

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord : TEXCOORD3;
                #endif

                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);

                #ifdef USE_APV_PROBE_OCCLUSION
                    float4 probeOcclusion : TEXCOORD5;
                #endif

                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Two random numbers (0-1) from a cell's coordinates: the same
            // cell always gives the same two. Multiply, keep the fraction,
            // mix, repeat - no sine, which gives different answers on
            // different graphics chips. (Dave Hoskins' "hash without sine".)
            float2 Hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Smooth noise, two values at once (each 0-1): a random pair at
            // every whole-number corner of a grid, blended across each
            // square, so it varies gently from place to place with no
            // pattern. One unit of p = one blob. The "f * f * (3 - 2f)"
            // curve eases the blend so the grid's lines don't show.
            float2 Noise2(float2 p)
            {
                float2 corner = floor(p);
                float2 f = p - corner;
                float2 blend = f * f * (3.0 - 2.0 * f);

                float2 a = Hash22(corner);
                float2 b = Hash22(corner + float2(1.0, 0.0));
                float2 c = Hash22(corner + float2(0.0, 1.0));
                float2 d = Hash22(corner + float2(1.0, 1.0));

                return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
            }

            // Brings a grid corner's coordinates back into 0-1023, like a
            // clock going round. See FleckNoise2().
            float2 WrapCorner(float2 corner)
            {
                return corner - 1024.0 * floor(corner / 1024.0);
            }

            // Noise2() for the flecks, which can be under a millimetre: a
            // few metres from the world's origin their grid coordinates
            // are already in the tens of thousands, and Hash22() loses its
            // randomness on numbers that big (it relies on the digits
            // after the decimal point, and a float has only so many
            // digits in all). So each corner's coordinates are wrapped
            // into 0-1023 before being hashed. The noise then repeats
            // every 1024 flecks, far too many to notice.
            float2 FleckNoise2(float2 p)
            {
                float2 corner = floor(p);
                float2 f = p - corner;
                float2 blend = f * f * (3.0 - 2.0 * f);

                float2 a = Hash22(WrapCorner(corner));
                float2 b = Hash22(WrapCorner(corner + float2(1.0, 0.0)));
                float2 c = Hash22(WrapCorner(corner + float2(0.0, 1.0)));
                float2 d = Hash22(WrapCorner(corner + float2(1.0, 1.0)));

                return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
            }

            // The smaller of a and b, but where they're within k of each
            // other the answer dips a little below both, so the change from
            // one to the other is a curve instead of a corner.
            float SmoothMin(float a, float b, float k)
            {
                float h = max(k - abs(a - b), 0.0) / k;
                return min(a, b) - h * h * k * 0.25;
            }

            // Which stone the point uv is on (cell = its grid cell, for
            // random numbers), and how far inside that stone it is (edge, in
            // cells; 0 on the stone's edge, below 0 in the gap).
            //
            // First loop: the point in each of the cells round uv, and
            // which is nearest (allowing for the stones' weights) - that's
            // the stone. Second loop: for each of the other points, the
            // distance from uv to the border its stone shares with this
            // one; the smallest is the nearest border.
            //
            // How many cells round uv are searched matters. Every stone
            // that could border this one must be among them: a stone left
            // out gives no border, so the answer is wrong in the part of
            // the stone that border shapes - and where the list of cells
            // changes (at a cell's edge) the height jumps, which the
            // lighting draws as a thin straight line, with a wrongly-lit
            // wedge beside it. Stones about one cell across are safe with
            // 3 x 3 cells (SEARCH_RADIUS 1). Size Variation makes stones
            // up to twice that, and a strong Corner Roundness reaches
            // further too, so the default is 5 x 5 (SEARCH_RADIUS 2) -
            // nearly three times the work. Narrow Search switches back.
            #if defined(_NARROW_SEARCH)
                #define SEARCH_RADIUS 1
            #else
                #define SEARCH_RADIUS 2
            #endif

            #define SEARCH_WIDTH (SEARCH_RADIUS * 2 + 1)
            #define SEARCH_COUNT (SEARCH_WIDTH * SEARCH_WIDTH)

            void Cobbles(float2 uv, out float edge, out float2 cell)
            {
                float2 baseCell = floor(uv);
                float2 inCell = uv - baseCell;

                // For each point: the way to it, and its "power" - its
                // distance squared, less the stone's weight. A pixel
                // belongs to the point with the lowest power, so a heavier
                // stone wins ground a plain nearest-point contest would
                // have given its neighbours: bigger and smaller stones.
                float2 toPoint[SEARCH_COUNT];
                float power[SEARCH_COUNT];
                float2 nearest = float2(0.0, 0.0);
                float nearestPower = 100.0;
                cell = baseCell;

                for (int y = -SEARCH_RADIUS; y <= SEARCH_RADIUS; y++)
                {
                    for (int x = -SEARCH_RADIUS; x <= SEARCH_RADIUS; x++)
                    {
                        float2 cellOffset = float2(x, y);
                        float2 jitter = Hash22(baseCell + cellOffset + _Seed);

                        // The cell's point: its middle, moved by up to half
                        // a cell each way at full irregularity.
                        float2 r = cellOffset + 0.5 + (jitter - 0.5) * _Irregularity - inCell;

                        // The stone's weight: a third random number, made
                        // by scrambling the two already in hand rather than
                        // paying for another. Up to half a cell squared at
                        // full size variation.
                        float weight = frac(jitter.x * 91.7 + jitter.y * 37.3) * _SizeVariation * 0.5;
                        float pointPower = dot(r, r) - weight;

                        int index = (y + SEARCH_RADIUS) * SEARCH_WIDTH + (x + SEARCH_RADIUS);
                        toPoint[index] = r;
                        power[index] = pointPower;

                        if (pointPower < nearestPower)
                        {
                            nearestPower = pointPower;
                            nearest = r;
                            cell = baseCell + cellOffset;
                        }
                    }
                }

                // Two running answers. "rounded" blends the border distances
                // smoothly, which rounds the corners (and falls below the
                // true distance near them). "border" is the plain smallest:
                // the true distance to the nearest border, corners sharp.
                float k = max(_CornerRoundness, 0.001);
                float rounded = 8.0;
                float border = 8.0;

                for (int i = 0; i < SEARCH_COUNT; i++)
                {
                    float2 between = toPoint[i] - nearest;
                    float sqr = dot(between, between);

                    // Not the stone's own point (which is zero away from
                    // itself).
                    if (sqr > 0.00001)
                    {
                        // The border between two stones is the straight
                        // line where their powers are equal, and half the
                        // difference in power over the distance between
                        // the two points is how far uv is from it - a true
                        // distance, whatever the weights, so the gap stays
                        // the same width between stones of any size. (With
                        // no weights this is the line halfway between the
                        // two points.)
                        float d = 0.5 * (power[i] - nearestPower) * rsqrt(sqr);
                        rounded = SmoothMin(rounded, d, k);
                        border = min(border, d);
                    }
                }

                // How far inside the stone uv is (below 0 = in the gap).
                // The stone is what's inside BOTH outlines: the rounded
                // one, drawn in by half the gap width (or grown, if that's
                // negative), and the true border drawn in by half the
                // minimum gap. Each stone gives up half of a gap; its
                // neighbour gives the other half.
                edge = min(rounded - _GapWidth * 0.5, border - _MinGap * 0.5);
            }

            // How high the stone is, from how far inside it a pixel is: 0
            // at its edge (and in the gap), rising over the shoulder to 1
            // on top. t * (2 - t) climbs steeply at first and levels off -
            // a rounded shoulder.
            float Height(float edge)
            {
                float t = saturate(edge / max(_Bevel, 0.001));
                return t * (2.0 - t);
            }

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                #if defined(_FOG_FRAGMENT)
                    half fogFactor = 0;
                #else
                    half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                #endif

                output.positionWS = vertexInput.positionWS;
                output.positionCS = vertexInput.positionCS;
                output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);

                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);

                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
                #else
                    output.fogFactor = fogFactor;
                #endif

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    output.shadowCoord = GetShadowCoord(vertexInput);
                #endif

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 positionWS = input.positionWS;
                half3 surfaceNormal = NormalizeNormalPerPixel(input.normalWS);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);

                // Lay the pattern out flat along whichever world axis the
                // surface faces most: seen from above for a floor (X and Z),
                // from the side for a wall. uAxis/vAxis are the two world
                // directions the pattern's coordinates run along.
                float3 facing = abs(surfaceNormal);
                bool isFloor = facing.y >= facing.x && facing.y >= facing.z;
                bool isSideX = !isFloor && facing.x >= facing.z;

                float2 planar = isFloor ? positionWS.xz : (isSideX ? positionWS.zy : positionWS.xy);
                float3 uAxis = (isSideX ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0));
                float3 vAxis = (isFloor ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0));

                // In cells: one unit = one cobble.
                float size = max(_CobbleSize, 0.001);
                float2 uv = planar / size;

                // How many cobbles one pixel covers here (fwidth = how much
                // a value changes from this pixel to the next). Once that
                // nears a whole cobble the pattern can't be drawn, only
                // shimmer - so it fades out to a flat colour.
                float cobblesPerPixel = max(fwidth(uv.x), fwidth(uv.y));
                half fade = smoothstep(_FadeStart, _FadeEnd, cobblesPerPixel);

                #if defined(_DIRT)
                    // Two slow noises, read once each. grimeNoise.x places
                    // the grime patches. warpNoise bends the pattern: its
                    // two values, re-centred on zero, push the coordinates
                    // sideways before the cobbles are found, so borders
                    // that would be straight lines wander a little. (Its y
                    // also adds some smaller detail to the grime patches.)
                    float2 grimeNoise = Noise2(planar / max(_GrimeScale, 0.01) + _Seed);
                    float2 warpNoise = Noise2(planar / max(_WarpScale, 0.01) + _Seed + 31.7);
                    uv += (warpNoise - 0.5) * 2.0 * _WarpAmount;
                #endif

                float edge;
                float2 cell;

                #if defined(_PARALLAX)
                    // Find the height here, then read the pattern from a
                    // spot shifted along the view by how far below the
                    // stone tops that is: gaps slide under the stones as
                    // the view tilts. One shift only ("offset limited"), so
                    // it's an impression, not a true depth.
                    Cobbles(uv, edge, cell);
                    float sink = (1.0 - Height(edge)) * (1.0 - fade) * _ParallaxDepth / size;
                    uv -= float2(dot(viewDirWS, uAxis), dot(viewDirWS, vAxis)) * sink;
                #endif

                Cobbles(uv, edge, cell);

                #if defined(_DIRT)
                    // The fine noise ("flecks"), read at the final spot so
                    // it stays put on the stones under parallax.
                    //
                    // A fleck smaller than a few pixels would shimmer, and
                    // fading the flecks out there (how this was first
                    // built) left the dirt showing only near the camera -
                    // with 5mm flecks, within about a metre. Instead the
                    // flecks GROW with distance: whenever one would be
                    // under _FleckPixels across, the noise is read at double
                    // the size, and double again, as often as it takes.
                    // "level" is how many doublings that is (log2 = how
                    // many times a number halves down to 1); it's read at
                    // the whole number below and the one above, and the
                    // two are blended by the fraction, so the change of
                    // size is gradual. There is always some texture.
                    float fleckSize = max(_MottleScale, 0.0002);
                    float metresPerPixel = cobblesPerPixel * size;
                    float level = max(log2(metresPerPixel * max(_FleckPixels, 1.0) / fleckSize), 0.0);
                    float growth = exp2(floor(level));
                    float between = frac(level);
                    float2 fleckUV = uv * size / fleckSize;

                    float2 fleck = lerp(
                        FleckNoise2(fleckUV / growth),
                        FleckNoise2(fleckUV / (growth * 2.0) + 7.7),
                        between);

                    // Two noises averaged are flatter than either alone
                    // (their highs and lows partly cancel), which would
                    // show as bands of weaker dirt at certain distances.
                    // This stretches the blend back to full contrast.
                    fleck = 0.5 + (fleck - 0.5) * rsqrt(between * between + (1.0 - between) * (1.0 - between));

                    // fleck.y chips the outline: it moves the stone's edge
                    // in and out.
                    edge += (fleck.y - 0.5) * 2.0 * _EdgeRaggedness * (1.0 - fade);
                #endif

                float height = Height(edge);

                // Stone or gap, with the boundary blended over one pixel so
                // it isn't jagged.
                half stone = saturate(edge / max(fwidth(edge), 0.0001) + 0.5);

                // This stone's own random numbers: where along A-B-C its
                // colour is, and how bright.
                float2 random = Hash22(cell + _Seed + 17.31);
                half3 stoneColor = random.x < 0.5
                    ? lerp(_ColorA.rgb, _ColorB.rgb, random.x * 2.0)
                    : lerp(_ColorB.rgb, _ColorC.rgb, random.x * 2.0 - 1.0);
                stoneColor *= 1.0 + (random.y - 0.5) * 2.0 * _BrightnessVariation;
                stoneColor *= lerp(1.0 - _EdgeDarkening, 1.0, height);

                half3 gapColor = _GapColor.rgb;

                #if defined(_DIRT)
                    // Flecks lighten and darken the stone and the gap.
                    half fleckShade = (fleck.x - 0.5) * 2.0;
                    stoneColor *= 1.0 + fleckShade * _MottleAmount;
                    gapColor *= 1.0 + fleckShade * _GapNoiseAmount;
                #endif

                half3 albedo = lerp(gapColor, stoneColor, stone);

                // The flat colour it fades to: the average stone, with the
                // gaps' share mixed in.
                half3 averageStone = (_ColorA.rgb + 2.0 * _ColorB.rgb + _ColorC.rgb) * 0.25;
                half3 farColor = lerp(_GapColor.rgb, averageStone, saturate(1.0 - 2.5 * max(_GapWidth, _MinGap)));
                albedo = lerp(albedo, farColor, fade);

                half dirt = 0.0;

                #if defined(_DIRT)
                    // Grime, after the distance fade: the patches are far
                    // bigger than a cobble, so they still show (and don't
                    // shimmer) long after the cobbles have faded out.
                    // A patch is wherever the slow noise is above a level
                    // set by the coverage; dirt also lies in low places
                    // everywhere (far away, where the height is no longer
                    // drawn, an in-between amount). The flecks break it up.
                    half grime = grimeNoise.x * 0.65 + warpNoise.y * 0.35;
                    half grimePatch = smoothstep(0.75 - _GrimeCoverage * 0.75, 1.0 - _GrimeCoverage * 0.75, grime);
                    half lowness = 1.0 - lerp(height, 0.6, fade);
                    dirt = _GrimeAmount * saturate(grimePatch + lowness * _GrimeLowBias);
                    dirt = saturate(dirt * (0.7 + 0.6 * fleck.x));
                    albedo = lerp(albedo, _DirtColor.rgb, dirt);
                #endif

                // Crevice shadow, standing in for ambient occlusion: less
                // light finds its way to the foot of a stone and the floor
                // of a narrow gap than to the stone's top. "openness" is how
                // clear of the crevice a pixel is: 0 on the line where stone
                // meets gap, rising to 1 over the shadow's width on BOTH
                // sides of it - up the stone, and out towards the middle of
                // the gap. A narrow gap never gets far from a stone, so it
                // stays dark all the way across; the wide pockets where
                // several stones meet lighten in the middle. smoothstep is
                // level at each end, so the shade runs through that line
                // with no crease, which is what softens the step from
                // stone colour to gap colour. Last, so the dirt is shaded
                // too; and faded out with the pattern in the distance.
                half openness = smoothstep(0.0, max(_OcclusionWidth, 0.001), abs(edge));
                half occlusion = 1.0 - _OcclusionStrength * (1.0 - openness) * (1.0 - fade);
                albedo *= occlusion;

                // Tilt the normal down the stones' shoulders. The slope is
                // read from how the height (in metres) changes to the next
                // pixel across and down, against how the world position
                // changes over the same step (Mikkelsen's method) - which
                // works on any surface with no tangents.
                float heightMetres = height * _Depth * _NormalStrength * (1.0 - fade);

                #if defined(_DIRT)
                    // Flecks pit the stones' surface a little as well.
                    heightMetres += (fleck.x - 0.5) * _MottleBump * _NormalStrength * stone * (1.0 - fade);
                #endif
                float3 dpdx = ddx(positionWS);
                float3 dpdy = ddy(positionWS);
                float3 r1 = cross(dpdy, surfaceNormal);
                float3 r2 = cross(surfaceNormal, dpdx);
                float det = dot(dpdx, r1);
                float3 slope = sign(det) * (ddx(heightMetres) * r1 + ddy(heightMetres) * r2);
                half3 normalWS = normalize(abs(det) * surfaceNormal - slope);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0;
                surfaceData.occlusion = 1.0;
                // Only the stones shine, and less where they're dirty or in
                // the crevice shadow.
                surfaceData.specular = _SpecColor.rgb * stone * (1.0 - dirt) * occlusion;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0.0, 0.0, 1.0);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    inputData.shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif

                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    inputData.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), input.fogFactorAndVertexLight.x);
                    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
                #else
                    inputData.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), input.fogFactor);
                    inputData.vertexLighting = half3(0, 0, 0);
                #endif

                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // Baked light: a lightmap, or light probes.
                #if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
                        GetAbsolutePositionWS(inputData.positionWS),
                        inputData.normalWS,
                        inputData.viewDirectionWS,
                        input.positionCS.xy,
                        input.probeOcclusion,
                        inputData.shadowMask);
                #else
                    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
                #endif

                half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        // Draws the surface into shadow maps, so it casts shadows.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing

            // A point or spot light's shadow map needs the bias worked out
            // from the light's position rather than one direction.
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                // Pushed a little away from the light, so the surface
                // doesn't shadow itself; then kept inside the map's depth
                // range.
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Depth only: used when the pipeline draws depth before colour.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth and the surface's plain normal: used by effects that need
        // both (none on Quest; here so the floor isn't missing from them).
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
