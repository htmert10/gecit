using System.Collections.Generic;
using TLab.WebView;
using UnityEngine;
using UnityEngine.UI;

// Meta tarzı pencere: içerik (tarayıcı), alttaki hap çubuk (taşıma), sağ alt köşe (boyut).
// Taşırken sabitleme noktasından çözülür, bırakınca yeniden bağlanır.
public class Window : PointerTarget
{
    public static Window First;
    public static readonly List<Window> Windows = new List<Window>();
    public static Window LastUsed;

    public Transform head;
    public RectTransform content;
    public BrowserContainer container;
    public Collider moveBar;
    public Collider resizeHandle;
    public Collider resizeHandle2;   // sol alt köşe
    public Image resizeImage2;
    public Image barImage;
    public Image resizeImage;
    public WindowAnchor anchor;
    public float minScale = 0.4f, maxScale = 3f;

    enum Part { None, Content, Bar, Resize }
    enum Mode { None, Move, Resize, Touch, TwoHand }
    int modeOwner = -1;                       // hangi işaretçi (el) tutuyor
    readonly Ray[] lastRays = new Ray[2];
    float twoStartDist, twoStartScale;
    Part lastPart;
    Mode mode;
    float grabDistance;
    Vector3 grabOffset;
    float startScale, startDistance;
    long downTime;
    int lastX, lastY;
    int startX, startY;
    bool dragging;
    const int DragThreshold = 40;   // bu kadar pikselden az oynama = tıklama (kaydırma değil)
    float hoverAlpha;
    bool hoverBar;

    static readonly Color BarIdle = new Color(0.92f, 0.93f, 0.97f, 0.45f);
    static readonly Color BarHover = new Color(1f, 1f, 1f, 0.95f);

    public bool IsGrabbed => mode == Mode.Move || mode == Mode.Resize;

    public string Title => name.Replace("Pencere ", "");
    public VRBrowser Browser { get; private set; }
    CanvasGroup toolbar;
    Image holo;
    Text titleText;
    Transform canvasT;
    Vector3 canvasScale;
    float pop = 1f, holoShown = -1f;

    void Awake()
    {
        if (First == null) First = this;
        Windows.Add(this);
        Browser = GetComponentInChildren<VRBrowser>(true);
        canvasT = content.GetComponentInParent<Canvas>(true).transform;
        canvasScale = canvasT.localScale;
    }

    // Açılınca: hafif küçükten yaylanarak büyür
    protected override void OnEnable()
    {
        base.OnEnable();
        pop = 0f;
    }

