// Sanal dünyadaki el / kumanda / oyun nesneleri (Meta tarzı): yumuşak ışıklı gövde, kenarlarda parlama.
// _UseFade = 1 (el): bileğe doğru kaybolur ve elin genel görünürlüğüne (_GecitHandAlpha) uyar.
// _UseFade = 0 (kumanda, balon, hedef): her zaman tam görünür.
// İlk geçiş yalnız derinlik yazar; üst üste binen parçalar tek bir nesne gibi görünür.
Shader "Gecit/VirtualHand"
{
    Properties
    {
        _Color ("Govde", Color) = (0.86, 0.87, 0.91, 0.9)
        _RimColor ("Kenar", Color) = (1.0, 1.0, 1.0, 0.95)
        _RimPower ("Kenar Inceligi", Float) = 3.2
        _UseFade ("Bilek Solmasi", Float) = 1
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    float4 _GecitWristPos;   // xyz: bilek
    float4 _GecitHandDir;    // xyz: bilekten orta parmak köküne birim yön
    float _GecitHandAlpha;   // belirip kaybolma
    float4 _GecitTipA, _GecitTipB;   // baş ve işaret parmağı ucu
    float _GecitPinch;               // kıstırmaya yakınlık (0-1)
    ENDCG

    SubShader
    {
        Tags { "Queue" = "Transparent-5" "RenderType" = "Transparent" }

        Pass
        {
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
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

            fixed4 _Color, _RimColor;
            float _RimPower, _UseFade;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                float3 wpos : TEXCOORD2;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = _WorldSpaceCameraPos - o.wpos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 vd = normalize(i.viewDir);
                float diff = 0.62 + 0.25 * saturate(dot(n, normalize(float3(0.3, 1.0, -0.2)))) + 0.13 * saturate(dot(n, vd));
                float rim = pow(1.0 - saturate(abs(dot(n, vd))), _RimPower);
                float3 col = lerp(_Color.rgb * diff, _RimColor.rgb, rim * 0.6);
                // Kıstırmaya yaklaşınca parmak uçları mavi parlar (yalnız el)
                float tip = max(saturate(1.0 - distance(i.wpos, _GecitTipA.xyz) / 0.022), saturate(1.0 - distance(i.wpos, _GecitTipB.xyz) / 0.022));
                col = lerp(col, float3(0.45, 0.72, 1.0), tip * _GecitPinch * _UseFade);
                float along = dot(i.wpos - _GecitWristPos.xyz, _GecitHandDir.xyz);
                float fade = saturate((along + 0.035) / 0.05) * _GecitHandAlpha;
                float a = lerp(_Color.a, _RimColor.a, rim) * lerp(1.0, fade, _UseFade);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
