using System.Collections.Generic;
using UnityEngine;

// Köstebek vur: önünde bel hizasında 3x3 delikli yeşil bir tahta; köstebekler rastgele çıkıp kısa süre kalır.
// Işınla nişan alıp tıkla (kumanda OK ya da el modunda kıstır). 45 saniye; altın köstebek 3 puan.
// Zaman geçtikçe köstebekler daha hızlı çıkıp kaçar.
public class WhackGame : MonoBehaviour
{
    public Transform head;
    public Material material;
    public float duration = 45f;

    public bool Active { get; private set; }

    class Mole { public Transform body; public ClickTarget click; public float upUntil; public float t; public bool gold; public bool hit; }

    readonly List<Mole> moles = new List<Mole>();
    Transform board;
    TextMesh hud, info;
    float endTime, nextPop, finishedAt = -1f;
    int score;

    void Start()
    {
        hud = MiniGames.MakeText(transform, "Kostebek Skor", 0.010f, Color.white);
        info = MiniGames.MakeText(transform, "Kostebek Bilgi", 0.005f, new Color(0.85f, 0.9f, 1f));
        hud.gameObject.SetActive(false);
        info.gameObject.SetActive(false);
    }

    public void Begin()
    {
        if (Active) return;
        MiniGames.Begin(Stop);
        Active = true;
        var fwd = head.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        BuildBoard(head.position + fwd * 0.95f + Vector3.down * 0.55f, fwd);
        score = 0;
        endTime = Time.time + duration;
        nextPop = Time.time + 0.8f;
        finishedAt = -1f;
        hud.gameObject.SetActive(true);
        info.gameObject.SetActive(true);
        MiniGames.Face(hud.transform, head.position + fwd * 1.6f + Vector3.up * 0.25f, head.position);
        MiniGames.Face(info.transform, head.position + fwd * 1.6f + Vector3.up * 0.1f, head.position);
        info.text = "Çıkan köstebeklere tıkla!";
        Debug.Log("GECIT OYUN kostebek basladi");
    }

    public void Stop()
    {
        if (!Active) return;
        Active = false;
        if (board != null) Destroy(board.gameObject);
        moles.Clear();
        hud.gameObject.SetActive(false);
        info.gameObject.SetActive(false);
        MiniGames.End();
    }

    void BuildBoard(Vector3 center, Vector3 fwd)
    {
        board = new GameObject("Kostebek Tahtasi").transform;
        board.position = center;
        board.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-20f, 0f, 0f);   // sana doğru eğik
        Part(board, PrimitiveType.Cube, new Color(0.22f, 0.45f, 0.20f, 1f), new Vector3(0f, -0.03f, 0f), new Vector3(0.72f, 0.05f, 0.62f));
        Part(board, PrimitiveType.Cube, new Color(0.40f, 0.27f, 0.17f, 1f), new Vector3(0f, -0.09f, 0f), new Vector3(0.76f, 0.08f, 0.66f));
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                var p = new Vector3((c - 1) * 0.22f, 0f, (1 - r) * 0.19f);
                Part(board, PrimitiveType.Cylinder, new Color(0.08f, 0.06f, 0.05f, 1f), p + Vector3.up * 0.001f, new Vector3(0.12f, 0.004f, 0.12f));
                var mole = new Mole();
                mole.body = new GameObject("Kostebek").transform;
                mole.body.SetParent(board, false);
                mole.body.localPosition = p + Vector3.down * 0.09f;
                Part(mole.body, PrimitiveType.Capsule, new Color(0.55f, 0.38f, 0.25f, 1f), Vector3.up * 0.04f, new Vector3(0.085f, 0.06f, 0.085f));
                Part(mole.body, PrimitiveType.Sphere, Color.white, new Vector3(-0.018f, 0.075f, -0.035f), Vector3.one * 0.018f);
                Part(mole.body, PrimitiveType.Sphere, Color.white, new Vector3(0.018f, 0.075f, -0.035f), Vector3.one * 0.018f);
                Part(mole.body, PrimitiveType.Sphere, new Color(0.95f, 0.55f, 0.6f, 1f), new Vector3(0f, 0.055f, -0.042f), Vector3.one * 0.016f);
                mole.click = mole.body.gameObject.AddComponent<ClickTarget>();
                mole.click.radius = 0.07f;
                mole.click.growOnHover = false;
                mole.click.enabled = false;
                var m = mole;
                mole.click.onClick = _ => Hit(m);
                moles.Add(mole);
            }
    }

    void Part(Transform parent, PrimitiveType type, Color color, Vector3 pos, Vector3 scale)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = material;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        block.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.5f));
        r.SetPropertyBlock(block);
    }

    void Hit(Mole m)
    {
        if (m.hit || m.t < 0.3f) return;
        m.hit = true;
        m.upUntil = Time.time;
        score += m.gold ? 3 : 1;
        Sfx.Slice();
        MiniGames.Burst(m.body.position + m.body.up * 0.06f, m.gold ? new Color(1f, 0.85f, 0.2f) : new Color(0.55f, 0.38f, 0.25f), material, 6);
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
            info.text = $"Bitti! {score} puan";
            finishedAt = Time.time;
            foreach (var m in moles) m.click.enabled = false;
            Sfx.Click();
            return;
        }

        float elapsed = duration - left;
        if (Time.time >= nextPop)
        {
            nextPop = Time.time + Mathf.Lerp(0.9f, 0.45f, elapsed / duration);
            var free = moles.FindAll(x => x.t <= 0.01f && x.upUntil < Time.time);
            if (free.Count > 0)
            {
                var m = free[Random.Range(0, free.Count)];
                m.gold = Random.value < 0.12f;
                m.hit = false;
                m.upUntil = Time.time + Mathf.Lerp(1.4f, 0.8f, elapsed / duration);
                var block = new MaterialPropertyBlock();
                var col = m.gold ? new Color(1f, 0.8f, 0.2f, 1f) : new Color(0.55f, 0.38f, 0.25f, 1f);
                block.SetColor("_Color", col);
                block.SetColor("_RimColor", Color.Lerp(col, Color.white, 0.5f));
                m.body.GetChild(0).GetComponent<Renderer>().SetPropertyBlock(block);
            }
        }

        foreach (var m in moles)
        {
            bool up = Time.time < m.upUntil && !m.hit;
            m.t = Mathf.MoveTowards(m.t, up ? 1f : 0f, Time.deltaTime * (up ? 8f : 10f));
            var lp = m.body.localPosition;
            lp.y = Mathf.Lerp(-0.09f, 0.0f, m.t);
            m.body.localPosition = lp;
            m.click.enabled = m.t > 0.3f && !m.hit;
        }
    }
}
