using UnityEngine;

// El (Meta passthrough tarzı): gerçek el olduğu gibi görünür. Parmak kapsülleri + avuç plakası görünmez bir
// maske olarak (shellMaterial = Gecit/HandMask) elin gerçek derinliğini yazar; elin arkasındaki pencereler orada
// çizilmez, el önlerinde görünür. Maske elden biraz küçüktür ki etrafında halka kalmasın.
// Görünen tek şey: kıstırmaya yaklaşınca baş ve işaret parmağı ucunda beliren küçük parıltı (gloveMaterial).
public class HandVisual : MonoBehaviour
{
    public Material gloveMaterial;
    public Material shellMaterial;

    // Parmak zincirleri (avuç kökünden uca)
    static readonly int[][] Fingers =
    {
        new[] { 1, 2, 3, 4 },
        new[] { 5, 6, 7, 8 },
        new[] { 9, 10, 11, 12 },
        new[] { 13, 14, 15, 16 },
        new[] { 17, 18, 19, 20 },
    };
    // Kalınlık (yarıçap, m): kökten uca incelir
    static readonly float[][] Radius =
    {
        new[] { 0.0125f, 0.0115f, 0.0100f, 0.0085f },
        new[] { 0.0100f, 0.0092f, 0.0084f, 0.0074f },
        new[] { 0.0104f, 0.0095f, 0.0086f, 0.0076f },
        new[] { 0.0098f, 0.0090f, 0.0082f, 0.0072f },
        new[] { 0.0088f, 0.0080f, 0.0072f, 0.0064f },
    };
    // Avuç kenarı (sırayla, çevre)
    static readonly int[] PalmRing = { 0, 1, 2, 5, 9, 13, 17 };
    const float PalmThickness = 0.011f;
    const float MaskScale = 0.8f;   // maske gerçek parmaktan biraz ince (etrafta halka kalmasın)

    Transform[][] spheres;
    Transform[][] tubes;
    Mesh palmMesh;
    GameObject palmGO;
    Renderer thumbTip, indexTip;
    MaterialPropertyBlock tipBlock;
    float alpha;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int RimId = Shader.PropertyToID("_RimColor");
    Color baseColor, baseRim;

    void Start()
    {
        baseColor = gloveMaterial.GetColor(ColorId);
        baseRim = gloveMaterial.GetColor(RimId);
        tipBlock = new MaterialPropertyBlock();

        spheres = new Transform[Fingers.Length][];
        tubes = new Transform[Fingers.Length][];
        for (int f = 0; f < Fingers.Length; f++)
        {
            int n = Fingers[f].Length;
            spheres[f] = new Transform[n];
            tubes[f] = new Transform[n - 1];
            for (int k = 0; k < n; k++)
                spheres[f][k] = Part(PrimitiveType.Sphere, $"Eklem {Fingers[f][k]}");
            for (int k = 0; k < n - 1; k++)
                tubes[f][k] = Part(PrimitiveType.Cylinder, $"Boğum {Fingers[f][k]}-{Fingers[f][k + 1]}");
        }
        thumbTip = spheres[0][3].GetComponent<Renderer>();
        indexTip = spheres[1][3].GetComponent<Renderer>();
        if (shellMaterial == null)
        {
            // Maske yok: gerçek el olduğu gibi görünür, sanal parçalar çizilmez (yalnız parmak ucu parıltısı)
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            thumbTip.enabled = indexTip.enabled = true;
            thumbTip.sharedMaterial = gloveMaterial;
            indexTip.sharedMaterial = gloveMaterial;
        }
        else
        {
            thumbTip.sharedMaterials = new[] { shellMaterial, gloveMaterial };
            indexTip.sharedMaterials = new[] { shellMaterial, gloveMaterial };
        }

        palmGO = new GameObject("Avuc", typeof(MeshFilter), typeof(MeshRenderer));
        palmGO.transform.SetParent(transform, false);
        palmMesh = new Mesh { name = "Avuc" };
        palmMesh.MarkDynamic();
        palmGO.GetComponent<MeshFilter>().sharedMesh = palmMesh;
        var palmRenderer = palmGO.GetComponent<MeshRenderer>();
        palmRenderer.sharedMaterial = shellMaterial;
        palmRenderer.enabled = shellMaterial != null;
        BuildPalmTopology();
    }

