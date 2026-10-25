// A held tool burning away, or burning back into being: the bow put away or
// drawn, an arrow let go of (Player.ToolBurn plays it). The surface is
// URP's Lit look - the same colour, normal and metallic textures, lit by the
// same PBR sum - so swapping a URP Lit material for this one at the start of
// a burn shows no change; then holes open across it and spread until
// nothing is left. Just ahead of each hole's edge the surface chars dark,
// and the edge itself glows: orange, hottest (yellow) right at the edge.
//
// Where the holes open comes from noise worked out from the point's place in
// the object's own space, so the pattern is stuck to the object and doesn't
// slide over it as the hand moves. Measured in metres (the object's scale
// taken out), so _NoiseScale means the same on any model.
//
// _Burn runs 0 (whole) to 1 (gone). Every point has a noise value 0-1;
// points whose value the burn has passed are thrown away (clip), points a
// little above it are charred and glowing. The burn starts a char width
// below 0, so at _Burn 0 nothing is charred, and ends just past 1, so at
// _Burn 1 nothing is left.
//
// Cost: a URP Lit pixel plus three octaves of noise, and clip (which makes
// the early depth test less effective). Only worn for the second or two a
// burn takes - Player.ToolBurn swaps it in and back out - on small objects,
// so it's never paid on the screen at large.
//
// No shadow pass: the tools that burn cast no shadows. No glow on the world
// (no bloom, no light): the edge is just bright.
//
// In the Resources folder so it's always included in builds - otherwise
// Shader.Find() only works in the editor.
Shader "TeaLeaf/Burn"
{
    Properties
    {
        // The URP Lit material's own values, copied across by ToolBurn.
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _BaseMap ("Texture", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        // Metallic in red, smoothness in alpha, each times the numbers
        // below. White (none set) = just the numbers, as URP Lit with no
        // map.
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5

        [Header(Burn)]
        // 0 whole, 1 gone. Set by ToolBurn each frame of a burn.
        _Burn ("Burn", Range(0, 1)) = 0
        // How many noise blobs to a metre: larger = smaller holes.
        _NoiseScale ("Noise Scale (per m)", Float) = 12
        // How far ahead of the edge (in noise, 0-1) the surface is
        // charred, and glows. The glow is never wider than the char.
        _CharWidth ("Char Width", Range(0.001, 0.5)) = 0.12
        _GlowWidth ("Glow Width", Range(0.001, 0.5)) = 0.04
        _CharColor ("Char Colour", Color) = (0.05, 0.03, 0.02, 1)
        // The glow fading back from the edge, and right at it.
        [HDR] _GlowColor ("Glow Colour", Color) = (3, 0.6, 0.05, 1)
        [HDR] _EdgeColor ("Edge Colour", Color) = (6, 3.5, 0.8, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "AlphaTest"
        }

        // Shared by every pass. The properties must be declared identically
        // in all of them, inside this CBUFFER, for the SRP Batcher.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Metallic;
            half _Smoothness;
            half _Burn;
            float _NoiseScale;
            half _CharWidth;
            half _GlowWidth;
            half4 _CharColor;
            half4 _GlowColor;
            half4 _EdgeColor;
        CBUFFER_END

        // A random-looking number 0-1 for each whole-number point in space
        // (Inigo Quilez's hash: a few multiplies, no texture).
        float Hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        // Smooth noise 0-1: the hash at the eight whole-number corners round
        // the point, blended by how near the point is to each (eased, so
        // there are no creases along the cube faces).
        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            float a = lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x);
            float b = lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x);
            float c = lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x);
            float d = lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x);
            return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
        }

        // How far past the burn this point of the object is: below zero
        // burnt away, a little above it charred and glowing. Three octaves
        // of noise (big blobs, ragged edges), stretched so their values,
        // which bunch round the middle, spread over 0-1 and the burn moves
        // at an even pace.
        float BurnDistance(float3 positionOS)
        {
            // The object's scale, so the noise is per metre.
            float scale = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
            float3 p = positionOS * scale * _NoiseScale;

            float n = ValueNoise(p) * 0.5 + ValueNoise(p * 2.03) * 0.25 + ValueNoise(p * 4.01) * 0.125;
            n = saturate((n / 0.875 - 0.25) * 2.0);

            float burnLine = lerp(-_CharWidth, 1.01, _Burn);
            return n - burnLine;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // The same lighting options URP's Lit shader compiles for:
            // shadows, extra lights (Forward+ too), baked light, reflection
            // probes, fog, foveation.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            // Single-pass instanced stereo (each eye is an instance).
            #pragma multi_compile_instancing

            // The normal map is always read (both materials worn have one).
            #define _NORMALMAP 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            TEXTURE2D(_MetallicGlossMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;

                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    half4 fogFactorAndVertexLight : TEXCOORD4;
                #else
                    half fogFactor : TEXCOORD4;
                #endif

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord : TEXCOORD5;
                #endif

                // Held tools are never lightmapped: light probes only.
                half3 vertexSH : TEXCOORD6;

                #ifdef USE_APV_PROBE_OCCLUSION
                    float4 probeOcclusion : TEXCOORD7;
                #endif

                // The point in the object's own space, for the noise.
                float3 positionOS : TEXCOORD8;

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
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                #if defined(_FOG_FRAGMENT)
                    half fogFactor = 0;
                #else
                    half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                #endif

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionOS = input.positionOS.xyz;
                output.positionWS = vertexInput.positionWS;
                output.positionCS = vertexInput.positionCS;
                output.normalWS = normalInput.normalWS;

                // The tangent's w says which way the bitangent points
                // (mirrored UVs), times a mirrored object's own flip.
                output.tangentWS = half4(normalInput.tangentWS, input.tangentOS.w * GetOddNegativeScale());

                float4 unusedProbeOcclusion = 0;
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, unusedProbeOcclusion);

                #ifdef USE_APV_PROBE_OCCLUSION
                    output.probeOcclusion = unusedProbeOcclusion;
                #endif

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

                // Burnt away: nothing drawn.
                float burnDistance = BurnDistance(input.positionOS);
                clip(burnDistance);

                // 1 right at the edge, 0 a char (or glow) width away.
                half charred = 1.0 - saturate(burnDistance / _CharWidth);
                half glow = 1.0 - saturate(burnDistance / min(_GlowWidth, _CharWidth));

                // URP Lit's surface, from the same textures.
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half4 metallicGloss = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, input.uv);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, input.uv), _BumpScale);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = lerp(albedo.rgb, _CharColor.rgb, charred);
                surfaceData.metallic = metallicGloss.r * _Metallic * (1.0 - charred);
                surfaceData.smoothness = metallicGloss.a * _Smoothness * (1.0 - charred);
                surfaceData.normalTS = normalTS;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;

                float3 positionWS = input.positionWS;
                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.tangentToWorld = tangentToWorld;
                inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);

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

                // Light probes (held tools are never in a lightmap).
                #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
                    float4 probeOcclusion = 0;
                    #ifdef USE_APV_PROBE_OCCLUSION
                        probeOcclusion = input.probeOcclusion;
                    #endif
                    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
                        GetAbsolutePositionWS(inputData.positionWS),
                        inputData.normalWS,
                        inputData.viewDirectionWS,
                        input.positionCS.xy,
                        probeOcclusion,
                        inputData.shadowMask);
                #else
                    inputData.bakedGI = SampleSHPixel(input.vertexSH, inputData.normalWS);
                    inputData.shadowMask = half4(1, 1, 1, 1);
                #endif

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // The glowing edge, added on top of the lighting so it shows
                // in the dark too: orange fading back from the edge, hotter
                // towards it.
                color.rgb += lerp(_GlowColor.rgb, _EdgeColor.rgb, glow * glow) * glow;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        // Depth only: used when the pipeline draws depth before colour. The
        // burnt holes are cut here too, or the depth drawn first would hide
        // what's behind them.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

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
                float3 positionOS : TEXCOORD0;
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
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half DepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(BurnDistance(input.positionOS));
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth and the surface's normal: used by effects that need both
        // (none in use; here so a burning tool isn't missing from them).
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

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
                float3 positionOS : TEXCOORD1;
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
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(BurnDistance(input.positionOS));
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
