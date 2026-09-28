// Debug wireframes that show in the headset (InHeadsetGizmos): a line mesh
// coloured per vertex, drawn after everything else. Unity's Gizmos can't be
// used for this - they're drawn for one flat camera, so in VR they show in
// one eye only, offset and distorted.
//
// The depth test is a material property (_ZTest), so the same shader can
// draw through walls (Always - e.g. to see a hand capsule held back inside
// a wall) or be hidden by them (LessEqual).
//
// In the Resources folder so it's always included in builds - otherwise
// Shader.Find() only works in the editor.
Shader "TeaLeaf/DebugLines"
{
    Properties
    {
        // Set from code (InHeadsetGizmos.seeThroughWalls). 8 = Always.
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
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
            Name "DebugLines"

            // SRPDefaultUnlit: URP draws this pass for any object in its
            // queue range - no lighting, shadows or depth prepass involved.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest [_ZTest]
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Needed for single-pass instanced stereo (the project's OpenXR
            // render mode): each eye is an instance, and without this plus the
            // stereo macros below the lines would only draw in one eye.
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.color;
            }
            ENDHLSL
        }
    }
}
