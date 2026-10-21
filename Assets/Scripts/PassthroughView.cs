using UnityEngine;

// Quest'teki gibi arka planda gerçek oda: AR kamerası telefon kamerasını bir dokuya çizer,
// her gözün arkasındaki büyük perde bu dokuyu gösterir. Göz kameralarının görüş açısı
// telefon kamerasınınkiyle eşitlenir ki sanal nesneler odada doğru yerde dursun.
//
// Gözlük (VR kutusu): iki gözün görüntüsü ekranın tam ortasından bölünmez; her gözün görüntüsünün ORTASI gözlüğün
// merceğinin ortasına (göz arası, ~63 mm) gelir. Ekran ortadan bölününce merkezler ~77 mm aralıklı kalıyordu,
// gözler birleştiremiyordu (kullanıcı: "gözüm kör oldu"). Milimetre, ekranın piksel yoğunluğundan hesaplanır.
// Gerçek oda görüntüsü tek kameradan (iki gözde aynı) uzakta gösterilir; sanal nesneler gerçek derinlikte.
public class PassthroughView : MonoBehaviour
{
    public Camera arCamera;
    public Camera leftEye, rightEye;
    public Renderer leftScreen, rightScreen;
    public float screenDistance = 15f;

    RenderTexture rt;
    int width, height;

    public static bool Stereo;
    public static PassthroughView Instance;
    const string StereoKey = "gozluk_modu";
    const string IpdKey = "gozluk_ipd2";   // eski anahtar yanlış dpi ile kaydedilmişti (52 "mm" = gerçek 61 mm)
    public static float IpdMm = 63f;      // göz arası / mercek arası (mm)
    // Mercek odak uzaklığı (mm): gözlükte görüş açısı buradan hesaplanır. Eskiden göz kameraları telefon kamerasının
    // açısıyla (~33° dikey) çiziyordu, mercek bunu ~75°'ye geriyordu: oda 2,1–2,9 kat büyük, başı çevirince 2,5 kat
    // hızlı kayıyordu (kullanıcı: "gözlerim acıyor"). Artık göz kameraları merceğin açısıyla çizer, oda görüntüsü
    // gerçek açısında (1:1) ortada durur. Gözlüğe göre "Boyut" −/+ ile ayarlanır.
    const string LensKey = "gozluk_mercek2";   // eski anahtar yanlış dpi ile (52 = gerçek ölçüyle ~61)
    public static float LensMm = 40f;
    static readonly int EyeScaleId = Shader.PropertyToID("_GecitEyeScale");

    void Awake()
    {
        Instance = this;
        Stereo = PlayerPrefs.GetInt(StereoKey, 0) == 1;   // gözlük gelene kadar varsayılan: düz ekran
        IpdMm = PlayerPrefs.GetFloat(IpdKey, 61f);   // kullanıcının bulduğu ayar (gerçek ~61 mm)
        LensMm = PlayerPrefs.GetFloat(LensKey, 61f);   // kullanıcının seçtiği görüntü boyu (eski 52 × 384,7/450 düzeltmesi)

        // Gözlükte göz görüntülerinin dışında kalan sağ/sol şeritleri hiçbir kamera temizlemiyordu: eski kareler
        // çizgi çizgi kalıyordu (kullanıcı: "sağda solda haritamdan eski görüntüler"). Her karede tüm ekranı siyaha
        // boyayan, hiçbir şey çizmeyen kamera (göz kameralarından önce).
        var clear = new GameObject("Ekran Temizleyici").AddComponent<Camera>();
        clear.cullingMask = 0;
        clear.clearFlags = CameraClearFlags.SolidColor;
        clear.backgroundColor = Color.black;
        clear.depth = -50;
        clear.rect = new Rect(0f, 0f, 1f, 1f);
        clear.useOcclusionCulling = false;
        clear.allowHDR = false;
        clear.allowMSAA = false;
    }

    public static void SetLens(float mm)
    {
        LensMm = Mathf.Clamp(mm, 28f, 90f);
        PlayerPrefs.SetFloat(LensKey, LensMm);
        PlayerPrefs.Save();
        if (Instance != null) Instance.ApplyMode();
        Debug.Log("GECIT GOZLUK BOYUT mercek=" + LensMm + "mm");
    }

