// Oda taraması blokları (çoklu çizim / instancing): holografik küp — kenarlarında parlak neon çizgi, içi çok
// saydam. Renk yüksekliğe göre (zemin camgöbeği -> tavan mor). Kameradan dışa yayılan tarama dalgası.
Shader "Gecit/ScanVoxel"
{
    Properties
    {
        _Alpha ("Görünürlük", Float) = 1
        _Floor ("Zemin yüksekliği", Float) = -1.3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Back
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _Alpha, _Floor;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 lp : TEXCOORD0; float3 wp : TEXCOORD1; };

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.lp = v.vertex.xyz;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 a = abs(i.lp) * 2.0;                         // 0 orta, 1 yüz
                float lo = min(a.x, min(a.y, a.z));
                float mid = a.x + a.y + a.z - lo - max(a.x, max(a.y, a.z));
                float edge = smoothstep(0.78, 0.96, mid);            // iki ekseni de kenarda: küp kenarı
                float h = saturate((i.wp.y - _Floor) / 2.4);
                float3 col = lerp(float3(0.25, 0.85, 1.0), float3(0.65, 0.4, 1.0), h);
                float dist = distance(i.wp, _WorldSpaceCameraPos);
                float wave = pow(frac(dist * 0.7 - _Time.y * 0.5), 12.0);
                float alpha = (0.06 + 0.7 * edge + wave * 0.35) * _Alpha;
                return fixed4(lerp(col, float3(1, 1, 1), wave * 0.5), alpha);
            }
            ENDCG
        }
    }
}
