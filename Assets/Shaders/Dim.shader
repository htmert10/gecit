// Sinema modu karartması: başın etrafındaki büyük kürenin iç yüzü. Pencerelerden önce çizilir; kürenin
// ötesindeki her şey (oda görüntüsü, sanal dünyanın uzakları) kararır, önündeki perde (pencere) etkilenmez.
Shader "Gecit/Dim"
{
    Properties { _Color ("Renk", Color) = (0, 0, 0, 0.9) }
    SubShader
    {
        Tags { "Queue" = "Transparent-20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest LEqual
        Cull Front
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
