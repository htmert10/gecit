// Teknolojik ışıklı çerçeve (arayüz için): yuvarlak köşeli ince neon çizgi + yumuşak hale + köşelerde parlak
// parantezler + çevresinde dönen kuyruklu ışık. Toplamalı karışım (holografik görünür).
// Image'ın rengi = çizgi rengi, alfası = parlaklık (üstüne gelince artar). Boyut piksel olarak malzemede.
Shader "Gecit/HoloFrame"
{
    Properties
    {
        [PerRendererData] _MainTex ("Doku", 2D) = "white" {}
        _Size ("Boyut px (x, y), köşe parantezi boyu (z)", Vector) = (100, 100, 30, 0)
        _Radius ("Köşe yarıçapı", Float) = 10
        _Inset ("Kenardan iç pay", Float) = 20
        _Line ("Çizgi kalınlığı", Float) = 1.6
        _Glow ("Hale genişliği", Float) = 12
        _Sweep ("Dönen ışık hızı", Float) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Size;
            float _Radius, _Inset, _Line, _Glow, _Sweep;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * _Size.xy;
                float2 h = _Size.xy * 0.5 - _Inset;
                float r = min(_Radius, min(h.x, h.y));
                float2 q = abs(p) - (h - r);
                float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;   // çizgiye işaretli uzaklık

                float core = exp(-sd * sd / (_Line * _Line));
                float halo = exp(-abs(sd) / _Glow) * (sd > 0.0 ? 0.5 : 0.22);

                // Köşe parantezleri: köşeye yakın kısımda çizgi daha parlak ve kalın
                float2 c = abs(p) - (h - r - _Size.z);
                float corner = saturate(c.x * 0.5) * saturate(c.y * 0.5);
                float thick = exp(-sd * sd / (_Line * _Line * 4.0));

                // Çevrede dönen iki kuyruklu ışık
                float ang = atan2(p.y / max(h.y, 1.0), p.x / max(h.x, 1.0)) / 6.2831853 + 0.5;
                float s = frac(ang - _Time.y * 0.11 * _Sweep);
                float comet = pow(s, 14.0) + pow(frac(s + 0.5), 14.0);
                comet *= step(0.001, _Sweep);

                // Görüntünün kenarına doğru sıfıra in (hale dikdörtgen gibi kesilmesin)
                float2 e = _Size.xy * 0.5 - abs(p);
                float edge = saturate(min(e.x, e.y) / max(_Inset * 0.7, 1.0));
                edge *= edge;

                float a = (core * 0.6 + halo * 0.7 + corner * thick * 0.9 + comet * (core * 1.6 + halo * 1.2)) * edge;
                float3 col = lerp(i.color.rgb, float3(1, 1, 1), saturate(core * 0.35 + comet * core * 0.6));
                return fixed4(col, saturate(a) * i.color.a);
            }
            ENDCG
        }
    }
}
