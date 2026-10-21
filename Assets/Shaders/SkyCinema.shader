// Özel sinema salonu gökyüzü: tam karanlık, perdenin olduğu yönde (önde, _Front) perdeden yansıyan yumuşak
// mavimsi ışık, yukarı doğru siyaha kararır.
Shader "Gecit/SkyCinema"
{
    Properties
    {
        _Front ("Perde yönü", Vector) = (0, 0, 1, 0)
        _Glow ("Perde ışığı", Color) = (0.10, 0.12, 0.18, 1)
        _Base ("Karanlık", Color) = (0.010, 0.011, 0.016, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Front;
            fixed4 _Glow, _Base;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float front = saturate(dot(d, normalize(_Front.xyz)));
                float glow = pow(front, 6.0) * (1.0 - saturate(d.y * 1.5));
                float3 col = _Base.rgb + _Glow.rgb * glow;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
