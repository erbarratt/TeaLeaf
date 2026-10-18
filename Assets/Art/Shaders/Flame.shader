// A small flame that burns in one place: a lamp post's wick, a wall torch,
// a brazier. One quad per flame, nothing else - no particles, no
// per-frame script. The quad turns to face the eye (around the up axis
// only, so the flame always stands upright), and the flame's shape and
// movement are painted onto it by this shader from a tiling noise
// texture, scrolled upwards: the noise bends the texture coordinates
// before a teardrop shape is drawn, so the shape ripples and licks like a
// flame rather than playing a looping picture. Each flame starts at a
// different point in the noise (its world position decides where), so no
// two in view move together.
//
// Why a quad and a shader rather than a particle system: a static mesh is
// drawn by the SRP Batcher in one batch with every other flame in view and
// costs nothing on the CPU; a particle system is a draw call and a
// simulation per lamp. In a headset a camera-facing sprite is still a
// sprite, but a flame is a soft, bright, moving shape with no edges or
// texture detail to give the flatness away, and lamp flames are seen from
// a few metres at head height - the view a single plane handles.
//
// Cost: two texture samples a pixel over a few thousand pixels per flame.
// Nothing here reads the depth or opaque texture, and nothing is lit:
// the flame outputs its own colour. The level's light from a flame is the
// point light beside it.
//
// Stereo: the quad is built from each eye's own position
// (GetCameraPositionWS() is per eye in single-pass instanced rendering).
// The two eyes therefore see the plane turned about a degree apart,
// which on a soft shape this size is not visible.
Shader "TeaLeaf/Flame"
{
    Properties
    {
        [Header(Shape)]
        // How wide the flame is at its widest, as a fraction of the quad.
        _Width ("Width", Range(0.1, 1)) = 0.6

        // Where up the flame the widest point is (0 = base, 1 = tip).
        _Belly ("Belly Height", Range(0.05, 0.6)) = 0.25

        // How far the edge fades: small = crisp, large = a soft glow.
        _Softness ("Edge Softness", Range(0.02, 1)) = 0.35

        [Header(Colour)]
        // The hot heart of the flame and its outer skin. Colour is picked by
        // how deep inside the shape a pixel is.
        _InnerColor ("Inner Colour", Color) = (1.0, 0.92, 0.55, 1)
        _OuterColor ("Outer Colour", Color) = (1.0, 0.35, 0.05, 1)

        // How quickly the inner colour gives way to the outer: higher keeps
        // the bright heart small.
        _CoreSharpness ("Core Sharpness", Range(0.5, 6)) = 2.5

        // Overall brightness.
        _Intensity ("Intensity", Range(0, 3)) = 1.2

        // How much the flame covers what is behind it (0 = pure glow added
        // on top, 1 = a solid shape). Fire is mostly glow.
        _Cover ("Cover", Range(0, 1)) = 0.6

        [Header(Movement)]
        // The tiling noise the flame is bent by (FlameNoise.png: value
        // noise that repeats seamlessly). Any seamless noise will do.
        [NoScaleOffset] _NoiseTex ("Noise", 2D) = "gray" {}

        // How many times the noise repeats across the quad: more = finer
        // ripples.
        _NoiseScale ("Noise Scale", Range(0.5, 8)) = 2

        // How fast the noise rises, in quad heights per second.
        _ScrollSpeed ("Scroll Speed", Range(0, 4)) = 1.2

        // How far the noise bends the shape, as a fraction of the quad.
        // The tip is bent fully, the base hardly at all - a flame is
        // anchored at the wick.
        _Distortion ("Distortion", Range(0, 0.5)) = 0.14

        [Header(Flicker)]
        // How much the whole flame's brightness wavers (0.2 = 20% either way).
        _Flicker ("Flicker", Range(0, 1)) = 0.25

        // How much its height wavers with it.
        _FlickerStretch ("Flicker Stretch", Range(0, 1)) = 0.25

        // How fast it flickers.
        _FlickerSpeed ("Flicker Speed", Range(0, 10)) = 3
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
            Name "Flame"

            // SRPDefaultUnlit: URP draws this pass for any object in its
            // queue range - no lighting, shadows or depth prepass involved.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // Hidden by anything nearer, never writing depth itself. Both
            // faces drawn: the quad is turned by the shader, and the
            // vertex order can come out either way round.
            ZTest LEqual
            ZWrite Off
            Cull Off

            // "Premultiplied" blending: result = ours + theirs * (1 - alpha).
            // The colour is already scaled by the shape, so where the
            // flame is faint it adds a little glow, and where it is strong
            // it covers what is behind by _Cover.
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Single-pass instanced stereo: each eye is an instance.
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            // Every material property in one block, so the SRP Batcher can
            // draw all flames sharing this shader together.
            CBUFFER_START(UnityPerMaterial)
                float _Width;
                float _Belly;
                float _Softness;
                half4 _InnerColor;
                half4 _OuterColor;
                float _CoreSharpness;
                float _Intensity;
                float _Cover;
                float _NoiseScale;
                float _ScrollSpeed;
                float _Distortion;
                float _Flicker;
                float _FlickerStretch;
                float _FlickerSpeed;
            CBUFFER_END

            struct Attributes
            {
                // Unity's built-in Quad: 1 x 1 in the XY plane, centred on
                // the origin. Only its xy and uv are used; the shader
                // decides where the corners go in the world.
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;

                // xy = where on the flame (x across, y up, both 0..1);
                // z = this flame's own offset into the noise;
                // w = this moment's flicker brightness.
                float4 flame : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // The flame's base is the object's position in the world;
                // its width and height are the object's x and y scale.
                float3 base = UNITY_MATRIX_M._m03_m13_m23;
                float width = length(UNITY_MATRIX_M._m00_m10_m20);
                float height = length(UNITY_MATRIX_M._m01_m11_m21);

                // Where in the noise this flame lives: a number from its
                // position that is different for every flame but the same
                // every frame. Hashed from the ground position only, so
                // two flames at different heights on one post still differ
                // by x and z, and the result is steady for positions well
                // within a level's size.
                float phase = frac(sin(dot(base.xz, float2(12.9898, 78.233))) * 43758.5453);

                // Flicker: one noise sample, slowly travelling, read once
                // per vertex (all four corners read the same spot, so the
                // whole flame brightens and stretches together).
                float2 flickerUV = float2(_Time.y * _FlickerSpeed * 0.05 + phase, phase * 3.1);
                float flicker = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, flickerUV, 0).r - 0.5;
                float brightness = 1.0 + flicker * 2.0 * _Flicker;
                height *= 1.0 + flicker * 2.0 * _FlickerStretch;

                // Face the eye, turning around the up axis only: a flame
                // stands upright however it is looked at. From directly
                // above there is no sideways direction to turn to, so one
                // is made up.
                float3 toEye = GetCameraPositionWS() - base;
                toEye.y = 0.0;
                float3 right = dot(toEye, toEye) > 0.0001 ? normalize(float3(toEye.z, 0.0, -toEye.x)) : float3(1.0, 0.0, 0.0);

                // The quad's corners run -0.5..0.5 each way; the flame's
                // base is its bottom edge.
                float3 positionWS = base + right * (input.positionOS.x * width) + float3(0.0, (input.positionOS.y + 0.5) * height, 0.0);
                output.positionCS = TransformWorldToHClip(positionWS);

                output.flame = float4(input.uv, phase, brightness);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.flame.xy;
                float phase = input.flame.z;

                // Two layers of noise rising at different speeds and sizes,
                // so the movement never repeats visibly. The flame's own
                // phase shifts both, so neighbouring flames differ.
                float rise = _Time.y * _ScrollSpeed;
                float2 noiseUV1 = uv * _NoiseScale + float2(phase * 5.0, -rise + phase * 13.0);
                float2 noiseUV2 = uv * _NoiseScale * 1.9 + float2(0.37 - phase * 3.0, -rise * 1.6 + phase * 7.0);
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV1).r * 0.65
                            + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV2).r * 0.35;

                // Bend the coordinates by the noise (centred, so it bends as
                // much one way as the other), more towards the tip: the base
                // is anchored at the wick.
                float bend = _Distortion * (uv.y * 0.85 + 0.15);
                float2 bent = uv + (noise - 0.5) * bend * float2(1.0, 1.4);

                // The teardrop: how wide the flame is at this height. Full
                // width at the belly, narrowing to a point at the tip and
                // rounding off at the base.
                float y = saturate(bent.y);
                float belly = _Belly;
                float profile = y < belly
                    ? sqrt(saturate(y / belly))
                    : pow(saturate((1.0 - y) / (1.0 - belly)), 0.65);
                float halfWidth = 0.5 * _Width * profile;

                // How far out from the centre line this pixel is, as a
                // fraction of the width here: 0 on the centre line, 1 at
                // the edge, beyond 1 outside.
                float across = abs(bent.x - 0.5) / max(halfWidth, 0.001);

                // Empty above the tip and below the base.
                float inside = step(0.0, bent.y) * step(bent.y, 1.0);

                // The soft edge: full inside, fading out over _Softness of
                // the half width.
                half body = smoothstep(0.0, _Softness, 1.0 - across) * inside;

                // The bright heart: rounded across (so there is no sharp
                // line down the middle), and sitting low in the flame -
                // the hottest part is just above the wick, and the tip is
                // all outer colour.
                half heart = pow(saturate(1.0 - across * across), _CoreSharpness) * smoothstep(1.0, 0.25, y);
                half3 color = lerp(_OuterColor.rgb, _InnerColor.rgb, heart) * _Intensity * input.flame.w;

                return half4(color * body, body * _Cover);
            }
            ENDHLSL
        }
    }
}
