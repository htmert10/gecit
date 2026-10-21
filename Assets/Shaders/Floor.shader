// Teras zemini: sıcak tonlu ahşap tahtalar (her tahta hafif farklı ton), aralarında ince koyu çizgi,
// kenara doğru hafif kararır.
Shader "Gecit/Floor"
{
    Properties
    {
        _Base ("Ahsap", Color) = (0.46, 0.33, 0.23, 1)
        _Line ("Ara", Color) = (0.22, 0.15, 0.10, 1)
        _Center ("Merkez", Vector) = (0, 0, 0, 0)
        _Radius ("Yaricap", Float) = 3.2
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Base, _Line;
            float4 _Center;
            float _Radius;

            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float hash(float n) { return frac(sin(n) * 43758.5453); }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.wpos.xz - _Center.xz;
                float plank = floor(p.x / 0.16);
                float seg = floor((p.y + hash(plank) * 3.0) / 1.4);
                float tone = 0.85 + 0.3 * hash(plank * 7.13 + seg * 3.7);
                float grain = 0.95 + 0.05 * sin(p.y * 40.0 + hash(plank) * 20.0);
                float gx = abs(frac(p.x / 0.16) - 0.5) * 0.16;
                float gy = abs(frac((p.y + hash(plank) * 3.0) / 1.4) - 0.5) * 1.4;
                float gap = smoothstep(0.003, 0.0, 0.08 - gx) + smoothstep(0.004, 0.0, 0.7 - gy);
                float3 col = lerp(_Base.rgb * tone * grain, _Line.rgb, saturate(gap));
                float r = length(p) / _Radius;
                col *= lerp(1.0, 0.75, smoothstep(0.7, 1.0, r));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
