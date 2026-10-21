using UnityEngine;
using UnityEngine.UI;

// Meta tarzı yuvarlak düğme: üzerine gelince açılır ve hafif büyür, kıstırınca tıklar.
public class UiButton : PointerTarget
{
    public System.Action onClick;
    public Image background;
    public Image icon;
    public Color idle = new Color(0.16f, 0.17f, 0.21f, 0.85f);
    public Color hover = new Color(0.32f, 0.35f, 0.42f, 0.95f);
    public Color pressed = new Color(0.45f, 0.62f, 1f, 1f);
    public float padding = 8f;
    public float hoverScale = 1.1f;

    bool isHover, isPressed;
    float t;
    RectTransform rt;

    void Awake() { rt = (RectTransform)transform; }

    public static UiButton Create(Transform parent, string name, Vector2 position, float size, UiKit.Icon iconType, System.Action action)
    {
        var bg = UiKit.Box(parent, name, position, new Vector2(size, size), Color.white, null, 80f / size);   // tam yuvarlak
        var ic = UiKit.Box(bg.transform, "Simge", Vector2.zero, new Vector2(size * 0.62f, size * 0.62f), Color.white, UiKit.GetIcon(iconType));
        var b = bg.gameObject.AddComponent<UiButton>();
        b.background = bg;
        b.icon = ic;
        b.onClick = action;
        bg.color = b.idle;
        return b;
    }

    float shownT = -1f;
    bool shownPressed;
    Image glow;
    float appear = 1f, appearDelay, punch;
    public bool glowEdge = true;

    void Start()
    {
        // Kenarda neon çizgi: hep hafifçe görünür, üstüne gelince parlar
        if (!glowEdge || background == null || glow != null) return;
        var r = ((RectTransform)background.transform).rect;
        float m = Mathf.Max(background.pixelsPerUnitMultiplier, 0.01f);
        float radius = Mathf.Min(40f / m, Mathf.Min(r.width, r.height) * 0.5f);
        glow = UiKit.Holo(background.transform, "Parilti", Vector2.zero, r.size, radius, new Color(0.3f, 0.82f, 1f, 0f), 12f, 0f);
        glow.transform.SetAsFirstSibling();
        shownT = -1f;
    }

    // Normal (üstünde değilken) rengi değiştir (ör. açık/kapalı anahtar)
    public void SetIdle(Color c)
    {
        if (idle == c) return;
        idle = c;
        shownT = -1f;
    }

    // Menü açılırken: küçükten yaylanarak büyü (sırayla gelsin diye gecikmeli)
    public void PopIn(float delay)
    {
        appear = 0f;
        appearDelay = delay;
        transform.localScale = Vector3.one * 0.001f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        t = Mathf.MoveTowards(t, isHover || isPressed ? 1f : 0f, dt * 10f);
        bool animating = appear < 1f || punch > 0f;
        if (!animating && t == shownT && isPressed == shownPressed) return;   // değişmediyse dokunma: tuval yeniden kurulmasın
        if (appear < 1f)
        {
            if (appearDelay > 0f) appearDelay -= dt;
            else appear = Mathf.MoveTowards(appear, 1f, dt / 0.32f);
        }
        punch = Mathf.MoveTowards(punch, 0f, dt * 4f);
        shownT = t;
        shownPressed = isPressed;
        background.color = isPressed ? pressed : Color.Lerp(idle, hover, t);
        float s = Mathf.Lerp(1f, hoverScale, t) * UiKit.EaseOutBack(appear) * (1f - 0.1f * Mathf.Sin(punch * Mathf.PI));
        transform.localScale = Vector3.one * Mathf.Max(0.001f, s);
        if (glow != null)
        {
            var c = glow.color;
            c.a = Mathf.Clamp01((0.22f + 0.78f * t + punch * 0.8f) * Mathf.Clamp01(appear * 1.5f));
            glow.color = c;
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        isHover = isPressed = false;
        t = 0f;
        punch = 0f;
        shownT = -1f;
    }

    public override bool Raycast(Ray ray, out float distance, out Vector3 point) =>
        UiKit.HitRect(rt, ray, padding, out distance, out point, out _) && CanvasVisible();

    // Düğmenin çevresinde 60 birimlik pay (menüde düğmeler arasında nokta kaybolmasın)
    public override bool Near(Ray ray, out float distance, out Vector3 point) =>
        UiKit.HitRect(rt, ray, 60f, out distance, out point, out _) && CanvasVisible();

    public override bool Snap(Ray ray, out float angle, out float distance, out Vector3 point)
    {
        angle = distance = 0f; point = Vector3.zero;
        return CanvasVisible() && SnapRect(rt, ray, out angle, out distance, out point);
    }

    bool CanvasVisible()
    {
        var g = GetComponentInParent<CanvasGroup>();
        return g == null || g.alpha > 0.2f;
    }

    public override void Hover(bool on)
    {
        if (on && !isHover) Sfx.Hover();
        isHover = on;
    }

    public override void Press(Ray ray, Vector3 point)
    {
        isPressed = true;
        punch = 1f;
        Debug.Log("GECIT DUGME " + name);
        Sfx.Click();
        onClick?.Invoke();
    }

    public override void Release() { isPressed = false; }
}
