// In-world UI marker that fills from the bottom up, like a meter: below the
// fill line it's _FillColor, above it _Color. Drawn on top of everything,
// exactly as TeaLeaf/Overlay (same queue, no depth test, both eyes) - see
// that shader for why no built-in shader can.
//
// The fill line is in object space: the mesh is built 0 to 1 high in Y, so
// _Fill 0 = empty, 1 = full, whatever size the marker is drawn at. Used by
// the guards' alert marks (AI.GuardAlertMarker).
//
// In the Resources folder so it's always included in builds - otherwise
// Shader.Find() only works in the editor.
Shader "TeaLeaf/OverlayFill"
{
    Properties
    {
        _Color ("Base Color", Color) = (1, 1, 1, 1)
        _FillColor ("Fill Color", Color) = (1, 0.85, 0.1, 1)
        _Fill ("Fill", Range(0, 1)) = 0
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
            Name "OverlayFill"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Single-pass instanced stereo: without this and the stereo
            // macros the marker draws in one eye only.
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FillColor;
                half _Fill;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // Height up the mark, 0 at its foot, 1 at its top.
                float height : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.height = input.positionOS.y;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // step() is 1 below the fill line: a hard edge, like a
                // liquid level.
                return lerp(_Color, _FillColor, step(input.height, _Fill));
            }
            ENDHLSL
        }
    }
}
