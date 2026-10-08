// A brick or dressed-stone wall made entirely in the shader: no textures.
// The sister of TeaLeaf/Cobblestone, for regular, rectangular courses
// rather than rounded stones. Like it, every pixel works out which brick
// it's on from its position in the WORLD, so:
// - the bricks are the same size on any object, however it's scaled, and
//   wall pieces side by side join up with no seam;
// - no two bricks need look alike: each one's colour, length and the way
//   its face tilts come from random numbers made from its row and column.
//
// How the pattern is made: the wall is cut into rows (courses) one brick
// high, and each row into bricks one brick long. Every other row is slid
// along by part of a brick (Row Offset - half a brick is the usual
// "running bond"). The lines between rows and between bricks can each be
// nudged at random (Height / Length Variation), which turns even brickwork
// into coursed stone. A pixel's distance to the nearest side of its brick
// gives the mortar joint (near the side) and the brick's bevelled edge (a
// little further in).
//
// Unlike the cobbles, nothing has to be searched for: a pixel's brick is
// found directly from its row and column, so this is much the cheaper of
// the two - about 6 random numbers per pixel, twice that with Parallax,
// plus 16 with the Dirt layer.
//
// Lighting is URP's simple (Blinn-Phong) model, with the surface normal
// tilted on the bricks' edges - and a little differently on every brick's
// face (Face Tilt) - worked out from how the height changes between
// neighbouring pixels, so no tangents or normal map are needed.
//
// Which way the courses run: on a wall, along the wall and level, whatever
// direction the wall faces - so a wall built at an angle gets bricks of the
// right length. On a floor or ceiling, along world X. The pattern is fixed
// to the world, so it slides across anything that moves: for walls and
// paving, not props. Where two walls meet at a corner the bricks don't
// wrap round it; each face has its own run.
//
// No Meta pass yet: a lightmap bake won't see the bricks' colours.
Shader "TeaLeaf/BrickWall"
{
    Properties
    {
        [Header(Layout)]
        // One brick and its share of mortar, in metres.
        _BrickWidth ("Brick Length (m)", Range(0.03, 2)) = 0.225
        _BrickHeight ("Brick Height (m)", Range(0.02, 1)) = 0.075
        // How far every other row is slid along, in bricks. 0.5 = running
        // bond (each joint over the middle of the brick below), 0 = joints
        // stacked in straight lines.
        _RowOffset ("Row Offset", Range(0, 1)) = 0.5
        // An extra random slide for each row, in bricks - so the joints
        // don't line up every second row. For stone.
        _RowOffsetVariation ("Row Offset Variation", Range(0, 1)) = 0
        // How unequal the bricks' lengths are within a row, and how unequal
        // the rows' heights. 0 = all the same.
        _WidthVariation ("Length Variation", Range(0, 1)) = 0
        _HeightVariation ("Height Variation", Range(0, 1)) = 0
        // Any number: a different one gives a different wall.
        _Seed ("Seed", Float) = 0

        [Header(Shape)]
        // All in metres. The mortar joint between two bricks; how far in
        // from the joint the brick's edge slopes before its flat face; how
        // rounded its corners are; and how deep the joints are raked, which
        // sets how steep the edges look in the lighting.
        _MortarWidth ("Mortar Width (m)", Range(0, 0.05)) = 0.01
        _Bevel ("Edge Width (m)", Range(0.0005, 0.05)) = 0.006
        _CornerRadius ("Corner Radius (m)", Range(0, 0.05)) = 0.004
        _Depth ("Joint Depth (m)", Range(0, 0.05)) = 0.006
        // How much each brick's face leans off flat, a different way for
        // each: catches the light unevenly, as a real wall does.
        _FaceTilt ("Face Tilt", Range(0, 0.3)) = 0.03
        _NormalStrength ("Lighting Strength", Range(0, 4)) = 1

        [Header(Colour)]
        // Each brick takes a colour somewhere along A - B - C.
        _ColorA ("Brick Colour A", Color) = (0.30, 0.12, 0.09, 1)
        _ColorB ("Brick Colour B", Color) = (0.36, 0.17, 0.11, 1)
        _ColorC ("Brick Colour C", Color) = (0.27, 0.19, 0.16, 1)
        _MortarColor ("Mortar Colour", Color) = (0.34, 0.33, 0.30, 1)
        // How much lighter or darker than its colour a brick can be.
        _BrightnessVariation ("Brightness Variation", Range(0, 1)) = 0.25
        // How much darker a brick is down its edge than on its face.
        _EdgeDarkening ("Edge Darkening", Range(0, 1)) = 0.3

        [Header(Shine)]
        [Toggle(_SPECULAR_COLOR)] _Specular ("Shine", Float) = 0
        _SpecColor ("Shine Colour", Color) = (0.08, 0.08, 0.08, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3

        [Header(Parallax)]
        // Makes the joints look sunk behind the bricks by shifting where
        // the pattern is read, by how far the view is off straight-on.
        // Works the pattern out twice.
        [Toggle(_PARALLAX)] _Parallax ("Parallax", Float) = 0
        _ParallaxDepth ("Parallax Depth (m)", Range(0, 0.05)) = 0.008

        [Header(Dirt)]
        // A second layer that roughens everything up: grime in patches,
        // mottled bricks, gritty mortar, chipped edges and courses that
        // aren't quite straight. Off, none of it is worked out.
        [Toggle(_DIRT)] _Dirt ("Dirt", Float) = 0
        _DirtColor ("Dirt Colour", Color) = (0.10, 0.085, 0.065, 1)
        // Grime: how strongly it covers, how much of the wall has a patch
        // on it, and how big the patches are.
        _GrimeAmount ("Grime Amount", Range(0, 1)) = 0.5
        _GrimeCoverage ("Grime Coverage", Range(0, 1)) = 0.45
        _GrimeScale ("Grime Patch Size (m)", Range(0.1, 10)) = 1.5
        // How much dirt lies in the joints and on the bricks' edges
        // everywhere, patch or not.
        _GrimeLowBias ("Dirt In Joints", Range(0, 1)) = 0.4
        // Fine noise: lighter and darker flecks on the bricks, how big the
        // flecks are, the fewest pixels one is ever drawn across (further
        // away they grow instead of shimmering), how much they pit the
        // surface for the lighting, and how much they vary the mortar.
        _MottleAmount ("Brick Mottling", Range(0, 1)) = 0.3
        _MottleScale ("Fleck Size (m)", Range(0.0005, 0.2)) = 0.006
        _FleckPixels ("Smallest Fleck (pixels)", Range(1, 8)) = 4
        _MottleBump ("Pitting Depth (m)", Range(0, 0.01)) = 0.0005
        _GapNoiseAmount ("Mortar Grit", Range(0, 1)) = 0.4
        // How far the same flecks push a brick's outline in and out, in
        // metres: chipped, worn edges.
        _EdgeRaggedness ("Edge Raggedness (m)", Range(0, 0.02)) = 0.002
        // Bends the whole pattern, so the courses wander a little: how far
        // (metres) and over what distance.
        _WarpAmount ("Course Warp (m)", Range(0, 0.1)) = 0
        _WarpScale ("Course Warp Size (m)", Range(0.05, 5)) = 0.8

        [Header(Distance)]
        // Far away, bricks get smaller than a pixel and would shimmer, so
        // they fade to one flat colour. Measured in bricks (by their
        // smaller side) per pixel: the fade starts at the first value and
        // is complete at the second.
        _FadeStart ("Fade Start (bricks per pixel)", Range(0.01, 2)) = 0.25
        _FadeEnd ("Fade End (bricks per pixel)", Range(0.02, 3)) = 0.7
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
            float _BrickWidth;
            float _BrickHeight;
            float _RowOffset;
            float _RowOffsetVariation;
            float _WidthVariation;
            float _HeightVariation;
            float _Seed;
            float _MortarWidth;
            float _Bevel;
            float _CornerRadius;
            float _Depth;
            float _FaceTilt;
            float _NormalStrength;
            half4 _ColorA;
            half4 _ColorB;
            half4 _ColorC;
            half4 _MortarColor;
            half _BrightnessVariation;
            half _EdgeDarkening;
            half4 _SpecColor;
            half _Smoothness;
            float _ParallaxDepth;
            half4 _DirtColor;
            half _GrimeAmount;
            half _GrimeCoverage;
            float _GrimeScale;
            half _GrimeLowBias;
            half _MottleAmount;
            float _MottleScale;
            float _FleckPixels;
            float _MottleBump;
            half _GapNoiseAmount;
            float _EdgeRaggedness;
            float _WarpAmount;
            float _WarpScale;
            float _FadeStart;
            float _FadeEnd;
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

            // Two random numbers (0-1) from a pair of coordinates: the same
            // pair always gives the same two. Multiply, keep the fraction,
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
            // randomness on numbers that big. So each corner's coordinates
            // are wrapped into 0-1023 before being hashed. The noise then
            // repeats every 1024 flecks, far too many to notice.
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

            // Where dividing line number k is, along a row or up the wall,
            // in bricks: at k itself, nudged up to 0.45 of a brick either
            // way at full variation. salt tells one family of lines from
            // another (each row's joints from the next row's, and the
            // joints from the lines between rows).
            float LinePosition(float k, float salt, float variation)
            {
                return k + (Hash22(float2(k, salt) + _Seed).x - 0.5) * variation * 0.9;
            }

            // Finds the two dividing lines either side of t (in bricks):
            // low and high, and which brick lies between them (index).
            //
            // The nearest line's number is t rounded. If t is below that
            // line, the brick is the one before it and the line is its high
            // side; otherwise the brick starts at the line. Either way one
            // more line is needed, the next one out. A line never strays
            // as far as half a brick from its number, so no other line can
            // come between - two random numbers find the brick.
            void FindSpan(float t, float salt, float variation, out float low, out float high, out float index)
            {
                float nearestLine = floor(t + 0.5);
                float nearestPosition = LinePosition(nearestLine, salt, variation);
                bool isBelow = t < nearestPosition;

                float otherLine = nearestLine + (isBelow ? -1.0 : 1.0);
                float otherPosition = LinePosition(otherLine, salt, variation);

                low = isBelow ? otherPosition : nearestPosition;
                high = isBelow ? nearestPosition : otherPosition;
                index = isBelow ? otherLine : nearestLine;
            }

            // Which brick the point p is on (p in metres: x along the
            // course, y up the wall).
            // - brick: its column and row, for random numbers.
            // - edge: how far inside the brick p is, in metres (0 on the
            //   brick's edge, below 0 in the mortar).
            // - fromCentre: where p is from the middle of the brick, in
            //   metres, for tilting its face.
            void Bricks(float2 p, out float edge, out float2 brick, out float2 fromCentre)
            {
                float width = max(_BrickWidth, 0.001);
                float height = max(_BrickHeight, 0.001);

                // The row first: the brick's sides depend on which row.
                float v = p.y / height;
                float bottom;
                float top;
                float row;
                FindSpan(v, 913.7, _HeightVariation, bottom, top, row);

                // Every other row is slid along by the row offset ("row
                // minus twice half of it, rounded down" is 0 for even
                // rows and 1 for odd, negative rows included), plus this
                // row's own random slide.
                float isOddRow = row - 2.0 * floor(row * 0.5);
                float rowSlide = (Hash22(float2(row, 57.7) + _Seed).y - 0.5) * _RowOffsetVariation;
                float u = p.x / width + isOddRow * _RowOffset + rowSlide;

                float left;
                float right;
                float column;
                FindSpan(u, row, _WidthVariation, left, right, column);

                brick = float2(column, row);
                fromCentre = float2((u - 0.5 * (left + right)) * width, (v - 0.5 * (bottom + top)) * height);

                // Distance to the nearest side, across and up, in metres.
                float2 toSide = float2(min(u - left, right - u) * width, min(v - bottom, top - v) * height);

                // Rounded corners: within a corner's radius of BOTH sides,
                // measure from a circle of that radius tucked into the
                // corner, instead of from the nearer side.
                float radius = _CornerRadius;
                float2 inCorner = radius - toSide;
                float inside = radius - (length(max(inCorner, 0.0)) + min(max(inCorner.x, inCorner.y), 0.0));

                // Each brick gives up half the mortar joint; its neighbour
                // gives the other half.
                edge = inside - _MortarWidth * 0.5;
            }

            // How high the brick is, from how far inside it a pixel is: 0
            // at its edge (and in the mortar), rising over the bevel to 1
            // on its face. t * (2 - t) climbs steeply at first and levels
            // off - a rounded edge.
            float Height(float edge)
            {
                float t = saturate(edge / max(_Bevel, 0.0001));
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

                // The two world directions the pattern runs along. On a
                // wall: level along the wall (square to both "up" and the
                // way the wall faces - what a cross product gives), and
                // straight up. So the courses are level and the bricks
                // their true length whichever way the wall is turned. On a
                // floor or ceiling there's no "along the wall", so world X
                // and Z.
                bool isFloor = abs(surfaceNormal.y) > 0.7;
                float3 alongWall = SafeNormalize(cross(float3(0.0, 1.0, 0.0), surfaceNormal));
                float3 uAxis = isFloor ? float3(1.0, 0.0, 0.0) : alongWall;
                float3 vAxis = isFloor ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);

                // The pixel's place on the surface, in metres.
                float2 p = float2(dot(positionWS, uAxis), dot(positionWS, vAxis));

                // How much of a brick (by its smaller side) one pixel
                // covers here (fwidth = how much a value changes from this
                // pixel to the next). Once that nears a whole brick the
                // pattern can't be drawn, only shimmer - so it fades out to
                // a flat colour.
                float metresPerPixel = max(fwidth(p.x), fwidth(p.y));
                float bricksPerPixel = metresPerPixel / max(min(_BrickWidth, _BrickHeight), 0.001);
                half fade = smoothstep(_FadeStart, _FadeEnd, bricksPerPixel);

                #if defined(_DIRT)
                    // Two slow noises, read once each. grimeNoise.x places
                    // the grime patches. warpNoise bends the pattern: its
                    // two values, re-centred on zero, push the position
                    // sideways before the bricks are found, so the courses
                    // wander a little. (Its y also adds some smaller detail
                    // to the grime patches.)
                    float2 grimeNoise = Noise2(p / max(_GrimeScale, 0.01) + _Seed);
                    float2 warpNoise = Noise2(p / max(_WarpScale, 0.01) + _Seed + 31.7);
                    p += (warpNoise - 0.5) * 2.0 * _WarpAmount;
                #endif

                float edge;
                float2 brick;
                float2 fromCentre;

                #if defined(_PARALLAX)
                    // Find the height here, then read the pattern from a
                    // spot shifted along the view by how far behind the
                    // bricks' faces that is: joints slide behind the bricks
                    // as the view tilts. One shift only ("offset limited"),
                    // so it's an impression, not a true depth.
                    Bricks(p, edge, brick, fromCentre);
                    float sink = (1.0 - Height(edge)) * (1.0 - fade) * _ParallaxDepth;
                    p -= float2(dot(viewDirWS, uAxis), dot(viewDirWS, vAxis)) * sink;
                #endif

                Bricks(p, edge, brick, fromCentre);

                #if defined(_DIRT)
                    // The fine noise ("flecks"), read at the final spot so
                    // it stays put on the bricks under parallax. A fleck
                    // smaller than a few pixels would shimmer, so the
                    // flecks GROW with distance: whenever one would be
                    // under _FleckPixels across, the noise is read at
                    // double the size, and double again, as often as it
                    // takes. "level" is how many doublings that is; it's
                    // read at the whole number below and the one above,
                    // and the two are blended by the fraction, so the
                    // change of size is gradual.
                    float fleckSize = max(_MottleScale, 0.0002);
                    float level = max(log2(metresPerPixel * max(_FleckPixels, 1.0) / fleckSize), 0.0);
                    float growth = exp2(floor(level));
                    float between = frac(level);
                    float2 fleckUV = p / fleckSize;

                    float2 fleck = lerp(
                        FleckNoise2(fleckUV / growth),
                        FleckNoise2(fleckUV / (growth * 2.0) + 7.7),
                        between);

                    // Two noises averaged are flatter than either alone,
                    // which would show as bands of weaker dirt at certain
                    // distances. This stretches the blend back to full
                    // contrast.
                    fleck = 0.5 + (fleck - 0.5) * rsqrt(between * between + (1.0 - between) * (1.0 - between));

                    // fleck.y chips the outline: it moves the brick's edge
                    // in and out.
                    edge += (fleck.y - 0.5) * 2.0 * _EdgeRaggedness * (1.0 - fade);
                #endif

                float height = Height(edge);

                // Brick or mortar, with the boundary blended over one pixel
                // so it isn't jagged.
                half isBrick = saturate(edge / max(fwidth(edge), 0.00001) + 0.5);

                // This brick's own random numbers: where along A-B-C its
                // colour is, how bright, and which way its face leans.
                float2 random = Hash22(brick + _Seed + 17.31);
                float2 lean = Hash22(brick + _Seed + 71.93) - 0.5;

                half3 brickColor = random.x < 0.5
                    ? lerp(_ColorA.rgb, _ColorB.rgb, random.x * 2.0)
                    : lerp(_ColorB.rgb, _ColorC.rgb, random.x * 2.0 - 1.0);
                brickColor *= 1.0 + (random.y - 0.5) * 2.0 * _BrightnessVariation;
                brickColor *= lerp(1.0 - _EdgeDarkening, 1.0, height);

                half3 mortarColor = _MortarColor.rgb;

                #if defined(_DIRT)
                    // Flecks lighten and darken the brick and the mortar.
                    half fleckShade = (fleck.x - 0.5) * 2.0;
                    brickColor *= 1.0 + fleckShade * _MottleAmount;
                    mortarColor *= 1.0 + fleckShade * _GapNoiseAmount;
                #endif

                half3 albedo = lerp(mortarColor, brickColor, isBrick);

                // The flat colour it fades to: the average brick, with the
                // mortar's share of the wall mixed in (roughly the joint's
                // width against each side of the brick).
                half3 averageBrick = (_ColorA.rgb + 2.0 * _ColorB.rgb + _ColorC.rgb) * 0.25;
                half mortarShare = saturate(_MortarWidth / max(_BrickWidth, 0.001) + _MortarWidth / max(_BrickHeight, 0.001));
                half3 farColor = lerp(averageBrick, _MortarColor.rgb, mortarShare);
                albedo = lerp(albedo, farColor, fade);

                half dirt = 0.0;

                #if defined(_DIRT)
                    // Grime, after the distance fade: the patches are far
                    // bigger than a brick, so they still show (and don't
                    // shimmer) long after the bricks have faded out. A
                    // patch is wherever the slow noise is above a level set
                    // by the coverage; dirt also lies in the joints
                    // everywhere (far away, where the height is no longer
                    // drawn, an in-between amount). The flecks break it up.
                    half grime = grimeNoise.x * 0.65 + warpNoise.y * 0.35;
                    half grimePatch = smoothstep(0.75 - _GrimeCoverage * 0.75, 1.0 - _GrimeCoverage * 0.75, grime);
                    half lowness = 1.0 - lerp(height, 0.8, fade);
                    dirt = _GrimeAmount * saturate(grimePatch + lowness * _GrimeLowBias);
                    dirt = saturate(dirt * (0.7 + 0.6 * fleck.x));
                    albedo = lerp(albedo, _DirtColor.rgb, dirt);
                #endif

                // The surface's height in metres, for the lighting: the
                // joint's depth, plus this brick's face leaning one way or
                // another (higher at one side than the other). Both are
                // multiplied by the edge's rise, so the surface still
                // meets the mortar at zero and there's no step.
                float faceLean = dot(fromCentre, lean) * 2.0 * _FaceTilt;
                float heightMetres = height * (_Depth + faceLean) * _NormalStrength * (1.0 - fade);

                #if defined(_DIRT)
                    // Flecks pit the bricks' surface a little as well.
                    heightMetres += (fleck.x - 0.5) * _MottleBump * _NormalStrength * isBrick * (1.0 - fade);
                #endif

                // Tilt the normal to match. The slope is read from how the
                // height changes to the next pixel across and down, against
                // how the world position changes over the same step
                // (Mikkelsen's method) - which works on any surface with no
                // tangents.
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

                // Only the bricks shine, and less where they're dirty.
                surfaceData.specular = _SpecColor.rgb * isBrick * (1.0 - dirt);
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
        // both (none on Quest; here so the wall isn't missing from them).
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
