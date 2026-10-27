// A hand-placed shaft of moonlight: the beam the moon throws through a
// window or a gap in a roof, made visible by the dust in the air. The
// lantern cone's shader (TeaLeaf/LightCone) for a box: drawn on an open
// four-sided tube (no top, no bottom) that GodRayBox builds from eight
// corners placed in the editor, the top four at the opening and the
// bottom four where the light dies away.
//
// How the box reads as a volume: the light is strongest where the eye
// looks square on through a side and fades to nothing where it looks
// along it. Only each side's outer face is drawn: the near sides show,
// the far sides (seen from inside the box) don't.
// Unlike the cone's many sides, a box's four meet at hard corners, so
// each side also fades towards its two long edges (_EdgeSoftness), but
// only as far as the side beyond that edge is seen edge on: an edge on
// the outline (its neighbour edge on) fades to nothing, so the outline
// never shows as a bright line, while a corner pointing at the eye (both
// sides seen at a slant) keeps its light, so it never shows as a dark
// crease. At the edge itself both sides then come to the same light
// (each side's rim times the other's), so the corner shows no seam. The light fades in
// just below the opening (no ring at the top), dies away down the box
// (so it never cuts into the floor), and drifts with a slow scrolling
// dust noise.
//
// Seen from far, faded near: the shaft shows only from a distance. It
// fades out as the eye comes within _NearFadeStart + _NearFadeLength and
// is gone by _NearFadeStart, so walking into it never fills the view; it
// also fades out beyond _FarFadeStart (gone _FarFadeLength further on).
//
// Cost: additive, no depth write, one texture sample a pixel over the
// pixels the box covers. Nothing here reads the depth or opaque texture;
// the box is depth tested, so walls and the floor cut it off.
//
// Stereo: single-pass instanced; the view direction and the distance
// fades use each eye's own position (GetCameraPositionWS() is per eye).
Shader "TeaLeaf/GodRayBox"
{
    Properties
    {
        [Header(Light)]
        // The light's colour (the moon's cool white).
        _Color ("Color", Color) = (0.85, 0.9, 1.0, 1)

        // Overall brightness of the shaft: higher is a denser, brighter glow.
        _Intensity ("Intensity", Range(0, 2)) = 0.25

        // How quickly the light fades where the eye looks along a side:
        // 1 is a broad soft glow, higher keeps it to sides seen square on.
        _RimPower ("Rim Power", Range(0.5, 6)) = 1.2

        [Header(Shape)]
        // How far in from each long edge of a side the light fades out, as
        // a share of the side's width (0.5 fades all the way to the middle,
        // the softest; small keeps the corners crisp).
        _EdgeSoftness ("Edge Softness", Range(0.02, 0.5)) = 0.3

        // Share of the shaft's length, from the opening, over which the
        // light fades in (0.1 = the top tenth), so the opening shows no ring.
        _TopFade ("Top Fade", Range(0.01, 0.5)) = 0.1

        // How the light dies away down the shaft, from the opening (full)
        // to the bottom (none): 1 is even, higher fades it sooner.
        _LengthFalloff ("Length Falloff", Range(0.2, 4)) = 1.5

        [Header(Distance Fade)]
        // Within this distance of the eye, in metres, the shaft is gone.
        _NearFadeStart ("Near Fade Start (m)", Range(0, 20)) = 4

        // Over this many metres beyond Near Fade Start the shaft fades in
        // to full (4 + 8 = full from 12m away).
        _NearFadeLength ("Near Fade Length (m)", Range(0.1, 20)) = 8

        // Beyond this distance, in metres, the shaft starts to fade out.
        _FarFadeStart ("Far Fade Start (m)", Range(5, 100)) = 40

        // Over this many metres beyond Far Fade Start the shaft fades out.
        _FarFadeLength ("Far Fade Length (m)", Range(0.1, 50)) = 15

        [Header(Dust)]
        // The tiling noise the dust is read from (FlameNoise.png: seamless
        // value noise). Any seamless noise will do.
        [NoScaleOffset] _NoiseTex ("Noise", 2D) = "gray" {}

        // How much the dust breaks up the light (0 = even, 1 = full contrast).
        _Dust ("Dust", Range(0, 1)) = 0.6

        // How many times the noise repeats round the box (whole numbers
        // only, so it meets itself at the seam).
        _DustAround ("Dust Repeats Around", Range(1, 8)) = 2

        // How many times the noise repeats down the shaft.
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
            Name "GodRayBox"

            // SRPDefaultUnlit: URP draws this pass for any object in its
            // queue range - no lighting, shadows or depth prepass involved.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // Hidden by anything nearer, never writing depth itself. Only
            // the outer faces drawn (GodRayBox winds every side to face out
            // of the box): the sides facing the eye show, the far sides
            // (seen from inside) don't, and from inside the box nothing does.
            ZTest LEqual
            ZWrite Off
            Cull Back

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
            // draw all shafts sharing this shader together.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _RimPower;
                float _EdgeSoftness;
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

                // The side's normal (flat: every vertex of a side has the
                // side's own).
                float3 normalOS : NORMAL;

                // x = round the box (0..1, a quarter per side, meeting at a
                // seam), y = down it (0 at the opening, 1 at the bottom).
                float2 uv : TEXCOORD0;

                // x = across this side (0 at one long edge, 1 at the other).
                float2 side : TEXCOORD1;

                // The normals of the sides beyond this side's two long
                // edges: the one past across 0, and the one past across 1.
                float3 neighbourBeforeOS : TEXCOORD2;
                float3 neighbourAfterOS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;

                // xy = round and down the box, z = across the side.
                float3 uv : TEXCOORD2;
                float3 neighbourBeforeWS : TEXCOORD3;
                float3 neighbourAfterWS : TEXCOORD4;
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
                output.uv = float3(input.uv, input.side.x);
                output.neighbourBeforeWS = TransformObjectToWorldNormal(input.neighbourBeforeOS);
                output.neighbourAfterWS = TransformObjectToWorldNormal(input.neighbourAfterOS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float around = input.uv.x;
                float along = input.uv.y;
                float across = input.uv.z;

                // The eye's view of this point: how far away, and how
                // square on to the side.
                float3 toEye = GetCameraPositionWS() - input.positionWS;
                float eyeDistance = length(toEye);
                float3 viewDir = toEye / max(eyeDistance, 0.0001);
                float facing = abs(dot(viewDir, normalize(input.normalWS)));
                float rim = pow(facing, _RimPower);

                // Soft towards each long edge (a corner of the box) as far
                // as the side beyond it is seen edge on: fully on the
                // outline, not at all at a corner pointing at the eye.
                float rimBefore = pow(abs(dot(viewDir, normalize(input.neighbourBeforeWS))), _RimPower);
                float rimAfter = pow(abs(dot(viewDir, normalize(input.neighbourAfterWS))), _RimPower);
                float edgeBefore = lerp(smoothstep(0.0, _EdgeSoftness, across), 1.0, rimBefore);
                float edgeAfter = lerp(smoothstep(0.0, _EdgeSoftness, 1.0 - across), 1.0, rimAfter);
                float edge = edgeBefore * edgeAfter;

                // Fades in below the opening, dies away down the shaft.
                float topFade = smoothstep(0.0, _TopFade, along);
                float lengthFade = pow(saturate(1.0 - along), _LengthFalloff);

                // Gone close up, full from Near Fade Start + Length, fading
                // out again beyond Far Fade Start.
                float nearFade = saturate((eyeDistance - _NearFadeStart) / _NearFadeLength);
                nearFade = nearFade * nearFade * (3.0 - 2.0 * nearFade);
                float farFade = 1.0 - saturate((eyeDistance - _FarFadeStart) / _FarFadeLength);

                // Dust: the noise wrapped a whole number of times round the
                // box (no seam), drifting slowly down and round it.
                float2 dustUV = float2(around * round(_DustAround), along * _DustAlong) + _Time.y * _DustSpeed * float2(0.37, -1.0);
                float dustNoise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, dustUV).r;
                float dust = 1.0 + (dustNoise * 2.0 - 1.0) * _Dust;

                float amount = rim * edge * topFade * lengthFade * nearFade * farFade * dust;
                half3 color = _Color.rgb * _Intensity * amount;
                return half4(color, 0.0);
            }
            ENDHLSL
        }
    }
}
