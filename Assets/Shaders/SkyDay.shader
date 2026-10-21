// Gündüz gökyüzü (öğleden sonra): mavi tepe, sıcak ufuk, güneş ve parıltısı, yavaşça kayan bulutlar.
Shader "Gecit/SkyDay"
{
    Properties
    {
        _Top ("Ust", Color) = (0.30, 0.50, 0.82, 1)
        _Horizon ("Ufuk", Color) = (0.86, 0.84, 0.80, 1)
        _Bottom ("Alt", Color) = (0.55, 0.62, 0.66, 1)
        _SunDir ("Gunes Yonu", Vector) = (0.45, 0.32, 0.83, 0)
        _SunColor ("Gunes", Color) = (1.0, 0.92, 0.78, 1)
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
            fixed4 _Top, _Horizon, _Bottom, _SunColor;
            float4 _SunDir;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p) { float v = 0.0, a = 0.5; for (int k = 0; k < 5; k++) { v += a * noise(p); p *= 2.03; a *= 0.5; } return v; }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir); float h = d.y;
                float3 col = h > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(h), 0.55)) : lerp(_Horizon.rgb, _Bottom.rgb, pow(saturate(-h), 0.4));
                float3 sd = normalize(_SunDir.xyz); float s = saturate(dot(d, sd));
                col += _SunColor.rgb * (pow(s, 900.0) * 3.0 + pow(s, 12.0) * 0.35 + pow(s, 3.0) * 0.08);
                if (h > 0.02)
                {
                    float2 uv = d.xz / (h + 0.12) * 1.6 + float2(_Time.y * 0.004, _Time.y * 0.002);
                    float cover = smoothstep(0.52, 0.78, fbm(uv * 1.3)) * smoothstep(0.02, 0.25, h);
                    float3 cloudCol = lerp(float3(0.78, 0.80, 0.85), float3(1.0, 0.98, 0.95), saturate(fbm(uv * 2.6 + 3.0) + s * 0.4));
                    col = lerp(col, cloudCol, cover * 0.85);
                }
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
