using System.Collections.Generic;
using UnityEngine;

// Işın kılıcı oyunu: elde parlayan bir kılıç; karşıdan küpler gelir, kılıçla kesince ikiye ayrılır.
// Kaçan küp kombo'yu sıfırlar. Menüden başlatılır/bitirilir; oyun sırasında pencereler gizlenir.
public class SaberGame : MonoBehaviour
{
    public static bool Playing;

    public Transform head;
    public Material coreMaterial;     // kılıcın beyaz özü
    public Material glowMaterial;     // kılıcın saydam parıltısı
    public Material[] cubeMaterials;  // küp renkleri
    public Material sparkMaterial;

    public float bladeLength = 0.85f;
    public float cubeSize = 0.2f;

    public bool Active { get; private set; }

    class Cube { public Transform t; public Vector3 velocity; public Vector3 spin; }
    class Piece { public Transform t; public Vector3 velocity; public Vector3 spin; public float life, maxLife; public Vector3 scale; }

    readonly List<Cube> cubes = new List<Cube>();
    readonly List<Piece> pieces = new List<Piece>();
    readonly List<Window> hidden = new List<Window>();
    Transform saber, core, glow;
    TextMesh scoreText, infoText;
    Vector3 origin, forward, right;
    float nextSpawn, startTime;
    int score, combo, best;
    Vector3 hilt, tip, lastTip, prevHilt, prevTip;
    bool hasPrev;
    float lastHandTime = -10f;
    Vector3 lastHandHilt, lastHandDir = Vector3.up;
    readonly OneEuroFilter hiltFilter = new OneEuroFilter(2f, 1f);
    readonly OneEuroFilter dirFilter = new OneEuroFilter(2f, 1f);

    void Start()
    {
        saber = new GameObject("Isin Kilici").transform;
        saber.SetParent(transform, false);
        var handle = Prim(PrimitiveType.Cylinder, saber, "Kabza", sparkMaterial);
        handle.localScale = new Vector3(0.03f, 0.07f, 0.03f);
        handle.localPosition = new Vector3(0f, -0.06f, 0f);
        core = Prim(PrimitiveType.Cylinder, saber, "Oz", coreMaterial);
        glow = Prim(PrimitiveType.Cylinder, saber, "Parilti", glowMaterial);
        core.localScale = new Vector3(0.014f, bladeLength * 0.5f, 0.014f);
        core.localPosition = new Vector3(0f, bladeLength * 0.5f, 0f);
        glow.localScale = new Vector3(0.042f, bladeLength * 0.5f + 0.01f, 0.042f);
        glow.localPosition = core.localPosition;
        saber.gameObject.SetActive(false);

        scoreText = MakeText("Skor", 0.012f, new Color(1f, 1f, 1f, 0.95f));
        infoText = MakeText("Bilgi", 0.007f, new Color(0.8f, 0.9f, 1f, 0.9f));
        scoreText.gameObject.SetActive(false);
        infoText.gameObject.SetActive(false);
    }

