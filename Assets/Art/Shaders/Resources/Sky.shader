// The night sky's stars and moon: flat discs drawn at "infinity". Used by
// ProceduralSky, which builds the meshes this shader draws. There is no
// skybox: the camera clears to black and these are drawn over it.
//
// Each disc is a quad (two triangles). The mesh doesn't store where its
// corners are - it stores the direction of the disc's centre, which corner
// each vertex is, and how big the disc is, and this shader works the corner
// out. That lets it do two things a fixed mesh couldn't:
//
//  - Lock the sky to the camera. Corners are placed relative to the eye's
//    own position, so walking never moves a star (no parallax), and both
//    eyes see it in exactly the same direction - which is what an object
//    infinitely far away looks like in stereo.
//  - Keep stars from shimmering. A star smaller than a pixel flickers as
//    the head moves, so it is never drawn smaller than _MinPixelRadius; it
//    is made dimmer instead, by as much as it was enlarged.
//
// Both are per vertex (four per star), so they cost next to nothing.
//
// Nothing here is lit or lights anything: it only outputs the colours it is
// given. How bright the level is comes from the Directional Light and the
// ambient setting.
//
// In the Resources folder so it's always included in builds - otherwise
// Shader.Find() only works in the editor.
Shader "TeaLeaf/Sky"
{
    Properties
    {
        // The smallest radius a star is drawn at, in screen pixels.
        _MinPixelRadius ("Min Pixel Radius", Float) = 0.75

        // The moon's face (a picture, set by ProceduralSky), which the
        // disc's colour is multiplied by; its scale and offset pick the
        // square of the picture the moon fills. Only read when the
        // _SURFACE_TEXTURE keyword is on: the moon's material, not the
        // stars'.
        _SurfaceTex ("Surface Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"

            // After all solid geometry, so stars behind a building are
            // thrown away by the depth test before they cost anything, and
            // before every other see-through thing, which belongs in front.
            "Queue" = "Transparent-400"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Sky"

            // SRPDefaultUnlit: URP draws this pass for any object in its
            // queue range - no lighting, shadows or depth prepass involved.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // Hidden by anything nearer, never writing depth itself.
            ZTest LEqual
            ZWrite Off
            Cull Off

            // "Premultiplied" blending: result = ours + theirs * (1 - alpha).
            // With alpha 0 that is plain adding (a star brightens what is
            // behind it); with alpha 1 it replaces it (the moon covers the
            // stars behind it). One shader does both, picked per vertex.
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Single-pass instanced stereo, as in the Overlay shader: each
            // eye is an instance.
            #pragma multi_compile_instancing

            // Two versions of the shader: plain discs (stars) and discs
            // with a picture on them (the moon). multi_compile rather
            // than shader_feature, which would leave the second version
            // out of a build - no saved material asks for it, the moon's
            // material being made in code.
            #pragma multi_compile_local _ _SURFACE_TEXTURE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SurfaceTex);
            SAMPLER(sampler_SurfaceTex);

            CBUFFER_START(UnityPerMaterial)
                float _MinPixelRadius;
                float4 _SurfaceTex_ST;
            CBUFFER_END

            struct Attributes
            {
                // The direction of the disc's centre (unit length), the
                // same for all four of its vertices.
                float4 positionOS : POSITION;

                // rgb = colour (linear, already times brightness);
                // a = 0 to add to what's behind, 1 to cover it.
                half4 color : COLOR;

                // xy = which corner (-1 or 1 each way), z = the disc's
                // radius (at 1m away, so near enough radians).
                float3 corner : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Only the object's rotation is used (that is how the moon
                // is aimed); its position is ignored.
                float3 direction = TransformObjectToWorldDir(input.positionOS.xyz);

                float radius = input.corner.z;
                half brightness = 1.0;

                // How many pixels the radius covers: the projection matrix
                // turns "radius at 1m" into a fraction of half the screen's
                // height.
                float pixelRadius = radius * abs(UNITY_MATRIX_P._m11) * _ScreenParams.y * 0.5;

                // Too small to draw steadily: draw it at the minimum size,
                // dimmed by how much its area grew, so it gives the same
                // light overall.
                if (pixelRadius < _MinPixelRadius)
                {
                    float shrink = pixelRadius / _MinPixelRadius;
                    radius /= max(shrink, 0.0001);
                    brightness = shrink * shrink;
                }

                // Two directions at right angles to the star's, to lay the
                // quad out across the line of sight: right level, up as
                // near the sky's up as it can be, so the moon's picture
                // stands upright.
                float3 reference = abs(direction.y) > 0.999 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
                float3 right = normalize(cross(reference, direction));
                float3 up = cross(direction, right);
                float3 cornerDirection = direction + (right * input.corner.x + up * input.corner.y) * radius;

                // 1m from this eye, in the star's direction: locked to the
                // camera (see the top of the file).
                output.positionCS = TransformWorldToHClip(_WorldSpaceCameraPos + cornerDirection);

                // Then pushed to the very back of the depth range, so the
                // 1m doesn't put it in front of anything. (Unity stores
                // depth backwards on most platforms: far is 0.)
                #if UNITY_REVERSED_Z
                    output.positionCS.z = 0.0;
                #else
                    output.positionCS.z = output.positionCS.w;
                #endif

                output.color = half4(input.color.rgb * brightness, input.color.a);
                output.uv = input.corner.xy;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Distance from the disc's centre: 0 in the middle, 1 at
                // its edge (the quad's corners are beyond that, and empty).
                float distanceFromCentre = length(input.uv);

                // fwidth() is how much that distance changes from this
                // pixel to the next, so the edge fades over one pixel:
                // smooth, but as sharp as the display allows.
                float edge = max(fwidth(distanceFromCentre), 0.0001);
                half coverage = saturate((1.0 - distanceFromCentre) / edge);

                half3 color = input.color.rgb;

                // The quad's corners run -1 to 1 and the picture 0 to 1,
                // then into the square the picture's moon fills. The
                // picture's own see-through edge trims the disc too.
                #if defined(_SURFACE_TEXTURE)
                    float2 pictureUV = input.uv * 0.5 + 0.5;
                    half4 surface = SAMPLE_TEXTURE2D(_SurfaceTex, sampler_SurfaceTex, TRANSFORM_TEX(pictureUV, _SurfaceTex));
                    color *= surface.rgb * surface.a;
                    coverage *= surface.a;
                #endif

                return half4(color * coverage, input.color.a * coverage);
            }
            ENDHLSL
        }
    }
}
