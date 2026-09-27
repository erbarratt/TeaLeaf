// In-world UI markers (hand reticles, the mantle arrow, later UI): a flat
// colour, drawn after everything else and on top of it - no depth test, so
// no wall or hand model can hide it. Built by OverlayMaterial.Create().
//
// A shader of our own because nothing built in can do this: URP's Unlit has
// no depth test setting, and UI/Default reads its depth test from
// unity_GUIZTestMode, which isn't one of its material properties - so
// setting it on a material does nothing (found 2026-09-27, the markers were
// still hidden by walls and hands).
//
// In the Resources folder so it's always included in builds - otherwise
// Shader.Find() only works in the editor.
Shader "TeaLeaf/Overlay"
{
    Properties
    {
        // Material.color writes _Color.
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Overlay"

            // SRPDefaultUnlit: URP draws this pass for any object in its
            // queue range - no lighting, shadows or depth prepass involved.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // The whole point: ignore depth, never write it, draw both sides
            // (the markers are flat), and blend by alpha.
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Needed for single-pass instanced stereo (the project's OpenXR
            // render mode): each eye is an instance, and without this plus the
            // stereo macros below the marker would only draw in one eye.
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // In the UnityPerMaterial buffer, like URP's own shaders, so it
            // stays compatible with the SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

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

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return _Color;
            }
            ENDHLSL
        }
    }
}