    Transform Prim(PrimitiveType type, Transform parent, string name, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    TextMesh MakeText(string name, float size, Color color)
    {
        var go = new GameObject(name, typeof(TextMesh));
        go.transform.SetParent(transform, false);
        var tm = go.GetComponent<TextMesh>();
        tm.font = UiKit.Font;
        go.GetComponent<MeshRenderer>().sharedMaterial = UiKit.Font.material;
        tm.fontSize = 64;
        tm.characterSize = size;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        return tm;
    }

    public void Begin()
    {
        if (Active) return;
        if (MiniGames.Playing) MiniGames.StopCurrent();
        Active = Playing = true;
        hidden.Clear();
        foreach (var w in Window.Windows)
            if (w.IsOpen) { hidden.Add(w); w.Close(); }

        origin = head.position;
        forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        right = Vector3.Cross(Vector3.up, forward);
        score = combo = 0;
        startTime = Time.time;
        nextSpawn = Time.time + 1.5f;
        hiltFilter.Reset();
        dirFilter.Reset();
        hasPrev = false;

        saber.gameObject.SetActive(true);
        scoreText.gameObject.SetActive(true);
        infoText.gameObject.SetActive(true);
        Place(scoreText.transform, 2.6f, 0.55f);
        Place(infoText.transform, 2.6f, 0.38f);
        UpdateTexts();
    }

    public void Stop()
    {
        if (!Active) return;
        Active = Playing = false;
        foreach (var c in cubes) Destroy(c.t.gameObject);
        foreach (var p in pieces) Destroy(p.t.gameObject);
        cubes.Clear();
        pieces.Clear();
        saber.gameObject.SetActive(false);
        scoreText.gameObject.SetActive(false);
        infoText.gameObject.SetActive(false);
        foreach (var w in hidden) if (w != null) w.Open();
        hidden.Clear();
    }

    void Place(Transform t, float distance, float height)
    {
        t.position = origin + forward * distance + Vector3.up * height;
        t.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    void Update()
    {
        if (!Active) return;
        float dt = Time.deltaTime;
        UpdateSaber(dt);

        // Küp gönder: zamanla hızlanır
        float elapsed = Time.time - startTime;
        float speed = Mathf.Min(1.3f + elapsed * 0.015f, 2.8f);
        if (Time.time >= nextSpawn)
        {
            nextSpawn = Time.time + Mathf.Max(0.7f, 1.5f - elapsed * 0.01f);
            Spawn(speed);
        }

        var bladeDir = tip - hilt;
        float tipSpeed = (tip - lastTip).magnitude / Mathf.Max(dt, 1e-4f);
        for (int i = cubes.Count - 1; i >= 0; i--)
        {
            var c = cubes[i];
            c.t.position += c.velocity * dt;
            c.t.Rotate(c.spin * dt, Space.Self);

            if (Swept(c.t.position, cubeSize * 0.75f + 0.03f))
            {
                SliceCube(c, bladeDir, tip - lastTip);
                cubes.RemoveAt(i);
                score += 10 * (1 + Mathf.Min(combo, 7)) + (tipSpeed > 1.5f ? 5 : 0);
                combo++;
                best = Mathf.Max(best, score);
                UpdateTexts();
                continue;
            }
            if (Vector3.Dot(c.t.position - origin, forward) < -0.3f)
            {
                Destroy(c.t.gameObject);
                cubes.RemoveAt(i);
                combo = 0;
                Sfx.Miss();
                UpdateTexts();
            }
        }

        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            var p = pieces[i];
            p.life += dt;
            p.velocity += Vector3.down * 3.5f * dt;
            p.t.position += p.velocity * dt;
            p.t.Rotate(p.spin * dt, Space.Self);
            float k = 1f - p.life / p.maxLife;
            p.t.localScale = p.scale * Mathf.Max(0f, k);
            if (p.life >= p.maxLife) { Destroy(p.t.gameObject); pieces.RemoveAt(i); }
        }
        lastTip = tip;
    }

    // Kılıç: avucun içinden, elin "yukarı" yönünde (parmaklar yukarıyken dik durur) ve biraz ileri eğik.
    // El kısa süre kaybolursa son yerinde bekler; uzun kaybolursa bakışla kontrol edilir.
    void UpdateSaber(float dt)
    {
        Vector3 h, dir;
        if (HandTracker.Visible)
        {
            lastHandTime = Time.time;
            h = HandTracker.PalmCenter;
            dir = (HandTracker.HandUp * 0.8f + Flat(head.forward)).normalized;   // ileri uzanır, görüşte kalır
            lastHandHilt = h; lastHandDir = dir;
        }
        else if (Time.time - lastHandTime < 0.5f)
        {
            h = lastHandHilt; dir = lastHandDir;
        }
        else
        {
            h = head.position - head.up * 0.25f + head.forward * 0.35f;
            dir = (head.up * 0.8f + head.forward).normalized;
        }
        prevHilt = hilt; prevTip = tip;
        hilt = hiltFilter.Filter(h, dt);
        dir = dirFilter.Filter(dir, dt).normalized;
        tip = hilt + dir * bladeLength;
        if (!hasPrev) { prevHilt = hilt; prevTip = tip; hasPrev = true; }
        saber.position = hilt;
        saber.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        // Hafif titreşen parıltı
        float pulse = 0.042f + Mathf.Sin(Time.time * 30f) * 0.003f;
        glow.localScale = new Vector3(pulse, glow.localScale.y, pulse);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    // Kılıcın bu karedeki savrulma alanına (önceki konumdan şimdikine) küp değiyor mu
    bool Swept(Vector3 p, float radius)
    {
        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f;
            if (DistanceToSegment(p, Vector3.Lerp(prevHilt, hilt, t), Vector3.Lerp(prevTip, tip, t)) < radius) return true;
        }
        return false;
    }

    void Spawn(float speed)
    {
        var mat = cubeMaterials[Random.Range(0, cubeMaterials.Length)];
        var t = Prim(PrimitiveType.Cube, transform, "Kup", mat);
        t.localScale = Vector3.one * cubeSize;
        // Kılıcın rahat yetiştiği bölge: önde, göğüs-göz arası, kamera görüşünün içinde
        float lateral = Random.Range(-0.25f, 0.25f);
        float height = Random.Range(-0.12f, 0.18f);
        t.position = origin + forward * 7f + right * lateral + Vector3.up * height;
        t.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, Random.Range(0, 4) * 90f);
        // Oyuncunun önüne (0.55 m) doğru gelir
        var target = origin + forward * 0.55f + right * lateral * 0.6f + Vector3.up * height;
        var vel = (target - t.position).normalized * speed;
        cubes.Add(new Cube { t = t, velocity = vel, spin = new Vector3(0f, 0f, Random.Range(-40f, 40f)) });
    }

