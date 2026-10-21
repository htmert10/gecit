// Ortak: eli "oda görüntüsüyle aynı düzlemde" çizmek.
// Oda (passthrough) tek kameradan gelir ve iki göze de aynı resim olarak gösterilir. Eli gerçek 3B derinlikte
// çizersek her gözde resimdeki gerçek elden kayar. Bu dönüşüm, noktayı AR kamerasının gördüğü yere (resimdeki
// yerine) koyar — iki gözde de aynı yerde; derinlik ise göz kamerasınınki gibi hesaplanır, böylece pencereler
// elin arkasındaysa kesilir.
#ifndef GECIT_HAND_INCLUDED
#define GECIT_HAND_INCLUDED
#include "UnityCG.cginc"

float4x4 _GecitCamV;
float4x4 _GecitCamP;
float _GecitEyeScale;   // gözlük modunda 2 (göz, resmin orta yarısını gösterir), düz ekranda 1

float4 PerdeClip(float3 worldPos, float pull)
{
    float4 v = mul(_GecitCamV, float4(worldPos, 1.0));
    float4 c = mul(_GecitCamP, v);
    float2 ndc = c.xy / c.w;
    v.z += pull;                       // görünüm uzayında ileri -z: kameraya yaklaştır (yalnız derinlik)
    float4 e = mul(UNITY_MATRIX_P, v);
    // Göz, oda resminin orta yarısını gösterir: göz ndc.x = 2 * kamera ndc.x, y aynı
    e.x = ndc.x * max(_GecitEyeScale, 1.0) * e.w;
    e.y = ndc.y * e.w * _ProjectionParams.x;
    return e;
}
#endif
