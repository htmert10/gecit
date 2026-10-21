// Deniz (sahil ortamı): yakında turkuaz-mavi, uzakta ufkun gün batımı rengine karışır; dalgalı parıltılar ve
// güneşe doğru uzanan altın rengi yansıma şeridi.
Shader "Gecit/Sea"
{
    Properties
    {
        _SunDir ("Gunes Yonu", Vector) = (0, 0.05, 1, 0)
        _Shallow ("Sig", Color) = (0.10, 0.32, 0.42, 1)
        _Deep ("Derin", Color) = (0.05, 0.10, 0.22, 1)
        _Horizon ("Ufuk", Color) = (0.95, 0.55, 0.40, 1)
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
            float4 _SunDir;
            fixed4 _Shallow, _Deep, _Horizon;
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz; return o; }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 v = i.wpos - _WorldSpaceCameraPos;
                float dist = length(v.xz);
                float3 col = lerp(_Shallow.rgb, _Deep.rgb, saturate(dist / 60.0));
                // Dalgalar: kayan gürültü ile açık/koyu benekler
                float w = noise(i.wpos.xz * 0.6 + _Time.y * float2(0.25, 0.1)) * noise(i.wpos.xz * 1.7 - _Time.y * float2(0.1, 0.3));
                col *= 0.85 + 0.35 * w;
                // Güneşe doğru altın yansıma şeridi
                float2 sdir = normalize(_SunDir.xz);
                float along = saturate(dot(normalize(v.xz), sdir));
                float streak = pow(along, 300.0) * smoothstep(0.35, 0.75, w + 0.3) * saturate(dist / 20.0);
                col += float3(1.0, 0.65, 0.3) * streak * 1.6;
                col = lerp(col, _Horizon.rgb, pow(saturate(dist / 750.0), 1.6));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
