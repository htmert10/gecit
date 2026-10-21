using System.Collections.Generic;
using UnityEngine;

// Basket: önünde 3,2 m ötede potalı bir pano. İmleçle nereye nişan alırsan top o yöne, yukarı doğru bir kavisle
// fırlatılır (kumanda OK ya da el modunda kıstır). Top çembere yukarıdan girerse sayı. 10 atış hakkı.
// Panoya ve çembere çarpan top seker; yere düşen top kaybolur.
public class BasketGame : MonoBehaviour
{
    public Transform head;
    public Material material;
    public int shots = 10;
    public float throwSpeed = 6.2f;

    public bool Active { get; private set; }

    class Ball { public Transform t; public Vector3 vel; public float life; public bool scored; }

    readonly List<Ball> balls = new List<Ball>();
    Transform court;
    Vector3 rimCenter, boardNormal;
    TextMesh hud, info;
    ClickTarget aimCatcher;
    int left, made;
    float finishedAt = -1f;
    const float RimRadius = 0.23f, BallRadius = 0.12f, Gravity = 9.8f;

    void Start()
    {
        hud = MiniGames.MakeText(transform, "Basket Skor", 0.010f, Color.white);
        info = MiniGames.MakeText(transform, "Basket Bilgi", 0.005f, new Color(0.85f, 0.9f, 1f));
        hud.gameObject.SetActive(false);
        info.gameObject.SetActive(false);
    }

    public void Begin()
    {
        if (Active) return;
        MiniGames.Begin(Stop);
        Active = true;
        var fwd = head.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        BuildCourt(head.position + fwd * 3.2f, fwd);
        left = shots; made = 0; finishedAt = -1f;
        hud.gameObject.SetActive(true);
        info.gameObject.SetActive(true);
        MiniGames.Face(hud.transform, rimCenter + Vector3.up * 0.95f, head.position);
        MiniGames.Face(info.transform, rimCenter + Vector3.up * 0.78f, head.position);
        info.text = "Potanın biraz üstüne nişan al, at!";
        UpdateHud();
        Debug.Log("GECIT OYUN basket basladi");
    }

    public void Stop()
    {
        if (!Active) return;
        Active = false;
        foreach (var b in balls) if (b.t != null) Destroy(b.t.gameObject);
        balls.Clear();
        if (court != null) Destroy(court.gameObject);
        hud.gameObject.SetActive(false);
        info.gameObject.SetActive(false);
        MiniGames.End();
    }