    void SliceCube(Cube c, Vector3 bladeDir, Vector3 swing)
    {
        Sfx.Slice();
        // Kesme düzleminin normali: kılıç yönü x savrulma yönü (yoksa kılıç x ileri)
        var n = Vector3.Cross(bladeDir, swing.sqrMagnitude > 1e-6f ? swing : forward);
        if (n.sqrMagnitude < 1e-6f) n = right;
        n.Normalize();
        var mat = c.t.GetComponent<Renderer>().sharedMaterial;
        for (int s = -1; s <= 1; s += 2)
        {
            var half = Prim(PrimitiveType.Cube, transform, "Yarim", mat);
            half.rotation = Quaternion.LookRotation(Vector3.Cross(n, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(n, Vector3.up) : forward, n);
            var scale = new Vector3(cubeSize, cubeSize * 0.48f, cubeSize);
            half.localScale = scale;
            half.position = c.t.position + n * s * cubeSize * 0.26f;
            pieces.Add(new Piece
            {
                t = half, scale = scale, maxLife = 0.9f,
                velocity = c.velocity * 0.4f + n * s * 1.3f + Vector3.up * 0.4f,
                spin = Random.insideUnitSphere * 300f
            });
        }
        for (int k = 0; k < 10; k++)
        {
            var spark = Prim(PrimitiveType.Cube, transform, "Kivilcim", sparkMaterial);
            var sc = Vector3.one * Random.Range(0.012f, 0.025f);
            spark.localScale = sc;
            spark.position = c.t.position;
            pieces.Add(new Piece
            {
                t = spark, scale = sc, maxLife = Random.Range(0.3f, 0.6f),
                velocity = Random.onUnitSphere * Random.Range(1.2f, 2.6f),
                spin = Random.insideUnitSphere * 600f
            });
        }
        Destroy(c.t.gameObject);
    }

    void UpdateTexts()
    {
        scoreText.text = score.ToString();
        infoText.text = combo > 1 ? $"x{Mathf.Min(combo, 8)} kombo" : $"en iyi {best}   ·   çıkmak için menü";
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector3.Distance(p, a + ab * t);
    }
}