    Transform Part(PrimitiveType type, string name)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(transform, false);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = shellMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    void LateUpdate()
    {
        bool visible = HandTracker.Visible;
        alpha = Mathf.MoveTowards(alpha, visible ? 1f : 0f, Time.deltaTime * (visible ? 8f : 12f));
        bool show = alpha > 0.01f;
        if (palmGO.activeSelf != show)
        {
            foreach (Transform c in transform) c.gameObject.SetActive(show);
        }
        if (!show) return;

        var j = HandTracker.Joints;
        for (int f = 0; f < Fingers.Length; f++)
        {
            var chain = Fingers[f];
            for (int k = 0; k < chain.Length; k++)
            {
                var s = spheres[f][k];
                s.position = j[chain[k]];
                s.localScale = Vector3.one * Radius[f][k] * 2f * MaskScale;
            }
            for (int k = 0; k < chain.Length - 1; k++)
            {
                Vector3 a = j[chain[k]], b = j[chain[k + 1]];
                var t = tubes[f][k];
                var dir = b - a;
                float len = dir.magnitude;
                if (len < 1e-4f) { t.localScale = Vector3.zero; continue; }
                float r = (Radius[f][k] + Radius[f][k + 1]) * 0.5f * MaskScale;
                t.position = (a + b) * 0.5f;
                t.rotation = Quaternion.FromToRotation(Vector3.up, dir / len);
                t.localScale = new Vector3(r * 2f, len * 0.5f, r * 2f);
            }
        }
        UpdatePalm(j);

        // Parmak ucu parıltısı: yalnız kıstırmaya yaklaşınca belirir, kıstırınca en parlak
        float glow = Mathf.InverseLerp(0.25f, 1f, HandTracker.PinchAmount) * alpha;
        var tipBody = new Color(0.75f, 0.9f, 1f, 0.55f * glow);
        var tipRim = new Color(1f, 1f, 1f, 0.95f * glow);
        tipBlock.SetColor(ColorId, tipBody);
        tipBlock.SetColor(RimId, tipRim);
        int gi = shellMaterial == null ? 0 : 1;
        thumbTip.SetPropertyBlock(tipBlock, gi);
        indexTip.SetPropertyBlock(tipBlock, gi);
    }

    // Avuç: çevre noktalarından iki kat (ön/arka) + yan şerit
    Vector3[] palmVerts;

    void BuildPalmTopology()
    {
        int n = PalmRing.Length;
        palmVerts = new Vector3[(n + 1) * 2];
        var tris = new System.Collections.Generic.List<int>();
        int front = 0, back = n + 1;
        for (int i = 0; i < n; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % n;
            tris.AddRange(new[] { front, a, b });
            tris.AddRange(new[] { back, back + b, back + a });
            tris.AddRange(new[] { a, back + a, b });
            tris.AddRange(new[] { b, back + a, back + b });
        }
        palmMesh.vertices = palmVerts;
        palmMesh.triangles = tris.ToArray();
    }

    void UpdatePalm(Vector3[] j)
    {
        int n = PalmRing.Length;
        var center = Vector3.zero;
        for (int i = 0; i < n; i++) center += j[PalmRing[i]];
        center /= n;
        var normal = Vector3.Cross(j[5] - j[0], j[17] - j[0]);
        normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.forward;
        var off = normal * PalmThickness * 0.5f;

        var local = transform.worldToLocalMatrix;
        palmVerts[0] = local.MultiplyPoint3x4(center + off);
        palmVerts[n + 1] = local.MultiplyPoint3x4(center - off);
        for (int i = 0; i < n; i++)
        {
            // Kenarı biraz dışarı taşır, parmak köklerini örter
            var p = Vector3.Lerp(center, j[PalmRing[i]], 0.9f);   // avucun biraz içinde kalır
            palmVerts[1 + i] = local.MultiplyPoint3x4(p + off);
            palmVerts[n + 2 + i] = local.MultiplyPoint3x4(p - off);
        }
        palmMesh.vertices = palmVerts;
        palmMesh.RecalculateNormals();
        palmMesh.RecalculateBounds();
    }
}
