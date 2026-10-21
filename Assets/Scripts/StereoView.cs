using UnityEngine;
using UnityEngine.UI;

// AR kamerasının görüntüsünü ekranın iki yarısına (sol göz / sağ göz) basar.
public class StereoView : MonoBehaviour
{
    public Camera arCamera;
    public RawImage leftEye;
    public RawImage rightEye;

    RenderTexture rt;
    int width, height;

    void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Application.targetFrameRate = 30; // ARCore kamerası 30 fps
        Rebuild();
    }

    void Update()
    {
        if (Screen.width != width || Screen.height != height)
            Rebuild();
    }

    void Rebuild()
    {
        width = Screen.width;
        height = Screen.height;

        if (rt != null)
        {
            arCamera.targetTexture = null;
            rt.Release();
            Destroy(rt);
        }

        // Yarı çözünürlük: her göz zaten ekranın yarısını görüyor, telefon daha az yorulur
        rt = new RenderTexture(width / 2, height / 2, 24);
        rt.Create();
        arCamera.targetTexture = rt;

        // Tam ekran görüntünün ortadaki yarısı her göze; en-boy oranı bozulmaz.
        var uv = new Rect(0.25f, 0f, 0.5f, 1f);
        leftEye.texture = rt;
        leftEye.uvRect = uv;
        rightEye.texture = rt;
        rightEye.uvRect = uv;
    }
}
