// A shaft of moonlight: the beam the moon throws through an opening - the
// mouth of an alley between two buildings, a gap in a roof, a window -
// made visible by the dust in the air. Drawn as a prism of six sheets
// (the opening, its far end on the ground, and the four sides joining
// them) that MoonShaft builds at load from the opening and the moon's
// direction; this shader shades those sheets so that together they read
// as one soft volume of light rather than a glowing box.
//
// How the sheets add up to a volume: each sheet stands for the beam
// behind it. A sheet seen face on shows the beam's full thickness
// behind it (the opening, looked down the beam, shows its whole length;
// a side, looked at square, shows its width); a sheet seen edge on shows
// nothing, so the seams where two sheets meet never show. The light
// fades towards each sheet's edges (so the prism has no hard corners),
// along its length (so it dies away before the ground rather than
// cutting into it), and as the eye comes close to a sheet (so standing
// in the beam never fills the view with a bright wall). Streaks of
// slightly brighter and darker light run along the beam, and motes
// drift down it, both from one tiling noise texture.
//
// Driven by the moon: the colour is the moon's Light colour times its
// intensity (_MoonShaftLight, a global MoonShaft sets from the
// Directional Light when it builds - not URP's _MainLightColor, which
// is black wherever the moon is a Baked light) times a tint, so a dimmer
// or warmer moon dims or warms every shaft. The direction is the mesh's,
// built from the same light by MoonShaft.
//
// Cost: additive, no depth write, two texture samples a pixel over the
// pixels the sheets cover. Nothing here reads the depth or opaque
// texture. The near fade keeps the fill rate down when the player walks
// through a shaft; a shaft is never drawn on top of nearer geometry
// (depth tested), so the ground and walls cut it off where they should.
//
// Stereo: single-pass instanced; the view direction and the near fade
// use each eye's own position (GetCameraPositionWS() is per eye).
Shader "TeaLeaf/MoonShaft"
{
    Properties
    {
        [Header(Light)]
        // Multiplies the main light's colour: white keeps the moon's own
        // colour, a cooler tint makes the dust look bluer than the light.
        _Tint ("Tint", Color) = (0.85, 0.9, 1.0, 1)

        // Overall brightness of the shafts, on top of the moon's intensity.
        _Intensity ("Intensity", Range(0, 4)) = 0.5

        // How much beam each metre of its thickness adds: the amount of
        // light seen through a sheet is 1 - exp(-density x thickness).
        // 0.08 makes a 3m-wide beam faint from the side and a 20m-long
        // one strong looked down.
        _Density ("Density (per metre)", Range(0.005, 0.5)) = 0.08

        [Header(Shape)]
        // How far in from each edge of a sheet the light fades out, as a
        // share of the sheet's width (0.5 fades all the way to the middle,
        // the softest; small keeps the edges crisp).
        _EdgeSoftness ("Edge Softness", Range(0.02, 0.5)) = 0.3

        // How the light dies away along the beam, from the opening (full)
        // to its far end (none): 1 is even, higher fades it sooner.
        _LengthFalloff ("Length Falloff", Range(0.2, 4)) = 1.5

        // Within this distance of the eye, in metres, a sheet fades out,
        // so walking into a shaft never fills the view.
        _NearFade ("Near Fade (m)", Range(0, 5)) = 1.5

        [Header(Dust)]
        // The tiling noise the streaks and motes are read from
        // (FlameNoise.png: seamless value noise). Any seamless noise will do.
        [NoScaleOffset] _NoiseTex ("Noise", 2D) = "gray" {}

        // How much the light breaks into streaks along the beam (0 = an
        // even beam, 1 = full contrast).
        _Streaks ("Streaks", Range(0, 1)) = 0.5

        // How many times the streak noise repeats across a sheet.
        _StreakScale ("Streak Scale", Range(0.1, 4)) = 0.6

        // How fast the streaks drift across the beam, in sheets per second.
        _StreakDrift ("Streak Drift", Range(0, 0.5)) = 0.03

        // How much the motes drifting down the beam show (0 = none).
        _Motes ("Motes", Range(0, 1)) = 0.3

        // How many times the mote noise repeats across a sheet.
        _MoteScale ("Mote Scale", Range(0.5, 8)) = 3

        // How fast the motes drift along the beam, in beam lengths per second.
        _MoteSpeed ("Mote Speed", Range(0, 1)) = 0.06
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
            Name "MoonShaft"

            // SRPDefaultUnlit: URP draws this pass for any object in its
            // queue range - no lighting, shadows or depth prepass involved.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // Hidden by anything nearer, never writing depth itself. Both
            // faces drawn: the sheets are seen from inside when the player
            // stands in the beam.
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

            // The moon's colour times its intensity, linear: a global set by
            // MoonShaft from the Directional Light, outside the material
            // block since it belongs to the scene, not to a material.
            half4 _MoonShaftLight;

            // Every material property in one block, so the SRP Batcher can
            // draw all shafts sharing this shader together.
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Intensity;
                float _Density;
                float _EdgeSoftness;
                float _LengthFalloff;
                float _NearFade;
                float _Streaks;
                float _StreakScale;
                float _StreakDrift;
                float _Motes;
                float _MoteScale;
                float _MoteSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;

                // The sheet's normal (flat: every vertex of a sheet has
                // the sheet's own).
                float3 normalOS : NORMAL;

                // Where on its sheet this vertex is, 0..1 each way. A cap's
                // corners run round its square; a side's run 0..1 across
                // it, with y held at 0.5, so only its across edges fade
                // (the length fade softens it along the beam).
                float2 sheet : TEXCOORD0;

                // x = how far along the beam (0 at the opening, 1 at the
                // far end); y = the beam's thickness behind this sheet, in
                // metres.
                float2 beam : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;

                // xy = where on the sheet, z = along the beam,
                // w = thickness behind the sheet.
                float4 shaft : TEXCOORD2;
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
                output.shaft = float4(input.sheet, input.beam);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 sheet = input.shaft.xy;
                float along = input.shaft.z;
                float thickness = input.shaft.w;

                // The eye's view of this point: how far away, and how
                // square on to the sheet.
                float3 toEye = GetCameraPositionWS() - input.positionWS;
                float eyeDistance = length(toEye);
                float3 viewDir = toEye / max(eyeDistance, 0.0001);
                float facing = abs(dot(viewDir, normalize(input.normalWS)));

                // How much beam this sheet stands for from here: its
                // thickness, seen square on; nothing seen edge on.
                float seen = 1.0 - exp(-_Density * thickness * facing);

                // Soft towards the sheet's edges (how far in from each
                // edge, as a share of the sheet), dying away along the
                // beam's length, and faded out right by the eye.
                float2 fromEdge = min(sheet, 1.0 - sheet);
                float2 edge = smoothstep(0.0, _EdgeSoftness, fromEdge);
                float profile = edge.x * edge.y;
                float lengthFade = pow(saturate(1.0 - along), _LengthFalloff);
                float nearFade = saturate(eyeDistance / max(_NearFade, 0.01));
                nearFade *= nearFade;

                // Streaks: a slow-drifting pattern fixed across the sheet,
                // so on a side each streak runs the length of the beam.
                float2 streakUV = sheet * _StreakScale + _Time.y * _StreakDrift * float2(1.0, 0.37);
                float streakNoise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, streakUV).r;
                float streaks = 1.0 + (streakNoise * 2.0 - 1.0) * _Streaks;

                // Motes: a finer pattern sliding down the beam.
                float moteRepeat = _MoteScale * 3.0;
                float2 moteUV = float2(sheet.x * _MoteScale, along * moteRepeat - _Time.y * _MoteSpeed * moteRepeat);
                float moteNoise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, moteUV).r;
                float motes = 1.0 + (moteNoise * 2.0 - 1.0) * _Motes;

                float amount = seen * profile * lengthFade * nearFade * streaks * motes;
                half3 color = _MoonShaftLight.rgb * _Tint.rgb * _Intensity * amount;
                return half4(color, 0.0);
            }
            ENDHLSL
        }
    }
}
