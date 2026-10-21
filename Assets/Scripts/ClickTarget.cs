using UnityEngine;

// Işınla tıklanabilen 3B nesne (oyun köşesindeki nesneler, balonlar, hedefler): küre şeklinde isabet alanı.
// Üzerine gelince hafif büyür; tıklayınca onClick çalışır.
public class ClickTarget : PointerTarget
{
    public float radius = 0.15f;
    public System.Action<Vector3> onClick;   // isabet noktası
    public bool growOnHover = true;

    bool hover;
    Vector3 baseScale;
    float t;

    void Awake() { baseScale = transform.localScale; }

    public void SetBaseScale(Vector3 s) { baseScale = s; transform.localScale = s; }

    void Update()
    {
        if (!growOnHover) return;
        t = Mathf.MoveTowards(t, hover ? 1f : 0f, Time.deltaTime * 8f);
        transform.localScale = baseScale * (1f + 0.15f * t);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        hover = false;
        t = 0f;
    }

    public override bool Raycast(Ray ray, out float distance, out Vector3 point)
    {
        // Işın-küre kesişimi
        var c = transform.position;
        float r = radius * transform.lossyScale.x / Mathf.Max(baseScale.x, 1e-4f);
        var oc = ray.origin - c;
        float b = Vector3.Dot(oc, ray.direction);
        float cc = Vector3.Dot(oc, oc) - r * r;
        float disc = b * b - cc;
        distance = 0f; point = Vector3.zero;
        if (disc < 0f) return false;
        distance = -b - Mathf.Sqrt(disc);
        if (distance < 0f) return false;
        point = ray.GetPoint(distance);
        return true;
    }

    // Nesnenin çevresinde iki kat yarıçap pay
    public override bool Near(Ray ray, out float distance, out Vector3 point)
    {
        distance = 0f; point = Vector3.zero;
        var c = transform.position;
        float r = 2.2f * radius * transform.lossyScale.x / Mathf.Max(baseScale.x, 1e-4f);
        float t = Vector3.Dot(c - ray.origin, ray.direction);
        if (t <= 0f) return false;
        var closest = ray.GetPoint(t);
        if ((closest - c).sqrMagnitude > r * r) return false;
        distance = t;
        point = closest;
        return true;
    }

    public override void Hover(bool on)
    {
        if (on && !hover) Sfx.Hover();
        hover = on;
    }

    public override void Press(Ray ray, Vector3 point)
    {
        Sfx.Click();
        onClick?.Invoke(point);
    }
}
