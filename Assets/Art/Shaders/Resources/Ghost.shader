// Ghost hands: a faint, flat-coloured copy of a hand at the real controller
// while a surface holds the hand visual back (HandGhost). Built in code by
// HandGhost - the material's colour comes from PlayerHandVisuals.
//
// Like TeaLeaf/Overlay it ignores depth: the real hand is usually INSIDE the
// wall it's pushed into, so a depth-tested ghost would always be hidden by
// that wall's face. Unlike Overlay it's a closed 3D mesh drawn see-through,
// and without depth the triangles behind (the palm behind the fingers, the
// back of the hand) would blend on top of each other and darken wherever
// they overlap. A stencil test fixes that: the first triangle to cover a
// pixel marks it, and nothing of the ghost draws there again. The colour is
// flat, so which triangle got there first makes no difference.
//
// In the Resources folder so it's always included in builds - otherwise
// Shader.Find() only works in the editor.
Shader "TeaLeaf/Ghost"
{
    Properties
    {
        // Material.color writes _Color. Keep the alpha low - it's a ghost.
        _Color ("Color", Color) = (1, 1, 1, 0.2)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            // Just before Overlay, so the reticles and other UI markers
            // (Overlay queue) still draw on top of a ghost.
            "Queue" = "Overlay-1"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Ghost"

            // SRPDefaultUnlit: URP draws this pass for any object in its
            // queue range - no lighting, shadows or depth prepass involved.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest Always
            ZWrite Off
            // A closed mesh, so the back faces are never needed (Unity flips
            // this for the mirrored right hand's negative scale by itself).
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            // Draw a pixel only if no ghost has drawn it yet this frame, then
            // mark it. One bit (128) of the stencil buffer, read and written
            // through masks, so any other stencil use keeps its other bits.
            // Both ghosts share the bit, so where they overlap on screen the
            // colour stays even too.
            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Needed for single-pass instanced stereo (the project's OpenXR
            // render mode) - see Overlay.shader.
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
