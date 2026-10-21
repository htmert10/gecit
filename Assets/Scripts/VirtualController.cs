using UnityEngine;

// Sanal dünyada, "El ile tıkla" KAPALIYKEN (kumanda modu) elinin yerinde Quest kumandasına benzeyen bir kumanda:
// koyu gri tutma sapı, üstünde halka, kafa kısmında iki düğme ve analog çubuk, önde tetik.
// Avucunun ortasında durur, elinin yönüne göre eğilir. OK'a basınca tetik ve halka mavi parlar.
public class VirtualController : MonoBehaviour
{
    public Material material;   // Gecit/VirtualHand, solma kapalı
    public Camera arCamera;

    Transform model;
    Renderer ring, trigger;
    MaterialPropertyBlock block;
    float alpha;
    Vector3 smoothPos;
    Quaternion smoothRot = Quaternion.identity;
    bool started;

    static readonly Color Body = new Color(0.16f, 0.17f, 0.19f, 1f);
    static readonly Color BodyRim = new Color(0.55f, 0.58f, 0.64f, 1f);
    static readonly Color Light = new Color(0.82f, 0.84f, 0.88f, 1f);

    void Start()
    {
        block = new MaterialPropertyBlock();
        model = new GameObject("Kumanda Modeli").transform;
        model.SetParent(transform, false);

        // Tutma sapı (aşağı doğru hafif eğik kapsül)
        var grip = Part(PrimitiveType.Capsule, "Sap", Body, BodyRim);
        grip.localPosition = new Vector3(0f, -0.045f, 0.012f);
        grip.localRotation = Quaternion.Euler(-18f, 0f, 0f);
        grip.localScale = new Vector3(0.036f, 0.055f, 0.04f);

        // Kafa (yassı elipsoit) ve üstündeki yüz
        var head = Part(PrimitiveType.Sphere, "Kafa", Body, BodyRim);
        head.localPosition = new Vector3(0f, 0.012f, 0f);
        head.localScale = new Vector3(0.058f, 0.03f, 0.052f);

        // Analog çubuk + iki düğme
        var stick = Part(PrimitiveType.Cylinder, "Analog", new Color(0.1f, 0.1f, 0.11f, 1f), BodyRim);
        stick.localPosition = new Vector3(-0.008f, 0.03f, -0.006f);
        stick.localScale = new Vector3(0.016f, 0.006f, 0.016f);
        var b1 = Part(PrimitiveType.Sphere, "Dugme A", Light, Color.white);
        b1.localPosition = new Vector3(0.012f, 0.027f, 0.006f);
        b1.localScale = new Vector3(0.01f, 0.004f, 0.01f);
        var b2 = Part(PrimitiveType.Sphere, "Dugme B", Light, Color.white);
        b2.localPosition = new Vector3(0.016f, 0.026f, -0.008f);
        b2.localScale = new Vector3(0.01f, 0.004f, 0.01f);

        // Tetik (önde)
        trigger = Part(PrimitiveType.Capsule, "Tetik", Body, BodyRim).GetComponent<Renderer>();
        trigger.transform.localPosition = new Vector3(0f, -0.002f, 0.03f);
        trigger.transform.localRotation = Quaternion.Euler(-60f, 0f, 0f);
        trigger.transform.localScale = new Vector3(0.016f, 0.016f, 0.012f);

        // Halka (kafanın üstünde, öne-yukarı eğik): ince torus
        var ringGO = new GameObject("Halka", typeof(MeshFilter), typeof(MeshRenderer));
        ringGO.transform.SetParent(model, false);
        ringGO.transform.localPosition = new Vector3(0f, 0.022f, 0.006f);
        ringGO.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
        ringGO.GetComponent<MeshFilter>().sharedMesh = Torus(0.047f, 0.0055f, 48, 10);
        ring = ringGO.GetComponent<MeshRenderer>();
        ring.sharedMaterial = material;
        Tint(ring, Body, BodyRim);

        model.gameObject.SetActive(false);
        started = true;
    }

    Transform Part(PrimitiveType type, string name, Color body, Color rim)
    {
        var go = GameObject.CreatePrimitive(type);
        if (Application.isPlaying) Destroy(go.GetComponent<Collider>()); else DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(model, false);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Tint(r, body, rim);
        return go.transform;
    }

    void Tint(Renderer r, Color body, Color rim)
    {
        block.SetColor("_Color", body);
        block.SetColor("_RimColor", rim);
        r.SetPropertyBlock(block);
    }

    // Editörde önizleme için
    public void BuildNow() { if (!started) Start(); }
    public void PlaceNow(Vector3 palm, Vector3 up, Vector3 toCamera)
    {
        model.gameObject.SetActive(true);
        model.position = palm;
        model.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(-toCamera, up).normalized, up);
    }

    void LateUpdate()
    {
        bool want = VirtualWorld.Active && HandTracker.Visible && !HandTracker.HandClicks;
        alpha = Mathf.MoveTowards(alpha, want ? 1f : 0f, Time.deltaTime * 6f);
        bool show = alpha > 0.05f;
        if (model.gameObject.activeSelf != show) model.gameObject.SetActive(show);
        if (!show) return;

        // Avucun ortasında, elin "yukarı" yönüne göre; ön yüzü kameradan uzağa (ileri) bakar
        var j = HandTracker.ShapeJoints;
        var palm = (j[0] + j[5] + j[9] + j[13] + j[17]) / 5f;
        var up = (j[9] - j[0]);
        up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
        up = Vector3.Slerp(up, Vector3.up, 0.35f).normalized;   // kumanda dik tutulur gibi
        var fwd = Vector3.ProjectOnPlane(arCamera.transform.forward, up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = arCamera.transform.forward;
        var rot = Quaternion.LookRotation(fwd.normalized, up);
        float k = 1f - Mathf.Exp(-Time.deltaTime * 20f);
        smoothPos = model.gameObject.activeSelf && smoothPos != Vector3.zero ? Vector3.Lerp(smoothPos, palm, k) : palm;
        smoothRot = Quaternion.Slerp(smoothRot, rot, k);
        model.position = smoothPos;
        model.rotation = smoothRot;

        bool pressed = PointerInput.Pressed;
        var glow = new Color(0.35f, 0.6f, 1f, 1f);
        Tint(ring, pressed ? glow : Body, pressed ? Color.white : BodyRim);
        Tint(trigger, pressed ? glow : Body, pressed ? Color.white : BodyRim);
    }

    static Mesh Torus(float major, float minor, int seg, int side)
    {
        var v = new Vector3[seg * side];
        var n = new Vector3[seg * side];
        var tris = new int[seg * side * 6];
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            var center = new Vector3(Mathf.Cos(a) * major, 0f, Mathf.Sin(a) * major);
            var outward = center.normalized;
            for (int j = 0; j < side; j++)
            {
                float b = j * Mathf.PI * 2f / side;
                var nn = outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                v[i * side + j] = center + nn * minor;
                n[i * side + j] = nn;
            }
        }
        int t = 0;
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < side; j++)
            {
                int a0 = i * side + j, a1 = ((i + 1) % seg) * side + j;
                int b0 = i * side + (j + 1) % side, b1 = ((i + 1) % seg) * side + (j + 1) % side;
                tris[t++] = a0; tris[t++] = b0; tris[t++] = a1;
                tris[t++] = a1; tris[t++] = b0; tris[t++] = b1;
            }
        var m = new Mesh { name = "Halka", vertices = v, normals = n, triangles = tris };
        m.RecalculateBounds();
        return m;
    }
}
