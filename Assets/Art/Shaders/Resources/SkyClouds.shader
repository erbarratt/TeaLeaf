// The night sky's clouds: a cubemap of clouds drawn over the stars and the
// moon, as Thief VR's skybox draws its cloud layer. Used by ProceduralSky,
// which builds the mesh this shader draws (a cube round the eye) and sets
// every property here.
//
// Like TeaLeaf/Sky it is locked to the camera at "infinity": each corner of
// the cube is placed 1m from the eye and pushed to the back of the depth
// range, so walking never moves the clouds, both eyes see them in the same
// direction, and anything nearer hides them before they cost anything.
//
// The clouds drift with the wind without ever sliding away: the cubemap is
// read twice, each read pushed a little further downwind as time goes on
// and then jumping back, half a cycle apart, and the two are cross-faded so
// a read is invisible as it jumps ("flow mapping", with one wind for the
// whole sky). Two cubemap reads per sky pixel; nothing on the CPU.
//
// Nothing here is lit or lights anything. In the Resources folder so it's
// always included in builds - otherwise Shader.Find() only works in the
// editor.
Shader "TeaLeaf/SkyClouds"
{
    Properties
    {
        // The clouds: colour = their light (black where the sky is clear),
        // alpha = how thick they are.
        [NoScaleOffset] _CloudTex ("Cloud Cubemap", Cube) = "black" {}

        // What the cubemap's colour is multiplied by. A plain vector, not a
        // colour, so the numbers are used as they are (no conversion to
        // linear).
        _Tint ("Tint", Vector) = (0.118, 0.102, 0.18, 1)

        // Brightness on top of the tint.
        _Exposure ("Exposure", Range(0, 8)) = 1.1

        // Turns the clouds round the vertical axis, in degrees.
        _Rotation ("Rotation", Range(0, 360)) = 214

        // Keeps turning the clouds round the vertical axis, in degrees per
        // second, so they travel across the sky and over the moon.
        _TurnSpeed ("Turn Speed", Range(-2, 2)) = 0.3

        // How much thick cloud hides what's behind it (stars, moon):
        // 0 = clouds only add light, 1 = the thickest cloud hides it all.
        _Opacity ("Opacity", Range(0, 1)) = 1

        // xy = which way the clouds flow, across the sky (world x, z);
        // z = how far one cycle carries them (radians, near enough);
        // w = cycles per second.
        _Wind ("Wind", Vector) = (0.78, 2.27, 0.02, 0.05)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"

            // After the stars and moon (same queue; ProceduralSky's
            // sorting order puts these after them), so they cover them.
            "Queue" = "Transparent-400"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SkyClouds"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest LEqual
            ZWrite Off
            Cull Off

            // Premultiplied, as TeaLeaf/Sky: the cloud's light is added and
            // what's behind is dimmed by its thickness.
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_CloudTex);
            SAMPLER(sampler_CloudTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Exposure;
                float _Rotation;
                float _TurnSpeed;
                float _Opacity;
                float4 _Wind;
            CBUFFER_END

            struct Attributes
            {
                // A corner of the cube round the eye: a direction.
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;

                // The direction to read the cubemap in, already turned by
                // _Rotation. Not unit length between corners; the cubemap
                // read doesn't need it to be.
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 direction = input.positionOS.xyz;

                // Locked to the camera, at the very back of the depth range
                // (see the top of the file, and TeaLeaf/Sky).
                output.positionCS = TransformWorldToHClip(_WorldSpaceCameraPos + direction);

                #if UNITY_REVERSED_Z
                    output.positionCS.z = 0.0;
                #else
                    output.positionCS.z = output.positionCS.w;
                #endif

                // Turning the clouds one way is reading the cubemap turned
                // the other. The turn grows with time (kept within one
                // revolution, so the angle never loses precision).
                float angle = radians(_Rotation + fmod(_Time.y * _TurnSpeed, 360.0));
                float s = sin(angle);
                float c = cos(angle);
                output.direction = float3(direction.x * c - direction.z * s, direction.y, direction.x * s + direction.z * c);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 direction = normalize(input.direction);

                // Two reads half a cycle apart, each carried downwind from
                // 0 to the full distance over its cycle. Each one's weight
                // is 0 as it jumps back, so the jump never shows.
                float3 flow = float3(_Wind.x, 0.0, _Wind.y) * _Wind.z;
                float phaseA = frac(_Time.y * _Wind.w);
                float phaseB = frac(phaseA + 0.5);
                half4 cloudA = SAMPLE_TEXTURECUBE(_CloudTex, sampler_CloudTex, direction - flow * phaseA);
                half4 cloudB = SAMPLE_TEXTURECUBE(_CloudTex, sampler_CloudTex, direction - flow * phaseB);
                half weightB = abs(phaseA * 2.0 - 1.0);
                half4 cloud = lerp(cloudA, cloudB, weightB);

                // Colour is the cloud's light, alpha how thick it is (0 in
                // the clear gaps), so a dark underside hides the moon as
                // well as a lit top does.
                return half4(cloud.rgb * _Tint.rgb * _Exposure, cloud.a * _Opacity);
            }
            ENDHLSL
        }
    }
}
