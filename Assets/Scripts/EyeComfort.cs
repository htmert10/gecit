using UnityEngine;

// Göz koruma: gözlükte parlak beyaz sayfalar / perde / gökyüzü merceğin ardından gözü yoruyordu (kullanıcı: "gözlerim
// acıyor"; örnek alınan videoda da aynı sebeple kontrast düşürülmüş). Gözlük açıkken içerik %28 kısılır.
// Gözlük panelinden açılıp kapanır; varsayılan açık. Düz ekranda etkisiz.
public static class EyeComfort
{
    const string Key = "goz_koruma";
    public const float Dim = 0.72f;
    static bool loaded, enabled = true;

    public static bool Enabled
    {
        get
        {
            if (!loaded) { enabled = PlayerPrefs.GetInt(Key, 1) == 1; loaded = true; }
            return enabled;
        }
    }

    public static void Set(bool on)
    {
        enabled = on;
        loaded = true;
        PlayerPrefs.SetInt(Key, on ? 1 : 0);
        PlayerPrefs.Save();
        if (VirtualWorld.Instance != null) VirtualWorld.Instance.RefreshExposure();
        Debug.Log("GECIT GOZ KORUMA " + (on ? "acik" : "kapali"));
    }

    public static float Factor => PassthroughView.Stereo && Enabled ? Dim : 1f;
    public static Color Tint { get { float f = Factor; return new Color(f, f, f, 1f); } }
}
