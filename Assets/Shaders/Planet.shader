// Uzay ortamındaki dev gezegen: enlem boyunca kıvrımlı renk kuşakları (gaz devi), güneş tarafı aydınlık,
// gece tarafı karanlık, kenarlarında mavimsi atmosfer parıltısı.
Shader "Gecit/Planet"
{
    Properties
    {
        _SunDir ("Gunes Yonu", Vector) = (0.6, 0.25, 0.75, 0)
        _ColorA ("Kusak 1", Color) = (0.86, 0.70, 0.50, 1)
        _ColorB ("Kusak 2", Color) = (0.58, 0.40, 0.30, 1)
        _Atmo ("Atmosfer", Color) = (0.55, 0.70, 1.0, 1)
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
            fixed4 _ColorA, _ColorB, _Atmo;
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 on : TEXCOORD1; float3 vd : TEXCOORD2; };
            v2f vert(appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal); o.on = v.normal;
                o.vd = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n), on = normalize(i.on), vd = normalize(i.vd);
                float lon = atan2(on.z, on.x);
                float lat = on.y + 0.08 * noise(float2(lon * 3.0, on.y * 8.0));
                float bands = 0.5 + 0.5 * sin(lat * 24.0 + 2.0 * noise(float2(lon * 2.0, lat * 4.0)));
                float3 col = lerp(_ColorB.rgb, _ColorA.rgb, bands);
                col = lerp(col, float3(0.9, 0.45, 0.3), smoothstep(0.9, 1.0, noise(float2(lon * 6.0, lat * 18.0))) * 0.5);   // fırtınalar
                float light = saturate(dot(n, normalize(_SunDir.xyz)));
                col *= 0.04 + 0.96 * light;
                float rim = pow(1.0 - saturate(dot(n, vd)), 3.0);
                col += _Atmo.rgb * rim * (0.15 + 0.85 * light);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
