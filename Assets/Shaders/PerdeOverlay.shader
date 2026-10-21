// Ekrandaki gerçek elin tam üstüne (oda resmiyle aynı düzlemde) çizilen, her zaman üstte duran çizgiler
// (eli saran halka). Düz ekranda da gözlük modunda da elin üstüne oturur.
Shader "Gecit/PerdeOverlay"
{
    Properties { _MainTex ("Doku", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "GecitHand.cginc"

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = PerdeClip(mul(unity_ObjectToWorld, v.vertex).xyz, 0.0);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