    void Start()
    {
        // Pencerenin altındaki düğmeler: solda Geri / Yenile, sağda Tam Ekran / Kapat
        var canvas = content.GetComponentInParent<Canvas>().transform;
        // Kenarda neon çerçeve (çerçevenin dış kenarında); üstüne gelince / tutunca parlar
        var size = ((RectTransform)canvas).sizeDelta + new Vector2(28f, 28f);
        holo = UiKit.Holo(canvas, "Isikli Cerceve", Vector2.zero, size, 40f, new Color(0.3f, 0.82f, 1f, 0.3f), 36f, 0.7f, 70f);
        holo.transform.SetSiblingIndex(1);
        holo.transform.localPosition += new Vector3(0f, 0f, 1f);
        var go = new GameObject("Dugmeler", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(canvas, false);
        toolbar = go.GetComponent<CanvasGroup>();
        float y = -((RectTransform)canvas).sizeDelta.y * 0.5f - 44f;
        UiButton.Create(go.transform, "Geri", new Vector2(-250f, y), 56f, UiKit.Icon.Back, () => Browser.Back());
        UiButton.Create(go.transform, "Yenile", new Vector2(-186f, y), 56f, UiKit.Icon.Reload, () => Browser.Reload());
        UiButton.Create(go.transform, "Tam Ekran", new Vector2(186f, y), 56f, UiKit.Icon.Fullscreen, () => Browser.ToggleFullscreen());
        UiButton.Create(go.transform, "Kapat", new Vector2(250f, y), 56f, UiKit.Icon.Close, Close);

        // Üstte başlık hapı (Quest gibi): sitenin adı; pencereye yaklaşınca belirginleşir
        float top = ((RectTransform)canvas).sizeDelta.y * 0.5f + 46f;
        var chip = UiKit.Box(go.transform, "Baslik", new Vector2(0f, top), new Vector2(300f, 50f), new Color(0.05f, 0.07f, 0.1f, 0.82f), null, 80f / 50f);
        UiKit.Box(chip.transform, "Nokta", new Vector2(-122f, 0f), new Vector2(12f, 12f), UiKit.HoloColor, null, 80f / 12f);
        titleText = UiKit.Label(chip.transform, Title, 26, new Color(1f, 1f, 1f, 0.95f), new Vector2(8f, 0f), new Vector2(240f, 50f));
    }

    public bool IsOpen => gameObject.activeSelf;

    public void Close()
    {
        Browser.container.browser.EvaluateJS("document.querySelectorAll('video').forEach(function(v){v.pause();});window.__gecitExitFs&&window.__gecitExitFs();");
        ForceRelease();
        gameObject.SetActive(false);
    }

    public void Open() { gameObject.SetActive(true); RefreshTitle(); }

    public void RefreshTitle() { if (titleText != null) titleText.text = Title; }

    // Pencereyi verilen yere taşıyıp kullanıcıya döndürür, yeni yerde yeniden sabitler
    public void MoveTo(Vector3 position)
    {
        anchor.Suspend();
        transform.position = position;
        transform.rotation = ViewFrame.Facing(position, head);   // uzanmışken bakışa eğik
        anchor.Resume();
        if (MarkerSpace.Instance != null) MarkerSpace.Instance.Remember(this);
    }

    void Update()
    {
        // Göz koruma: gözlükte sayfa biraz kısık
        if (Browser != null && Browser.view != null)
        {
            var tint = EyeComfort.Tint;
            if (Browser.view.color != tint) Browser.view.color = tint;
        }
        bool near = mode != Mode.None || (PointerInput.Valid && Near(PointerInput.Ray));
        hoverAlpha = Mathf.MoveTowards(hoverAlpha, near ? 1f : 0f, Time.deltaTime * 4f);
        var bc = (hoverBar || mode == Mode.Move) ? BarHover : BarIdle;
        if (barImage.color != bc) barImage.color = bc;
        var rc = resizeImage.color;
        float ra = hoverAlpha * (mode == Mode.Resize ? 1f : 0.6f);
        if (!Mathf.Approximately(rc.a, ra)) { rc.a = ra; resizeImage.color = rc; if (resizeImage2 != null) resizeImage2.color = rc; }
        float glow = mode != Mode.None ? 1f : PointerManager.Hovered == this ? 0.75f : 0.2f + 0.15f * hoverAlpha;
        if (holo != null && Mathf.Abs(glow - holoShown) > 0.01f)
        {
            holoShown = Mathf.MoveTowards(holoShown < 0f ? glow : holoShown, glow, Time.deltaTime * 4f);
            var hc = holo.color; hc.a = holoShown; holo.color = hc;
        }
        if (pop < 1f)
        {
            pop = Mathf.MoveTowards(pop, 1f, Time.deltaTime / 0.4f);
            canvasT.localScale = canvasScale * Mathf.LerpUnclamped(0.8f, 1f, UiKit.EaseOutBack(pop));
        }
        float ta = Mathf.Lerp(0.35f, 1f, hoverAlpha);
        if (toolbar != null && !Mathf.Approximately(toolbar.alpha, ta)) toolbar.alpha = ta;
    }

    public override bool Raycast(Ray ray, out float distance, out Vector3 point)
    {
        lastPart = Part.None;
        distance = float.MaxValue;
        point = Vector3.zero;

        if (hoverAlpha > 0.3f && resizeHandle.Raycast(ray, out RaycastHit rh, 30f))
        { lastPart = Part.Resize; distance = rh.distance; point = rh.point; }
        else if (hoverAlpha > 0.3f && resizeHandle2 != null && resizeHandle2.Raycast(ray, out RaycastHit rh2, 30f))
        { lastPart = Part.Resize; distance = rh2.distance; point = rh2.point; }
        else if (moveBar.Raycast(ray, out RaycastHit bh, 30f))
        { lastPart = Part.Bar; distance = bh.distance; point = bh.point; }
        else if (ContentHit(ray, out point, out _, out _))
        { lastPart = Part.Content; distance = Vector3.Distance(ray.origin, point); }

        hoverBar = lastPart == Part.Bar;
        return lastPart != Part.None;
    }

    public override void Hover(bool on) { if (!on) hoverBar = false; }

    // Pencerenin çevresinde enin/boyun %25'i kadar pay (alttaki düğmeler ve taşıma çubuğu da içinde)
    public override bool Near(Ray ray, out float distance, out Vector3 point)
    {
        distance = 0f; point = Vector3.zero;
        var plane = new Plane(content.forward, content.position);
        if (!plane.Raycast(ray, out distance)) return false;
        point = ray.GetPoint(distance);
        var local = content.InverseTransformPoint(point);
        var r = content.rect;
        return Mathf.Abs(local.x) < r.width * 0.75f && Mathf.Abs(local.y) < r.height * 0.8f;
    }

    public override void Press(Ray ray, Vector3 point)
    {
        LastUsed = this;
        int pid = PointerTarget.CurrentPointer;
        lastRays[pid] = ray;
        // Takılı kalmış durum: pencereyi tutan işaretçi artık basmıyorsa (pencere kapandı, el kayboldu...) sıfırla.
        // Yoksa sonraki bütün tıklamalar yok sayılıyordu (kullanıcı: "el ile tıklama çalışmıyor").
        if (mode != Mode.None && modeOwner != pid && !OwnerPressing()) ForceRelease();
        if (mode != Mode.None && modeOwner != pid)
        {
            // İkinci el gerçekten ayrı bir yeri tutmuyorsa (aynı elin kopyası / çok yakın) tıklamayı bozma
            if (TwoHandDistance() < 0.15f) return;
            // İkinci el de pencereyi tuttu: iki elle açıp kapayarak boyutlandır (Quest gibi)
            if (mode == Mode.Touch) container.browser.TouchEvent(lastX, lastY, 1, downTime);   // dokunmayı bitir
            if (mode != Mode.Move && mode != Mode.Resize) anchor.Suspend();
            mode = Mode.TwoHand;
            twoStartScale = transform.localScale.x;
            twoStartDist = Mathf.Max(0.05f, TwoHandDistance());
            return;
        }
        modeOwner = pid;
        switch (lastPart)
        {
            case Part.Resize:
                anchor.Suspend();
                mode = Mode.Resize;
                grabDistance = Vector3.Distance(ray.origin, point);
                startScale = transform.localScale.x;
                startDistance = Mathf.Max(0.05f, Vector3.Distance(point, transform.position));
                break;
            case Part.Bar:
                anchor.Suspend();
                mode = Mode.Move;
                grabDistance = Vector3.Distance(ray.origin, point);
                grabOffset = transform.position - point;
                break;
            case Part.Content:
                ContentHit(ray, out _, out float u, out float v);
                mode = Mode.Touch;
                var b = container.browser;
                lastX = (int)(u * b.viewSize.x);
                lastY = (int)(v * b.viewSize.y);
                startX = lastX; startY = lastY;
                dragging = false;
                downTime = b.TouchEvent(lastX, lastY, 0, downTime);
                break;
        }
    }

    public override void Drag(Ray ray)
    {
        int pid = PointerTarget.CurrentPointer;
        lastRays[pid] = ray;
        if (mode == Mode.TwoHand)
        {
            float target = Mathf.Clamp(twoStartScale * TwoHandDistance() / twoStartDist, minScale, maxScale);
            float sc = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-Time.deltaTime * 16f));
            transform.localScale = Vector3.one * sc;
            return;
        }
        if (pid != modeOwner) return;
        switch (mode)
        {
            case Mode.Move:
                // Yumuşak takip: el titremesi / imleç sıçraması pencereyi silkelemesin
                transform.position = Vector3.Lerp(transform.position, ray.GetPoint(grabDistance) + grabOffset, 1f - Mathf.Exp(-Time.deltaTime * 20f));
                transform.rotation = Quaternion.Slerp(transform.rotation, ViewFrame.Facing(transform.position, head), 0.3f);
                break;
            case Mode.Resize:
                var plane = new Plane(transform.forward, transform.position);
                if (!plane.Raycast(ray, out float d)) return;
                grabDistance = d;
                float target = Mathf.Clamp(startScale * Vector3.Distance(ray.GetPoint(d), transform.position) / startDistance, minScale, maxScale);
                float scale = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-Time.deltaTime * 16f));   // yumuşak, sıçramasız
                if (Mathf.Abs(scale - transform.localScale.x) > 1e-4f) transform.localScale = Vector3.one * scale;
                break;
            case Mode.Touch:
                if (!ContentHit(ray, out _, out float u, out float v)) return;
                var b = container.browser;
                int x = (int)(u * b.viewSize.x), y = (int)(v * b.viewSize.y);
                if (!dragging && Mathf.Abs(x - startX) + Mathf.Abs(y - startY) < DragThreshold) return;
                dragging = true;
                if (x == lastX && y == lastY) return;
                b.TouchEvent(x, y, 2, downTime);
                lastX = x; lastY = y;
                break;
        }
    }

    public override void Release()
    {
        int pid = PointerTarget.CurrentPointer;
        if (mode == Mode.TwoHand)
        {
            mode = Mode.None;
            modeOwner = -1;
            anchor.Resume();
            if (MarkerSpace.Instance != null) MarkerSpace.Instance.Remember(this);
            return;
        }
        if (pid != modeOwner && modeOwner >= 0) return;
        modeOwner = -1;
        if (mode == Mode.Touch)
        {
            if (!dragging) { lastX = startX; lastY = startY; }
            container.browser.TouchEvent(lastX, lastY, 1, downTime);
        }
        bool wasGrab = mode == Mode.Move || mode == Mode.Resize;
        mode = Mode.None;
        if (wasGrab)
        {
            anchor.Resume();
            if (MarkerSpace.Instance != null) MarkerSpace.Instance.Remember(this);
        }
    }

    public override Vector3? GrabPoint(Ray ray)
    {
        if (mode == Mode.TwoHand)
        {
            var plane = new Plane(transform.forward, transform.position);
            return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : (Vector3?)null;
        }
        return (mode == Mode.Move || mode == Mode.Resize) && PointerTarget.CurrentPointer == modeOwner ? ray.GetPoint(grabDistance) : (Vector3?)null;
    }

    bool OwnerPressing() => modeOwner == 0 ? PointerInput.Pressed : modeOwner == 1 && PointerInput.Second.Pressed;

    // Kim tutuyor olursa olsun bırak (pencere kapanırken, takılı kalınca)
    void ForceRelease()
    {
        if (mode == Mode.Touch) container.browser.TouchEvent(dragging ? lastX : startX, dragging ? lastY : startY, 1, downTime);
        bool wasGrab = mode == Mode.Move || mode == Mode.Resize || mode == Mode.TwoHand;
        mode = Mode.None;
        modeOwner = -1;
        if (wasGrab) anchor.Resume();
    }

    // İki elin ışınlarının pencere düzlemine değdiği noktalar arası uzaklık
    float TwoHandDistance()
    {
        var plane = new Plane(transform.forward, transform.position);
        if (!plane.Raycast(lastRays[0], out float d0) || !plane.Raycast(lastRays[1], out float d1)) return twoStartDist;
        return Vector3.Distance(lastRays[0].GetPoint(d0), lastRays[1].GetPoint(d1));
    }

    bool Near(Ray ray)
    {
        var plane = new Plane(content.forward, content.position);
        if (!plane.Raycast(ray, out float d)) return false;
        var local = content.InverseTransformPoint(ray.GetPoint(d));
        var r = content.rect;
        return Mathf.Abs(local.x) < r.width * 0.65f && Mathf.Abs(local.y) < r.height * 0.7f;
    }

    bool ContentHit(Ray ray, out Vector3 point, out float u, out float v)
    {
        point = Vector3.zero; u = v = 0f;
        var plane = new Plane(content.forward, content.position);
        if (!plane.Raycast(ray, out float d)) return false;
        point = ray.GetPoint(d);
        var local = content.InverseTransformPoint(point);
        var rect = content.rect;
        u = local.x / rect.width + content.pivot.x;
        v = 1f - (local.y / rect.height + content.pivot.y);
        return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
    }
}
