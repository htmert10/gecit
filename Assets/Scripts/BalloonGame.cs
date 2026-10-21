using System.Collections.Generic;
using UnityEngine;

// Balon patlatma: etrafından renkli balonlar yükselir; ışınla nişan alıp tıkla (kumanda OK ya da el modunda kıstır).
// 45 saniye; her balon 1 puan, altın balon 5 puan. Kaçan balon puan götürmez.
public class BalloonGame : MonoBehaviour
{
    public Transform head;
    public Material material;        // Gecit/VirtualHand (solma kapalı)
    public float duration = 45f;

    public bool Active { get; private set; }

    readonly List<(Transform t, float speed, float sway)> balloons = new List<(Transform, float, float)>();
    TextMesh hud, info;
    Vector3 origin, forward;
    float endTime, nextSpawn, finishedAt = -1f;
    int score;

    static readonly Color[] Colors =
    {
        new Color(0.95f, 0.25f, 0.30f, 1f), new Color(0.25f, 0.55f, 1f, 1f), new Color(1f, 0.85f, 0.25f, 1f),
        new Color(0.35f, 0.85f, 0.45f, 1f), new Color(0.95f, 0.45f, 0.85f, 1f),
    };

    void Start()
    {
        hud = MiniGames.MakeText(transform, "Balon Skor", 0.012f, Color.white);
        info = MiniGames.MakeText(transform, "Balon Bilgi", 0.006f, new Color(0.85f, 0.9f, 1f));
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
        nextSpawn = Time.time + 0.5f;
        finishedAt = -1f;
        hud.gameObject.SetActive(true);
        info.gameObject.SetActive(true);
        MiniGames.Face(hud.transform, origin + forward * 3f + Vector3.up * 0.75f, origin);
        MiniGames.Face(info.transform, origin + forward * 3f + Vector3.up * 0.55f, origin);
        info.text = "Balonlara nişan al, tıkla!";
        Debug.Log("GECIT OYUN balon basladi");
    }

    public void Stop()
    {
        if (!Active) return;
        Active = false;
        foreach (var b in balloons) if (b.t != null) Destroy(b.t.gameObject);
        balloons.Clear();
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
            foreach (var b in balloons) if (b.t != null) Destroy(b.t.gameObject);
            balloons.Clear();
            info.text = $"Bitti! {score} puan";
            finishedAt = Time.time;
            Sfx.Click();
            return;
        }

        if (Time.time >= nextSpawn)
        {
            nextSpawn = Time.time + Random.Range(0.45f, 0.9f);
            Spawn();
        }

        for (int i = balloons.Count - 1; i >= 0; i--)
        {
            var (t, speed, sway) = balloons[i];
            if (t == null) { balloons.RemoveAt(i); continue; }
            t.position += Vector3.up * speed * Time.deltaTime + Vector3.right * Mathf.Sin(Time.time * 1.3f + sway) * 0.08f * Time.deltaTime;
            if (t.position.y > origin.y + 1.8f) { Destroy(t.gameObject); balloons.RemoveAt(i); }
        }
    }

    void Spawn()
    {
        bool gold = Random.value < 0.1f;
        var color = gold ? new Color(1f, 0.78f, 0.15f, 1f) : Colors[Random.Range(0, Colors.Length)];
        float ang = Random.Range(-55f, 55f);
        float dist = Random.Range(2.0f, 3.4f);
        var dir = Quaternion.Euler(0f, ang, 0f) * forward;
        var pos = origin + dir * dist + Vector3.up * Random.Range(-0.9f, -0.5f);

        var root = new GameObject("Balon").transform;
        root.position = pos;
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root, false);
        body.transform.localScale = new Vector3(0.24f, 0.30f, 0.24f) * (gold ? 0.8f : 1f);
        var r = body.GetComponent<Renderer>();
        r.sharedMaterial = material;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        block.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.6f));
        r.SetPropertyBlock(block);
        var str = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(str.GetComponent<Collider>());
        str.transform.SetParent(root, false);
        str.transform.localPosition = new Vector3(0f, -0.32f, 0f);
        str.transform.localScale = new Vector3(0.004f, 0.17f, 0.004f);
        str.GetComponent<Renderer>().sharedMaterial = material;

        var target = root.gameObject.AddComponent<ClickTarget>();
        target.radius = gold ? 0.13f : 0.16f;
        target.growOnHover = false;
        target.onClick = _ =>
        {
            score += gold ? 5 : 1;
            Sfx.Slice();
            MiniGames.Burst(root.position, color, material);
            Destroy(root.gameObject);
        };
        balloons.Add((root, Random.Range(0.3f, 0.5f), Random.value * 10f));
    }
}
