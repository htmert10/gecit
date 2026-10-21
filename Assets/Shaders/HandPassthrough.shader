// Gerçek eli pencerelerin önüne çıkarır: el şeklindeki (biraz şişirilmiş) görünmez gövde, oda resmindeki
// elin tam üstüne (aynı düzlemde) ve elin gerçek derinliğinde derinlik yazar. Elin arkasındaki pencereler
// orada çizilmez, odadaki gerçek el görünür; el pencerenin arkasındaysa pencere üstte kalır.
Shader "Gecit/HandMask"
{
    Properties
    {
        _Inflate ("Sisirme (m)", Float) = 0.0
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+50" "RenderType" = "Opaque" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "GecitHand.cginc"

            float _Inflate;

            float4 vert(appdata_base v) : SV_POSITION
            {
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                wp += UnityObjectToWorldNormal(v.normal) * _Inflate;
                return PerdeClip(wp, 0.0);
            }

            fixed4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
}
