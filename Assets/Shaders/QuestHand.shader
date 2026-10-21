// Quest tarzı gerçekçi el: yumuşak ten rengi, ışığı saran (yumuşak gölgeli) aydınlatma, gölgede hafif kızıllık
// (deri altı), kenarlarda ince açık ışık. Bileğe doğru kaybolur. Kıstırmaya yaklaşınca baş ve işaret parmağı ucu
// mavi parlar (_TipA/_TipB/_Pinch: ModelHand ayarlar). Önce derinlik yazılır (el kendi içinden görünmez).
Shader "Gecit/QuestHand"
{
    Properties
    {
        _Color ("Ten", Color) = (0.93, 0.76, 0.66, 1)
        _Shadow ("Gölge", Color) = (0.72, 0.42, 0.38, 1)
        _Rim ("Kenar ışığı", Color) = (1, 0.93, 0.88, 1)
        _Alpha ("Görünürlük", Float) = 1
        _Wrist ("Bilek (dünya)", Vector) = (0, 0, 0, 0)
        _Dir ("Bilekten parmaklara", Vector) = (0, 1, 0, 0)
        _TipA ("Baş parmak ucu", Vector) = (0, -100, 0, 0)
        _TipB ("İşaret parmağı ucu", Vector) = (0, -100, 0, 0)
        _Pinch ("Kıstırma", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite On
            ColorMask 0
        }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color, _Shadow, _Rim;
            float _Alpha, _Pinch;
            float4 _Wrist, _Dir, _TipA, _TipB;

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
                float3 l = normalize(float3(0.3, 0.85, -0.45));
                float wrap = saturate((dot(n, l) + 0.6) / 1.6);              // yumuşak, saran ışık
                float3 col = lerp(_Shadow.rgb, _Color.rgb, wrap);
                float3 h = normalize(l + v);
                col += pow(saturate(dot(n, h)), 24.0) * 0.12;                  // hafif parlaklık
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                col += _Rim.rgb * rim * 0.25;

                float ga = exp(-dot(i.wp - _TipA.xyz, i.wp - _TipA.xyz) / 0.00025);
                float gb = exp(-dot(i.wp - _TipB.xyz, i.wp - _TipB.xyz) / 0.00025);
                float glow = saturate(ga + gb) * _Pinch;
                col = lerp(col, float3(0.45, 0.75, 1.0), glow * 0.7);

                float along = dot(i.wp - _Wrist.xyz, _Dir.xyz);
                float fade = smoothstep(-0.02, 0.035, along);                  // bileğe doğru kaybol
                return fixed4(col, saturate(_Alpha * fade));
            }
            ENDCG
        }
    }
}
