using UnityEngine;

// Sanal dünyadaki el (Meta tarzı yumuşak eldiven): her karede TEK PARÇA pürüzsüz bir yüzey üretilir —
// parmaklar eklemlerden geçen yumuşak eğri boyunca kesintisiz, uca doğru incelen, ucu yuvarlak borular;
// avuç yumuşak köşeli yassı bir yastık; bilekten koluna doğru kaybolan kısa bir manşet. Eklem şişkinliği yok,
// iskelet gibi görünmez. Şekil modelin metrik 3B el şeklinden gelir (HandTracker.ShapeJoints), biraz küçük çizilir.
// Yalnız sanal dünyada ve el modunda görünür. Kıstırmaya yaklaşınca baş ve işaret parmağı ucu mavi parlar.
public class VirtualHand : MonoBehaviour
{
    public Material material;

    static readonly int[][] Fingers =
    {
        new[] { 1, 2, 3, 4 },
        new[] { 5, 6, 7, 8 },
        new[] { 9, 10, 11, 12 },
        new[] { 13, 14, 15, 16 },
        new[] { 17, 18, 19, 20 },
    };
    // Parmak kalınlığı (yarıçap, m): kökten uca
    static readonly float[][] Radius =
    {
        new[] { 0.0145f, 0.0122f, 0.0105f, 0.0092f },
        new[] { 0.0102f, 0.0094f, 0.0086f, 0.0078f },
        new[] { 0.0106f, 0.0097f, 0.0088f, 0.0080f },
        new[] { 0.0100f, 0.0092f, 0.0084f, 0.0076f },
        new[] { 0.0090f, 0.0082f, 0.0074f, 0.0068f },
    };

    const float Shrink = 0.82f;   // sanal el gerçekten biraz küçük (kullanıcı isteği)
    const int Ring = 12;          // boru çevresi köşe sayısı
    const int PerSegment = 4;     // eklemler arası örnek
    const int CapRings = 3;
    const int PalmLat = 10, PalmLon = 16;
    const int CuffRings = 0;   // bilek manşeti yok (üçgen kuyruk gibi duruyordu); el bileğe doğru kaybolur

    Mesh mesh;
    Vector3[] verts, normals;
    int[] tris;
    readonly Vector3[] j = new Vector3[21];
    float alpha;
    bool shown = true;
    MeshRenderer meshRenderer;

    static readonly int WristId = Shader.PropertyToID("_GecitWristPos");
    static readonly int DirId = Shader.PropertyToID("_GecitHandDir");
    static readonly int AlphaId = Shader.PropertyToID("_GecitHandAlpha");
    static readonly int TipAId = Shader.PropertyToID("_GecitTipA");
    static readonly int TipBId = Shader.PropertyToID("_GecitTipB");
    static readonly int PinchId = Shader.PropertyToID("_GecitPinch");

    static int FingerSamples => 3 * PerSegment + 2;   // + 1 kökten içeri uzatma

