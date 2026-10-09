// The lockpicking pieces: the big lock that floats in front of a door while
// it's being picked (Interaction.BigLock), and the two picks themselves. A
// plain lit colour that can be faded in and out.
//
// The colour comes from the mesh, one colour per point (set when the mesh is
// built in code), so the lock's body, its keyway, its marks and the picks
// are all one material and never need a texture.
//
// Cheap, for Quest: all the lighting is worked out per point of the mesh
// (per vertex), not per pixel - the shapes are flat-sided, so it looks the
// same. Only the moon and the scene's ambient light are counted, with no
// shadows, and the result never goes below _MinLight: levels are dark, and a
// lock nobody could see would be no use. A pixel just copies the colour its
// three points agreed on.
//
// Fading: one shader, set up two ways by the material (see
// Interaction.LockMeshBuilder.CreateMaterial()). Solid: Geometry queue, no
// blending - an ordinary opaque surface. See-through: Transparent queue,
// blended by _Alpha. The big lock only wears the see-through one for the
// fraction of a second it takes to fade in or out, then swaps to the solid
// one, so the cost of blending isn't paid while it's just sitting there.
// Depth is written either way, so its near parts hide its far parts.
//
// In the Resources folder so it's always included in builds - otherwise
// Shader.Find() only works in the editor.
Shader "TeaLeaf/LockFade"
{
    Properties
    {
        // Multiplies the mesh's own colours. White = leave them alone.
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)

        // How solid it is, 0-1. Only matters to the see-through material.
        _Alpha ("Alpha", Range(0, 1)) = 1

        // The least light it's ever shown in, however dark the room.
        _MinLight ("Least Light", Range(0, 1)) = 0.35

        // How the colour is mixed with what's behind it. Set by code: One
        // and Zero for solid, SrcAlpha and OneMinusSrcAlpha for see-through.
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // Shared by both passes. The properties must be declared the same
        // way in each, inside this CBUFFER, for the SRP Batcher.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Alpha;
            half _MinLight;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Single-pass instanced stereo (each eye is an instance).
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                half3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // The scene's ambient light on a surface facing this way,
                // plus the main light (the moon) by how squarely it faces
                // it. Never less than the least light.
                Light mainLight = GetMainLight();
                half3 light = SampleSH(normalWS) + mainLight.color * saturate(dot(normalWS, mainLight.direction));
                light = max(light, _MinLight);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color.rgb * _BaseColor.rgb * light;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(input.color, _Alpha);
            }
            ENDHLSL
        }

        // Depth only: used when the pipeline draws depth before colour (the
        // solid material only - see-through things aren't drawn into it).
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
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
    }
}