    void BuildCourt(Vector3 basePos, Vector3 fwd)
    {
        court = new GameObject("Basket Sahasi").transform;
        court.position = basePos;
        court.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        float rimHeight = 0.55f;   // başın biraz üstü
        Part(court, PrimitiveType.Cube, new Color(0.95f, 0.95f, 0.97f, 1f), new Vector3(0f, rimHeight + 0.32f, 0.32f), new Vector3(1.1f, 0.75f, 0.03f));        // pano
        Part(court, PrimitiveType.Cube, new Color(0.9f, 0.25f, 0.2f, 1f), new Vector3(0f, rimHeight + 0.2f, 0.305f), new Vector3(0.45f, 0.35f, 0.01f));         // kare
        Part(court, PrimitiveType.Cube, new Color(0.95f, 0.95f, 0.97f, 1f), new Vector3(0f, rimHeight + 0.2f, 0.3f), new Vector3(0.39f, 0.29f, 0.012f));
        Part(court, PrimitiveType.Cylinder, new Color(0.25f, 0.27f, 0.3f, 1f), new Vector3(0f, rimHeight - 1.3f, 0.42f), new Vector3(0.09f, 1.55f, 0.09f));     // direk
        // Çember: ince turuncu halka (küçük küplerden)
        rimCenter = court.TransformPoint(new Vector3(0f, rimHeight, 0.05f));
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f;
            var p = new Vector3(Mathf.Cos(a) * RimRadius, rimHeight, 0.05f + Mathf.Sin(a) * RimRadius);
            Part(court, PrimitiveType.Cube, new Color(1f, 0.45f, 0.1f, 1f), p, new Vector3(0.06f, 0.018f, 0.018f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f));
        }
        // File (yarı görünür ince çizgiler)
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 2f / 8f;
            var p = new Vector3(Mathf.Cos(a) * RimRadius * 0.8f, rimHeight - 0.14f, 0.05f + Mathf.Sin(a) * RimRadius * 0.8f);
            Part(court, PrimitiveType.Cylinder, new Color(0.95f, 0.95f, 0.95f, 1f), p, new Vector3(0.006f, 0.14f, 0.006f));
        }
        boardNormal = -court.forward;

        // Atış için nişan yakalayıcı: önündeki geniş görünmez alan (ışın nereye bakarsa atış oraya)
        var catcher = new GameObject("Nisan").transform;
        catcher.SetParent(court, false);
        catcher.localPosition = new Vector3(0f, rimHeight + 0.3f, 0f);
        aimCatcher = catcher.gameObject.AddComponent<ClickTarget>();
        aimCatcher.radius = 2.2f;
        aimCatcher.growOnHover = false;
        aimCatcher.onClick = Throw;
    }

    void Part(Transform parent, PrimitiveType type, Color color, Vector3 pos, Vector3 scale, Quaternion? rot = null)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        if (rot.HasValue) g.transform.localRotation = rot.Value;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = material;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        block.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.4f));
        r.SetPropertyBlock(block);
    }

    // Işının işaret ettiği noktaya doğru, yukarı kavisli atış
    void Throw(Vector3 aimPoint)
    {
        if (left <= 0 || finishedAt > 0f) return;
        left--;
        var start = head.position + head.forward * 0.3f + Vector3.down * 0.25f;
        var dir = (aimPoint - start).normalized;
        var vel = dir * throwSpeed + Vector3.up * 2.4f;
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(g.GetComponent<Collider>());
        g.transform.position = start;
        g.transform.localScale = Vector3.one * BallRadius * 2f;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = material;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", new Color(0.95f, 0.5f, 0.15f, 1f));
        block.SetColor("_RimColor", new Color(1f, 0.8f, 0.6f, 1f));
        r.SetPropertyBlock(block);
        balls.Add(new Ball { t = g.transform, vel = vel });
        Sfx.Release();
        UpdateHud();
    }

    void UpdateHud() { hud.text = $"{made} basket   ·   {left} top"; }

    void Update()
    {
        if (!Active) return;
        float dt = Time.deltaTime;
        for (int i = balls.Count - 1; i >= 0; i--)
        {
            var b = balls[i];
            var prev = b.t.position;
            b.vel += Vector3.down * Gravity * dt;
            var pos = prev + b.vel * dt;

            // Panoya çarpma (pano düzlemi, çerçevesi içinde)
            var boardCenter = court.TransformPoint(new Vector3(0f, 0.87f, 0.32f));
            float dPrev = Vector3.Dot(prev - boardCenter, boardNormal), dNow = Vector3.Dot(pos - boardCenter, boardNormal);
            if (dPrev > BallRadius && dNow <= BallRadius)
            {
                var local = court.InverseTransformPoint(pos);
                if (Mathf.Abs(local.x) < 0.55f && Mathf.Abs(local.y - 0.87f) < 0.38f)
                {
                    b.vel = Vector3.Reflect(b.vel, boardNormal) * 0.6f;
                    pos = prev;
                    Sfx.Hover();
                }
            }
            // Çembere giriş: çember yüksekliğini aşağı doğru geçerken merkeze yakınsa sayı
            if (!b.scored && prev.y >= rimCenter.y && pos.y < rimCenter.y && b.vel.y < 0f)
            {
                var flat = new Vector2(pos.x - rimCenter.x, pos.z - rimCenter.z);
                float d = flat.magnitude;
                if (d < RimRadius - BallRadius * 0.6f)
                {
                    b.scored = true;
                    made++;
                    info.text = made % 3 == 0 ? "Harika! Üst üste basket!" : "Basket!";
                    Sfx.Slice();
                    MiniGames.Burst(rimCenter, new Color(1f, 0.6f, 0.2f), material, 8);
                    UpdateHud();
                }
                else if (d < RimRadius + BallRadius)
                {
                    // Çembere çarptı: dışarı sek
                    var outward = new Vector3(flat.x, 0f, flat.y).normalized;
                    b.vel = new Vector3(outward.x * 1.5f, Mathf.Abs(b.vel.y) * 0.4f, outward.z * 1.5f);
                    Sfx.Hover();
                }
            }
            b.t.position = pos;
            b.t.Rotate(400f * dt, 0f, 0f);
            b.life += dt;
            if (pos.y < head.position.y - 2.2f || b.life > 5f) { Destroy(b.t.gameObject); balls.RemoveAt(i); }
        }

        if (finishedAt < 0f && left <= 0 && balls.Count == 0)
        {
            finishedAt = Time.time;
            info.text = $"Bitti! {made}/{shots} basket";
            Sfx.Click();
        }
        if (finishedAt > 0f && Time.time - finishedAt > 3.5f) Stop();
    }
}
