using UnityEngine;
using UnityEngine.UI;

// Meta tarzı taşıma çubuğu (hap): kıstırıp tutunca bağlı olduğu nesneyi (klavye, menü paneli) ışınla taşır,
// nesne hep kullanıcıya dönük kalır.
public class MoveHandle : PointerTarget
{
    public Transform root;
    public Transform head;
    public Image image;
    public System.Action onMoved;
    public float padding = 22f;
    public float minDistance = 0.25f, maxDistance = 2.5f;

    static readonly Color Idle = new Color(0.92f, 0.93f, 0.97f, 0.45f);
    static readonly Color Hot = new Color(1f, 1f, 1f, 0.95f);

    bool hover, grabbed;
    float grabDistance;
    Vector3 grabOffset;
    RectTransform rt;

    public bool Grabbed => grabbed;

    public static MoveHandle Create(Transform canvas, Vector2 position, float width, Transform root, Transform head)
    {
        var img = UiKit.Box(canvas, "Tasima Cubugu", position, new Vector2(width, 14f), Idle, null, 80f / 14f);
        var h = img.gameObject.AddComponent<MoveHandle>();
        h.image = img;
        h.root = root;
        h.head = head;
        return h;
    }

    void Awake() { rt = (RectTransform)transform; }

    void Update()
    {
        var c = hover || grabbed ? Hot : Idle;
        if (image.color != c) image.color = c;
    }

    public override bool Raycast(Ray ray, out float distance, out Vector3 point)
    {
        var g = GetComponentInParent<CanvasGroup>();
        if (g != null && g.alpha < 0.5f) { distance = 0f; point = Vector3.zero; return false; }
        return UiKit.HitRect(rt, ray, padding, out distance, out point, out _);
    }

    public override void Hover(bool on) { hover = on; }

    public override bool Snap(Ray ray, out float angle, out float distance, out Vector3 point)
    {
        angle = distance = 0f; point = Vector3.zero;
        var g = GetComponentInParent<CanvasGroup>();
        if (g != null && g.alpha < 0.5f) return false;
        return SnapRect(rt, ray, out angle, out distance, out point);
    }

    public override void Press(Ray ray, Vector3 point)
    {
        grabbed = true;
        grabDistance = Vector3.Distance(ray.origin, point);
        grabOffset = root.position - point;
        Sfx.Click();
    }

    public override void Drag(Ray ray)
    {
        if (!grabbed) return;
        var target = ray.GetPoint(grabDistance) + grabOffset;
        var fromHead = target - head.position;
        float d = fromHead.magnitude;
        if (d > 1e-3f) target = head.position + fromHead / d * Mathf.Clamp(d, minDistance, maxDistance);
        root.position = target;
        root.rotation = ViewFrame.Facing(root.position, head);
    }

    public override Vector3? GrabPoint(Ray ray) => grabbed ? ray.GetPoint(grabDistance) : (Vector3?)null;

    public override void Release()
    {
        if (!grabbed) return;
        grabbed = false;
        onMoved?.Invoke();
    }
}