    void Start()
    {
        var go = new GameObject("Eldiven Yuzeyi", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(transform, false);
        mesh = new Mesh { name = "Eldiven" };
        mesh.MarkDynamic();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = go.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        BuildTopology();
        SetVisible(false);
    }

    // ---- Köşe/üçgen düzeni (bir kez) ----
    int fingerVertCount, palmStart, cuffStart;

    void BuildTopology()
    {
        int perFinger = FingerSamples * Ring + CapRings * Ring + 1;
        fingerVertCount = perFinger;
        palmStart = Fingers.Length * perFinger;
        int palmVerts = (PalmLat + 1) * (PalmLon + 1);
        cuffStart = palmStart + palmVerts;
        int total = cuffStart + CuffRings * Ring;
        verts = new Vector3[total];
        normals = new Vector3[total];

        var t = new System.Collections.Generic.List<int>();
        for (int f = 0; f < Fingers.Length; f++)
        {
            int b = f * perFinger;
            int rings = FingerSamples + CapRings;
            for (int r = 0; r < rings - 1; r++)
                for (int k = 0; k < Ring; k++)
                {
                    int a0 = b + r * Ring + k, a1 = b + r * Ring + (k + 1) % Ring;
                    int c0 = a0 + Ring, c1 = a1 + Ring;
                    t.AddRange(new[] { a0, c0, a1, a1, c0, c1 });
                }
            int tip = b + rings * Ring;
            int last = b + (rings - 1) * Ring;
            for (int k = 0; k < Ring; k++) t.AddRange(new[] { last + k, tip, last + (k + 1) % Ring });
        }
        for (int la = 0; la < PalmLat; la++)
            for (int lo = 0; lo < PalmLon; lo++)
            {
                int a0 = palmStart + la * (PalmLon + 1) + lo, a1 = a0 + 1;
                int c0 = a0 + PalmLon + 1, c1 = c0 + 1;
                t.AddRange(new[] { a0, a1, c0, a1, c1, c0 });
            }
        for (int r = 0; r < CuffRings - 1; r++)
            for (int k = 0; k < Ring; k++)
            {
                int a0 = cuffStart + r * Ring + k, a1 = cuffStart + r * Ring + (k + 1) % Ring;
                t.AddRange(new[] { a0, a1, a0 + Ring, a1, a1 + Ring, a0 + Ring });
            }
        tris = t.ToArray();
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris;
    }

    void SetVisible(bool on)
    {
        if (shown == on) return;
        shown = on;
        foreach (Transform c in transform) c.gameObject.SetActive(on);
    }

    // Editörde önizleme için
    public void BuildNow() { Start(); }
    public void UpdateNow() { alpha = 1f; shown = false; SetVisible(true); Rebuild(); }

    void LateUpdate()
    {
        bool want = VirtualWorld.Active && HandTracker.Visible && HandTracker.HandClicks;   // kumanda modunda kumanda modeli görünür
        alpha = Mathf.MoveTowards(alpha, want ? 1f : 0f, Time.deltaTime * (want ? 6f : 8f));
        SetVisible(alpha > 0.01f);
        if (!shown) return;
        Rebuild();
    }

    void Rebuild()
    {
        // Biraz küçük: avuç merkezine göre
        var src = HandTracker.ShapeJoints;
        var mid = (src[0] + src[5] + src[9] + src[13] + src[17]) / 5f;
        for (int i = 0; i < 21; i++) j[i] = mid + (src[i] - mid) * Shrink;

        var up = j[9] - j[0];
        var side = j[5] - j[17];
        var palmNormal = Vector3.Cross(up, side);
        if (up.sqrMagnitude < 1e-8f || palmNormal.sqrMagnitude < 1e-10f) return;
        up.Normalize(); palmNormal.Normalize();

        for (int f = 0; f < Fingers.Length; f++) BuildFinger(f, palmNormal);
        BuildPalm(up, side, palmNormal);

        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.RecalculateBounds();

        Shader.SetGlobalVector(WristId, j[0] - up * 0.03f);
        Shader.SetGlobalVector(DirId, up);
        Shader.SetGlobalFloat(AlphaId, alpha);
        Shader.SetGlobalVector(TipAId, j[4]);
        Shader.SetGlobalVector(TipBId, j[8]);
        Shader.SetGlobalFloat(PinchId, HandTracker.HandClicks ? Mathf.InverseLerp(0.3f, 1f, HandTracker.PinchAmount) : 0f);
    }

    // Parmak: Catmull-Rom eğrisi boyunca dönmesi en az çerçeveli halkalar + yuvarlak uç
    readonly Vector3[] ctrl = new Vector3[6];
    readonly float[] ctrlR = new float[6];

    void BuildFinger(int f, Vector3 palmNormal)
    {
        var chain = Fingers[f];
        // Kontrol noktaları: kökten avucun içine biraz uzatma + 4 eklem + uçtan öteye hayali nokta
        var p0 = j[chain[0]]; var p1 = j[chain[1]];
        ctrl[0] = p0 + (p0 - p1) * (f == 0 ? 0.12f : 0.6f);   // baş parmağın kökü bileğe yakın: avucun altından taşmasın
        for (int k = 0; k < 4; k++) ctrl[k + 1] = j[chain[k]];
        ctrl[5] = ctrl[4] + (ctrl[4] - ctrl[3]);
        ctrlR[0] = Radius[f][0] * Shrink * 1.05f;
        for (int k = 0; k < 4; k++) ctrlR[k + 1] = Radius[f][k] * Shrink;
        ctrlR[5] = ctrlR[4];

        int b = f * fingerVertCount;
        Vector3 n = Vector3.zero;
        int ringIndex = 0;
        Vector3 lastPos = Vector3.zero, lastT = Vector3.forward;
        float lastR = 0f;
        // Örnekler: segment 0 (uzatma->kök) uç noktası dahil 2 örnek, sonra 3 segment x PerSegment
        for (int s = 0; s < FingerSamples; s++)
        {
            float u; int seg;
            if (s < 2) { seg = 0; u = s * 0.5f; }
            else { int q = s - 2; seg = 1 + q / PerSegment; u = (q % PerSegment) / (float)PerSegment; }
            if (s == FingerSamples - 1) { seg = 3; u = 1f; }
            var pos = CatmullRom(seg, u);
            var tan = CatmullRomTangent(seg, u).normalized;
            float r = Mathf.Lerp(ctrlR[seg], ctrlR[seg + 1], u);
            if (s == 0)
            {
                n = Vector3.Cross(tan, palmNormal);
                if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(tan, Vector3.up);
                n.Normalize();
            }
            else
            {
                n = Vector3.ProjectOnPlane(n, tan);
                if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(tan, palmNormal);
                n.Normalize();
            }
            var bn = Vector3.Cross(tan, n);
            WriteRing(b + ringIndex * Ring, pos, n, bn, r, Vector3.zero, 0f);
            ringIndex++;
            lastPos = pos; lastT = tan; lastR = r;
        }
        // Yuvarlak uç
        var nn = n; var bb = Vector3.Cross(lastT, nn);
        for (int c = 1; c <= CapRings; c++)
        {
            float th = c / (float)(CapRings + 1) * Mathf.PI * 0.5f;
            WriteRing(b + ringIndex * Ring, lastPos + lastT * lastR * Mathf.Sin(th), nn, bb, lastR * Mathf.Cos(th), lastT, Mathf.Sin(th));
            ringIndex++;
        }
        int tip = b + ringIndex * Ring;
        verts[tip] = lastPos + lastT * lastR;
        normals[tip] = lastT;
    }

    void WriteRing(int start, Vector3 center, Vector3 n, Vector3 bn, float r, Vector3 axis, float axisBlend)
    {
        for (int k = 0; k < Ring; k++)
        {
            float a = k * Mathf.PI * 2f / Ring;
            var dir = n * Mathf.Cos(a) + bn * Mathf.Sin(a);
            verts[start + k] = center + dir * r;
            normals[start + k] = (dir * (1f - axisBlend) + axis * axisBlend).normalized;
        }
    }

    Vector3 CatmullRom(int seg, float u)
    {
        // seg 0..3 arası: ctrl[seg]..ctrl[seg+1] (ctrl[-1] ve ctrl[6] yerine uçlar tekrar)
        var a = ctrl[Mathf.Max(seg - 1, 0)]; var b = ctrl[seg]; var c = ctrl[seg + 1]; var d = ctrl[Mathf.Min(seg + 2, 5)];
        float u2 = u * u, u3 = u2 * u;
        return 0.5f * ((2f * b) + (-a + c) * u + (2f * a - 5f * b + 4f * c - d) * u2 + (-a + 3f * b - 3f * c + d) * u3);
    }

    Vector3 CatmullRomTangent(int seg, float u)
    {
        var a = ctrl[Mathf.Max(seg - 1, 0)]; var b = ctrl[seg]; var c = ctrl[seg + 1]; var d = ctrl[Mathf.Min(seg + 2, 5)];
        float u2 = u * u;
        return 0.5f * ((-a + c) + 2f * (2f * a - 5f * b + 4f * c - d) * u + 3f * (-a + 3f * b - 3f * c + d) * u2);
    }

    // Avuç: yumuşak köşeli yassı yastık (süper elipsoit)
    void BuildPalm(Vector3 up, Vector3 side, Vector3 normal)
    {
        var sideDir = side.normalized;
        var upDir = Vector3.Cross(sideDir, normal).normalized;
        if (Vector3.Dot(upDir, up) < 0f) upDir = -upDir;
        var center = (j[0] * 0.8f + j[5] + j[9] + j[13] + j[17]) / 4.8f + upDir * 0.004f;
        float halfW = side.magnitude * 0.5f + 0.011f * Shrink;
        float halfH = Vector3.Distance(j[0], j[9]) * 0.52f + 0.006f;
        float halfT = 0.019f * Shrink;
        const float e = 0.75f;   // köşelerin yuvarlaklığı (1 = elips, küçük = kutu)
        for (int la = 0; la <= PalmLat; la++)
        {
            float th = -Mathf.PI * 0.5f + la * Mathf.PI / PalmLat;
            for (int lo = 0; lo <= PalmLon; lo++)
            {
                float ph = lo * Mathf.PI * 2f / PalmLon;
                float cx = Mathf.Cos(th) * Mathf.Cos(ph), cy = Mathf.Sin(th), cz = Mathf.Cos(th) * Mathf.Sin(ph);
                float x = Sp(cx, e), y = Sp(cy, e), z = Sp(cz, e);
                int idx = palmStart + la * (PalmLon + 1) + lo;
                verts[idx] = center + sideDir * x * halfW + upDir * y * halfH + normal * z * halfT;
                // Normal: süper elipsoit eğiminin yaklaşığı
                var nn = sideDir * Sp(cx, 2f - e) / halfW + upDir * Sp(cy, 2f - e) / halfH + normal * Sp(cz, 2f - e) / halfT;
                normals[idx] = nn.normalized;
            }
        }
    }

    static float Sp(float v, float e) => Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), e);
}
