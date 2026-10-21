using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Oda tarama: ARCore derinlik görüntüsündeki noktaları 5 cm'lik bloklara (voksel) çevirip
// odanın 3D şeklini çıkarır. Ekrandaki her nokta için, arka plan görüntüsüyle aynı
// eşleme (displayMatrix) kullanılarak derinlik okunur ve aynı kameranın ışınıyla 3D'ye
// çevrilir; böylece bloklar tam görülen yüzeyin üstüne oturur.
public class RoomScanner : MonoBehaviour
{
    public static string Status = "-";
    public static bool Done;
    public static int VoxelCount;

    public ARCameraManager cameraManager;
    public AROcclusionManager occlusion;
    public Camera arCamera;
    public Material scanMaterial;
    public Material occluderMaterial;
    public GameObject showWhenDone;
    public GameObject scanPanel;

    public float voxelSize = 0.05f;
    public int maxVoxels = 60000;
    public int sampleColumns = 40;
    public int sampleRows = 20;
    public int everyNthFrame = 3;
    public float minDepth = 0.3f;
    public float maxDepth = 4f;
    public float holdSeconds = 1f;

    readonly Dictionary<Vector3Int, byte> hits = new Dictionary<Vector3Int, byte>();
    readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
    Mesh cube;
    Matrix4x4 displayMatrix;
    bool hasDisplayMatrix;
    float touchStart = -1f;
    int frame;

    void Awake()
    {
        Done = false;
        VoxelCount = 0;
    }

    void OnEnable() { cameraManager.frameReceived += OnFrame; }
    void OnDisable() { cameraManager.frameReceived -= OnFrame; }

    void OnFrame(ARCameraFrameEventArgs args)
    {
        if (args.displayMatrix.HasValue)
        {
            displayMatrix = args.displayMatrix.Value;
            hasDisplayMatrix = true;
        }
    }

    void Start()
    {
        cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        if (showWhenDone != null) showWhenDone.SetActive(false);
        if (scanPanel != null) scanPanel.SetActive(true);
    }

    void Update()
    {
        HandleTouch();
        if (!Done && (++frame % everyNthFrame == 0)) Integrate();
        Draw();

        Status = Done
            ? $"Oda kayitli ({VoxelCount} blok). Kisa dokun: kupleri onune getir"
            : $"TARAMA: {VoxelCount} blok. Bitirmek icin 1 sn basili tut";
    }

    void HandleTouch()
    {
        if (Done || Input.touchCount == 0) return;
        var t = Input.GetTouch(0);
        if (t.phase == TouchPhase.Began) touchStart = Time.time;
        if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) touchStart = -1f;
        if (touchStart >= 0f && Time.time - touchStart >= holdSeconds)
        {
            touchStart = -1f;
            Finish();
        }
    }

    void Finish()
    {
        Done = true;
        if (showWhenDone != null) showWhenDone.SetActive(true);
        if (scanPanel != null) scanPanel.SetActive(false);
    }

    void Integrate()
    {
        if (!hasDisplayMatrix || VoxelCount >= maxVoxels) return;
        if (!occlusion.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image)) return;

        using (image)
        {
            int w = image.width, h = image.height;
            var plane = image.GetPlane(0);
            var data = plane.data;
            int rowStride = plane.rowStride, pixelStride = plane.pixelStride;
            bool isFloat = image.format == XRCpuImage.Format.DepthFloat32;
            var m = displayMatrix;

            for (int r = 0; r < sampleRows; r++)
            {
                for (int c = 0; c < sampleColumns; c++)
                {
                    // Ekrandaki (görüntüdeki) nokta
                    float vx = (c + 0.5f) / sampleColumns;
                    float vy = (r + 0.5f) / sampleRows;

                    // Arka plan çizimindeki ile aynı dönüşüm: ekran -> görüntü koordinatı
                    float u = vx * m.m00 + vy * m.m10 + m.m20;
                    float v = vx * m.m01 + vy * m.m11 + m.m21;
                    int col = Mathf.Clamp((int)(u * w), 0, w - 1);
                    int row = Mathf.Clamp((int)(v * h), 0, h - 1);

                    int i = row * rowStride + col * pixelStride;
                    float d = isFloat ? data.ReinterpretLoad<float>(i) : data.ReinterpretLoad<ushort>(i) * 0.001f;
                    if (d < minDepth || d > maxDepth) continue;

                    // Aynı ekran noktasından, o derinlikte dünyadaki nokta
                    var world = arCamera.ViewportToWorldPoint(new Vector3(vx, vy, d));
                    var key = Vector3Int.FloorToInt(world / voxelSize);

                    hits.TryGetValue(key, out byte count);
                    if (count >= 2) continue;
                    count++;
                    hits[key] = count;

                    // Gürültüyü elemek için aynı blok en az 2 kez görülmeli
                    if (count == 2)
                    {
                        var center = ((Vector3)key + Vector3.one * 0.5f) * voxelSize;
                        matrices.Add(Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * voxelSize * 0.9f));
                        VoxelCount++;
                    }
                }
            }
        }
    }

    void Draw()
    {
        if (matrices.Count == 0 || cube == null) return;
        var rp = new RenderParams(Done ? occluderMaterial : scanMaterial) { camera = arCamera };
        for (int start = 0; start < matrices.Count; start += 1023)
            Graphics.RenderMeshInstanced(rp, cube, 0, matrices, Mathf.Min(1023, matrices.Count - start), start);
    }
}
