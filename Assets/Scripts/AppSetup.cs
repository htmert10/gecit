using UnityEngine;

// Uygulama açılışında ekran ve kare hızı ayarları.
public class AppSetup : MonoBehaviour
{
    void Awake()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Screen.fullScreen = true;
        QualitySettings.antiAliasing = 2; // kenar yumuşatma
    }

    // Tam çözünürlük: gözlükte mercek ekranı büyütür, 0,8 ölçek yazıları pikselli gösteriyordu

    void Start()
    {
        int w = Display.main.systemWidth, h = Display.main.systemHeight;
        if (w < h) (w, h) = (h, w);   // yatay
        Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
        Debug.Log($"GECIT COZUNURLUK {w}x{h}");
    }

    // Tarayıcı ya da bildirimler üst çubuğu açarsa tam ekrana geri dön
    void OnApplicationFocus(bool focus)
    {
        if (focus) Screen.fullScreen = true;
    }
}
