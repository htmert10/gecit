// Gerçek 360° fotoğraf gökyüzü (eşdikdörtgen panorama). Ortam kullanıcının baktığı yöne kurulduğu için
// _Rotation ile döndürülür (derece). Mip yok: dikiş çizgisi oluşmasın diye tek seviyeden okunur.
Shader "Gecit/PanoSky"
{
    Properties
    {
        _MainTex ("Panorama", 2D) = "black" {}
        _Rotation ("Dönüş (derece)", Float) = 0
        _Exposure ("Parlaklık", Float) = 1
        _Tint ("Renk", Color) = (1, 1, 1, 1)
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

            sampler2D _MainTex;
            float _Rotation, _Exposure;
            fixed4 _Tint;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float a = radians(_Rotation);
                float s = sin(a), c = cos(a);
                float3 d = v.vertex.xyz;
                o.dir = float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);   // y ekseni etrafında -_Rotation
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float u = atan2(d.x, d.z) * 0.15915494 + 0.5;
                float v = asin(clamp(d.y, -1.0, 1.0)) * 0.31830989 + 0.5;
                fixed3 col = tex2Dlod(_MainTex, float4(u, v, 0, 0)).rgb * _Tint.rgb * _Exposure;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
