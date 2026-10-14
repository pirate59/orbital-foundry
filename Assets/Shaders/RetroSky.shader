// Deep-space backdrop for the 3D board: a warm horizon band between deep teal space above and below,
// a soft glow around the sun, and a hashed star field so no texture is needed.
Shader "OrbitalFoundry/RetroSky"
{
    Properties
    {
        _Top ("Zenith", Color) = (0.03, 0.07, 0.13, 1)
        _Horizon ("Horizon band", Color) = (0.30, 0.16, 0.24, 1)
        _Bottom ("Nadir", Color) = (0.015, 0.02, 0.05, 1)
        _Glow ("Sun glow", Color) = (1, 0.62, 0.32, 1)
        _SunDir ("Sun direction", Vector) = (0.4, 0.5, 0.3, 0)
        _StarThreshold ("Star threshold", Range(0.98, 0.9999)) = 0.9965
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Horizon, _Bottom, _Glow;
                float4 _SunDir;
                float _StarThreshold;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 col = h > 0
                    ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(h), 0.45))
                    : lerp(_Horizon.rgb, _Bottom.rgb, pow(saturate(-h), 0.3));

                float s = saturate(dot(d, normalize(_SunDir.xyz)));
                col += _Glow.rgb * (pow(s, 6) * 0.25 + pow(s, 400) * 2.0);

                // Two layers of stars: sparse bright ones and a fine dust.
                float3 g = d * 180.0;
                float r = Hash(floor(g));
                if (r > _StarThreshold)
                {
                    float3 f = frac(g) - 0.5;
                    float core = saturate(1.0 - length(f) * 2.4);
                    float bright = (r - _StarThreshold) / (1.0 - _StarThreshold);
                    float3 tint = lerp(float3(0.75, 0.85, 1.0), float3(1.0, 0.85, 0.6), frac(r * 97.0));
                    col += tint * core * core * (0.6 + bright * 2.2);
                }
                float dust = Hash(floor(d * 520.0));
                if (dust > 0.997) col += 0.18;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
