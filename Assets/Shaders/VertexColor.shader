// Dağlar, vadi, ağaçlar, göl için: ışıksız köşe rengi; uzaklaştıkça ufuktaki pus rengine karışır
// (hava perspektifi: uzak dağlar soluk ve mavimsi görünür).
Shader "Gecit/VertexColor"
{
    Properties
    {
        _Fog ("Pus", Color) = (0.74, 0.78, 0.82, 1)
        _FogStart ("Pus Baslangic", Float) = 20
        _FogEnd ("Pus Bitis", Float) = 420
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

            fixed4 _Fog;
            float _FogStart, _FogEnd;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float dist : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.dist = length(UnityObjectToViewPos(v.vertex.xyz));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float f = saturate((i.dist - _FogStart) / (_FogEnd - _FogStart));
                f = 1.0 - (1.0 - f) * (1.0 - f);
                return fixed4(lerp(i.color.rgb, _Fog.rgb, f), 1);
            }
            ENDCG
        }
    }
}
