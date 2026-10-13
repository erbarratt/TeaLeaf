// Harbour water at night: a dark, opaque surface with no textures but an
// optional foam mask. Three things make it read as water:
// - the SWELL: the surface really rises and falls. Each vertex is moved up
//   and down by three sine waves crossing each other, so the water needs a
//   mesh with vertices spread across it (see below), and the movement is
//   correct in stereo. Only up and down, never sideways, so the water's
//   edge stays put against a quay wall.
// - the RIPPLES: two layers of smooth noise sliding across each other, used
//   only to tilt the surface for the lighting (the mesh stays smooth).
// - the GLINTS: the moon and any torch in range shine off that tilted
//   surface, which breaks each light into a long streak of dabs.
// The water itself is nearly black, and turns to the reflected sky's colour
// where it is seen at a shallow angle (the "Fresnel" effect: water is a
// mirror edge-on and see-through looking straight down).
//
// Everything is laid out in the WORLD, so several water meshes side by side
// join with no seam, and the surface must be level and face up.
//
// The mesh: Unity's default Plane (10m, a vertex every metre) is right as it
// is; tile planes rather than scaling one up. A swell wave needs at least
// four vertices along its length, or it turns into flat facets.
//
// Opaque, and it never reads the camera's depth or colour: no see-through
// shallows and no refraction. Foam is painted, not found from depth.
//
// Cost: two noise reads (8 random numbers) a pixel, a foam texture read
// with Foam on, and one highlight per light. The swell is per vertex.
//
// No ShadowCaster pass (water casts no shadow) and no Meta pass (it is left
// out of the lightmap bake: its ambient light comes from light probes).
Shader "TeaLeaf/Water"
{
    Properties
    {
        [Header(Colour)]
        // The water looking straight down into it.
        _WaterColor ("Water Colour", Color) = (0.012, 0.025, 0.032, 1)
        // What it turns to at a shallow angle: the night sky it reflects.
        _HorizonColor ("Reflected Sky Colour", Color) = (0.035, 0.05, 0.075, 1)
        // Higher keeps the reflected colour to the far distance; lower
        // brings it closer in.
        _FresnelPower ("Reflection Falloff", Range(1, 8)) = 4

        [Header(Swell)]
        // Each wave: X = the compass direction it travels (degrees, 0 = world
        // +X, 90 = +Z), Y = its length in metres crest to crest, Z = how far a
        // crest rises above still water in metres, W = its speed in metres a
        // second. Z of 0 switches a wave off.
        _WaveA ("Wave A (direction, length, height, speed)", Vector) = (20, 9, 0.05, 1.1)
        _WaveB ("Wave B (direction, length, height, speed)", Vector) = (75, 6, 0.03, 0.9)
        _WaveC ("Wave C (direction, length, height, speed)", Vector) = (-40, 4, 0.015, 0.7)

        [Header(Ripples)]
        // Metres across one ripple of the larger layer. The second layer is
        // a little under half that, and half as high.
        _RippleSize ("Ripple Size (m)", Range(0.05, 3)) = 0.45
        // How tall the ripples are taken to be: sets how far they tilt the
        // surface, so how broken up the glints are.
        _RippleHeight ("Ripple Height (m)", Range(0, 0.1)) = 0.012
        _RippleDirection ("Ripple Direction (degrees)", Range(0, 360)) = 30
        _RippleSpeed ("Ripple Speed (m/s)", Range(0, 2)) = 0.25
        // Far away, ripples get smaller than a pixel and would sparkle, so
        // they fade out. Measured in ripples per pixel: the fade starts at
        // the first value and is complete at the second.
        _FadeStart ("Fade Start (ripples per pixel)", Range(0.01, 2)) = 0.15
        _FadeEnd ("Fade End (ripples per pixel)", Range(0.02, 3)) = 0.5

        [Header(Glints)]
        // The colour and strength of a light's shine off the water (on top
        // of the light's own colour).
        _GlintColor ("Glint Colour", Color) = (0.8, 0.85, 0.9, 1)
        _GlintStrength ("Glint Strength", Range(0, 8)) = 2
        // Higher = a smaller, tighter shine.
        _Smoothness ("Smoothness", Range(0, 1)) = 0.75
        // 0 = an ordinary soft highlight. Higher cuts it into flat,
        // hard-edged dabs, as if painted. Too hard and the dabs sparkle as
        // the head moves.
        _GlintHardness ("Glint Hardness", Range(0, 0.95)) = 0.6

        [Header(Foam)]
        // Foam where the water meets a quay, a hull or a post. Painted: a
        // black texture seen from above, white where the foam is thickest,
        // fading to black over the width of the foam. Off, none of it is
        // worked out.
        [Toggle(_FOAM)] _Foam ("Foam", Float) = 0
        // Import with Wrap Mode Clamp, and leave its border black.
        [NoScaleOffset] _FoamMask ("Foam Mask (white = foam)", 2D) = "black" {}
        // The patch of the world the mask covers: X and Y = the world X and
        // Z of its middle, Z and W = its size along world X and Z in metres.
        _FoamArea ("Foam Area (centre X, centre Z, size X, size Z)", Vector) = (0, 0, 100, 100)
        _FoamColor ("Foam Colour", Color) = (0.5, 0.55, 0.58, 1)
        // How much the ripples break up the foam's edge.
        _FoamBreakup ("Foam Breakup", Range(0, 1)) = 0.6
        // How far the foam's edge washes in and out, and how fast.
        _FoamLap ("Foam Lapping", Range(0, 1)) = 0.3
        _FoamLapSpeed ("Foam Lapping Speed", Range(0, 4)) = 0.8
        // How soft the foam's edge is.
        _FoamSoftness ("Foam Edge Softness", Range(0.01, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // Shared by every pass. The properties must be declared identically
        // in all of them, inside this CBUFFER, for the SRP Batcher to batch
        // objects using the shader. The swell is here too: every pass has to
        // move the vertices the same way, or the depth passes would draw
        // still water where the colour pass draws waves.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _WaterColor;
            half4 _HorizonColor;
            half _FresnelPower;
            float4 _WaveA;
            float4 _WaveB;
            float4 _WaveC;
            float _RippleSize;
            float _RippleHeight;
            float _RippleDirection;
            float _RippleSpeed;
            float _FadeStart;
            float _FadeEnd;
            half4 _GlintColor;
            half _GlintStrength;
            half _Smoothness;
            half _GlintHardness;
            float4 _FoamArea;
            half4 _FoamColor;
            half _FoamBreakup;
            half _FoamLap;
            float _FoamLapSpeed;
            half _FoamSoftness;
        CBUFFER_END

        // One sine wave travelling across the water: returns how high the
        // surface is at xz, and adds to "slope" how steeply it rises there
        // along world X and Z (the lighting needs the slope to know which
        // way the surface faces). wave = direction in degrees, length,
        // height, speed.
        //
        // "phase" is how far through one wave the point is: the distance
        // along the wave's direction, less how far the wave has travelled,
        // turned into an angle (a whole length = a full turn). The height is
        // the sine of it; the slope of a sine is its cosine.
        float SwellWave(float2 xz, float4 wave, inout float2 slope)
        {
            float directionSin;
            float directionCos;
            sincos(radians(wave.x), directionSin, directionCos);
            float2 direction = float2(directionCos, directionSin);

            float turnsPerMetre = TWO_PI / max(wave.y, 0.01);
            float phase = turnsPerMetre * (dot(direction, xz) - wave.w * _Time.y);

            float s;
            float c;
            sincos(phase, s, c);

            slope += direction * (turnsPerMetre * wave.z * c);
            return wave.z * s;
        }

        // The three waves added together: the height of the water at xz, in
        // metres above still water, and its slope.
        float Swell(float2 xz, out float2 slope)
        {
            slope = float2(0.0, 0.0);
            return SwellWave(xz, _WaveA, slope)
                + SwellWave(xz, _WaveB, slope)
                + SwellWave(xz, _WaveC, slope);
        }

        // A vertex's place in the world with the swell applied.
        float3 SwellPositionWS(float3 positionOS, out float2 slope)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            positionWS.y += Swell(positionWS.xz, slope);
            return positionWS;
        }

        // Which way the water faces, from its slope: straight up when
        // level, leaning away from the way it rises.
        half3 NormalFromSlope(float2 slope)
        {
            return normalize(float3(-slope.x, 1.0, -slope.y));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // This material's own switch. shader_feature: only the
            // combinations some material actually uses end up in a build.
            #pragma shader_feature_local_fragment _FOAM

            // The lighting options: shadows on the water, extra lights
            // (torches), mixed lights' baked shadows, fog, foveation. No
            // lightmap: the water isn't baked.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            // Single-pass instanced stereo (each eye is an instance).
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // A texture can't go in the CBUFFER; it's declared on its own.
            TEXTURE2D(_FoamMask);
            SAMPLER(sampler_FoamMask);

            // The second ripple layer is read from coordinates turned by
            // this much (a 3-4-5 triangle's angle, about 37 degrees), so
            // the two layers' grids never line up; and it is this fraction
            // of the first layer's size.
            #define RIPPLE_TURN_COS 0.8
            #define RIPPLE_TURN_SIN 0.6
            #define RIPPLE_SECOND_SCALE 0.45

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                // xy = the first ripple layer's coordinates, zw = the second's.
                float4 rippleUV : TEXCOORD1;
                // xy = the swell's slope, z = fog.
                float3 slopeAndFog : TEXCOORD2;
                half3 ambient : TEXCOORD3;
                // xy = where to read the foam mask, z = the foam's lapping.
                float3 foamUV : TEXCOORD4;

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord : TEXCOORD5;
                #endif

                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // One random number (0-1) from a cell's coordinates: the same
            // cell always gives the same one. Multiply, keep the fraction,
            // mix, repeat - no sine, which gives different answers on
            // different graphics chips. (Dave Hoskins' "hash without sine".)
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Brings a grid corner's coordinates back into 0-1023, like a
            // clock going round: the ripples' coordinates keep growing as
            // they slide, and Hash21() loses its randomness on big numbers.
            float2 WrapCorner(float2 corner)
            {
                return corner - 1024.0 * floor(corner / 1024.0);
            }

            // Smooth noise that also says how it is changing. A random
            // number at every whole-number corner of a grid, blended across
            // each square. Returns x = the value (0-1), and yz = how fast
            // the value rises along p's two directions - worked out exactly
            // from the blend, where the cobblestone shader compares
            // neighbouring pixels: exact slopes don't go blocky when the
            // surface is seen edge-on, as water mostly is. (Inigo Quilez's
            // "value noise derivatives".)
            float3 RippleNoise(float2 p)
            {
                float2 corner = floor(p);
                float2 f = p - corner;

                // The blend curve, and its own slope.
                float2 blend = f * f * (3.0 - 2.0 * f);
                float2 blendSlope = 6.0 * f * (1.0 - f);

                float2 wrapped = WrapCorner(corner);
                float a = Hash21(wrapped);
                float b = Hash21(WrapCorner(wrapped + float2(1.0, 0.0)));
                float c = Hash21(WrapCorner(wrapped + float2(0.0, 1.0)));
                float d = Hash21(WrapCorner(wrapped + float2(1.0, 1.0)));

                float alongX = b - a;
                float alongY = c - a;
                float twist = a - b - c + d;

                float value = a + alongX * blend.x + alongY * blend.y + twist * blend.x * blend.y;
                float2 rise = blendSlope * float2(alongX + twist * blend.y, alongY + twist * blend.x);
                return float3(value, rise);
            }

            // What one light adds: a little of the water's own colour where
            // the light falls on it, and the glint. The glint is brightest
            // where the surface faces halfway between the light and the eye
            // (Blinn-Phong); "shininess" sets how quickly it falls away
            // from there. Glint Hardness then squeezes that soft falloff
            // into an edge: everything above the middle becomes full
            // brightness, everything below becomes none.
            half3 LightWater(Light light, half3 normalWS, half3 viewDirWS, half3 albedo, half3 glintColor, half shininess)
            {
                half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);
                half3 diffuse = albedo * saturate(dot(normalWS, light.direction));

                half3 halfway = SafeNormalize(light.direction + viewDirWS);
                half glint = pow(saturate(dot(normalWS, halfway)), shininess);
                half width = (1.0 - _GlintHardness) * 0.5;
                glint = smoothstep(0.5 - width, 0.5 + width, glint);

                return radiance * (diffuse + glintColor * glint);
            }

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float2 slope;
                float3 positionWS = SwellPositionWS(input.positionOS.xyz, slope);
                float4 positionCS = TransformWorldToHClip(positionWS);
                float2 xz = positionWS.xz;

                // Where each ripple layer is read. Worked out here, once a
                // vertex, because it changes evenly across the surface: the
                // pixels in between get exactly the right answer for free.
                // Layer one slides along the ripple direction; layer two is
                // turned, finer, and slides across the first.
                float directionSin;
                float directionCos;
                sincos(radians(_RippleDirection), directionSin, directionCos);
                float2 direction = float2(directionCos, directionSin);
                float size = max(_RippleSize, 0.01);
                float travelled = _RippleSpeed * _Time.y;

                float2 turned = float2(
                    RIPPLE_TURN_COS * xz.x + RIPPLE_TURN_SIN * xz.y,
                    -RIPPLE_TURN_SIN * xz.x + RIPPLE_TURN_COS * xz.y);
                float2 across = float2(-direction.y, direction.x);

                output.rippleUV.xy = (xz - direction * travelled) / size;
                output.rippleUV.zw = (turned - across * (travelled * 0.8)) / (size * RIPPLE_SECOND_SCALE);

                #if defined(_FOG_FRAGMENT)
                    half fogFactor = 0;
                #else
                    half fogFactor = ComputeFogFactor(positionCS.z);
                #endif

                output.positionWS = positionWS;
                output.positionCS = positionCS;
                output.slopeAndFog = float3(slope, fogFactor);

                // Ambient light from the light probes, for a surface facing
                // straight up. Once a vertex: it barely changes across water.
                output.ambient = SampleSH(half3(0.0, 1.0, 0.0));

                // The foam mask is laid over its patch of the world; and the
                // foam's edge washes in and out on a slow wave of its own,
                // offset from place to place so the whole shore doesn't
                // breathe together.
                output.foamUV.xy = (xz - _FoamArea.xy) / max(_FoamArea.zw, 0.01) + 0.5;
                output.foamUV.z = sin(_Time.y * _FoamLapSpeed + xz.x * 0.7 + xz.y * 0.9);

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    VertexPositionInputs vertexInput = (VertexPositionInputs)0;
                    vertexInput.positionWS = positionWS;
                    vertexInput.positionCS = positionCS;
                    output.shadowCoord = GetShadowCoord(vertexInput);
                #endif

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 positionWS = input.positionWS;
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);

                // How many ripples one pixel covers here (ddx/ddy = how much
                // a value changes from this pixel to the next, across and
                // down). Once that nears a whole ripple it can't be drawn,
                // only sparkle - so each layer fades out as it gets there,
                // the finer one first.
                float2 uv1 = input.rippleUV.xy;
                float2 uv2 = input.rippleUV.zw;
                float ripplesPerPixel = max(length(ddx(uv1)), length(ddy(uv1)));
                half visible1 = 1.0 - smoothstep(_FadeStart, _FadeEnd, ripplesPerPixel);
                half visible2 = 1.0 - smoothstep(_FadeStart, _FadeEnd, ripplesPerPixel / RIPPLE_SECOND_SCALE);

                float3 ripple1 = RippleNoise(uv1);
                float3 ripple2 = RippleNoise(uv2);

                // Each layer's slope in the world, in metres up per metre
                // along: the noise's rise, times the ripple's height, over
                // its size. The second layer's is turned back from its
                // turned coordinates first.
                float size = max(_RippleSize, 0.01);
                float2 slope1 = ripple1.yz * (_RippleHeight / size);
                float2 rise2 = float2(
                    RIPPLE_TURN_COS * ripple2.y - RIPPLE_TURN_SIN * ripple2.z,
                    RIPPLE_TURN_SIN * ripple2.y + RIPPLE_TURN_COS * ripple2.z);
                float2 slope2 = rise2 * (_RippleHeight * 0.5 / (size * RIPPLE_SECOND_SCALE));

                // The swell's slope and the ripples' simply add.
                float2 slope = input.slopeAndFog.xy + slope1 * visible1 + slope2 * visible2;
                half3 normalWS = NormalFromSlope(slope);

                // Dark looking down into it, the sky's colour edge-on.
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
                half3 albedo = _WaterColor.rgb;
                half3 reflection = _HorizonColor.rgb * fresnel;
                half3 glintColor = _GlintColor.rgb * _GlintStrength;

                #if defined(_FOAM)
                    // The mask says how deep into the foam this pixel is
                    // (1 at the wall, 0 in open water); the foam is drawn
                    // where that passes one half. Before the test it is
                    // scaled up and down by the ripples (a broken edge) and
                    // by the lapping (an edge that washes in and out). A
                    // scale, not an offset, so open water - where the mask
                    // is 0 - never foams. The ripples' share fades with
                    // them in the distance.
                    half mask = SAMPLE_TEXTURE2D(_FoamMask, sampler_FoamMask, input.foamUV.xy).r;
                    half rippleValue = (ripple1.x - 0.5) * visible1 + (ripple2.x - 0.5) * visible2;
                    half depth = mask * (1.0 + rippleValue * 2.0 * _FoamBreakup + input.foamUV.z * _FoamLap);
                    half foam = smoothstep(0.5 - _FoamSoftness, 0.5 + _FoamSoftness, depth);

                    // Foam is matt: lit like a pale surface, with no
                    // reflection and no glint.
                    albedo = lerp(albedo, _FoamColor.rgb, foam);
                    reflection *= 1.0 - foam;
                    glintColor *= 1.0 - foam;
                #endif

                // Smoothness as URP's Simple Lit reads it. Where the ripples
                // have faded out, the glint is spread wider instead: the
                // ripples too small to draw are still there, scattering it.
                half shininess = exp2(10.0 * _Smoothness + 1.0) * lerp(0.25, 1.0, visible1);

                // The light loop below reads these two from a variable of
                // this name.
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                #else
                    float4 shadowCoord = float4(0, 0, 0, 0);
                #endif

                // Mixed lights' baked shadows, from the light probes (the
                // water has no lightmap).
                half4 shadowMask = SAMPLE_SHADOWMASK(float2(0, 0));

                half3 color = albedo * input.ambient + reflection;

                // The moon. In a quay's shadow it adds nothing, so the
                // glint stops where the shadow falls.
                Light mainLight = GetMainLight(shadowCoord, positionWS, shadowMask);
                color += LightWater(mainLight, normalWS, viewDirWS, albedo, glintColor, shininess);

                // Torches and lamps in range.
                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();

                    #if USE_CLUSTER_LIGHT_LOOP
                        for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++) {
                            Light light = GetAdditionalLight(lightIndex, positionWS, shadowMask);
                            color += LightWater(light, normalWS, viewDirWS, albedo, glintColor, shininess);
                        }
                    #endif

                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light light = GetAdditionalLight(lightIndex, positionWS, shadowMask);
                        color += LightWater(light, normalWS, viewDirWS, albedo, glintColor, shininess);
                    LIGHT_LOOP_END
                #endif

                half fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), input.slopeAndFog.z);
                color = MixFog(color, fogCoord);
                return half4(color, 1.0);
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

                float2 slope;
                output.positionCS = TransformWorldToHClip(SwellPositionWS(input.positionOS.xyz, slope));
                return output;
            }

            half DepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth and the surface's normal (the swell's, without ripples):
        // used by effects that need both.
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

                float2 slope;
                output.positionCS = TransformWorldToHClip(SwellPositionWS(input.positionOS.xyz, slope));
                output.normalWS = NormalFromSlope(slope);
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
