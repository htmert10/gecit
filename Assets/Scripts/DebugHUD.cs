using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Ekranda sadece önemli uyarılar (takip zayıfsa); ayrıntılı durum telefon kayıtlarına (logcat) yazılır.
public class DebugHUD : MonoBehaviour
{
    public Text label;
    public Transform arCamera;
    public ARCameraManager cameraManager;
    public Camera eye;
    float nextLog;
    int frames;
    bool loggedConfigs;

    float worstFrame;
    int lastGc;
    public static int LastThermal = -1;   // son ölçülen ısınma (el takibi yüke göre hız ayarlar)
    public static float LastFps = 30f;

    void Update()
    {
        frames++;
        worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
        label.text = Warning();

        if (Time.unscaledTime < nextLog) return;
        float fps = frames / 2f;
        frames = 0;
        nextLog = Time.unscaledTime + 2f;
        int gc = System.GC.CollectionCount(0);
        LastThermal = Thermal();
        LastFps = fps;
        Debug.Log($"GECIT PERF fps={fps:F0} en_kotu={worstFrame * 1000f:F0}ms gc={gc - lastGc} isi={LastThermal} el_hizi={(HandTracker.LoadLimited ? "20" : "tam")} cozunurluk={Screen.width}x{Screen.height} donme={ARPoseUpdater.Source} jiro={(ARPoseUpdater.GyroReady ? "hazir" : "olculuyor")} pivot={ARPoseUpdater.PivotOffset.z:F2} {ARPoseUpdater.GyroDiag} sensor={(ARPoseUpdater.UsingGameRotation ? "pusulasiz" : "eski")}");
        lastGc = gc;
        worstFrame = 0f;
        var p = arCamera.position;
        if (Window.First != null)
        {
            var w = Window.First.transform.position;
            Debug.Log($"GECIT OLCUM gozFOV={(eye != null ? eye.fieldOfView : 0f):F1} kamera={p.x:F2},{p.y:F2},{p.z:F2} pencere={w.x:F2},{w.y:F2},{w.z:F2} uzaklik={Vector3.Distance(p, w):F2} sabit={Window.First.anchor.IsAnchored}");
        }
        Debug.Log($"GECIT DURUM fps={fps:F0} ar={ARSession.state}/{ARSession.notTrackingReason} konum={p.x:F2},{p.y:F2},{p.z:F2} kontrol={PointerInput.Source} basili={PointerInput.Pressed} el_tikla={(HandTracker.HandClicks ? "acik" : "kapali")} desen={TrackingQuality.FeatureCount} {HandTracker.Status} kumanda={GecitController.Status}");

        if (!loggedConfigs && cameraManager != null && cameraManager.subsystem != null && cameraManager.subsystem.running)
        {
            loggedConfigs = true;
            using var configs = cameraManager.GetConfigurations(Unity.Collections.Allocator.Temp);
            foreach (var c in configs) Debug.Log("GECIT KAMERA AYARI: " + c);
            Debug.Log("GECIT KAMERA SECILI: " + cameraManager.currentConfiguration);
        }
    }

    // Telefonun ısınma durumu (Android 10+): 0 yok, 1 hafif, 2 orta, 3 ağır (yavaşlatma), 4+ kritik
    static AndroidJavaObject powerManager;
    static int Thermal()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (powerManager == null)
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                powerManager = activity.Call<AndroidJavaObject>("getSystemService", "power");
            }
            return powerManager.Call<int>("getCurrentThermalStatus");
        }
        catch { return -1; }
#else
        return -1;
#endif
    }

    static string Warning()
    {
        if (ARSession.state == ARSessionState.SessionTracking)
            return TrackingQuality.Weak ? "Bu yuzey duz, takip zayif. Desenli bir yere bak." : "";
        switch (ARSession.notTrackingReason)
        {
            case NotTrackingReason.InsufficientFeatures: return "Takip zayif: duz yuzey. Desenli bir yere bak.";
            case NotTrackingReason.InsufficientLight: return "Takip zayif: ortam karanlik.";
            case NotTrackingReason.ExcessiveMotion: return "Takip zayif: cok hizli hareket.";
            case NotTrackingReason.Initializing: return "Oda taniniyor... telefonu yavasca gezdir.";
            case NotTrackingReason.Relocalizing: return "Oda yeniden taniniyor...";
            default: return ARSession.state == ARSessionState.SessionInitializing ? "Oda taniniyor..." : "";
        }
    }
}
