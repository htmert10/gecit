using UnityEngine;
using UnityEngine.UI;

// Kavisli sinema perdesi (Meta'daki gibi): seni saran silindir parçası. Bir pencerenin tarayıcı görüntüsünü
// gösterir ve ona dokunulur: ışın silindire değdiği yer tarayıcıda dokunma / kaydırma olur.
public class CurvedScreen : PointerTarget
{
    public Material material;   // Gecit/CurvedScreen
    public float radius = 7f;
    public float arcDeg = 64f;
    public float lift = 0.35f;  // perdenin ortası göz hizasının biraz üstünde

    Window source;
    RawImage view;
    MeshRenderer mr;
    Material mat;
    float height;
    long downTime;
    int lastX, lastY, startX, startY;
    bool touching, dragging;
    const int DragThreshold = 40;
    static readonly int BrightId = Shader.PropertyToID("_Bright");

    public bool Showing => source != null;

    void Awake()
    {
        mr = gameObject.AddComponent<MeshRenderer>();
        gameObject.AddComponent<MeshFilter>();
        mr.enabled = false;
    }

    public void Show(Window w, Vector3 center, Vector3 forward)
    {
        source = w;
        view = w.Browser != null ? w.Browser.view : null;
        forward.y = 0f;
        transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward));
        if (mat == null) mat = new Material(material);
        mr.sharedMaterial = mat;
        BuildMesh();
        mr.enabled = true;
    }

    // Editör önizlemesi: pencere olmadan bir resimle göster
    public void ShowTexture(Texture tex, Vector3 center, Vector3 forward)
    {
        transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward));
        if (mat == null) mat = new Material(material);
        mat.mainTexture = tex;
        if (mr == null) Awake();
        mr.sharedMaterial = mat;
        BuildMesh();
        mr.enabled = true;
    }

    public void Hide()
    {
        if (touching) Release();
        source = null;
        view = null;
        mr.enabled = false;
    }

    void BuildMesh()
    {
        const int N = 64;
        float arc = arcDeg * Mathf.Deg2Rad;
        height = radius * arc * 9f / 16f;   // yay boyu : yükseklik = 16 : 9
        var v = new Vector3[(N + 1) * 2];
        var uv = new Vector2[v.Length];
        var tris = new int[N * 6];
        for (int i = 0; i <= N; i++)
        {
            float u = i / (float)N;
            float phi = -arc * 0.5f + arc * u;
            var p = new Vector3(Mathf.Sin(phi) * radius, 0f, Mathf.Cos(phi) * radius);
            v[i * 2] = p + Vector3.up * (lift - height * 0.5f);
            v[i * 2 + 1] = p + Vector3.up * (lift + height * 0.5f);
            uv[i * 2] = new Vector2(u, 0f);
            uv[i * 2 + 1] = new Vector2(u, 1f);
            if (i == N) continue;
            int a = i * 2, k = i * 6;
            tris[k] = a; tris[k + 1] = a + 1; tris[k + 2] = a + 2;
            tris[k + 3] = a + 1; tris[k + 4] = a + 3; tris[k + 5] = a + 2;
        }
        var mesh = new Mesh { name = "Kavisli Perde" };
        mesh.vertices = v;
        mesh.uv = uv;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    void LateUpdate()
    {
        if (source == null || view == null) return;
        if (!source.IsOpen) { Hide(); return; }
        var tex = view.texture;
        if (mat.mainTexture != tex) mat.mainTexture = tex;
        float bright = EyeComfort.Factor;   // göz koruma
        if (mat.GetFloat(BrightId) != bright) mat.SetFloat(BrightId, bright);
        var r = view.uvRect;   // tarayıcı görüntüsü ters olabilir: pencerenin kullandığı ayarın aynısı
        mat.mainTextureScale = new Vector2(r.width, r.height);
        mat.mainTextureOffset = new Vector2(r.x, r.y);
    }

    // Işın silindirin İÇ yüzüne nereden değiyor (kullanıcı silindirin içinde)
    bool Hit(Ray ray, float margin, out float distance, out Vector3 point, out float u, out float v)
    {
        distance = 0f; point = Vector3.zero; u = v = 0f;
        if (source == null) return false;
        var o = transform.InverseTransformPoint(ray.origin);
        var d = transform.InverseTransformDirection(ray.direction);
        float a = d.x * d.x + d.z * d.z;
        if (a < 1e-6f) return false;
        float b = 2f * (o.x * d.x + o.z * d.z);
        float c = o.x * o.x + o.z * o.z - radius * radius;
        float disc = b * b - 4f * a * c;
        if (disc < 0f) return false;
        float t = (-b + Mathf.Sqrt(disc)) / (2f * a);
        if (t <= 0f) return false;
        var p = o + d * t;
        float phi = Mathf.Atan2(p.x, p.z);
        float arc = arcDeg * Mathf.Deg2Rad;
        u = (phi + arc * 0.5f) / arc;
        v = (p.y - (lift - height * 0.5f)) / height;
        if (u < -margin || u > 1f + margin || v < -margin || v > 1f + margin) return false;
        distance = t;
        point = transform.TransformPoint(p);
        return true;
    }

    public override bool Raycast(Ray ray, out float distance, out Vector3 point) =>
        Hit(ray, 0f, out distance, out point, out _, out _);

    public override bool Near(Ray ray, out float distance, out Vector3 point) =>
        Hit(ray, 0.15f, out distance, out point, out _, out _);

    public override void Press(Ray ray, Vector3 point)
    {
        if (!Hit(ray, 0f, out _, out _, out float u, out float v)) return;
        var b = source.container.browser;
        lastX = startX = (int)(u * b.viewSize.x);
        lastY = startY = (int)((1f - v) * b.viewSize.y);
        dragging = false;
        touching = true;
        downTime = b.TouchEvent(lastX, lastY, 0, downTime);
    }

    public override void Drag(Ray ray)
    {
        if (!touching || !Hit(ray, 0.2f, out _, out _, out float u, out float v)) return;
        var b = source.container.browser;
        int x = (int)(Mathf.Clamp01(u) * b.viewSize.x), y = (int)((1f - Mathf.Clamp01(v)) * b.viewSize.y);
        if (!dragging && Mathf.Abs(x - startX) + Mathf.Abs(y - startY) < DragThreshold) return;
        dragging = true;
        if (x == lastX && y == lastY) return;
        b.TouchEvent(x, y, 2, downTime);
        lastX = x; lastY = y;
    }

    public override void Release()
    {
        if (!touching || source == null) { touching = false; return; }
        if (!dragging) { lastX = startX; lastY = startY; }
        source.container.browser.TouchEvent(lastX, lastY, 1, downTime);
        touching = false;
    }
}
