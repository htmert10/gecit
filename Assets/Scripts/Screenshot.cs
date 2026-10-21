using System.Collections;
using UnityEngine;

// Fotoğraf çek (kumandada 9 ya da menüden): o anki görüntü telefonun Galerisine, "Pictures/GECIT" klasörüne
// kaydedilir. Ekran kısa bir an beyaz parlar ve deklanşör sesi gelir.
// (Video için telefonun kendi ekran kaydedicisi kullanılabilir: bildirim panelinde "Ekran kaydedici".)
public class Screenshot : MonoBehaviour
{
    static Screenshot instance;
    public CanvasGroup flash;   // beyaz parlama
    bool busy;

    void Awake() { instance = this; }

    public static void Take()
    {
        if (instance != null && !instance.busy) instance.StartCoroutine(instance.Capture());
    }

    IEnumerator Capture()
    {
        busy = true;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        var png = tex.EncodeToPNG();
        Destroy(tex);
        string name = "GECIT_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        bool ok = Save(png, name);
        Debug.Log("GECIT FOTO " + (ok ? "kaydedildi " : "kaydedilemedi ") + name);
        Sfx.Shutter();

        // Parlama
        if (flash != null)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                flash.alpha = 0.8f * (1f - t);
                yield return null;
            }
            flash.alpha = 0f;
        }
        busy = false;
    }

    // Android Galerisi (MediaStore): izin gerektirmez (Android 10+)
    static bool Save(byte[] png, string name)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
            using var values = new AndroidJavaObject("android.content.ContentValues");
            values.Call("put", "_display_name", name);
            values.Call("put", "mime_type", "image/png");
            values.Call("put", "relative_path", "Pictures/GECIT");
            using var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
            using var collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
            using var uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
            if (uri == null) return false;
            using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri);
            var data = new sbyte[png.Length];
            System.Buffer.BlockCopy(png, 0, data, 0, png.Length);
            stream.Call("write", data);
            stream.Call("close");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("GECIT FOTO hata: " + e.Message);
            return false;
        }
#else
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.persistentDataPath, name), png);
        return true;
#endif
    }
}
