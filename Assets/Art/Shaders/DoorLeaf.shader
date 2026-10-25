// A door's leaf, with a keyhole the player can look through: a lit
// surface (a colour times an optional texture, with an optional normal
// map), and a keyhole-shaped opening cut right out of it. Nothing is
// drawn inside the opening, so the room behind the door shows through it -
// seen with the player's own two eyes, so it has real depth, and nothing is
// drawn a second time (the cheap alternative to a second camera).
//
// Interaction.DoorKeyhole tells the shader where the opening is and how big
// (it grows as the head comes near). With no DoorKeyhole the size is zero
// and nothing is cut: an ordinary surface.
//
// The textures are there so a pack door (or the lock on it) can be drawn
// with its own art: a copy of its material on this shader with the same
// base map and normal map. Without them the base map is plain white and
// the normal map flat, so the door is just its colour. The pack's
// smoothness and occlusion maps aren't used.
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
        _BaseMap ("Texture", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}

        [Header(Keyhole)]
        // The band round the opening's edge, standing in for the dark
        // inside of the cut: its colour, and how wide it is in metres.
        _RimColor ("Rim Colour", Color) = (0.05, 0.04, 0.03, 1)
        _RimWidth ("Rim Width (m)", Range(0.0005, 0.02)) = 0.004

        // Set per door by DoorKeyhole, not by hand. Centre: the keyhole
        // circle's centre, in the leaf's own space. Right/Up: the directions across
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
            float4 _BaseMap_ST;
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
        // The outline is the lock plate's keyhole (the Lock Plate prefab):
        // a circle with a slot running down from it that widens towards
        // its bottom, all sized by one number, the circle's radius.
        // Measured from the circle's centre (the keyhole's place), the
        // slot starts inside the circle, half a radius down, half a radius
        // either side of the middle, and ends 3.4 radii down, 0.73 either
        // side. (DoorKeyhole's BuildOutline() draws the same shape for the
        // opening's walls: change one, change the other.)
        //
        // Each part has a simple distance of its own. A circle: how far
        // from its centre, less the radius. The slot is a trapezoid - a
        // box with sloping sides - whose distance is worked out in
        // Trapezoid() below. The keyhole is the two together, and a
        // point's distance to "either of two shapes" is the smaller of its
        // distances to each.
        static const float SlotTop = -0.5;
        static const float SlotBottom = -3.4;
        static const float SlotTopHalfWidth = 0.5;
        static const float SlotBottomHalfWidth = 0.73;

        // How far a point is from a trapezoid standing upright about its
        // own middle: halfHeight up and down, bottomHalf either side of
        // the middle line at its bottom, topHalf at its top. Below zero
        // inside. (Inigo Quilez's well-known formula.)
        //
        // abs() folds the left half onto the right, so only one sloping
        // side matters. Then the point's nearest spot is on either the
        // flat top or bottom (ca: how far past it, across and up) or the
        // sloping side (cb: the point less its nearest spot on that
        // side's line, clamped to the side's two ends). The distance is
        // the shorter of the two, made negative when the point is inside
        // both the side and the top and bottom.
        float Trapezoid(float2 p, float bottomHalf, float topHalf, float halfHeight)
        {
            float2 topCorner = float2(topHalf, halfHeight);
            float2 side = float2(topHalf - bottomHalf, 2.0 * halfHeight);
            p.x = abs(p.x);

            float2 ca = float2(p.x - min(p.x, p.y < 0.0 ? bottomHalf : topHalf), abs(p.y) - halfHeight);
            float2 cb = p - topCorner + side * saturate(dot(topCorner - p, side) / dot(side, side));
            float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
            return s * sqrt(min(dot(ca, ca), dot(cb, cb)));
        }

        float KeyholeDistance(float3 positionOS)
        {
            float3 offset = positionOS - _KeyholeCentre.xyz;
            float2 across = float2(dot(offset, _KeyholeRight.xyz), dot(offset, _KeyholeUp.xyz));

            float radius = _KeyholeSize.x;

            float toCircle = length(across) - radius;

            // The slot, measured from its own middle.
            float slotMiddle = 0.5 * (SlotTop + SlotBottom) * radius;
            float slotHalfHeight = 0.5 * (SlotTop - SlotBottom) * radius;
            float toSlot = Trapezoid(across - float2(0.0, slotMiddle), SlotBottomHalfWidth * radius, SlotTopHalfWidth * radius, slotHalfHeight);

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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
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

                // The textures' coordinates, and the direction across the
                // surface the normal map's "right" points (w: which way
                // its "up" points, for mirrored UVs).
                float2 uv : TEXCOORD7;
                half4 tangentWS : TEXCOORD8;

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

                output.positionOS = input.positionOS.xyz;
                output.positionWS = vertexInput.positionWS;
                output.positionCS = vertexInput.positionCS;
                output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.tangentWS = half4(normalInput.tangentWS, input.tangentOS.w * GetOddNegativeScale());

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
                half3 baseColor = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 albedo = lerp(_RimColor.rgb, baseColor, saturate(keyholeDistance / max(_RimWidth, 0.0001)));

                // The normal map turns the surface's normal: its three
                // values are amounts along the surface's right (tangent),
                // up (bitangent, worked out from the other two) and out
                // (normal). A mesh with no tangents (the opening's walls)
                // just keeps its own normal.
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv));
                half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0;
                surfaceData.occlusion = 1.0;
                surfaceData.normalTS = normalTS;

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(normalWS);
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
