// Meta tarzı eldiven: yarı saydam gövde + parlak kenar (fresnel). Oda resmiyle aynı düzlemde çizilir
// (GecitHand.cginc) ki iki gözde de gerçek elin tam üstüne otursun. El maskesinin hemen ardından,
// pencerelerden önce çizilir; derinliği biraz öne çekilir. İlk geçiş sadece derinlik yazar,
// böylece üst üste binen parçalar tek bir el gibi görünür.
Shader "Gecit/Glove"
{
    Properties
    {
        _Color ("Govde", Color) = (0.55, 0.62, 0.75, 0.12)
        _RimColor ("Kenar", Color) = (0.85, 0.93, 1.0, 0.9)
        _RimPower ("Kenar Inceligi", Float) = 2.2
        _Pull ("One Cekme (m)", Float) = 0.016
    }
    CGINCLUDE
    #include "GecitHand.cginc"
    float _Pull;

    float4 PulledClip(float4 vertex)
    {
        return PerdeClip(mul(unity_ObjectToWorld, vertex).xyz, _Pull);
    }
    ENDCG

    SubShader
    {
        Tags { "Queue" = "Geometry+60" "RenderType" = "Transparent" }

        Pass
        {
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return PulledClip(vertex); }
            fixed4 frag() : SV_Target { return 0; }
            ENDCG
        }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 _Color;
            fixed4 _RimColor;
            float _RimPower;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = PulledClip(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float ndv = saturate(abs(dot(normalize(i.normal), normalize(i.viewDir))));
                float rim = pow(1.0 - ndv, _RimPower);
                return lerp(_Color, _RimColor, rim);
            }
            ENDCG
        }
    }
}
