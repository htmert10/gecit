using System.Collections.Generic;
using UnityEngine;

// Hedef vurma: önünde, ağaçların arasında hedefler belirir ve kısa süre kalır. Işınla nişan alıp tıkla.
// Merkeze ne kadar yakınsa o kadar puan: merkez 10, iç halka 5, dış halka 2. 45 saniye.
public class TargetGame : MonoBehaviour
{
    public Transform head;
    public Material material;
    public float duration = 45f;

    public bool Active { get; private set; }

    readonly List<(Transform t, float until)> targets = new List<(Transform, float)>();
    TextMesh hud, info;
    Vector3 origin, forward;
    float endTime, nextSpawn, finishedAt = -1f;
    int score;
    const float Radius = 0.28f;

    void Start()
    {
        hud = MiniGames.MakeText(transform, "Hedef Skor", 0.012f, Color.white);
        info = MiniGames.MakeText(transform, "Hedef Bilgi", 0.006f, new Color(0.85f, 0.9f, 1f));
        hud.gameObject.SetActive(false);
        info.gameObject.SetActive(false);
    }

    public void Begin()
    {
        if (Active) return;
        MiniGames.Begin(Stop);
        Active = true;
        origin = head.position;
        forward = head.forward; forward.y = 0f; forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        score = 0;
        endTime = Time.time + duration;
        nextSpawn = Time.time + 0.6f;
        finishedAt = -1f;
        hud.gameObject.SetActive(true);
        info.gameObject.SetActive(true);
        MiniGames.Face(hud.transform, origin + forward * 3.5f + Vector3.up * 0.95f, origin);
        MiniGames.Face(info.transform, origin + forward * 3.5f + Vector3.up * 0.72f, origin);
        info.text = "Hedeflere nişan al, tıkla! Merkez 10 puan";
        Debug.Log("GECIT OYUN hedef basladi");
    }

    public void Stop()
    {
        if (!Active) return;
        Active = false;
        foreach (var x in targets) if (x.t != null) Destroy(x.t.gameObject);
        targets.Clear();
        hud.gameObject.SetActive(false);
        info.gameObject.SetActive(false);
        MiniGames.End();
    }

    void Update()
    {
        if (!Active) return;
        if (finishedAt > 0f)
        {
            if (Time.time - finishedAt > 3.5f) Stop();
            return;
        }
        float left = endTime - Time.time;
        hud.text = $"{score}   ·   {Mathf.CeilToInt(Mathf.Max(0f, left))} sn";
        if (left <= 0f)
        {
            foreach (var x in targets) if (x.t != null) Destroy(x.t.gameObject);
            targets.Clear();
            info.text = $"Bitti! {score} puan";
            finishedAt = Time.time;
            Sfx.Click();
            return;
        }
        if (Time.time >= nextSpawn && targets.Count < 3)
        {
            nextSpawn = Time.time + Random.Range(0.7f, 1.3f);
            Spawn(left);
        }
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            var (t, until) = targets[i];
            if (t == null) { targets.RemoveAt(i); continue; }
            float remain = until - Time.time;
            // Belirirken büyür, kaybolurken küçülür
            float s = Mathf.Clamp01((until - Time.time) / 0.25f) * Mathf.Clamp01((Time.time - (until - 2.4f)) / 0.2f);
            t.localScale = Vector3.one * Mathf.Max(0.001f, s);
            if (remain <= 0f) { Destroy(t.gameObject); targets.RemoveAt(i); }
        }
    }

    void Spawn(float timeLeft)
    {
        float ang = Random.Range(-45f, 45f);
        float dist = Random.Range(3f, 5.5f);
        var dir = Quaternion.Euler(0f, ang, 0f) * forward;
        var pos = origin + dir * dist + Vector3.up * Random.Range(-0.5f, 0.7f);

        var root = new GameObject("Hedef").transform;
        MiniGames.Face(root, pos, origin);
        // Halkalar: dıştan içe kırmızı / beyaz / kırmızı / sarı merkez (yassı silindirler, hafif önde)
        AddRing(root, Radius, new Color(0.9f, 0.2f, 0.22f, 1f), 0f);
        AddRing(root, Radius * 0.72f, new Color(0.95f, 0.95f, 0.95f, 1f), 0.004f);
        AddRing(root, Radius * 0.45f, new Color(0.9f, 0.2f, 0.22f, 1f), 0.008f);
        AddRing(root, Radius * 0.2f, new Color(1f, 0.85f, 0.2f, 1f), 0.012f);
        var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(stand.GetComponent<Collider>());
        stand.transform.SetParent(root, false);
        stand.transform.localPosition = new Vector3(0f, -Radius - 0.25f, 0.02f);
        stand.transform.localScale = new Vector3(0.025f, 0.25f, 0.025f);
        stand.GetComponent<Renderer>().sharedMaterial = material;

        var target = root.gameObject.AddComponent<ClickTarget>();
        target.radius = Radius;
        target.growOnHover = false;
        target.onClick = hit =>
        {
            float d = Vector3.Distance(Vector3.ProjectOnPlane(hit - root.position, root.forward), Vector3.zero);
            int pts = d < Radius * 0.2f ? 10 : d < Radius * 0.45f ? 5 : 2;
            score += pts;
            info.text = pts == 10 ? "Tam merkez! +10" : $"+{pts}";
            Sfx.Slice();
            MiniGames.Burst(hit, pts == 10 ? new Color(1f, 0.85f, 0.2f) : new Color(0.9f, 0.2f, 0.22f), material, 8);
            Destroy(root.gameObject);
        };
        targets.Add((root, Time.time + 2.4f));
    }

    void AddRing(Transform root, float r, Color c, float front)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(root, false);
        g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        g.transform.localPosition = new Vector3(0f, 0f, -front);
        g.transform.localScale = new Vector3(r * 2f, 0.01f, r * 2f);
        var rend = g.GetComponent<Renderer>();
        rend.sharedMaterial = material;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", c);
        block.SetColor("_RimColor", Color.Lerp(c, Color.white, 0.3f));
        rend.SetPropertyBlock(block);
    }
}
