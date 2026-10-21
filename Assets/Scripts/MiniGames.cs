using System.Collections.Generic;
using UnityEngine;

// Oyunların ortak işleri: oyun sırasında pencereleri gizleyip sonra geri açmak, skor yazısı, oyunu durdurmak.
public static class MiniGames
{
    static readonly List<Window> hidden = new List<Window>();
    public static System.Action StopCurrent;   // şu an oynanan oyunu durdurur (yoksa null)

    public static bool Playing => StopCurrent != null;

    // Yeni oyun başlarken: varsa önceki oyunu durdur, pencereleri gizle
    public static void Begin(System.Action stop)
    {
        StopCurrent?.Invoke();
        if (SaberGame.Playing) Object.FindFirstObjectByType<SaberGame>()?.Stop();
        StopCurrent = stop;
        hidden.Clear();
        foreach (var w in Window.Windows)
            if (w.IsOpen) { hidden.Add(w); w.Close(); }
    }

    public static void End()
    {
        StopCurrent = null;
        foreach (var w in hidden) if (w != null) w.Open();
        hidden.Clear();
    }

    // Kumandada ◄ (Geri) ya da menüden: oynanan oyun varsa durdur
    public static bool StopActive()
    {
        if (StopCurrent != null) { StopCurrent(); return true; }
        var saber = Object.FindFirstObjectByType<SaberGame>();
        if (saber != null && saber.Active) { saber.Stop(); return true; }
        return false;
    }

    public static TextMesh MakeText(Transform parent, string name, float size, Color color)
    {
        var go = new GameObject(name, typeof(TextMesh));
        go.transform.SetParent(parent, false);
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

    // Kullanıcıya dönük yerleştir
    public static void Face(Transform t, Vector3 position, Vector3 head)
    {
        t.position = position;
        var d = position - head; d.y = 0f;
        if (d.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
    }

    // Patlama parçaları (kısa ömürlü küçük küpler)
    public static void Burst(Vector3 at, Color color, Material material, int count = 10)
    {
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        for (int i = 0; i < count; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.position = at;
            g.transform.localScale = Vector3.one * Random.Range(0.02f, 0.045f);
            g.transform.rotation = Random.rotation;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.SetPropertyBlock(block);
            g.AddComponent<BurstPiece>().velocity = Random.onUnitSphere * Random.Range(1.2f, 2.5f) + Vector3.up * 0.8f;
        }
    }
}

public class BurstPiece : MonoBehaviour
{
    public Vector3 velocity;
    float life;
    Vector3 scale;

    void Start() { scale = transform.localScale; }

    void Update()
    {
        life += Time.deltaTime;
        velocity += Vector3.down * 4f * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;
        transform.Rotate(300f * Time.deltaTime, 200f * Time.deltaTime, 0f);
        transform.localScale = scale * Mathf.Max(0f, 1f - life / 0.7f);
        if (life > 0.7f) Destroy(gameObject);
    }
}
