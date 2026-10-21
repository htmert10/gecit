using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Kameranın seçeneklerini yazar; telefon 30'dan yüksek kare hızını (60 fps) destekliyorsa onu seçer.
// 30 fps'te her şey (görüntü, el, baş dönmesi) kare başına 33 ms geç kalır; 60'ta yarısı (kullanıcı: "ufak gecikme var").
public class CameraConfigPicker : MonoBehaviour
{
    public ARCameraManager cameraManager;
    bool done;

    void Update()
    {
        if (done || cameraManager == null || cameraManager.subsystem == null || !cameraManager.subsystem.running) return;
        using (var configs = cameraManager.GetConfigurations(Allocator.Temp))
        {
            if (!configs.IsCreated || configs.Length == 0) return;
            done = true;
            XRCameraConfiguration? best = null;
            foreach (var c in configs)
            {
                int fps = c.framerate ?? 30;
                Debug.Log($"GECIT KAMERA secenek {c.width}x{c.height} {fps}fps derinlik={c.depthSensorSupported}");
                if (c.width < 640) continue;
                if (best == null) { best = c; continue; }
                int bestFps = best.Value.framerate ?? 30;
                if (fps > bestFps || (fps == bestFps && c.width * c.height < best.Value.width * best.Value.height)) best = c;
            }
            var cur = cameraManager.currentConfiguration;
            Debug.Log("GECIT KAMERA su an " + (cur.HasValue ? $"{cur.Value.width}x{cur.Value.height} {cur.Value.framerate ?? 30}fps" : "-"));
            if (best.HasValue && (best.Value.framerate ?? 30) > 30)
            {
                cameraManager.currentConfiguration = best.Value;
                Application.targetFrameRate = best.Value.framerate ?? 60;
                Debug.Log($"GECIT KAMERA secildi {best.Value.width}x{best.Value.height} {best.Value.framerate}fps");
            }
        }
    }
}
