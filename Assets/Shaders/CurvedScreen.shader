// Kavisli sinema perdesi: tarayıcının görüntüsü (ışıksız), kenarlara doğru çok hafif kararır (perde hissi).
Shader "Gecit/CurvedScreen"
{
    Properties
    {
        _MainTex ("Görüntü", 2D) = "black" {}
        _Bright ("Parlaklık", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Bright;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 raw : TEXCOORD1; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv, _MainTex); o.raw = v.uv; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 col = tex2D(_MainTex, i.uv).rgb * _Bright;
                float2 e = min(i.raw, 1.0 - i.raw);
                float edge = saturate(min(e.x * 40.0, e.y * 25.0));   // en dışta ince yumuşak kenar
                return fixed4(col * lerp(0.0, 1.0, edge), 1);
            }
            ENDCG
        }
    }
}
