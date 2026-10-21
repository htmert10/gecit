using UnityEngine;

// Elden çıkan ışın (Quest gibi): elden penceredeki noktaya giden, her zaman kameraya dönük bir şerit.
// Uzunluk boyunca: elde görünmez başlar, ortada belirgin, noktaya doğru soluklaşır. Genişlik boyunca doku
// (Gecit/Ray) parlak çekirdek + yumuşak kenar verir. LineRenderer yerine: o tek kamerada şeridi yan
// çevirip ışını görünmez yapıyordu.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RayBeam : MonoBehaviour
{
    public Transform view;               // kamera
    public float startWidth = 0.011f;
    public float endWidth = 0.006f;
    public Color color = Color.white;

    const int Segments = 24;
    Mesh mesh;
    MeshRenderer mr;
    Vector3 a, b;
    float pulseAt = -10f;
    readonly Vector3[] verts = new Vector3[(Segments + 1) * 2];
    readonly Color[] cols = new Color[(Segments + 1) * 2];

    public bool Visible
    {
        get => Renderer.enabled;
        set => Renderer.enabled = value;
    }

    MeshRenderer Renderer
    {
        get
        {
            if (mr == null) mr = GetComponent<MeshRenderer>();
            return mr;
        }
    }

    // Tıklayınca: elden noktaya doğru ışının içinden parlak bir dalga geçer
    public void Pulse() { pulseAt = Time.time; }

    public void SetPosition(int index, Vector3 p)
    {
        if (index == 0) a = p; else b = p;
    }

    void LateUpdate()
    {
        if (Visible && view != null) Rebuild(view.position);
    }

    // Uçlarda soluk, ortada belirgin
    static float Alpha(float t)
    {
        // Elde parlak başlar, noktaya KADAR uzanır, sona doğru biraz soluklaşır (kullanıcı: "boyunu neden kısalttın";
        // noktaya varmadan sönmesi çizgiyi kısa gösteriyordu)
        if (t < 0.05f) return 0.9f * (t / 0.05f);
        return Mathf.Lerp(0.9f, 0.4f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 1f, t)));
    }

    public void Rebuild(Vector3 eye)
    {
        if (mesh == null) Init();
        var dir = b - a;
        float age = Application.isPlaying ? Time.time - pulseAt : 10f;
        float head = age / 0.22f;                  // dalganın ışın üzerindeki yeri (0 el, 1 nokta)
        float fade = Mathf.Clamp01(1f - age / 0.35f);
        for (int i = 0; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            var p = a + dir * t;
            var side = Vector3.Cross(dir, eye - p);
            side = side.sqrMagnitude > 1e-10f ? side.normalized : transform.right;
            float boost = fade * Mathf.Exp(-(t - head) * (t - head) / 0.012f);
            float half = 0.5f * Mathf.Lerp(startWidth, endWidth, t) * (1f + 0.4f * boost);
            verts[i * 2] = transform.InverseTransformPoint(p - side * half);
            verts[i * 2 + 1] = transform.InverseTransformPoint(p + side * half);
            var c = color;
            c.a *= Mathf.Clamp01(Alpha(t) + boost);
            if (boost > 0f) c = Color.Lerp(c, new Color(1f, 1f, 1f, c.a), boost);
            cols[i * 2] = c;
            cols[i * 2 + 1] = c;
        }
        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.bounds = new Bounds(transform.InverseTransformPoint(eye), Vector3.one * 1000f);
    }

    void Init()
    {
        mesh = new Mesh { name = "Isin" };
        mesh.MarkDynamic();
        var uvs = new Vector2[verts.Length];
        var tris = new int[Segments * 6];
        for (int i = 0; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            uvs[i * 2] = new Vector2(t, 0f);
            uvs[i * 2 + 1] = new Vector2(t, 1f);
            if (i == Segments) continue;
            int v = i * 2, k = i * 6;
            tris[k] = v; tris[k + 1] = v + 1; tris[k + 2] = v + 2;
            tris[k + 3] = v + 1; tris[k + 4] = v + 3; tris[k + 5] = v + 2;
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);   // hiç kırpılmasın
        GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = Renderer;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }
}