    void Start() { ApplyMode(); }

    public static void SetStereo(bool on)
    {
        bool changed = Stereo != on;
        Stereo = on;
        if (changed && Instance != null) Instance.width = -1;   // oda görüntüsü dokusu yeni çözünürlükle kurulsun
        PlayerPrefs.SetInt(StereoKey, on ? 1 : 0);
        PlayerPrefs.Save();
        if (Instance != null) Instance.ApplyMode();
        if (VirtualWorld.Instance != null) VirtualWorld.Instance.RefreshExposure();
        Debug.Log("GECIT GORUNUM " + (on ? "gozluk (iki goz)" : "duz ekran") + " goz arasi=" + IpdMm + "mm");
    }

    public static void SetIpd(float mm)
    {
        IpdMm = Mathf.Clamp(mm, 52f, 74f);
        PlayerPrefs.SetFloat(IpdKey, IpdMm);
        PlayerPrefs.Save();
        if (Instance != null) Instance.ApplyMode();
        Debug.Log("GECIT GOZ ARASI " + IpdMm + "mm");
    }

    // Ekranın gerçek piksel yoğunluğu. Unity'nin Screen.dpi'ı A26'da 450 veriyordu (gerçek 384,7): "63 mm" göz arası
    // gerçekte 73,6 mm oluyordu (kullanıcı: göz ağrısı, ayarı en alta indirdi). Önce bilinen model, sonra Android xdpi.
    static float realDpi = -1f;
    static float RealDpi()
    {
        if (realDpi > 0f) return realDpi;
        float xdpi = 0f;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var res = act.Call<AndroidJavaObject>("getResources"))
            using (var dm = res.Call<AndroidJavaObject>("getDisplayMetrics"))
                xdpi = dm.Get<float>("xdpi");
        }
        catch (System.Exception e) { Debug.Log("GECIT DPI okunamadi: " + e.Message); }
