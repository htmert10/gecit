// Tarama blokları için hafif, ışıksız malzeme (yüze göre basit gölgelendirme).
Shader "Gecit/Block"
{
    Properties
    {
        _Color ("Renk", Color) = (0, 0.85, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed shade : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = UnityObjectToWorldNormal(v.normal);
                o.shade = 0.55 + 0.45 * saturate(dot(n, normalize(float3(0.3, 0.8, 0.5))));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return fixed4(_Color.rgb * i.shade, 1); }
            ENDCG
        }
    }
}
