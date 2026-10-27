// A cone of lamplight: the glow a lantern throws down through dusty air,
// seen from across a street. Drawn on an open tapered tube hung under the
// lantern (LightCone.asset: narrow at the lantern, widening downwards,
// no caps); this shader shades the tube so it reads as a soft volume of
// light rather than a glowing lampshade.
//
// How the tube reads as a volume: the light is strongest where the eye
// looks square on through the tube's wall and fades to nothing where it
// looks along it, so the tube's outline never shows. Both faces are
// drawn, so the far side adds to the near side and the middle of the
// cone, where the eye looks through the most light, is brightest. The
// light fades in just below the lantern (no ring at the top), dies away
// down the cone (so it never cuts into the ground), and drifts with a
// slow scrolling dust noise.
//
// Seen from far, faded near: the cone shows only from a distance. It
// fades out as the eye comes within _NearFadeStart + _NearFadeLength and
// is gone by _NearFadeStart, so close up the lantern lights the street
// by itself and walking under it never fills the view; it also fades out
// beyond _FarFadeStart (gone _FarFadeLength further on).
//
// Cost: additive, no depth write, one texture sample a pixel over the
// pixels the cone covers. Nothing here reads the depth or opaque texture;
// the cone is depth tested, so walls and the ground cut it off.
//
// Stereo: single-pass instanced; the view direction and the distance
// fades use each eye's own position (GetCameraPositionWS() is per eye).
Shader "TeaLeaf/LightCone"
{
    Properties
    {
        [Header(Light)]
        // The light's colour (a lantern's warm orange).
        _Color ("Color", Color) = (1.0, 0.735, 0.278, 1)

        // Overall brightness of the cone: higher is a denser, brighter glow.
        _Intensity ("Intensity", Range(0, 2)) = 0.25

        // How quickly the light fades where the eye looks along the tube's
        // wall: 1 is a broad soft glow, higher keeps it to the middle of
        // the cone.
        _RimPower ("Rim Power", Range(0.5, 6)) = 1.2

        [Header(Shape)]
        // Share of the cone's length, from the lantern, over which the
        // light fades in (0.1 = the top tenth), so the narrow end shows no ring.
        _TopFade ("Top Fade", Range(0.01, 0.5)) = 0.1

        // How the light dies away down the cone, from the lantern (full)
        // to the wide end (none): 1 is even, higher fades it sooner.
        _LengthFalloff ("Length Falloff", Range(0.2, 4)) = 1.5

        [Header(Distance Fade)]
        // Within this distance of the eye, in metres, the cone is gone.
        _NearFadeStart ("Near Fade Start (m)", Range(0, 20)) = 4

        // Over this many metres beyond Near Fade Start the cone fades in
        // to full (4 + 8 = full from 12m away).
        _NearFadeLength ("Near Fade Length (m)", Range(0.1, 20)) = 8

        // Beyond this distance, in metres, the cone starts to fade out.
        _FarFadeStart ("Far Fade Start (m)", Range(5, 100)) = 40

        // Over this many metres beyond Far Fade Start the cone fades out.
        _FarFadeLength ("Far Fade Length (m)", Range(0.1, 50)) = 15

        [Header(Dust)]
        // The tiling noise the dust is read from (FlameNoise.png: seamless
        // value noise). Any seamless noise will do.
        [NoScaleOffset] _NoiseTex ("Noise", 2D) = "gray" {}

        // How much the dust breaks up the light (0 = even, 1 = full contrast).
        _Dust ("Dust", Range(0, 1)) = 0.6

        // How many times the noise repeats round the cone (whole numbers
        // only, so it meets itself at the seam).
        _DustAround ("Dust Repeats Around", Range(1, 8)) = 2

        // How many times the noise repeats down the cone.
        _DustAlong ("Dust Repeats Along", Range(0.1, 4)) = 1

        // How fast the dust drifts, in noise repeats per second (down and round).
        _DustSpeed ("Dust Speed", Range(0, 0.5)) = 0.03
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LightCone"

            // SRPDefaultUnlit: URP draws this pass for any object in its
            // queue range - no lighting, shadows or depth prepass involved.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // Hidden by anything nearer, never writing depth itself. Both
            // faces drawn: the far side of the tube adds its light too.
            ZTest LEqual
            ZWrite Off
            Cull Off

            // Additive: light only ever brightens what is behind it.
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Single-pass instanced stereo: each eye is an instance.
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            // Every material property in one block, so the SRP Batcher can
            // draw all cones sharing this shader together.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _RimPower;
                float _TopFade;
                float _LengthFalloff;
                float _NearFadeStart;
                float _NearFadeLength;
                float _FarFadeStart;
                float _FarFadeLength;
                float _Dust;
                float _DustAround;
                float _DustAlong;
                float _DustSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;

                // Outward from the tube's wall.
                float3 normalOS : NORMAL;

                // x = round the cone (0..1, meeting at a seam),
                // y = down it (0 at the lantern, 1 at the wide end).
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float around = input.uv.x;
                float along = input.uv.y;

                // The eye's view of this point: how far away, and how
                // square on to the tube's wall.
                float3 toEye = GetCameraPositionWS() - input.positionWS;
                float eyeDistance = length(toEye);
                float3 viewDir = toEye / max(eyeDistance, 0.0001);
                float facing = abs(dot(viewDir, normalize(input.normalWS)));
                float rim = pow(facing, _RimPower);

                // Fades in below the lantern, dies away down the cone.
                float topFade = smoothstep(0.0, _TopFade, along);
                float lengthFade = pow(saturate(1.0 - along), _LengthFalloff);

                // Gone close up, full from Near Fade Start + Length, fading
                // out again beyond Far Fade Start.
                float nearFade = saturate((eyeDistance - _NearFadeStart) / _NearFadeLength);
                nearFade = nearFade * nearFade * (3.0 - 2.0 * nearFade);
                float farFade = 1.0 - saturate((eyeDistance - _FarFadeStart) / _FarFadeLength);

                // Dust: the noise wrapped a whole number of times round the
                // cone (no seam), drifting slowly down and round it.
                float2 dustUV = float2(around * round(_DustAround), along * _DustAlong) + _Time.y * _DustSpeed * float2(0.37, -1.0);
                float dustNoise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, dustUV).r;
                float dust = 1.0 + (dustNoise * 2.0 - 1.0) * _Dust;

                float amount = rim * topFade * lengthFade * nearFade * farFade * dust;
                half3 color = _Color.rgb * _Intensity * amount;
                return half4(color, 0.0);
            }
            ENDHLSL
        }
    }
}
