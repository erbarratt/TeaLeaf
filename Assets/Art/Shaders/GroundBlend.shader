// The streets' ground: three textured layers - dirt underneath, cobbles and
// wet dirt (puddles) over it - blended by the mesh's vertex colours, the way
// a painted terrain is. Red is how much cobble covers a vertex, blue how
// much water, and where neither is painted the dirt shows. The level
// generator paints the vertices (LevelGen.GroundMesh); a hand-made mesh can
// be painted with any vertex colour tool.
//
// The blend follows the layers' height maps, not just the paint: where the
// cobbles fade into dirt, the dirt fills the joints between the stones
// first and the stones' tops show last, and water collects in the low
// places (the wet layer is blended by the height of what lies under it,
// turned upside down). So a soft paint at the vertices still gives a hard,
// believable edge at the pixels.
//
// Everything is read from the pixel's position in the world (x and z), so
// the mesh needs no texture coordinates but the lightmap's, the pattern is
// the same size everywhere and meshes side by side join with no seam. Each
// layer has its own repeat size and can be turned, so the three don't line
// up.
//
// Each layer has an albedo, a tangent-space normal map, and greyscale
// height, occlusion and smoothness maps (one channel each; the ground isn't
// metal, so no metallic map). The tangent frame is made in the shader from
// the world axes, since the mesh carries no tangents.
//
// Lit with URP's PBR model (the pack's own materials are URP Lit), so the
// puddles reflect the reflection probes. With Parallax on, each layer's
// textures are read where its height map and the view's slant say the
// surface is (offset mapping, one shift, never parallax occlusion), so the
// joints look sunk. Cost per pixel: five to fifteen texture reads, one more
// per layer with parallax (a layer painted to nothing on a triangle isn't
// read; the three greyscale maps are single-channel textures, a quarter
// the bytes of a colour one) and one PBR evaluation. Unmeasured on the
// target rig.
Shader "TeaLeaf/GroundBlend"
{
    Properties
    {
        [Header(Dirt the base everywhere)]
        [NoScaleOffset] _DirtAlbedo ("Dirt Albedo", 2D) = "gray" {}
        [NoScaleOffset] [Normal] _DirtNormal ("Dirt Normal", 2D) = "bump" {}
        // Greyscale maps, one channel each.
        [NoScaleOffset] _DirtHeight ("Dirt Height", 2D) = "gray" {}
        [NoScaleOffset] _DirtOcclusion ("Dirt Occlusion", 2D) = "white" {}
        [NoScaleOffset] _DirtSmoothness ("Dirt Smoothness", 2D) = "gray" {}
        _DirtTint ("Dirt Tint", Color) = (1, 1, 1, 1)
        // Metres the texture covers before it repeats.
        _DirtTile ("Dirt Repeat (m)", Range(0.5, 20)) = 3
        _DirtRotation ("Dirt Turn (degrees)", Range(0, 360)) = 0

        [Header(Cobbles vertex red)]
        [NoScaleOffset] _CobbleAlbedo ("Cobble Albedo", 2D) = "gray" {}
        [NoScaleOffset] [Normal] _CobbleNormal ("Cobble Normal", 2D) = "bump" {}
        [NoScaleOffset] _CobbleHeight ("Cobble Height", 2D) = "gray" {}
        [NoScaleOffset] _CobbleOcclusion ("Cobble Occlusion", 2D) = "white" {}
        [NoScaleOffset] _CobbleSmoothness ("Cobble Smoothness", 2D) = "gray" {}
        _CobbleTint ("Cobble Tint", Color) = (1, 1, 1, 1)
        _CobbleTile ("Cobble Repeat (m)", Range(0.5, 20)) = 3
        _CobbleRotation ("Cobble Turn (degrees)", Range(0, 360)) = 0
        // How sharply the cobbles' edge follows their height map: 0 is a
        // plain soft fade, 1 a hard line along the stones' joints.
        _CobbleSharpness ("Cobble Edge Sharpness", Range(0, 1)) = 0.85

        [Header(Wet dirt vertex blue)]
        [NoScaleOffset] _WetAlbedo ("Wet Albedo", 2D) = "gray" {}
        [NoScaleOffset] [Normal] _WetNormal ("Wet Normal", 2D) = "bump" {}
        [NoScaleOffset] _WetHeight ("Wet Height", 2D) = "gray" {}
        [NoScaleOffset] _WetOcclusion ("Wet Occlusion", 2D) = "white" {}
        [NoScaleOffset] _WetSmoothness ("Wet Smoothness", 2D) = "gray" {}
        _WetTint ("Wet Tint", Color) = (1, 1, 1, 1)
        _WetTile ("Wet Repeat (m)", Range(0.5, 20)) = 3
        _WetRotation ("Wet Turn (degrees)", Range(0, 360)) = 0
        // How sharply the water's edge follows the ground under it: 0 a
        // soft fade, 1 water only in the lowest places.
        _WetSharpness ("Wet Edge Sharpness", Range(0, 1)) = 0.6

        [Header(Surface)]
        _NormalStrength ("Bump Strength", Range(0, 2)) = 1
        // Scales every layer's smoothness map.
        _Smoothness ("Smoothness Scale", Range(0, 1.5)) = 1
        // How much the occlusion maps darken the ground.
        _Occlusion ("Occlusion Strength", Range(0, 1)) = 0.7

        [Header(Parallax)]
        // Shifts where each layer's textures are read by its height map and
        // the view's slant, so the joints look sunk below the stones. One
        // shift per layer (offset mapping, not parallax occlusion): an
        // extra height read per painted layer. Off, nothing is paid.
        [Toggle(_PARALLAX)] _Parallax ("Parallax", Float) = 1
        _ParallaxDepth ("Parallax Depth (m)", Range(0, 0.1)) = 0.015
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }

        // Shared by every pass. The properties must be declared identically
        // in all of them, inside this CBUFFER, for the SRP Batcher to batch
        // objects using the shader.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _DirtTint;
            float _DirtTile;
            float _DirtRotation;
            half4 _CobbleTint;
            float _CobbleTile;
            float _CobbleRotation;
            half _CobbleSharpness;
            half4 _WetTint;
            float _WetTile;
            float _WetRotation;
            half _WetSharpness;
            half _NormalStrength;
            half _Smoothness;
            half _Occlusion;
            float _ParallaxDepth;
        CBUFFER_END

        TEXTURE2D(_DirtAlbedo);
        TEXTURE2D(_DirtNormal);
        TEXTURE2D(_DirtHeight);
        TEXTURE2D(_DirtOcclusion);
        TEXTURE2D(_DirtSmoothness);
        TEXTURE2D(_CobbleAlbedo);
        TEXTURE2D(_CobbleNormal);
        TEXTURE2D(_CobbleHeight);
        TEXTURE2D(_CobbleOcclusion);
        TEXTURE2D(_CobbleSmoothness);
        TEXTURE2D(_WetAlbedo);
        TEXTURE2D(_WetNormal);
        TEXTURE2D(_WetHeight);
        TEXTURE2D(_WetOcclusion);
        TEXTURE2D(_WetSmoothness);
        // One sampler (repeat, trilinear, from the first texture's import
        // settings) serves all fifteen.
        SAMPLER(sampler_DirtAlbedo);

        // One layer read at a world position: its colour, its height-and-
        // occlusion-and-smoothness, and its normal in the world.
        struct Layer
        {
            half3 albedo;
            half occlusion;
            half height;
            half smoothness;
            half3 normalWS;
        };

        // The texture coordinates of a layer at a world position: the
        // ground's x and z turned by the layer's angle and divided by its
        // repeat size - and the world directions those coordinates grow
        // along (the tangent frame for its normal map), flattened onto the
        // surface.
        void LayerFrame(float3 positionWS, half3 normalWS, float tile, float rotationDegrees, out float2 uv, out half3 tangent, out half3 bitangent)
        {
            float radians = rotationDegrees * (PI / 180.0);
            float c = cos(radians);
            float s = sin(radians);
            float2 planar = positionWS.xz;
            uv = float2(planar.x * c + planar.y * s, -planar.x * s + planar.y * c) / max(tile, 0.01);
            // Where u grows and where v grows, on the ground, then pushed
            // onto the surface's own plane (a slope tilts them a little).
            half3 u = half3(c, 0.0, s);
            half3 v = half3(-s, 0.0, c);
            tangent = normalize(u - normalWS * dot(u, normalWS));
            bitangent = normalize(v - normalWS * dot(v, normalWS));
        }

        Layer ReadLayer(TEXTURE2D_PARAM(albedoMap, samplerState), TEXTURE2D(normalMap), TEXTURE2D(heightMap), TEXTURE2D(occlusionMap),
            TEXTURE2D(smoothnessMap), float3 positionWS, half3 normalWS, half3 viewDirWS, float tile, float rotation, half4 tint)
        {
            float2 uv;
            half3 tangent;
            half3 bitangent;
            LayerFrame(positionWS, normalWS, tile, rotation, uv, tangent, bitangent);

            #if defined(_PARALLAX)
                // Offset mapping: the view direction in the layer's own frame
                // (u, v, out), and the coordinates slid along it by how far
                // the surface here stands above or below the middle height,
                // in metres over the repeat size. The 0.42 added to the
                // out-of-surface part keeps the slide bounded at grazing
                // angles (Unity's own ParallaxOffset does the same), so a
                // floor seen from standing height never smears.
                half3 viewTS = half3(dot(viewDirWS, tangent), dot(viewDirWS, bitangent), dot(viewDirWS, normalWS));
                half above = SAMPLE_TEXTURE2D(heightMap, samplerState, uv).r - 0.5;
                uv += above * (_ParallaxDepth / max(tile, 0.01)) * viewTS.xy / (viewTS.z + 0.42);
            #endif

            Layer layer;
            layer.albedo = SAMPLE_TEXTURE2D(albedoMap, samplerState, uv).rgb * tint.rgb;
            layer.height = SAMPLE_TEXTURE2D(heightMap, samplerState, uv).r;
            layer.occlusion = SAMPLE_TEXTURE2D(occlusionMap, samplerState, uv).r;
            layer.smoothness = SAMPLE_TEXTURE2D(smoothnessMap, samplerState, uv).r;
            half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(normalMap, samplerState, uv), _NormalStrength);
            layer.normalWS = normalize(tangent * normalTS.x + bitangent * normalTS.y + normalWS * normalTS.z);
            return layer;
        }

        // Blends a layer over what is under it by its painted weight and
        // the two heights: the layer wins where its weight plus its height
        // beats the ground's, over a transition that sharpness narrows. The
        // classic height-blend: both scores are measured from a little
        // below the higher one, and whatever is above that line gets a
        // share. Returns the layer's final share (0-1).
        half HeightBlend(half weight, half heightUnder, half heightOver, half sharpness)
        {
            // At sharpness 0 the heights hardly matter and the blend is the
            // paint alone; at 1 the heights decide within a narrow band.
            half transition = lerp(1.0, 0.08, sharpness);
            half scoreUnder = (1.0 - weight) + heightUnder * sharpness;
            half scoreOver = weight + heightOver * sharpness;
            half start = max(scoreUnder, scoreOver) - transition;
            half shareUnder = max(scoreUnder - start, 0.0);
            half shareOver = max(scoreOver - start, 0.0);
            return shareOver / (shareUnder + shareOver);
        }

        Layer MixLayers(Layer under, Layer over, half share)
        {
            Layer mixed;
            mixed.albedo = lerp(under.albedo, over.albedo, share);
            mixed.occlusion = lerp(under.occlusion, over.occlusion, share);
            mixed.height = lerp(under.height, over.height, share);
            mixed.smoothness = lerp(under.smoothness, over.smoothness, share);
            mixed.normalWS = normalize(lerp(under.normalWS, over.normalWS, share));
            return mixed;
        }

        // The whole ground at a pixel: dirt, cobbles over it by the red
        // paint, water over that by the blue paint. A layer painted away
        // entirely on this triangle isn't read (the branch is uniform over
        // the triangle, so it costs nothing).
        Layer Ground(float3 positionWS, half3 normalWS, half3 viewDirWS, half4 paint)
        {
            Layer ground = ReadLayer(TEXTURE2D_ARGS(_DirtAlbedo, sampler_DirtAlbedo), _DirtNormal, _DirtHeight, _DirtOcclusion, _DirtSmoothness, positionWS, normalWS,
                viewDirWS, _DirtTile, _DirtRotation, _DirtTint);

            [branch]
            if (paint.r > 0.002)
            {
                Layer cobbles = ReadLayer(TEXTURE2D_ARGS(_CobbleAlbedo, sampler_DirtAlbedo), _CobbleNormal, _CobbleHeight, _CobbleOcclusion, _CobbleSmoothness, positionWS,
                    normalWS, viewDirWS, _CobbleTile, _CobbleRotation, _CobbleTint);
                half share = HeightBlend(paint.r, ground.height, cobbles.height, _CobbleSharpness);
                ground = MixLayers(ground, cobbles, share);
            }

            [branch]
            if (paint.b > 0.002)
            {
                Layer wet = ReadLayer(TEXTURE2D_ARGS(_WetAlbedo, sampler_DirtAlbedo), _WetNormal, _WetHeight, _WetOcclusion, _WetSmoothness, positionWS, normalWS,
                    viewDirWS, _WetTile, _WetRotation, _WetTint);
                // Water lies in the low places: it is blended by the height
                // of what is under it, upside down, so a joint between
                // stones fills before a stone's top does.
                half share = HeightBlend(paint.b, ground.height, 1.0 - ground.height, _WetSharpness);
                ground = MixLayers(ground, wet, share);
            }

            return ground;
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
            #pragma shader_feature_local_fragment _PARALLAX

            // The same lighting options URP's Lit shader compiles for:
            // shadows, extra lights, baked light, reflection probes, fog,
            // foveation.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
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
                half4 color : COLOR;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 paint : COLOR;

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
                output.paint = input.color;

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

                Layer ground = Ground(positionWS, surfaceNormal, viewDirWS, input.paint);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = ground.albedo;
                surfaceData.alpha = 1.0;
                surfaceData.metallic = 0.0;
                surfaceData.specular = half3(0.0, 0.0, 0.0);
                surfaceData.smoothness = saturate(ground.smoothness * _Smoothness);
                surfaceData.occlusion = lerp(1.0, ground.occlusion, _Occlusion);
                surfaceData.normalTS = half3(0.0, 0.0, 1.0);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = ground.normalWS;
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

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        // The ground's own shadow: nothing stands under a street, so the
        // generator turns shadow casting off on its renderers, but a
        // hand-placed piece of ground may want it.
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
                output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }

        // What the lightmapper sees when it bakes: the blended colour, so
        // bounced light off the ground carries the cobbles' and the dirt's
        // colour.
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex GroundMetaVertex
            #pragma fragment GroundMetaFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 paint : COLOR;
            };

            Varyings GroundMetaVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2, unity_LightmapST, unity_DynamicLightmapST);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.paint = input.color;
                return output;
            }

            half4 GroundMetaFragment(Varyings input) : SV_Target
            {
                // No view in a bake, so no parallax: the normal stands in.
                half3 normalWS = normalize(input.normalWS);
                Layer ground = Ground(input.positionWS, normalWS, normalWS, input.paint);
                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = ground.albedo;
                metaInput.Emission = half3(0.0, 0.0, 0.0);
                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }
}
