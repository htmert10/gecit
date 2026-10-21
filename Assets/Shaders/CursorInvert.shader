// İmleç: arka plana göre renk değiştiren nokta. Altındaki rengi ters çevirir: beyaz sayfada koyu, karanlıkta
// açık görünür. Gri zeminde kaybolmasın diye etrafına ince koyu yarı saydam bir halka çizilir.
// Doku: R = nokta, G = kenar halkası. Her zaman en üstte.
Shader "Gecit/CursorInvert"
{
    Properties { _MainTex ("Doku", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Overlay+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Off

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
        v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }
        ENDCG

        // 1) Kenar halkası: koyu, yarı saydam (gri zeminde de görünür). Basılıyken renk (mavi) burada.
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                float ring = tex2D(_MainTex, i.uv).g;
                float isWhite = step(2.97, i.color.r + i.color.g + i.color.b);   // beyaz = normal (koyu halka), başka renk = basılı
                fixed3 col = lerp(i.color.rgb, fixed3(0.1, 0.1, 0.12), isWhite);
                return fixed4(col, ring * 0.85);
            }
            ENDCG
        }

        // 2) Nokta: altındaki rengi ters çevir (beyaz -> koyu, siyah -> açık)
        Pass
        {
            Blend OneMinusDstColor OneMinusSrcColor
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).r * 0.92;
                return fixed4(a, a, a, a);
            }
            ENDCG
        }
    }
}
