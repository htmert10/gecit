using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Sanal dünya modu (Quest'teki sanal ortamlar gibi): oda görüntüsü kararıp kaybolur, seçilen ortam gelir.
// Ortamlar (menüdeki "Sanal dünya" panelinden seçilir, son seçim hatırlanır):
//  - Gece vadisi : vadiye bakan ahşap teras (fenerler), karanlık çam ormanı ve göl, ateş böcekleri, yıldızlar ve ay
//  - Gündüz vadisi: aynı yer güneşli öğleden sonra; bulutlar, karlı dağlar
//  - Uzay        : parlayan halkalı metal platform, halkalı dev gezegen, süzülen göktaşları, bulutsulu gökyüzü
//  - Sahil       : gün batımı; kumsalda ahşap teras, palmiyeler, güneşi yansıtan dalgalı deniz, ufukta adalar
// Her ortamda sağ tarafta oyun köşesi var (tıkla, oyun başlasın). Pencereler (YouTube vb.) yerinde kalır.
// Geçiş kısa bir karartmayla olur. Her şey kodla üretilir; bir ortam ilk seçildiğinde bir kez kurulur.
public class VirtualWorld : MonoBehaviour
{
    public enum Env { GeceVadisi, GunduzVadisi, Uzay, Sahil, Sinema }

    public static bool Active;
    public static VirtualWorld Instance;
    public Vector3 WorldForward => world != null ? world.forward : Vector3.forward;   // fotoğrafın "karşısı"
    public static Env Current = Env.GeceVadisi;

    public Transform head;
    public PassthroughView passthrough;
    public Material skyMaterial, floorMaterial, mountainMaterial, orbMaterial, fadeMaterial, gameMaterial;
    public Material skyDay, skySpace, skySunset, seaMaterial, planetMaterial;
    // Gerçek 360° fotoğraflar (Resources/Panorama): varsa gökyüzü + yer fotoğraftan gelir, çizilmiş arazi kurulmaz
    public Material panoMaterial;   // Gecit/PanoSky
    public Material holoMaterial;
    public Material cinemaSky;      // Gecit/SkyCinema: özel sinema salonu   // Gecit/Hologram: oyun köşesindeki nesneler (profesyonel, çocuksu değil)
    public bool[] panoAvailable = new bool[5];
    public static readonly string[] PanoNames = { "gece", "gunduz", "uzay", "sahil", "sinema" };
    public static readonly float[] PanoFront = { 36f, 18f, 86f, 43f, 92f };      // fotoğrafın en güzel yeri (derece) karşıya gelsin
    static readonly float[] PanoExposure = { 0.5f, 1f, 1f, 1f, 0.4f };   // sinema: salon ışıkları kısık   // gece: ay ışığı fotoğrafı karartılır
    Texture2D panoTex;
    Env panoEnv = (Env)(-1);
    public SaberGame saber;
    public BalloonGame balloon;
    public TargetGame target;
    public WhackGame whack;
    public BasketGame basket;
    public float floorBelowHead = 1.25f;

    Transform world;
    readonly Dictionary<Env, Transform> roots = new Dictionary<Env, Transform>();
    readonly Dictionary<Env, Material> terrainMats = new Dictionary<Env, Material>();
    Renderer fade;
    bool busy;
    float normalFar;
    readonly List<(Transform t, Vector3 basePos, float phase, float size)> fireflies = new List<(Transform, Vector3, float, float)>();
    readonly List<(Transform t, Vector3 basePos, float phase)> cornerItems = new List<(Transform, Vector3, float)>();
    readonly List<Transform> spinners = new List<Transform>();
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int CenterId = Shader.PropertyToID("_Center");
    const string EnvKey = "ortam";

    public static readonly string[] EnvNames = { "Gece vadisi", "Gündüz vadisi", "Uzay", "Sahil", "Sinema salonu" };

    void Awake()
    {
        Instance = this;
        Current = (Env)Mathf.Clamp(PlayerPrefs.GetInt(EnvKey, 0), 0, 3);   // sinema hatırlanmaz (menüden girilir)
    }

    void Start()
    {
        RenderSettings.skybox = skyMaterial;
        var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(f.GetComponent<Collider>());
        f.name = "Karartma";
        f.transform.SetParent(head, false);
        f.transform.localScale = Vector3.one * 0.6f;
        fade = f.GetComponent<Renderer>();
        fade.sharedMaterial = fadeMaterial;
        SetFade(0f);
    }

    // Kumanda 3 / menü: son seçilen ortama gir ya da odaya dön
    public void Toggle()
    {
        if (!busy) StartCoroutine(Switch(!Active, Current));
    }

    // Menüdeki ortam paneli: seçilen ortama geç (oda görünümündeysen girer)
    public void SetEnvironment(Env e)
    {
        if (busy || (Active && e == Current)) return;
        StartCoroutine(Switch(true, e));
    }

    public void ReturnToRoom()
    {
        if (!busy && Active) StartCoroutine(Switch(false, Current));
    }

    IEnumerator Switch(bool on, Env env)
    {
        busy = true;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f) { SetFade(t); yield return null; }
        SetFade(1f);

        if (world != null) world.gameObject.SetActive(false);
        Active = on;
        if (on)
        {
            Current = env;
            if (env != Env.Sinema) PlayerPrefs.SetInt(EnvKey, (int)env);
            PlayerPrefs.Save();
            world = GetRoot(env);
            // Ortam kullanıcının olduğu yere ve baktığı yöne kurulur
            var p = head.position;
            var fwd = head.forward; fwd.y = 0f;
            world.position = new Vector3(p.x, p.y - floorBelowHead, p.z);
            world.rotation = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity;
            ApplyLook(env, p);
            world.gameObject.SetActive(true);
            AmbientSound.Play((int)env);
        }
        else AmbientSound.Stop();
        passthrough.leftScreen.enabled = !on;
        passthrough.rightScreen.enabled = !on;
        var clear = on ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        foreach (var eye in new[] { passthrough.leftEye, passthrough.rightEye })
        {
            if (normalFar <= 0f) normalFar = eye.farClipPlane;
            eye.clearFlags = clear;
            eye.farClipPlane = on ? 2000f : normalFar;
        }
        Debug.Log("GECIT DUNYA " + (on ? EnvNames[(int)env] : "oda"));
        yield return null;

        for (float t = 1f; t > 0f; t -= Time.deltaTime / 0.35f) { SetFade(t); yield return null; }
        SetFade(0f);
        busy = false;
    }

    // Gökyüzü, güneş/ay yönü (dünya döndüğü için dünya yönüne çevrilir), zemin renkleri
    // Göz koruma / gözlük değişince gökyüzü parlaklığı
    public void RefreshExposure()
    {
        if (Active && HasPano(Current)) panoMaterial.SetFloat("_Exposure", PanoExposure[(int)Current] * EyeComfort.Factor);
    }

    bool HasPano(Env env) => env != Env.Sinema && panoMaterial != null && panoAvailable != null && (int)env < panoAvailable.Length && panoAvailable[(int)env];

    // Fotoğrafı yükle (yalnız açık ortamınki bellekte durur), ortamın yönüne çevir
    bool ApplyPano(Env env)
    {
        if (!HasPano(env)) return false;
        if (panoEnv != env || panoTex == null)
        {
            var old = panoTex;
            panoTex = Resources.Load<Texture2D>("Panorama/" + PanoNames[(int)env]);
            if (panoTex == null) return false;
            panoEnv = env;
            if (old != null && old != panoTex && Application.isPlaying) Resources.UnloadAsset(old);
        }
        panoMaterial.mainTexture = panoTex;
        panoMaterial.SetFloat("_Rotation", world.eulerAngles.y - PanoFront[(int)env]);
        panoMaterial.SetFloat("_Exposure", PanoExposure[(int)env] * EyeComfort.Factor);   // gözlükte göz koruma
        panoMaterial.SetColor("_Tint", env == Env.GeceVadisi ? new Color(0.8f, 0.9f, 1.08f, 1f) : Color.white);   // gece: hafif mavi ay ışığı
        RenderSettings.skybox = panoMaterial;
        return true;
    }

    void ApplyLook(Env env, Vector3 center)
    {
        var light = world.rotation * LightDir(env);
        if (env == Env.Sinema && cinemaSky != null)
        {
            RenderSettings.skybox = cinemaSky;
            cinemaSky.SetVector("_Front", world.forward);
            return;
        }
        if (ApplyPano(env))
        {
            if (env == Env.Uzay) planetMaterial.SetVector("_SunDir", light);
            return;
        }
        switch (env)
        {
            case Env.GeceVadisi: RenderSettings.skybox = skyMaterial; skyMaterial.SetVector("_MoonDir", light); break;
            case Env.GunduzVadisi: RenderSettings.skybox = skyDay; skyDay.SetVector("_SunDir", light); break;
            case Env.Uzay:
                RenderSettings.skybox = skySpace; skySpace.SetVector("_SunDir", light);
                planetMaterial.SetVector("_SunDir", light);
                break;
            case Env.Sahil:
                RenderSettings.skybox = skySunset; skySunset.SetVector("_SunDir", light);
                seaMaterial.SetVector("_SunDir", light);
                break;
        }
        floorMaterial.SetVector(CenterId, new Vector4(center.x, 0f, center.z, 0f));
        Color wood = env == Env.GeceVadisi ? new Color(0.30f, 0.21f, 0.15f) : env == Env.Sahil ? new Color(0.52f, 0.38f, 0.27f) : new Color(0.46f, 0.33f, 0.23f);
        floorMaterial.SetColor("_Base", wood);
        floorMaterial.SetColor("_Line", wood * 0.42f);
    }

    static Vector3 LightDir(Env env)
    {
        switch (env)
        {
            case Env.Sahil: return new Vector3(0.15f, 0.07f, 1f).normalized;       // güneş denizin üstünde, ufukta
            case Env.Uzay: return new Vector3(0.6f, 0.25f, 0.75f).normalized;
            default: return new Vector3(0.45f, 0.32f, 0.83f).normalized;          // vadi: güneş / ay
        }
    }

    void Update()
    {
        if (!Active) return;
        foreach (var (t, basePos, phase, size) in fireflies)
        {
            t.localPosition = basePos + new Vector3(Mathf.Sin(Time.time * 0.31f + phase) * 0.8f,
                                                    Mathf.Sin(Time.time * 0.47f + phase * 1.7f) * 0.4f,
                                                    Mathf.Cos(Time.time * 0.27f + phase) * 0.8f);
            float blink = Mathf.Max(0f, Mathf.Sin(Time.time * 1.6f + phase * 3f));
            t.localScale = Vector3.one * size * (0.3f + 0.7f * blink);
        }
        foreach (var (t, basePos, phase) in cornerItems)
        {
            t.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * 1.2f + phase) * 0.025f;
            t.localRotation = FacingUser(basePos) * Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.8f + phase) * 18f, 0f);
        }
        foreach (var s in spinners) s.Rotate(0f, 1.2f * Time.deltaTime, 0f, Space.Self);   // göktaşı kuşağı yavaşça döner
    }

    void SetFade(float a)
    {
        fade.enabled = a > 0.001f;
        fadeMaterial.SetColor(ColorId, new Color(0f, 0f, 0f, Mathf.Clamp01(a)));
    }

    // Editörde önizleme için (GecitSetup.PreviewWorld)
    public Transform BuildForPreview(Env env)
    {
        var r = GetRoot(env);
        r.gameObject.SetActive(true);
        world = r;
        return r;
    }

    // Editör önizlemesi: ortamın gökyüzünü ve ışığını uygula
    public void PreviewApply(Env env)
    {
        world = GetRoot(env);
        ApplyLook(env, Vector3.zero);
    }

    Transform GetRoot(Env env)
    {
        if (roots.TryGetValue(env, out var r)) return r;
        world = new GameObject("Sanal Dunya - " + EnvNames[(int)env]).transform;
        SetPalette(env);
        if (env == Env.Sinema) BuildCinema();
        else if (HasPano(env) && env != Env.Uzay)
        {
            // Fotoğraf ortamı: yer ve gökyüzü fotoğrafta; yalnız canlı ayrıntılar (ateşböcekleri) ve oyun köşesi
            if (env == Env.GeceVadisi) BuildFireflies(true);
        }
        else
        {
            switch (env)
            {
                case Env.GeceVadisi: case Env.GunduzVadisi: BuildValley(env); break;
                case Env.Uzay: BuildSpace(); break;
                case Env.Sahil: BuildBeach(); break;
            }
        }
        if (env != Env.Sinema) BuildGameCorner();   // sinemada yalnız perde
        world.gameObject.SetActive(false);
        roots[env] = world;
        return world;
    }

    Material TerrainMat(Env env)
    {
        if (terrainMats.TryGetValue(env, out var m)) return m;
        m = new Material(mountainMaterial);
        switch (env)
        {
            case Env.GeceVadisi: m.SetColor("_Fog", new Color(0.06f, 0.08f, 0.14f)); m.SetFloat("_FogStart", 30f); m.SetFloat("_FogEnd", 900f); break;
            case Env.GunduzVadisi: m.SetColor("_Fog", new Color(0.78f, 0.81f, 0.85f)); m.SetFloat("_FogStart", 40f); m.SetFloat("_FogEnd", 1400f); break;
            case Env.Sahil: m.SetColor("_Fog", new Color(0.95f, 0.62f, 0.48f)); m.SetFloat("_FogStart", 60f); m.SetFloat("_FogEnd", 1300f); break;
            case Env.Uzay: case Env.Sinema: m.SetFloat("_FogStart", 100000f); m.SetFloat("_FogEnd", 200000f); break;
        }
        terrainMats[env] = m;
        return m;
    }

    void AddMesh(string name, Mesh mesh, Material mat = null)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(world, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat != null ? mat : TerrainMat(Current);
    }

    void AddDeck()
    {
        var deck = new GameObject("Teras", typeof(MeshFilter), typeof(MeshRenderer));
        deck.transform.SetParent(world, false);
        deck.GetComponent<MeshFilter>().sharedMesh = Disc(3.2f, 64);
        deck.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
    }

    // ================= Renk paleti ve ışık =================
    enum LightMode { Moon, Day, Sunset, Space }

    static class Pal
    {
        public static LightMode light;
        public static Vector3 lightDir;
        public static Color grassA, grassB, rock, snow, treeA, treeB, lakeCenter, lakeEdge, ridgeBase, ridgeTop, ridgeSnow;
        public static bool lanternGlow;
    }

    void SetPalette(Env env)
    {
        Current = env;   // kurulumda doğru pus malzemesi seçilsin
        Pal.lightDir = LightDir(env);
        if (env == Env.GeceVadisi)
        {
            Pal.light = LightMode.Moon;
            Pal.grassA = new Color(0.10f, 0.20f, 0.11f); Pal.grassB = new Color(0.17f, 0.27f, 0.13f);
            Pal.rock = new Color(0.26f, 0.26f, 0.29f); Pal.snow = new Color(0.75f, 0.80f, 0.92f);
            Pal.treeA = new Color(0.06f, 0.14f, 0.09f); Pal.treeB = new Color(0.10f, 0.20f, 0.12f);
            Pal.lakeCenter = new Color(0.20f, 0.28f, 0.42f); Pal.lakeEdge = new Color(0.05f, 0.08f, 0.15f);
            Pal.ridgeBase = new Color(0.03f, 0.04f, 0.08f); Pal.ridgeTop = new Color(0.09f, 0.11f, 0.18f); Pal.ridgeSnow = new Color(0.50f, 0.56f, 0.70f);
            Pal.lanternGlow = true;
        }
        else
        {
            Pal.light = env == Env.Sahil ? LightMode.Sunset : env == Env.Uzay ? LightMode.Space : LightMode.Day;
            Pal.grassA = new Color(0.22f, 0.38f, 0.14f); Pal.grassB = new Color(0.36f, 0.50f, 0.18f);
            Pal.rock = new Color(0.47f, 0.45f, 0.42f); Pal.snow = new Color(0.93f, 0.94f, 0.96f);
            Pal.treeA = new Color(0.08f, 0.20f, 0.10f); Pal.treeB = new Color(0.15f, 0.30f, 0.14f);
            Pal.lakeCenter = new Color(0.42f, 0.62f, 0.72f); Pal.lakeEdge = new Color(0.30f, 0.48f, 0.55f);
            Pal.ridgeBase = new Color(0.36f, 0.42f, 0.48f); Pal.ridgeTop = new Color(0.55f, 0.6f, 0.66f); Pal.ridgeSnow = new Color(0.92f, 0.94f, 0.97f);
            Pal.lanternGlow = false;
        }
    }

    static Color Shade(Color c, Vector3 n)
    {
        float d = Mathf.Max(0f, Vector3.Dot(n.normalized, Pal.lightDir));
        switch (Pal.light)
        {
            case LightMode.Moon: { float l = 0.22f + 0.5f * d; return new Color(c.r * l * 0.85f, c.g * l * 0.92f, c.b * l * 1.15f, 1f); }
            case LightMode.Sunset: { float l = 0.42f + 0.6f * d; return new Color(c.r * l * 1.08f, c.g * l * 0.86f, c.b * l * 0.72f, 1f); }
            case LightMode.Space: { float l = 0.06f + 0.95f * d; return new Color(c.r * l, c.g * l, c.b * l, 1f); }
            default: { float l = 0.62f + 0.42f * d; return new Color(c.r * l, c.g * l, c.b * l, 1f); }
        }
    }

    // ================= Vadi (gece / gündüz) =================
    void BuildValley(Env env)
    {
        AddDeck();
        AddMesh("Arazi", Terrain());
        AddMesh("Teras Kenari ve Saksilar", TerraceDetails(true));
        AddMesh("Agaclar", Trees());
        AddMesh("Uzak Daglar", FarRidges());
        AddMesh("Gol", Lake());
        if (env == Env.GeceVadisi) BuildFireflies();
    }

    // Özel sinema: karanlık zemin (perdenin önü hafif aydınlık), tek kırmızı kadife koltuk (kullanıcının altında)
    void BuildCinema()
    {
        var b = new Builder();
        // Zemin: ortada koyu gri, dışa doğru siyaha; perdeye doğru hafif mavimsi ışık
        for (int i = 0; i < 30; i++)
        {
            float r0 = i * 0.5f, r1 = (i + 1) * 0.5f;
            float k = Mathf.Pow(Mathf.Clamp01(1f - i / 29f), 1.6f);
            b.AddAnnulus(r0, r1, 0f, Color.Lerp(new Color(0.0006f, 0.0006f, 0.0009f), new Color(0.006f, 0.0065f, 0.009f), k), true);   // doğrusal renk: ekranda çok koyu
        }
        var cube = PrimitiveMesh(PrimitiveType.Cube);
        var velvet = new Color(0.17f, 0.005f, 0.008f);   // koyu kırmızı kadife (doğrusal renk)
        var dark = new Color(0.008f, 0.007f, 0.008f);
        Matrix4x4 M(float x, float y, float z, float sx, float sy, float sz, float rx = 0f) =>
            Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(rx, 0f, 0f), new Vector3(sx, sy, sz));
        b.Add(cube, M(0f, 0.20f, 0.05f, 0.60f, 0.40f, 0.55f), dark);              // gövde
        b.Add(cube, M(0f, 0.46f, 0.08f, 0.56f, 0.13f, 0.56f), velvet);            // oturak minderi
        b.Add(cube, M(0f, 0.85f, -0.24f, 0.56f, 0.78f, 0.13f, -8f), velvet);      // sırtlık
        b.Add(cube, M(0.34f, 0.58f, 0.06f, 0.11f, 0.12f, 0.60f), dark);           // kolçaklar
        b.Add(cube, M(-0.34f, 0.58f, 0.06f, 0.11f, 0.12f, 0.60f), dark);
        b.Add(cube, M(0.34f, 0.40f, 0.06f, 0.09f, 0.30f, 0.50f), velvet);
        b.Add(cube, M(-0.34f, 0.40f, 0.06f, 0.09f, 0.30f, 0.50f), velvet);
        AddMesh("Sinema Zemini ve Koltuk", b.Build("Sinema"), TerrainMat(Env.Sinema));
    }

    void BuildFireflies(bool flat = false)
    {
        var rng = new System.Random(8);
        for (int i = 0; i < 45; i++)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (Application.isPlaying) Destroy(o.GetComponent<Collider>()); else DestroyImmediate(o.GetComponent<Collider>());
            o.name = "Atesbocegi " + i;
            o.transform.SetParent(world, false);
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = 5f + (float)rng.NextDouble() * 30f;
            float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
            float ground = flat ? -0.4f : Mathf.Max(Height(x, z), -6f);   // fotoğrafta yer düz kabul edilir
            var pos = new Vector3(x, ground + 0.6f + (float)rng.NextDouble() * 3f, z);
            o.transform.localPosition = pos;
            float size = 0.05f + (float)rng.NextDouble() * 0.05f;
            o.transform.localScale = Vector3.one * size;
            o.GetComponent<Renderer>().sharedMaterial = orbMaterial;
            fireflies.Add((o.transform, pos, (float)rng.NextDouble() * 10f, size));
        }
    }

    static readonly Vector3 LakeCenter = new Vector3(-6f, 0f, 62f);
    const float LakeRadius = 34f, ValleyY = -28f;

    static float Noise(float x, float z, float s) => Mathf.PerlinNoise(x * s + 100f, z * s + 100f);

    static float Height(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float n = (Noise(x, z, 0.018f) - 0.5f) * 14f + (Noise(x, z, 0.07f) - 0.5f) * 3f;
        float drop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 26f, z + Mathf.Max(0f, Mathf.Abs(x) - 18f) * 0.5f));
        float near = -0.35f + (Noise(x, z, 0.2f) - 0.5f) * 0.3f;
        float h = Mathf.Lerp(near, ValleyY + n, drop);
        float behind = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 80f, -z)) * (18f + n * 1.5f);
        float ring = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(130f, 330f, r)) * (75f + (Noise(x, z, 0.012f) - 0.5f) * 90f);
        h += Mathf.Max(behind, 0f) + ring;
        float lake = Vector2.Distance(new Vector2(x, z), new Vector2(LakeCenter.x, LakeCenter.z));
        if (lake < LakeRadius + 8f) h = Mathf.Lerp(ValleyY - 1.5f, h, Mathf.SmoothStep(0f, 1f, (lake - LakeRadius) / 8f));
        return h;
    }

    // Kutupsal ızgara arazi (yakında sık, uzakta seyrek); yükseklik ve renk fonksiyonu verilir
    static Mesh PolarTerrain(string name, float maxRadius, System.Func<float, float, float> height, System.Func<Vector3, Vector3, Color> color)
    {
        const int seg = 128;
        var radii = new List<float> { 3.0f };
        while (radii[radii.Count - 1] < maxRadius) radii.Add(radii[radii.Count - 1] * 1.07f + 0.4f);
        int rings = radii.Count;
        var v = new Vector3[rings * seg];
        for (int i = 0; i < rings; i++)
            for (int s = 0; s < seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                float x = Mathf.Sin(a) * radii[i], z = Mathf.Cos(a) * radii[i];
                v[i * seg + s] = new Vector3(x, height(x, z), z);
            }
        var tris = new List<int>();
        for (int i = 0; i < rings - 1; i++)
            for (int s = 0; s < seg; s++)
            {
                int a = i * seg + s, b = i * seg + (s + 1) % seg, c = a + seg, d = b + seg;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        var m = new Mesh { name = name };
        m.vertices = v;
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        var normals = m.normals;
        var colors = new Color[v.Length];
        for (int k = 0; k < v.Length; k++) colors[k] = Shade(color(v[k], normals[k]), normals[k]);
        m.colors = colors;
        m.RecalculateBounds();
        return m;
    }

    static Mesh Terrain() => PolarTerrain("Arazi", 520f, Height, (p, n) =>
    {
        float slope = 1f - Mathf.Abs(n.y);
        var grass = Color.Lerp(Pal.grassA, Pal.grassB, Noise(p.x, p.z, 0.05f));
        var c = Color.Lerp(grass, Pal.rock, Mathf.SmoothStep(0f, 1f, (slope - 0.25f) * 3f));
        if (p.y > 55f) c = Color.Lerp(c, Pal.snow, Mathf.SmoothStep(0f, 1f, (p.y - 55f) / 25f));
        if (Mathf.Abs(p.x) < 3.5f && Mathf.Abs(p.z) < 3.5f) c *= 0.8f;
        return c;
    });

    static Mesh Lake()
    {
        const int seg = 64;
        var v = new List<Vector3> { new Vector3(LakeCenter.x, ValleyY - 1.0f, LakeCenter.z) };
        var c = new List<Color> { Pal.lakeCenter };
        var tris = new List<int>();
        for (int s = 0; s < seg; s++)
        {
            float a = s * Mathf.PI * 2f / seg;
            float r = LakeRadius * (0.92f + 0.12f * Mathf.PerlinNoise(s * 0.3f, 7f));
            v.Add(new Vector3(LakeCenter.x + Mathf.Sin(a) * r, ValleyY - 1.0f, LakeCenter.z + Mathf.Cos(a) * r));
            c.Add(Pal.lakeEdge);
        }
        for (int s = 0; s < seg; s++) tris.AddRange(new[] { 0, 1 + s, 1 + (s + 1) % seg });
        var m = new Mesh { name = "Gol" };
        m.SetVertices(v); m.SetColors(c); m.SetTriangles(tris, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh Trees()
    {
        var b = new Builder();
        var rng = new System.Random(21);
        int placed = 0;
        for (int tries = 0; tries < 4000 && placed < 420; tries++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = 8f + (float)rng.NextDouble() * 230f;
            float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
            if (r < 22f) continue;
            if (Vector2.Distance(new Vector2(x, z), new Vector2(LakeCenter.x, LakeCenter.z)) < LakeRadius + 3f) continue;
            float h = Height(x, z);
            if (h > 50f) continue;
            if (Noise(x, z, 0.03f) < 0.42f) continue;
            float height = 6f + (float)rng.NextDouble() * 9f;
            b.AddPine(new Vector3(x, h - 0.3f, z), height, Color.Lerp(Pal.treeA, Pal.treeB, (float)rng.NextDouble()));
            placed++;
        }
        return b.Build("Agaclar");
    }

    static Mesh TerraceDetails(bool rail)
    {
        var b = new Builder();
        var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
        var sph = PrimitiveMesh(PrimitiveType.Sphere);
        b.Add(cyl, Matrix4x4.TRS(new Vector3(0f, -0.09f, 0f), Quaternion.identity, new Vector3(6.5f, 0.085f, 6.5f)), new Color(0.24f, 0.17f, 0.12f));
        var rng = new System.Random(5);
        float[] angles = { 70f, 115f, -70f, -115f, 160f, -160f };
        foreach (var deg in angles)
        {
            float a = deg * Mathf.Deg2Rad;
            var p = new Vector3(Mathf.Sin(a) * 2.6f, 0f, Mathf.Cos(a) * 2.6f);
            float s = 0.8f + (float)rng.NextDouble() * 0.4f;
            b.Add(cyl, Matrix4x4.TRS(p + Vector3.up * 0.16f * s, Quaternion.identity, new Vector3(0.46f, 0.16f, 0.46f) * s), new Color(0.42f, 0.24f, 0.16f));
            for (int k = 0; k < 5; k++)
            {
                var off = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.25f * s;
                float ls = (0.22f + (float)rng.NextDouble() * 0.18f) * s;
                var green = Color.Lerp(new Color(0.18f, 0.40f, 0.18f), new Color(0.32f, 0.55f, 0.24f), (float)rng.NextDouble());
                b.Add(sph, Matrix4x4.TRS(p + off + Vector3.up * (0.38f + k * 0.06f) * s, Quaternion.Euler(0f, k * 40f, 0f), new Vector3(ls, ls * 1.2f, ls) * 1.3f), green);
            }
        }
        if (rail)
        {
            var railColor = new Color(0.30f, 0.22f, 0.16f);
            for (int i = -6; i <= 6; i++)
            {
                float a = i * 12f * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a) * 3.15f, 0f, Mathf.Cos(a) * 3.15f);
                b.Add(cyl, Matrix4x4.TRS(p + Vector3.up * 0.45f, Quaternion.identity, new Vector3(0.05f, 0.45f, 0.05f)), railColor);
                if (i % 3 == 0)
                    b.Add(sph, Matrix4x4.TRS(p + Vector3.up * 1.0f, Quaternion.identity, Vector3.one * 0.09f), Pal.lanternGlow ? new Color(1f, 0.78f, 0.42f) : new Color(0.85f, 0.8f, 0.7f), Pal.lanternGlow);
                if (i < 6)
                {
                    float a2 = (i + 1) * 12f * Mathf.Deg2Rad;
                    var p2 = new Vector3(Mathf.Sin(a2) * 3.15f, 0f, Mathf.Cos(a2) * 3.15f);
                    var mid = (p + p2) * 0.5f + Vector3.up * 0.9f;
                    var dir = p2 - p;
                    b.Add(cyl, Matrix4x4.TRS(mid, Quaternion.FromToRotation(Vector3.up, dir.normalized), new Vector3(0.06f, dir.magnitude * 0.5f, 0.06f)), railColor);
                }
            }
        }
        return b.Build("Teras Detaylari");
    }

    static Mesh FarRidges()
    {
        var b = new Builder();
        const int seg = 220;
        float[] radii = { 650f, 900f };
        for (int layer = 0; layer < radii.Length; layer++)
        {
            float rad = radii[layer];
            int start = b.v.Count;
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                float n = Mathf.PerlinNoise(s * 0.09f + layer * 40f, 3.3f) * 0.7f + Mathf.PerlinNoise(s * 0.31f + layer * 10f, 9.1f) * 0.3f;
                float top = 60f + n * (layer == 0 ? 170f : 230f);
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                b.v.Add(dir * rad + Vector3.up * -40f);
                b.c.Add(Pal.ridgeBase);
                b.v.Add(dir * rad + Vector3.up * top);
                b.c.Add(top > 150f ? Pal.ridgeSnow : Pal.ridgeTop);
            }
            for (int s = 0; s < seg; s++)
            {
                int a0 = start + s * 2, a1 = start + (s + 1) * 2;
                b.t.AddRange(new[] { a0, a0 + 1, a1, a1, a0 + 1, a1 + 1 });
            }
        }
        return b.Build("Uzak Daglar");
    }

    // ================= Uzay =================
    void BuildSpace()
    {
        var b = new Builder();
        // Platform: koyu metal halkalar, parlayan camgöbeği çizgiler, kenarında ışıklı çember
        var metal = new Color(0.16f, 0.17f, 0.21f);
        var metal2 = new Color(0.21f, 0.22f, 0.27f);
        var glow = new Color(0.25f, 0.85f, 1f);
        b.AddAnnulus(0f, 0.9f, 0f, metal2, false);
        b.AddAnnulus(0.9f, 0.93f, 0.002f, glow, true);
        b.AddAnnulus(0.93f, 2.0f, 0f, metal, false);
        b.AddAnnulus(2.0f, 2.03f, 0.002f, glow, true);
        b.AddAnnulus(2.03f, 3.1f, 0f, metal2, false);
        b.AddAnnulus(3.1f, 3.2f, 0.004f, glow, true);
        // Platformun altı: içe daralan koni (kalınlık)
        b.AddCone(3.2f, 0f, -1.4f, new Color(0.10f, 0.11f, 0.14f));
        AddMesh("Platform", b.Build("Platform"), TerrainMat(Env.Uzay));

        // Halkalı dev gezegen
        var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        if (Application.isPlaying) Destroy(planet.GetComponent<Collider>()); else DestroyImmediate(planet.GetComponent<Collider>());
        planet.name = "Gezegen";
        planet.transform.SetParent(world, false);
        planet.transform.localPosition = new Vector3(-170f, 40f, 360f);
        planet.transform.localScale = Vector3.one * 220f;
        planet.GetComponent<Renderer>().sharedMaterial = planetMaterial;

        var rings = new Builder();
        var rot = Quaternion.Euler(18f, 0f, -12f);
        for (int k = 0; k < 7; k++)
        {
            float r0 = 140f + k * 13f, r1 = r0 + 11f;
            var band = Color.Lerp(new Color(0.70f, 0.60f, 0.45f), new Color(0.45f, 0.38f, 0.30f), (k % 3) / 2f) * (0.55f + 0.1f * (k % 2));
            rings.AddAnnulus(r0, r1, 0f, band, true, 128, rot, new Vector3(-170f, 40f, 360f));
        }
        AddMesh("Gezegen Halkasi", rings.Build("Halka"), TerrainMat(Env.Uzay));

        // Göktaşları: yavaşça dönen bir kuşak
        var belt = new GameObject("Goktasi Kusagi").transform;
        belt.SetParent(world, false);
        spinners.Add(belt);
        var rocks = new Builder();
        var sph = PrimitiveMesh(PrimitiveType.Sphere);
        var rng = new System.Random(13);
        for (int i = 0; i < 70; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = 14f + (float)rng.NextDouble() * 110f;
            var pos = new Vector3(Mathf.Sin(a) * r, -8f + (float)rng.NextDouble() * 24f, Mathf.Cos(a) * r);
            if (pos.z > 0f && Mathf.Abs(pos.x) < 6f && r < 30f) continue;   // önün açık kalsın
            float size = 0.4f + Mathf.Pow((float)rng.NextDouble(), 2f) * 4f;
            var tone = Color.Lerp(new Color(0.40f, 0.36f, 0.33f), new Color(0.55f, 0.50f, 0.45f), (float)rng.NextDouble());
            rocks.AddRock(sph, pos, size, Random.rotation, tone, rng);
        }
        var go = new GameObject("Goktaslari", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(belt, false);
        go.GetComponent<MeshFilter>().sharedMesh = rocks.Build("Goktaslari");
        go.GetComponent<MeshRenderer>().sharedMaterial = TerrainMat(Env.Uzay);
    }

    // ================= Sahil =================
    static float BeachHeight(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float h = -0.3f + (Noise(x, z, 0.15f) - 0.5f) * 0.15f;
        h = Mathf.Lerp(h, -2.6f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 24f, z)));      // önde denize iner
        float dunes = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 50f, -z)) * (2.5f + (Noise(x, z, 0.06f) - 0.5f) * 3f);
        float sides = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 90f, Mathf.Abs(x))) * (z < 60f ? 6f : 0f);
        float hills = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(140f, 420f, r)) * (z < 0f ? 40f + (Noise(x, z, 0.01f) - 0.5f) * 40f : 0f);
        return h + Mathf.Max(0f, dunes) + sides + hills;
    }

    void BuildBeach()
    {
        AddDeck();
        AddMesh("Teras Saksilar", TerraceDetails(false));
        AddMesh("Kum", PolarTerrain("Kum", 460f, BeachHeight, (p, n) =>
        {
            var sand = Color.Lerp(new Color(0.86f, 0.74f, 0.55f), new Color(0.80f, 0.68f, 0.50f), Noise(p.x, p.z, 0.1f));
            if (p.y < -0.6f) sand = Color.Lerp(sand, new Color(0.55f, 0.45f, 0.35f), Mathf.InverseLerp(-0.6f, -1.1f, p.y));   // ıslak kum
            if (p.y > 1.5f) sand = Color.Lerp(sand, new Color(0.42f, 0.50f, 0.25f), Mathf.InverseLerp(1.5f, 4f, p.y) * 0.8f); // otlu kumul
            if (p.y > 15f) sand = Color.Lerp(sand, new Color(0.30f, 0.38f, 0.22f), Mathf.InverseLerp(15f, 30f, p.y));      // tepeler
            return sand;
        }));

        // Deniz
        var sea = new GameObject("Deniz", typeof(MeshFilter), typeof(MeshRenderer));
        sea.transform.SetParent(world, false);
        sea.transform.localPosition = new Vector3(0f, -1.0f, 300f);
        sea.GetComponent<MeshFilter>().sharedMesh = Disc(950f, 96);
        sea.GetComponent<MeshRenderer>().sharedMaterial = seaMaterial;

        // Palmiyeler
        var b = new Builder();
        var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
        var sph = PrimitiveMesh(PrimitiveType.Sphere);
        var rng = new System.Random(31);
        Vector3[] spots =
        {
            new Vector3(-6f, 0f, 2f), new Vector3(-9f, 0f, -4f), new Vector3(7f, 0f, 1f), new Vector3(10f, 0f, -6f),
            new Vector3(-4f, 0f, -10f), new Vector3(4f, 0f, -13f), new Vector3(-15f, 0f, 3f), new Vector3(16f, 0f, 5f), new Vector3(0f, 0f, -18f),
        };
        foreach (var s in spots)
        {
            var basePos = new Vector3(s.x, BeachHeight(s.x, s.z) - 0.1f, s.z);
            b.AddPalm(cyl, sph, basePos, 6f + (float)rng.NextDouble() * 3f, rng);
        }
        // Ufukta adalar
        for (int i = 0; i < 5; i++)
        {
            float a = (-50f + i * 25f + (float)rng.NextDouble() * 10f) * Mathf.Deg2Rad;
            float d = 520f + (float)rng.NextDouble() * 250f;
            var pos = new Vector3(Mathf.Sin(a) * d, -6f, Mathf.Cos(a) * d + 300f);
            float w = 50f + (float)rng.NextDouble() * 70f;
            b.Add(sph, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(w, 22f + (float)rng.NextDouble() * 25f, w * 0.6f)), new Color(0.32f, 0.22f, 0.32f));
        }
        AddMesh("Palmiyeler ve Adalar", b.Build("Palmiyeler"));
    }

    // ================= Oyun köşesi =================
    void BuildGameCorner()
    {
        var corner = new GameObject("Oyun Kosesi").transform;
        corner.SetParent(world, false);
        var title = MiniGames.MakeText(corner, "Baslik", 0.006f, new Color(0.9f, 0.92f, 1f, 0.9f));
        title.text = "Oyunlar";
        PlaceLabel(title.transform, new Vector3(1.55f, 1.45f, 0.15f));

        AddCornerItem(corner, "Işın Kılıcı", new Vector3(1.0f, 1.05f, 1.15f), BuildSaberIcon, () => { if (saber != null) saber.Begin(); });
        AddCornerItem(corner, "Balon Patlat", new Vector3(1.38f, 1.05f, 0.7f), BuildBalloonIcon, () => { if (balloon != null) balloon.Begin(); });
        AddCornerItem(corner, "Hedef Vur", new Vector3(1.58f, 1.05f, 0.2f), BuildTargetIcon, () => { if (target != null) target.Begin(); });
        AddCornerItem(corner, "Köstebek Vur", new Vector3(1.58f, 1.05f, -0.32f), BuildMoleIcon, () => { if (whack != null) whack.Begin(); });
        AddCornerItem(corner, "Basket", new Vector3(1.38f, 1.05f, -0.82f), BuildBasketIcon, () => { if (basket != null) basket.Begin(); });
    }

    static Quaternion FacingUser(Vector3 localPos)
    {
        var away = new Vector3(localPos.x, 0f, localPos.z);
        return away.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(away.normalized, Vector3.up) : Quaternion.identity;
    }

    void PlaceLabel(Transform t, Vector3 localPos)
    {
        t.localPosition = localPos;
        var toUser = localPos - new Vector3(0f, localPos.y, 0f);
        t.localRotation = Quaternion.LookRotation(toUser.normalized, Vector3.up);
    }

    void AddCornerItem(Transform corner, string label, Vector3 localPos, System.Action<Transform> build, System.Action start)
    {
        var item = new GameObject(label).transform;
        item.SetParent(corner, false);
        item.localPosition = localPos;
        item.localRotation = FacingUser(localPos);
        build(item);
        IconPart(item, PrimitiveType.Cylinder, new Color(0.3f, 0.85f, 1f, 1f), new Vector3(0f, -0.17f, 0f), new Vector3(0.2f, 0.002f, 0.2f), Vector3.zero);   // ışıklı taban
        var click = item.gameObject.AddComponent<ClickTarget>();
        click.radius = 0.17f;
        click.onClick = _ => start();
        cornerItems.Add((item, localPos, localPos.x * 3f + localPos.z));

        var text = MiniGames.MakeText(corner, label + " Yazi", 0.0042f, new Color(0.75f, 0.92f, 1f, 0.95f));
        text.text = label;
        PlaceLabel(text.transform, localPos + Vector3.down * 0.24f);
    }

    Transform IconPart(Transform parent, PrimitiveType type, Color color, Vector3 pos, Vector3 scale, Vector3 euler)
    {
        var g = GameObject.CreatePrimitive(type);
        if (Application.isPlaying) Destroy(g.GetComponent<Collider>()); else DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.transform.localRotation = Quaternion.Euler(euler);
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = holoMaterial != null ? holoMaterial : gameMaterial;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        block.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.6f));
        r.SetPropertyBlock(block);
        return g.transform;
    }

    void BuildSaberIcon(Transform t)
    {
        IconPart(t, PrimitiveType.Cylinder, new Color(0.2f, 0.2f, 0.22f, 1f), new Vector3(0f, -0.09f, 0f), new Vector3(0.03f, 0.04f, 0.03f), new Vector3(0f, 0f, 25f));
        IconPart(t, PrimitiveType.Cylinder, new Color(0.45f, 0.8f, 1f, 1f), new Vector3(-0.045f, 0.04f, 0f), new Vector3(0.018f, 0.12f, 0.018f), new Vector3(0f, 0f, 25f));
    }

    void BuildBalloonIcon(Transform t)
    {
        IconPart(t, PrimitiveType.Sphere, new Color(0.95f, 0.25f, 0.3f, 1f), new Vector3(0f, 0.02f, 0f), new Vector3(0.15f, 0.19f, 0.15f), Vector3.zero);
        IconPart(t, PrimitiveType.Cylinder, new Color(0.9f, 0.9f, 0.9f, 1f), new Vector3(0f, -0.14f, 0f), new Vector3(0.004f, 0.07f, 0.004f), Vector3.zero);
    }

    void BuildTargetIcon(Transform t)
    {
        IconPart(t, PrimitiveType.Cylinder, new Color(0.9f, 0.2f, 0.22f, 1f), Vector3.zero, new Vector3(0.24f, 0.008f, 0.24f), new Vector3(90f, 0f, 0f));
        IconPart(t, PrimitiveType.Cylinder, new Color(0.95f, 0.95f, 0.95f, 1f), new Vector3(0f, 0f, -0.004f), new Vector3(0.16f, 0.008f, 0.16f), new Vector3(90f, 0f, 0f));
        IconPart(t, PrimitiveType.Cylinder, new Color(1f, 0.85f, 0.2f, 1f), new Vector3(0f, 0f, -0.008f), new Vector3(0.07f, 0.008f, 0.07f), new Vector3(90f, 0f, 0f));
    }

    void BuildMoleIcon(Transform t)
    {
        IconPart(t, PrimitiveType.Cylinder, new Color(0.25f, 0.45f, 0.2f, 1f), new Vector3(0f, -0.09f, 0f), new Vector3(0.2f, 0.015f, 0.2f), Vector3.zero);
        IconPart(t, PrimitiveType.Capsule, new Color(0.55f, 0.38f, 0.25f, 1f), new Vector3(0f, -0.01f, 0f), new Vector3(0.09f, 0.08f, 0.09f), Vector3.zero);
        IconPart(t, PrimitiveType.Sphere, Color.white, new Vector3(-0.02f, 0.03f, -0.04f), Vector3.one * 0.02f, Vector3.zero);
        IconPart(t, PrimitiveType.Sphere, Color.white, new Vector3(0.02f, 0.03f, -0.04f), Vector3.one * 0.02f, Vector3.zero);
        IconPart(t, PrimitiveType.Sphere, new Color(0.95f, 0.55f, 0.6f, 1f), new Vector3(0f, 0.01f, -0.045f), Vector3.one * 0.018f, Vector3.zero);
    }

    void BuildBasketIcon(Transform t)
    {
        IconPart(t, PrimitiveType.Sphere, new Color(0.95f, 0.5f, 0.15f, 1f), new Vector3(0f, -0.02f, 0f), Vector3.one * 0.15f, Vector3.zero);
        IconPart(t, PrimitiveType.Cylinder, new Color(0.15f, 0.1f, 0.08f, 1f), new Vector3(0f, -0.02f, -0.001f), new Vector3(0.152f, 0.004f, 0.152f), new Vector3(90f, 0f, 0f));
        IconPart(t, PrimitiveType.Cylinder, new Color(0.15f, 0.1f, 0.08f, 1f), new Vector3(0f, -0.02f, 0f), new Vector3(0.004f, 0.076f, 0.152f), Vector3.zero);
    }

    // ================= Yardımcılar =================
    static Mesh Disc(float radius, int segments)
    {
        var v = new Vector3[segments + 1];
        var tris = new int[segments * 3];
        v[0] = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            v[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            tris[i * 3] = 0;
            tris[i * 3 + 1] = 1 + (i + 1) % segments;
            tris[i * 3 + 2] = 1 + i;
        }
        var m = new Mesh { name = "Disk", vertices = v, triangles = tris };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        return mesh;
    }

    // Köşe renkli parça birleştirici
    class Builder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Color> c = new List<Color>();
        public readonly List<int> t = new List<int>();

        public void Add(Mesh prim, Matrix4x4 m, Color color, bool glow = false)
        {
            int start = v.Count;
            var pv = prim.vertices; var pn = prim.normals;
            for (int i = 0; i < pv.Length; i++)
            {
                v.Add(m.MultiplyPoint3x4(pv[i]));
                c.Add(glow ? color : Shade(color, m.MultiplyVector(pn[i])));
            }
            foreach (var i in prim.triangles) t.Add(start + i);
        }

        // Çam: 6 kenarlı koni (gövde) + ince kök
        public void AddPine(Vector3 basePos, float height, Color green)
        {
            const int sides = 6;
            float radius = height * 0.28f;
            var top = basePos + Vector3.up * height;
            var trunkTop = basePos + Vector3.up * height * 0.18f;
            int start = v.Count;
            v.Add(top); c.Add(Shade(green * 1.15f, Vector3.up));
            for (int s = 0; s < sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(trunkTop + dir * radius);
                c.Add(Shade(green, dir + Vector3.up * 0.6f));
            }
            for (int s = 0; s < sides; s++) t.AddRange(new[] { start, start + 1 + (s + 1) % sides, start + 1 + s });
            int ks = v.Count;
            var brown = new Color(0.28f, 0.2f, 0.14f);
            for (int s = 0; s < 4; s++)
            {
                float a = s * Mathf.PI * 0.5f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.15f;
                v.Add(basePos + dir); c.Add(Shade(brown, dir));
                v.Add(trunkTop + dir); c.Add(Shade(brown, dir));
            }
            for (int s = 0; s < 4; s++)
            {
                int a0 = ks + s * 2, a1 = ks + ((s + 1) % 4) * 2;
                t.AddRange(new[] { a0, a0 + 1, a1, a1, a0 + 1, a1 + 1 });
            }
        }

        // Halka (yassı, y yüksekliğinde); isteğe bağlı döndürme ve merkez
        public void AddAnnulus(float r0, float r1, float y, Color color, bool glow, int seg = 96, Quaternion? rot = null, Vector3 center = default)
        {
            var q = rot ?? Quaternion.identity;
            int start = v.Count;
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(center + q * (dir * r0 + Vector3.up * y));
                v.Add(center + q * (dir * r1 + Vector3.up * y));
                var col = glow ? color : Shade(color, q * Vector3.up);
                c.Add(col); c.Add(col);
            }
            for (int s = 0; s < seg; s++)
            {
                int a0 = start + s * 2, a1 = start + (s + 1) * 2;
                t.AddRange(new[] { a0, a1, a0 + 1, a0 + 1, a1, a1 + 1 });
            }
        }

        // Ters koni (platform altı)
        public void AddCone(float radius, float yTop, float yTip, Color color, int seg = 64)
        {
            int tip = v.Count;
            v.Add(new Vector3(0f, yTip, 0f)); c.Add(Shade(color, Vector3.down));
            for (int s = 0; s < seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(dir * radius + Vector3.up * yTop);
                c.Add(Shade(color, dir - Vector3.up * 0.5f));
            }
            for (int s = 0; s < seg; s++) t.AddRange(new[] { tip, tip + 1 + s, tip + 1 + (s + 1) % seg });
        }

        // Göktaşı: gürültüyle bozulmuş küre
        public void AddRock(Mesh sphere, Vector3 pos, float size, Quaternion rot, Color color, System.Random rng)
        {
            int start = v.Count;
            var pv = sphere.vertices;
            float seed = (float)rng.NextDouble() * 100f;
            var disp = new Vector3[pv.Length];
            for (int i = 0; i < pv.Length; i++)
            {
                var p = pv[i];
                float n = Mathf.PerlinNoise(p.x * 3f + seed, p.y * 3f + p.z * 2f + seed);
                disp[i] = pos + rot * (p * size * (0.75f + 0.5f * n));
            }
            var tmp = new Mesh { vertices = disp, triangles = sphere.triangles };
            tmp.RecalculateNormals();
            var nn = tmp.normals;
            for (int i = 0; i < pv.Length; i++) { v.Add(disp[i]); c.Add(Shade(color, nn[i])); }
            foreach (var i in sphere.triangles) t.Add(start + i);
        }

        // Palmiye: hafif eğik, kıvrılan gövde + sarkan yapraklar
        public void AddPalm(Mesh cyl, Mesh sph, Vector3 basePos, float height, System.Random rng)
        {
            var lean = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f).normalized;
            var trunk = new Color(0.45f, 0.33f, 0.22f);
            const int segs = 9;
            Vector3 prev = basePos;
            for (int i = 1; i <= segs; i++)
            {
                float k = i / (float)segs;
                var p = basePos + Vector3.up * height * k + lean * height * 0.25f * k * k;
                var d = p - prev;
                float r = Mathf.Lerp(0.22f, 0.13f, k);
                Add(cyl, Matrix4x4.TRS((p + prev) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized), new Vector3(r, d.magnitude * 0.55f, r)), trunk * (i % 2 == 0 ? 1f : 0.85f));
                prev = p;
            }
            var top = prev;
            for (int f = 0; f < 8; f++)
            {
                float a = f * Mathf.PI * 2f / 8f + (float)rng.NextDouble() * 0.3f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var leaf = Color.Lerp(new Color(0.16f, 0.38f, 0.16f), new Color(0.26f, 0.48f, 0.2f), (float)rng.NextDouble());
                for (int s = 0; s < 4; s++)
                {
                    float k = (s + 1) / 4f;
                    var p = top + dir * 1.6f * k + Vector3.up * (0.5f * k - 1.3f * k * k);
                    var rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(30f * k, 0f, 0f);
                    Add(sph, Matrix4x4.TRS(p, rot, new Vector3(0.45f * (1.1f - k * 0.5f), 0.06f, 0.75f)), leaf);
                }
            }
            Add(sph, Matrix4x4.TRS(top, Quaternion.identity, Vector3.one * 0.35f), new Color(0.35f, 0.25f, 0.15f));   // hindistancevizleri
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
