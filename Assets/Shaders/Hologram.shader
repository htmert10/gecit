// Holografik nesne (sanal dünyadaki oyun nesneleri): camgöbeğine kayan renk, kenarlarda parlak ışık (fresnel),
// yavaşça akan ince tarama çizgileri, yarı saydam. _Color nesnenin kendi rengi (MaterialPropertyBlock).
Shader "Gecit/Hologram"
{
    Properties
    {
        _Color ("Renk", Color) = (0.3, 0.8, 1, 1)
        _RimColor ("Kenar", Color) = (0.7, 0.95, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite On
            ColorMask 0
        }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color, _RimColor;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float3 n : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.n = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float rim = pow(1.0 - saturate(dot(n, v)), 2.0);
                float3 baseCol = lerp(_Color.rgb, float3(0.3, 0.85, 1.0), 0.45);
                float scan = 0.82 + 0.18 * sin(i.wp.y * 260.0 - _Time.y * 5.0);
                float3 col = baseCol * (0.45 + 0.55 * saturate(dot(n, normalize(float3(0.3, 0.9, -0.3))) * 0.5 + 0.5)) * scan
                           + float3(0.75, 0.95, 1.0) * rim * 0.8;
                float a = saturate((0.5 + 0.5 * rim) * scan);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
