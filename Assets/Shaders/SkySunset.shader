// Gün batımı gökyüzü (sahil): üstte lacivert, ortada mor-pembe, ufukta turuncu-sarı; ufka yakın büyük güneş
// ve parıltısı, güneş ışığıyla pembe-turuncu aydınlanan bulutlar.
Shader "Gecit/SkySunset"
{
    Properties { _SunDir ("Gunes Yonu", Vector) = (0, 0.05, 1, 0) }
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
                float3 sd = normalize(_SunDir.xyz);
                float s = saturate(dot(d, sd));
                float3 top = float3(0.10, 0.13, 0.32), mid = float3(0.55, 0.30, 0.48), low = float3(1.0, 0.55, 0.28), below = float3(0.35, 0.22, 0.25);
                float3 col = h > 0 ? (h < 0.25 ? lerp(low, mid, h / 0.25) : lerp(mid, top, saturate((h - 0.25) / 0.6))) : lerp(low * 0.8, below, saturate(-h * 4.0));
                col += float3(1.0, 0.6, 0.25) * pow(s, 6.0) * 0.45;              // güneş tarafı daha sıcak
                col += float3(1.0, 0.85, 0.5) * (pow(s, 1500.0) * 4.0 + pow(s, 60.0) * 0.5);   // güneş + hale
                if (h > 0.01)
                {
                    float2 uv = d.xz / (h + 0.08) * 1.2 + float2(_Time.y * 0.003, 0.0);
                    float c = smoothstep(0.5, 0.8, fbm(uv * 1.1)) * smoothstep(0.01, 0.2, h);
                    float3 cloudCol = lerp(float3(0.45, 0.25, 0.40), float3(1.0, 0.55, 0.45), pow(s, 3.0) + 0.2);
                    col = lerp(col, cloudCol, c * 0.75);
                }
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
