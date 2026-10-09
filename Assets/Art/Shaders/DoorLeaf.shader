// A door's leaf, with a keyhole the player can look through: a plain lit
// colour, and a keyhole-shaped opening cut right out of it. Nothing is
// drawn inside the opening, so the room behind the door shows through it -
// seen with the player's own two eyes, so it has real depth, and nothing is
// drawn a second time (the cheap alternative to a second camera).
//
// Interaction.DoorKeyhole tells the shader where the opening is and how big
// (it grows as the head comes near). With no DoorKeyhole the size is zero
// and nothing is cut: an ordinary flat-coloured surface.
//
// How the opening is made: each pixel works out how far it is from the
// keyhole's outline (a "signed distance": below zero inside the outline,
// above zero outside), and pixels inside are thrown away (clip). The
// distance is measured across the door's face only, not through it, so the
// same opening is cut in both faces, one straight behind the other. A
// face turned away from the camera is never drawn (back-face culling), so
// through the opening in the near face the player sees the room, not the
// inside of the far face. The cut itself has no depth: the opening's walls
// (the door's thickness) are a small mesh DoorKeyhole makes, drawn with
// this same shader.
//
// Lighting is URP's simple (Blinn-Phong) model, as the other level shaders.
//
// Cost, for Quest: a few sums per pixel. Throwing pixels away (clip) makes
// the graphics chip's early depth test less effective for this material, so
// keep it to door leaves, not whole walls. DoorKeyhole sets its values
// through a MaterialPropertyBlock, which takes that one leaf out of the SRP
// Batcher (one draw call of its own).
//
// The shadow pass does NOT cut the opening: the door still casts a whole
// shadow, so no spot of light appears behind it.
//
// No Meta pass: doors move, so they aren't in a lightmap bake.
Shader "TeaLeaf/DoorLeaf"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.45, 0.3, 0.18, 1)

        [Header(Keyhole)]
        // The band round the opening's edge, standing in for the dark
        // inside of the cut: its colour, and how wide it is in metres.
        _RimColor ("Rim Colour", Color) = (0.05, 0.04, 0.03, 1)
        _RimWidth ("Rim Width (m)", Range(0.0005, 0.02)) = 0.004

        // Set per door by DoorKeyhole, not by hand. Centre: the keyhole's
        // middle, in the leaf's own space. Right/Up: the directions across
        // the door's face, scaled so a distance along them comes out in
        // metres whatever the leaf's scale. Size: x is the radius of the
        // keyhole's circle in metres (the rest is unused). All zero = no
        // opening.
        [HideInInspector] _KeyholeCentre ("Keyhole Centre", Vector) = (0, 0, 0, 0)
        [HideInInspector] _KeyholeRight ("Keyhole Right", Vector) = (0, 0, 0, 0)
        [HideInInspector] _KeyholeUp ("Keyhole Up", Vector) = (0, 0, 0, 0)
        [HideInInspector] _KeyholeSize ("Keyhole Size", Vector) = (0, 0, 0, 0)
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
            half4 _BaseColor;
            half4 _RimColor;
            float _RimWidth;
            float4 _KeyholeCentre;
            float4 _KeyholeRight;
            float4 _KeyholeUp;
            float4 _KeyholeSize;
        CBUFFER_END

        // How far a point on the leaf (in the leaf's own space) is from the
        // opening's outline, in metres: below zero inside the opening,
        // above zero outside it.
        //
        // The outline is the classic keyhole: a circle with a slot running
        // down from it, all sized by one number, the circle's radius.
        // Measured from the keyhole's middle, the circle's centre is one
        // radius up; the slot is as wide as the radius, starts inside the
        // circle and ends two radii below the middle - so it sticks out
        // below the circle by the circle's diameter. (DoorKeyhole's
        // BuildOutline() draws the same shape for the opening's walls:
        // change one, change the other.)
        //
        // Each part has a simple distance of its own. A circle: how far
        // from its centre, less the radius. A rectangle: abs() folds its
        // four quarters onto one, then it's how far past the corner the
        // point is (outside), or how near the closest side (inside, below
        // zero). The keyhole is the two together, and a point's distance
        // to "either of two shapes" is the smaller of its distances to
        // each.
        float KeyholeDistance(float3 positionOS)
        {
            float3 offset = positionOS - _KeyholeCentre.xyz;
            float2 across = float2(dot(offset, _KeyholeRight.xyz), dot(offset, _KeyholeUp.xyz));

            float radius = _KeyholeSize.x;

            float toCircle = length(across - float2(0.0, radius)) - radius;

            // The slot: from one radius above the middle (the circle's
            // centre) to two below, so its own middle is half a radius
            // down, and it's half a radius by one and a half each way.
            float2 q = abs(across - float2(0.0, -0.5 * radius)) - float2(0.5, 1.5) * radius;
            float toSlot = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);

            // No size = no opening: push the distance well outside, or the
            // one pixel at the centre would count as on the outline.
            return min(toCircle, toSlot) + step(radius, 0.0);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // With multisample anti-aliasing on, a pixel's alpha decides how
            // many of its samples are kept - so the opening's edge is
            // smoothed like any other edge, instead of being stair-stepped.
            // Ignored when anti-aliasing is off.
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

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

                // The position in the leaf's own space, for the keyhole.
                float3 positionOS : TEXCOORD6;

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

                output.positionOS = input.positionOS.xyz;
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

                // Inside the opening: nothing is drawn. "coverage" is how
                // much of this pixel is door - 0 well inside the opening, 1
                // well outside, in between across the one pixel the outline
                // runs through (fwidth = how much the distance changes from
                // this pixel to the next). It goes out as the alpha, for
                // AlphaToMask above.
                float keyholeDistance = KeyholeDistance(input.positionOS);
                half coverage = saturate(keyholeDistance / max(fwidth(keyholeDistance), 0.00001) + 0.5);
                clip(coverage - 0.001);

                float3 positionWS = input.positionWS;

                // Dark right at the opening's edge, the door's own colour
                // by the rim's width away from it.
                half3 albedo = lerp(_RimColor.rgb, _BaseColor.rgb, saturate(keyholeDistance / max(_RimWidth, 0.0001)));

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0;
                surfaceData.occlusion = 1.0;
                surfaceData.normalTS = half3(0.0, 0.0, 1.0);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
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
                color.a = coverage;
                return color;
            }
            ENDHLSL
        }

        // Draws the leaf into shadow maps, so it casts shadows. The whole
        // leaf: the opening isn't cut here (see the top of the file).
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

        // Depth only: used when the pipeline draws depth before colour. The
        // opening is cut here too, or the depth drawn first would hide the
        // room behind it.
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
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(KeyholeDistance(input.positionOS));
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth and the surface's normal: used by effects that need both
        // (none on Quest; here so the door isn't missing from them).
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
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(KeyholeDistance(input.positionOS));
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
