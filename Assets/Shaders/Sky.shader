// Sanal dünya gökyüzü (gece): koyu lacivert gradyan, ufukta hafif mavi ışıma, yıldızlar (titreşir),
// samanyolu kuşağı, parlak ay ve halesi, ay ışığında belli belirsiz bulutlar.
Shader "Gecit/Sky"
{
    Properties
    {
        _Top ("Ust", Color) = (0.010, 0.016, 0.045, 1)
        _Horizon ("Ufuk", Color) = (0.07, 0.10, 0.18, 1)
        _Bottom ("Alt", Color) = (0.02, 0.025, 0.04, 1)
        _MoonDir ("Ay Yonu", Vector) = (0.45, 0.32, 0.83, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Top, _Horizon, _Bottom;
            float4 _MoonDir;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            float stars(float3 d, float scale, float threshold)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float r = hash3(cell);
                float3 f = frac(p) - 0.5;
                float s = step(threshold, r) * smoothstep(0.18, 0.0, length(f));
                float tw = 0.6 + 0.4 * sin(_Time.y * (1.3 + r * 3.0) + r * 60.0);
                return s * tw;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 col = h > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(h), 0.5))
                                   : lerp(_Horizon.rgb, _Bottom.rgb, pow(saturate(-h), 0.4));

                // Samanyolu: eğik bir kuşak boyunca yoğun, soluk yıldızlar ve hafif ışıma
                float band = exp(-pow(dot(d, normalize(float3(0.3, 0.55, -0.78))) * 4.0, 2.0));
                col += float3(0.10, 0.11, 0.16) * band * smoothstep(0.0, 0.4, h) * (0.6 + 0.4 * fbm(d.xz * 6.0));

                float vis = smoothstep(0.02, 0.3, h);
                col += stars(d, 260.0, 0.982) * vis * float3(0.95, 0.97, 1.0);
                col += stars(d, 520.0, 0.985 - band * 0.03) * vis * 0.55 * float3(0.8, 0.85, 1.0);

                // Ay + hale
                float3 md = normalize(_MoonDir.xyz);
                float m = dot(d, md);
                col += smoothstep(0.99955, 0.99970, m) * float3(1.0, 0.98, 0.9) * 1.2;
                col += pow(saturate(m), 300.0) * float3(0.25, 0.27, 0.35);
                col += pow(saturate(m), 12.0) * float3(0.04, 0.05, 0.08);

                // Ay ışığında soluk bulutlar
                if (h > 0.03)
                {
                    float2 uv = d.xz / (h + 0.15) * 1.4 + float2(_Time.y * 0.003, 0.0);
                    float c = smoothstep(0.55, 0.85, fbm(uv * 1.2)) * smoothstep(0.03, 0.3, h);
                    col = lerp(col, float3(0.10, 0.12, 0.18) + pow(saturate(m), 6.0) * 0.12, c * 0.6);
                }
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
