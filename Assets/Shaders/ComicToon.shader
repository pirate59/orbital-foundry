// Comic-book cel shading for the battle-scene craft: three hard light bands, halftone dots in the shadow band,
// a rim highlight, a hard specular glint, optional glow, and a thick ink outline drawn as an inverted hull along
// smoothed normals (TEXCOORD3, falling back to the vertex normal). Lighting comes from _ToonLightDir, not the
// scene, so the portraits look the same wherever they are rendered.
Shader "OrbitalFoundry/ComicToon"
{
    Properties
    {
        _BaseColor ("Base", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade", Color) = (0.35, 0.3, 0.5, 1)
        _InkColor ("Ink", Color) = (0.07, 0.05, 0.1, 1)
        _OutlineWidth ("Outline width", Float) = 0.012
        _Glow ("Glow", Range(0, 1)) = 0
        _Gloss ("Gloss", Range(0, 1)) = 0.5
        _HalftoneCell ("Halftone cell (px)", Float) = 5
        _ToonLightDir ("Light direction", Vector) = (-0.45, 0.75, -0.5, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _ShadeColor, _InkColor;
            float _OutlineWidth, _Glow, _Gloss, _HalftoneCell;
            float4 _ToonLightDir;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ComicForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 l = normalize(_ToonLightDir.xyz);
                float3 v = normalize(i.viewWS);
                float ndl = dot(n, l);

                // Three bands: lit, mid and shade.
                float3 lit = _BaseColor.rgb;
                float3 mid = lerp(_BaseColor.rgb, _ShadeColor.rgb, 0.45);
                float3 shade = _ShadeColor.rgb;
                float3 col = ndl > 0.35 ? lit : (ndl > -0.15 ? mid : shade);

                // Halftone dots fade the mid band into the shade, like printed comic shading.
                float2 cell = i.positionCS.xy / _HalftoneCell;
                float2 f = frac(float2(cell.x + cell.y * 0.5, cell.y)) - 0.5;
                float darkness = saturate((0.45 - ndl) / 0.9);
                float dotMask = step(length(f), darkness * 0.62);
                col = lerp(col, shade * 0.82, dotMask * step(ndl, 0.35) * 0.65);

                // Hard specular glint and a bright rim on the lit side.
                float3 h = normalize(l + v);
                float spec = step(1.0 - _Gloss * 0.08, dot(n, h));
                col = lerp(col, float3(1, 1, 0.96), spec * 0.85);
                float rim = step(0.72, 1.0 - saturate(dot(n, v))) * step(0.0, ndl);
                col = lerp(col, lit * 1.25 + 0.08, rim * 0.5);

                col = lerp(col, _BaseColor.rgb * 1.6 + 0.15, _Glow);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ComicInk"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 smoothNormal : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 n = dot(i.smoothNormal, i.smoothNormal) > 0.01 ? i.smoothNormal : i.normalOS;
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(n));
                positionWS += normalWS * _OutlineWidth;
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _InkColor; }
            ENDHLSL
        }
    }
}
