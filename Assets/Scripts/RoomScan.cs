using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Oda tarama (Meta'daki "alan ayarı" gibi): menüden "Oda tara" ile başlar. ARCore derinliği (telefonda derinlik
// sensörü yok; kamera hareketinden hesaplanır) 6 cm'lik bloklara çevrilir, oda holografik küplerle belirir.
// Önündeki panel ilerlemeyi gösterir; "Bitti" ile biter, bloklar yavaşça kaybolur (harita bellekte kalır).
// Derinlik yalnız tarama sırasında açıktır (işlemciyi boşuna yormasın).
public class RoomScan : MonoBehaviour
{
    public static RoomScan Instance;
    public static bool Scanning;
    public static bool HasScan;
    public static float FloorY;
    public static int VoxelCount => Instance != null ? Instance.matrices.Count : 0;

    public ARCameraManager cameraManager;
    public AROcclusionManager occlusion;
    public Camera arCamera;
    public Transform head;
    public Material voxelMaterial;   // Gecit/ScanVoxel (instancing açık)

    public float voxelSize = 0.06f;
    public int maxVoxels = 15000;
    public int targetVoxels = 5000;   // %100 sayılan miktar
    public int pixelStep = 4;
    public int everyNthFrame = 3;
    public float minDepth = 0.25f, maxDepth = 4.5f;

    readonly Dictionary<Vector3Int, byte> hits = new Dictionary<Vector3Int, byte>();
    readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
    readonly List<float> heights = new List<float>();
    Mesh cube;
    Matrix4x4 displayMatrix;
    bool hasMatrix;
    int frame, depthFrames;
    float startedAt, alpha, alphaTarget;
    static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    static readonly int FloorId = Shader.PropertyToID("_Floor");

    // Panel
    Transform panel;
    CanvasGroup group;
    Text title, info;
    RectTransform bar;
    Image barImg;

    void Awake() { Instance = this; }

    void Start()
    {
        cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        if (occlusion != null) occlusion.enabled = false;
        BuildPanel();
    }

    void OnEnable() { if (cameraManager != null) cameraManager.frameReceived += OnFrame; }
    void OnDisable() { if (cameraManager != null) cameraManager.frameReceived -= OnFrame; }

    void OnFrame(ARCameraFrameEventArgs args)
    {
        if (!args.displayMatrix.HasValue) return;
        displayMatrix = args.displayMatrix.Value;
        hasMatrix = true;
    }

    public void StartScan()
    {
        hits.Clear();
        matrices.Clear();
        heights.Clear();
        depthFrames = 0;
        Scanning = true;
        startedAt = Time.time;
        alphaTarget = 1f;
        if (occlusion != null)
        {
            occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Medium;
            occlusion.environmentDepthTemporalSmoothingRequested = true;
            occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;   // görüntüyü değiştirmesin
            occlusion.enabled = true;
        }
        PlacePanel();
        Debug.Log("GECIT TARAMA basla");
    }

    public void Finish()
    {
        if (!Scanning) return;
        Scanning = false;
        if (occlusion != null) occlusion.enabled = false;
        HasScan = matrices.Count > 0;
        alphaTarget = 0f;   // bloklar yavaşça kaybolur
        Debug.Log($"GECIT TARAMA bitti blok={matrices.Count} zemin={FloorY:F2}");
    }

    public void Cancel()
    {
        Finish();
        hits.Clear();
        matrices.Clear();
        HasScan = false;
    }

    void Update()
    {
        if (Scanning && ++frame % everyNthFrame == 0) Integrate();
        if (Scanning && matrices.Count >= maxVoxels) Finish();

        alpha = Mathf.MoveTowards(alpha, alphaTarget, Time.deltaTime * (alphaTarget > alpha ? 3f : 0.35f));
        if (alpha > 0.01f && matrices.Count > 0 && cube != null && voxelMaterial != null)
        {
            voxelMaterial.SetFloat(AlphaId, alpha);
            voxelMaterial.SetFloat(FloorId, FloorY);
            var rp = new RenderParams(voxelMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            for (int start = 0; start < matrices.Count; start += 1023)
                Graphics.RenderMeshInstanced(rp, cube, 0, matrices, Mathf.Min(1023, matrices.Count - start), start);
        }
        UpdatePanel();
    }

    void Integrate()
    {
        if (occlusion == null || !hasMatrix) return;
        if (!occlusion.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image)) return;
        using (image)
        {
            depthFrames++;
            int w = image.width, h = image.height;
            var plane = image.GetPlane(0);
            var data = plane.data;
            int rowStride = plane.rowStride, pixelStride = plane.pixelStride;
            bool isFloat = image.format == XRCpuImage.Format.DepthFloat32;
            for (int r = pixelStep / 2; r < h; r += pixelStep)
            {
                for (int c = pixelStep / 2; c < w; c += pixelStep)
                {
                    int i = r * rowStride + c * pixelStride;
                    float d = isFloat ? data.ReinterpretLoad<float>(i) : data.ReinterpretLoad<ushort>(i) * 0.001f;
                    if (d < minDepth || d > maxDepth) continue;
                    var vp = ImageToViewport(new Vector2((c + 0.5f) / w, (r + 0.5f) / h));
                    var world = arCamera.ViewportToWorldPoint(new Vector3(vp.x, vp.y, d));
                    var key = Vector3Int.FloorToInt(world / voxelSize);
                    hits.TryGetValue(key, out byte count);
                    if (count >= 3) continue;
                    hits[key] = ++count;
                    if (count != 3) continue;   // gürültü olmasın: aynı blok 3 kez görülmeli
                    var center = ((Vector3)key + Vector3.one * 0.5f) * voxelSize;
                    matrices.Add(Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * voxelSize * 0.86f));
                    heights.Add(center.y);
                    if (matrices.Count >= maxVoxels) return;
                }
            }
        }
        if (heights.Count > 50 && frame % 30 == 0)
        {
            // Zemin: en alttaki yoğun katman (alt %4)
            var sorted = new List<float>(heights);
            sorted.Sort();
            FloorY = sorted[sorted.Count / 25];
        }
    }

    // Kamera görüntüsü koordinatı (sol üst başlangıç) -> ekran (viewport): displayMatrix'in tersi (HandTracker ile aynı)
    Vector2 ImageToViewport(Vector2 p)
    {
        var m = displayMatrix;
        float a = m.m00, b = m.m10, c = m.m01, d = m.m11;
        float du = p.x - m.m20, dv = p.y - m.m21;
        float det = a * d - b * c;
        if (Mathf.Abs(det) < 1e-6f) return p;
        return new Vector2((d * du - b * dv) / det, (-c * du + a * dv) / det);
    }

    // ---- Panel: ilerleme + Bitti / İptal ----
    void BuildPanel()
    {
        panel = new GameObject("Oda Tarama Paneli").transform;
        var c = UiKit.WorldCanvas("Tuval", panel, new Vector2(460f, 170f), 0.001f);
        group = c.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        var t = c.transform;
        UiKit.Box(t, "Zemin", Vector2.zero, new Vector2(460f, 170f), new Color(0.05f, 0.07f, 0.1f, 0.88f), null, 1.4f);
        UiKit.Holo(t, "Isikli Cerceve", Vector2.zero, new Vector2(460f, 170f), 28f, new Color(0.3f, 0.82f, 1f, 0.7f), 24f, 1.4f);
        var icon = UiKit.Box(t, "Simge", new Vector2(-190f, 48f), new Vector2(40f, 40f), new Color(0.3f, 0.82f, 1f, 1f), UiKit.GetIcon(UiKit.Icon.Scan));
        icon.preserveAspect = true;
        title = UiKit.Label(t, "Oda taranıyor", 24, Color.white, new Vector2(10f, 50f), new Vector2(340f, 34f));
        title.alignment = TextAnchor.MiddleLeft;
        title.rectTransform.anchoredPosition = new Vector2(0f, 50f);
        info = UiKit.Label(t, "", 16, new Color(0.85f, 0.92f, 1f, 0.9f), new Vector2(0f, 12f), new Vector2(420f, 40f));
        info.horizontalOverflow = HorizontalWrapMode.Wrap;
        UiKit.Box(t, "Cubuk Zemin", new Vector2(0f, -22f), new Vector2(400f, 10f), new Color(1f, 1f, 1f, 0.12f), null, 8f);
        barImg = UiKit.Box(t, "Cubuk", new Vector2(-200f, -22f), new Vector2(0f, 10f), new Color(0.3f, 0.82f, 1f, 1f), null, 8f);
        bar = barImg.rectTransform;
        bar.pivot = new Vector2(0f, 0.5f);
        Pill(t, "Bitti", new Vector2(80f, -60f), new Color(0.2f, 0.5f, 0.95f, 1f), Finish);
        Pill(t, "İptal", new Vector2(-80f, -60f), new Color(0.22f, 0.24f, 0.3f, 1f), Cancel);
        panel.gameObject.SetActive(false);
    }

    void Pill(Transform parent, string label, Vector2 pos, Color color, System.Action action)
    {
        var bg = UiKit.Box(parent, label, pos, new Vector2(130f, 34f), color, null, 80f / 34f);
        UiKit.Label(bg.transform, label, 17, Color.white, Vector2.zero, new Vector2(130f, 34f));
        var b = bg.gameObject.AddComponent<UiButton>();
        b.background = bg;
        b.idle = color;
        b.hover = Color.Lerp(color, Color.white, 0.25f);
        b.pressed = Color.Lerp(color, Color.white, 0.5f);
        b.hoverScale = 1.06f;
        b.onClick = action;
    }

    void PlacePanel()
    {
        panel.position = head.position + ViewFrame.Forward(head) * 0.85f - ViewFrame.Up(head) * 0.12f;
        panel.rotation = ViewFrame.Facing(panel.position, head);
        panel.gameObject.SetActive(true);
        UiKit.PopIn(panel);
    }

    void UpdatePanel()
    {
        if (panel == null) return;
        float a = Mathf.MoveTowards(group.alpha, Scanning ? 1f : 0f, Time.deltaTime * 5f);
        if (a != group.alpha) group.alpha = a;
        if (panel.gameObject.activeSelf != a > 0.001f) panel.gameObject.SetActive(a > 0.001f);
        if (!Scanning) return;

        // Tarama sırasında baş çok dönerse panel yeniden önüne gelir
        if (ViewFrame.AngleFromView(head, panel.position) > 50f)
        {
            var goal = head.position + ViewFrame.Forward(head) * 0.85f - ViewFrame.Up(head) * 0.12f;
            panel.position = Vector3.Lerp(panel.position, goal, Time.deltaTime * 3f);
            panel.rotation = Quaternion.Slerp(panel.rotation, ViewFrame.Facing(panel.position, head), Time.deltaTime * 3f);
        }

        float progress = Mathf.Clamp01(matrices.Count / (float)targetVoxels);
        bar.sizeDelta = new Vector2(400f * progress, 10f);
        string txt;
        var support = occlusion != null && occlusion.descriptor != null ? occlusion.descriptor.environmentDepthImageSupported : Supported.Unknown;
        if (support == Supported.Unsupported) txt = "Bu telefon derinliği desteklemiyor.";
        else if (depthFrames == 0) txt = Time.time - startedAt > 4f ? "Derinlik bekleniyor... telefonu biraz yana kaydır." : "Hazırlanıyor...";
        else txt = $"Telefonu yavaşça gezdir: duvarlar, zemin, eşyalar.  %{Mathf.RoundToInt(progress * 100f)}";
        if (info.text != txt) info.text = txt;
        var tl = progress >= 1f ? "Oda tarandı — Bitti'ye bas" : "Oda taranıyor";
        if (title.text != tl) title.text = tl;
    }
}