#endif
        var model = SystemInfo.deviceModel ?? "";
        if (model.Contains("SM-A266")) realDpi = 384.7f;           // 6,7" 2340x1080
        else if (xdpi > 200f && xdpi < 700f) realDpi = xdpi;
        else realDpi = Screen.dpi > 50f ? Screen.dpi : 385f;
        Debug.Log($"GECIT DPI kullanilan={realDpi:F1} xdpi={xdpi:F1} unity={Screen.dpi:F1} model={model}");
        return realDpi;
    }

    // Ekranda 1 mm kaç piksel (çözünürlük ölçeği dahil)
    static float PxPerMm()
    {
        int sysW = Mathf.Max(Display.main.systemWidth, Display.main.systemHeight);
        float scale = sysW > 0 ? (float)Mathf.Max(Screen.width, Screen.height) / sysW : 1f;   // render ölçeği
        return RealDpi() / 25.4f * scale;
    }

    float lensFov = 75f;          // gözlükte göz kameralarının dikey görüş açısı (merceğin açısı)
    float eyeTanHalfX = 1f;       // gözlükte göz görüntüsünün yatay yarı açısının tanjantı
    float visibleFrac = 1f;       // oda görüntüsünün gözlükte görünen yatay payı
    public static float VisibleFracX => Instance != null && Stereo ? Instance.visibleFrac : 1f;   // el noktası bu alanda kalsın
    public static float LensFovDeg => Instance != null ? Instance.lensFov : 0f;

    void ApplyMode()
    {
        float W = Screen.width, H = Screen.height;
        if (Stereo && W > 0f)
        {
            float pxmm = PxPerMm();
            float half = IpdMm * 0.5f * pxmm;                    // ekran ortasından mercek ortasına (px)
            float w = Mathf.Min(2f * half, W * 0.5f);            // her göz görüntüsünün eni (birbirine taşmasın)
            float lc = W * 0.5f - half, rc = W * 0.5f + half;    // göz görüntülerinin ortaları
            leftEye.rect = new Rect((lc - w * 0.5f) / W, 0f, w / W, 1f);
            rightEye.rect = new Rect((rc - w * 0.5f) / W, 0f, w / W, 1f);
            float ipdM = IpdMm * 0.001f;
            leftEye.transform.localPosition = new Vector3(-ipdM * 0.5f, 0f, 0f);
            rightEye.transform.localPosition = new Vector3(ipdM * 0.5f, 0f, 0f);
            rightEye.enabled = true;
            // Göz görüntüsünün mercekten görünen açısı: yarı boy (mm) / odak (mm)
            lensFov = 2f * Mathf.Atan(H / pxmm * 0.5f / LensMm) * Mathf.Rad2Deg;
            eyeTanHalfX = w / pxmm * 0.5f / LensMm;
            Debug.Log($"GECIT GOZLUK goz arasi={IpdMm}mm mercek={LensMm}mm aci={lensFov:F0}° px/mm={pxmm:F1} goz eni={w:F0}px ekran={W}x{H}");
        }
        else
        {
            leftEye.rect = new Rect(0f, 0f, 1f, 1f);
            leftEye.transform.localPosition = Vector3.zero;
            rightEye.enabled = false;
        }
        foreach (var r in new[] { leftScreen, rightScreen })
        {
            if (r == null || r.material == null) continue;
            // Oda görüntüsünün tamamı, gerçek açısında (kırpma yok)
            r.material.mainTextureScale = Vector2.one;
            r.material.mainTextureOffset = Vector2.zero;
        }
        Shader.SetGlobalFloat(EyeScaleId, Stereo ? 2f : 1f);
    }

    static readonly int CamVId = Shader.PropertyToID("_GecitCamV");
    static readonly int CamPId = Shader.PropertyToID("_GecitCamP");

    void OnEnable() { Camera.onPreRender += BeforeEyeRender; }
    void OnDisable() { Camera.onPreRender -= BeforeEyeRender; }

    void BeforeEyeRender(Camera cam)
    {
        if (cam != leftEye && cam != rightEye) return;
        Shader.SetGlobalMatrix(CamVId, arCamera.worldToCameraMatrix);
        Shader.SetGlobalMatrix(CamPId, arCamera.projectionMatrix);
    }

    void Update()
    {
        if (Screen.width != width || Screen.height != height) Rebuild();

        var m = arCamera.projectionMatrix;
        if (m.m11 > 0.1f && m.m00 > 0.1f)
        {
            // Düz ekranda göz = telefon kamerası; gözlükte göz = merceğin açısı. Oda görüntüsü her durumda
            // kameranın gerçek açısında (1:1): başı çevirince oda doğru hızda döner.
            float fov = Stereo ? lensFov : 2f * Mathf.Atan(1f / m.m11) * Mathf.Rad2Deg;
            if (leftEye.fieldOfView != fov) { leftEye.fieldOfView = fov; rightEye.fieldOfView = fov; }
            visibleFrac = Mathf.Clamp01(eyeTanHalfX * m.m00);
            Fit(leftScreen.transform, m);
            Fit(rightScreen.transform, m);
        }
    }

    void Fit(Transform screen, Matrix4x4 m)
    {
        screen.localPosition = new Vector3(0f, 0f, screenDistance);
        screen.localRotation = Quaternion.identity;
        screen.localScale = new Vector3(2f * screenDistance / m.m00, 2f * screenDistance / m.m11, 1f);
    }

    void Rebuild()
    {
        width = Screen.width;
        height = Screen.height;
        if (rt != null) { arCamera.targetTexture = null; rt.Release(); Destroy(rt); }
        // Gözlükte her göz oda görüntüsünün yalnız ortasını görür: yarı çözünürlük yeter (telefon daha az yorulur,
        // gecikme azalır)
        rt = Stereo ? new RenderTexture(width / 2, height / 2, 16) : new RenderTexture(width, height, 16);
        rt.Create();
        arCamera.targetTexture = rt;
        foreach (var r in new[] { leftScreen, rightScreen })
        {
            r.material.mainTexture = rt;
        }
        ApplyMode();
    }
}
