using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Kamerada izlenebilir nokta (desen) sayısını ölçer. Az ise takip zayıftır (düz beyaz duvar):
// pencere hafifçe soluklaşır ve uyarı gösterilir.
public class TrackingQuality : MonoBehaviour
{
    public static bool Weak;
    public static int FeatureCount;

    public ARPointCloudManager pointClouds;
    public CanvasGroup[] windowGroups;
    public int weakThreshold = 25;
    float smoothCount = 100f;

    void Update()
    {
        int count = 0;
        foreach (var cloud in pointClouds.trackables)
            if (cloud.positions.HasValue) count += cloud.positions.Value.Length;
        FeatureCount = count;
        smoothCount = Mathf.Lerp(smoothCount, count, Time.deltaTime * 3f);
        Weak = ARSession.state == ARSessionState.SessionTracking && smoothCount < weakThreshold;
        if (windowGroups != null)
            foreach (var g in windowGroups)
                if (g != null)
                {
                    float a = Mathf.MoveTowards(g.alpha, Weak ? 0.55f : 1f, Time.deltaTime * 2f);
                    if (a != g.alpha) g.alpha = a;
                }
    }
}
