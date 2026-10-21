// Uzay gökyüzü: simsiyah, yoğun yıldızlar (iki katman, titreşir), renkli bulutsu (mor-mavi-pembe),
// uzakta parlak bir güneş.
Shader "Gecit/SkySpace"
{
    Properties { _SunDir ("Gunes Yonu", Vector) = (0.6, 0.25, 0.75, 0) }
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
            float hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p); float3 u = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(hash3(i), hash3(i + float3(1,0,0)), u.x), lerp(hash3(i + float3(0,1,0)), hash3(i + float3(1,1,0)), u.x), u.y);
                float b = lerp(lerp(hash3(i + float3(0,0,1)), hash3(i + float3(1,0,1)), u.x), lerp(hash3(i + float3(0,1,1)), hash3(i + float3(1,1,1)), u.x), u.y);
                return lerp(a, b, u.z);
            }
            float fbm3(float3 p) { float v = 0.0, a = 0.5; for (int k = 0; k < 4; k++) { v += a * noise3(p); p *= 2.07; a *= 0.5; } return v; }
            float stars(float3 d, float scale, float th)
            {
                float3 p = d * scale; float3 c = floor(p); float r = hash3(c); float3 f = frac(p) - 0.5;
                return step(th, r) * smoothstep(0.2, 0.0, length(f)) * (0.6 + 0.4 * sin(_Time.y * (1.0 + r * 3.0) + r * 50.0));
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 col = float3(0.005, 0.006, 0.015);
                // Bulutsu
                float n = fbm3(d * 2.2 + 3.0);
                float n2 = fbm3(d * 4.5 - 7.0);
                float neb = smoothstep(0.45, 0.85, n) * (0.6 + 0.4 * n2);
                float3 nebCol = lerp(float3(0.25, 0.08, 0.40), float3(0.05, 0.25, 0.55), saturate(n2 * 1.4));
                nebCol = lerp(nebCol, float3(0.65, 0.18, 0.35), smoothstep(0.6, 0.9, fbm3(d * 3.1 + 11.0)));
                col += nebCol * neb * 0.55;
                // Yıldızlar
                col += stars(d, 280.0, 0.975) * float3(0.95, 0.97, 1.0);
                col += stars(d, 600.0, 0.982) * 0.55 * float3(0.8, 0.85, 1.0);
                col += stars(d, 120.0, 0.994) * 1.4 * float3(1.0, 0.95, 0.85);
                // Güneş
                float s = saturate(dot(d, normalize(_SunDir.xyz)));
                col += float3(1.0, 0.95, 0.85) * (pow(s, 4000.0) * 6.0 + pow(s, 200.0) * 0.6 + pow(s, 20.0) * 0.08);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
