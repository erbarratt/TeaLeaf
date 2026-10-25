// A soft dark patch on the ground under a character (AI.GuardShadow): the
// shadow of something that moves, in a level whose lighting is all baked.
//
// Drawn on a flat quad lying on the ground. Nothing is lit and no texture is
// read: the patch is worked out from the quad's UVs - darkest at the middle,
// fading smoothly to nothing at the quad's edge - so the quad's own shape
// (scaled long and narrow, for a foot or a shadow stretched away from the
// moon) is the shadow's shape.
//
// Multiplied onto what's already drawn (Blend DstColor Zero): the ground is
// darkened by its own colour, so the patch is right in any light - deep in a
// lit street, hardly anything on ground that's already dark. Nothing to sort:
// multiplying in either order gives the same picture.
//
// _Taper makes one end fade sooner than the other: the near end (UV v = 0,
// under the feet) stays dark while the far end thins out, as a long shadow
// does away from what casts it. 0 = even all over.
//
// _Strength and _Taper are set per renderer by GuardShadow (property block),
// so these few small quads leave the SRP Batcher; cheap at this size.
//
// In the Resources folder so it's always included in builds - otherwise
// Shader.Find() only works in the editor.
Shader "TeaLeaf/BlobShadow"
{
    Properties
    {
        // How dark the middle gets, 0-1. 1 = black.
        _Strength ("Strength", Range(0, 1)) = 0.6

        // How much the far end (v = 1) fades sooner than the near end.
        _Taper ("Taper", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend DstColor Zero
            ZWrite Off
            Cull Off

            // Pulled a little towards the camera, so it wins against the
            // ground it lies on rather than flickering in and out of it.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Single-pass instanced stereo (each eye is an instance).
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Strength;
                half _Taper;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Distance from the middle, 0 there and 1 at the edge of the
                // quad's inscribed circle (an ellipse once the quad is
                // stretched). Smoothly dark in the middle, nothing at 1.
                float2 fromMiddle = input.uv * 2.0 - 1.0;
                half fade = 1.0 - smoothstep(0.0, 1.0, length(fromMiddle));

                // The far end thinner than the near end.
                fade *= 1.0 - _Taper * input.uv.y;

                // What the ground's colour is multiplied by: 1 leaves it
                // alone. Fog fades the shadow out with the ground under it.
                half shade = 1.0 - _Strength * fade;
                shade = MixFogColor(half3(shade, shade, shade), half3(1.0, 1.0, 1.0), input.fogFactor).r;
                return half4(shade, shade, shade, 1.0);
            }
            ENDHLSL
        }
    }
}
