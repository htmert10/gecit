using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Meta tarzı sanal klavye (Türkçe Q). Web sayfasında yazı alanına dokununca sayfadaki betik
// "Klavye" nesnesine Show/Hide gönderir; klavye kolunun yetişeceği yerde, o pencereye doğru belirir.
//  - Dokunarak yazma: işaret parmağının ucu tuşa yaklaşınca altında halka belirir, tuşu içeri itince basılır
//    (tuş göçer, tık sesi). Parmağı geri çekince bir sonraki basışa hazır olur.
//  - Uzaktan: ışınla seçip kıstırarak da basılabilir. Sil tuşu basılı tutulunca tekrarlar.
//  - Alttaki çubuktan tutup istediğin yere taşınır; taşıdıysan bir dahaki açılışta orada kalır.
public class VRKeyboard : PointerTarget
{
    public Transform head;

    const float Pitch = 54f, KeySize = 48f;
    const float MetersPerPixel = 0.00056f;   // tuş ~2,7 cm
    const float PokeOn = 0.006f;             // düzlemin bu kadar arkasına geçince bas (m)
    const float PokeOff = -0.004f;           // bu kadar öne çekince bırak
    const float PokeArm = -0.01f;            // yeni basış için parmak en az bu kadar önde olmalı
    const float PokeHover = -0.09f;          // bu kadar yakında halka görünür
    const bool PokeTyping = false;           // parmakla basarak yazma kapalı (tek kamerada derinlik güvenilmez)

    enum Kind { Char, Backspace, Enter, Shift, Space, Hide, Text }

    class Key
    {
        public Kind kind;
        public string lower, upper;
        public Rect rect;
        public Image bg;
        public Text label;
        public float downUntil;
        public Color shownColor;
        public bool shownDown;
    }

    static readonly string[][] Rows =
    {
        new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "#Sil" },
        new[] { "q", "w", "e", "r", "t", "y", "u", "ı", "o", "p", "ğ", "ü" },
        new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l", "ş", "i", "#Git" },
        new[] { "#Aa", "z", "x", "c", "v", "b", "n", "m", "ö", "ç", ".", "@" },
        new[] { "#Gizle", "/", "#bosluk", "-", "#.com" },
    };

    readonly List<Key> keys = new List<Key>();
    Canvas canvas;
    CanvasGroup group;
    RectTransform panel;
    Image ring;
    MoveHandle handle;
    VRBrowser target;
    bool shift;
    bool visible;
    bool userPlaced;
    float hideAt = -1f;
    Key hovered, pressed;
    float repeatAt;

    // Dokunarak yazma durumu
    bool pokeArmed = true;
    Key pokeHeld, pokeHover;
    float pokeRepeatAt, lastPokeTime;
    bool fingerNear;

    static readonly Color KeyIdle = new Color(0.19f, 0.2f, 0.25f, 0.95f);
    static readonly Color KeyHover = new Color(0.34f, 0.37f, 0.45f, 1f);
    static readonly Color KeyDown = new Color(0.42f, 0.6f, 1f, 1f);
    static readonly Color KeySpecial = new Color(0.13f, 0.14f, 0.18f, 0.95f);

    void Start()
    {
        var size = new Vector2(12 * Pitch + 30f, Rows.Length * Pitch + 30f);
        canvas = UiKit.WorldCanvas("Klavye Tuvali", transform, size, MetersPerPixel);
        group = canvas.GetComponent<CanvasGroup>();
        panel = (RectTransform)canvas.transform;
        UiKit.Box(panel, "Zemin", Vector2.zero, size, new Color(0.07f, 0.08f, 0.1f, 0.9f), null, 2f);

        for (int r = 0; r < Rows.Length; r++)
        {
            var row = Rows[r];
            float units = 0f;
            foreach (var k in row) units += Width(k);
            float x = -units * Pitch * 0.5f;
            float y = (Rows.Length * 0.5f - r - 0.5f) * Pitch;
            foreach (var spec in row)
            {
                float w = Width(spec);
                var key = MakeKey(spec);
                var keySize = new Vector2(w * Pitch - (Pitch - KeySize), KeySize);
                var center = new Vector2(x + w * Pitch * 0.5f, y);
                key.rect = new Rect(center - new Vector2(w * Pitch, Pitch) * 0.5f, new Vector2(w * Pitch, Pitch));
                key.bg = UiKit.Box(panel, "Tus " + spec, center, keySize, BaseColor(key), null, 2.4f);
                key.shownColor = key.bg.color;
                key.label = UiKit.Label(key.bg.transform, key.lower, key.kind == Kind.Char ? 26 : 19, Color.white, Vector2.zero, keySize);
                keys.Add(key);
                x += w * Pitch;
            }
        }

        // Parmak ucu halkası (dokunmaya yaklaştıkça küçülür)
        ring = UiKit.Box(panel, "Parmak Halkasi", Vector2.zero, new Vector2(40f, 40f), new Color(1f, 1f, 1f, 0.9f), UiKit.GetIcon(UiKit.Icon.Ring));
        ring.enabled = false;

        handle = MoveHandle.Create(panel, new Vector2(0f, -size.y * 0.5f - 26f), 200f, transform, head);
        handle.onMoved = () => userPlaced = true;

        SetVisible(false, true);
    }

    static float Width(string spec)
    {
        switch (spec)
        {
            case "#Sil": case "#Git": return 1.6f;
            case "#Gizle": return 2f;
            case "#bosluk": return 6f;
            case "#.com": return 2f;
            default: return 1f;
        }
    }

    static Key MakeKey(string spec)
    {
        switch (spec)
        {
            case "#Sil": return new Key { kind = Kind.Backspace, lower = "Sil", upper = "Sil" };
            case "#Git": return new Key { kind = Kind.Enter, lower = "Git", upper = "Git" };
            case "#Aa": return new Key { kind = Kind.Shift, lower = "Aa", upper = "AA" };
            case "#Gizle": return new Key { kind = Kind.Hide, lower = "Gizle", upper = "Gizle" };
            case "#bosluk": return new Key { kind = Kind.Space, lower = "boşluk", upper = "boşluk" };
            case "#.com": return new Key { kind = Kind.Text, lower = ".com", upper = ".com" };
            default:
                string up = spec == "i" ? "İ" : spec == "ı" ? "I" : spec.ToUpperInvariant();
                return new Key { kind = Kind.Char, lower = spec, upper = up };
        }
    }

    Color BaseColor(Key k)
    {
        if (k.kind == Kind.Shift && shift) return KeyHover;
        return k.kind == Kind.Char || k.kind == Kind.Space ? KeyIdle : KeySpecial;
    }

    // ---- Sayfadaki betikten gelen mesajlar (UnitySendMessage) ----
    public void Show(string windowName)
    {
        Debug.Log("GECIT KLAVYE ac " + windowName);
        hideAt = -1f;
        VRBrowser found = null;
        foreach (var b in FindObjectsByType<VRBrowser>(FindObjectsSortMode.None))
            if (b.windowName == windowName) found = b;
        if (found == null) return;
        bool firstShow = !visible;
        target = found;
        if (firstShow && !(userPlaced && InComfortZone())) Place(found.GetComponentInParent<Window>());
        SetVisible(true, false);
    }

    public void Hide(string windowName)
    {
        Debug.Log("GECIT KLAVYE kapat " + windowName);
        if (target != null && target.windowName != windowName) return;
        if (handle != null && handle.Grabbed) return;
        hideAt = Time.time + 0.3f;   // alanlar arası geçişte titreşmesin
    }

    // Kullanıcının taşıdığı yer hâlâ önünde ve uzanabileceği mesafede mi
    bool InComfortZone()
    {
        var to = transform.position - head.position;
        return to.magnitude < 1.2f && ViewFrame.AngleFromView(head, transform.position) < 60f;
    }

    // Kolun yetişeceği yer: pencereye doğru, ~40 cm önde, biraz aşağıda, yüzü sana dönük
    void Place(Window window)
    {
        var dir = ViewFrame.Forward(head);
        if (window != null)
        {
            var toWin = window.transform.position - head.position;
            if (!ViewFrame.Tilted(head)) toWin.y = 0f;
            if (toWin.sqrMagnitude > 1e-6f) dir = toWin.normalized;
        }
        var pos = head.position + dir * 0.4f - ViewFrame.Up(head) * 0.09f;
        transform.position = pos;
        transform.rotation = ViewFrame.Facing(pos, head);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    void SetVisible(bool on, bool instant)
    {
        visible = on;
        if (instant) group.alpha = on ? 1f : 0f;
        if (!on)
        {
            hovered = pressed = pokeHeld = pokeHover = null;
            pokeArmed = true;
            shift = false;
            RefreshLabels();
        }
    }

    void Update()
    {
        if (hideAt > 0f && Time.time >= hideAt) { hideAt = -1f; SetVisible(false, false); }
        float a = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.deltaTime * 6f);
        canvas.enabled = a > 0f;
        if (!canvas.enabled) { group.alpha = 0f; return; }
        if (a != group.alpha)
        {
            group.alpha = a;
            panel.localScale = Vector3.one * MetersPerPixel * Mathf.Lerp(0.92f, 1f, a);
        }

        UpdatePoke();

        foreach (var k in keys)
        {
            bool down = k == pressed || k == pokeHeld || Time.time < k.downUntil;
            var c = down ? KeyDown : (k == hovered || k == pokeHover) ? KeyHover : BaseColor(k);
            if (c != k.shownColor) { k.shownColor = c; k.bg.color = c; }
            if (down != k.shownDown)
            {
                k.shownDown = down;
                var lp = k.bg.rectTransform.localPosition;
                lp.z = down ? 9f : 0f;   // tuş içeri göçer
                k.bg.rectTransform.localPosition = lp;
                k.bg.rectTransform.localScale = Vector3.one * (down ? 0.94f : 1f);
            }
        }
    }

    // ---- Parmakla dokunarak yazma ----
    void UpdatePoke()
    {
        fingerNear = false;
        pokeHover = null;
        if (!PokeTyping || !visible || group.alpha < 0.8f || !HandTracker.Visible || (handle != null && handle.Grabbed))
        {
            if (ring.enabled) ring.enabled = false;
            pokeHeld = null;
            pokeArmed = true;
            return;
        }

        var tip = HandTracker.Joints[8];
        var local = panel.InverseTransformPoint(tip);
        float depth = local.z * panel.lossyScale.z;   // metre; artı = klavyenin arkasına geçti
        var r = panel.rect;
        bool inside = local.x > r.xMin - 10f && local.x < r.xMax + 10f && local.y > r.yMin - 10f && local.y < r.yMax + 10f;
        Key under = null;
        if (inside)
            foreach (var k in keys)
                if (k.rect.Contains(new Vector2(local.x, local.y))) { under = k; break; }

        fingerNear = inside && depth > -0.12f && depth < 0.06f;

        // Halka: tuşa yaklaştıkça küçülür ve parlar
        bool showRing = inside && depth > PokeHover && depth < 0.03f;
        if (ring.enabled != showRing) ring.enabled = showRing;
        if (showRing)
        {
            float t = Mathf.InverseLerp(PokeHover, 0f, depth);
            ring.rectTransform.anchoredPosition = new Vector2(local.x, local.y);
            ring.rectTransform.localPosition = new Vector3(local.x, local.y, -2f);
            ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.4f, 0.45f, t);
            ring.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 1f, t));
            pokeHover = under;
        }

        if (pokeHeld != null)
        {
            if (depth < PokeOff) { pokeHeld = null; }
            else if (pokeHeld.kind == Kind.Backspace && Time.time >= pokeRepeatAt)
            {
                pokeRepeatAt = Time.time + 0.08f;
                Activate(pokeHeld);
            }
        }
        if (depth < PokeArm) pokeArmed = true;

        if (pokeHeld == null && pokeArmed && under != null && depth > PokeOn && Time.time - lastPokeTime > 0.12f)
        {
            pokeArmed = false;
            pokeHeld = under;
            lastPokeTime = Time.time;
            pokeRepeatAt = Time.time + 0.45f;
            under.downUntil = Time.time + 0.12f;
            Sfx.Click();
            Activate(under);
        }
    }

    public override bool Near(Ray ray, out float distance, out Vector3 point)
    {
        distance = 0f; point = Vector3.zero;
        if (!visible || group.alpha < 0.5f) return false;
        return UiKit.HitRect(panel, ray, 80f, out distance, out point, out _);
    }

    // ---- Işın (uzaktan) ----
    public override bool Raycast(Ray ray, out float distance, out Vector3 point)
    {
        point = Vector3.zero;
        distance = 0f;
        if (!visible || group.alpha < 0.5f || fingerNear) return false;   // parmak yakındayken dokunma öncelikli
        if (!UiKit.HitRect(panel, ray, 0f, out distance, out point, out var local)) return false;
        hovered = null;
        foreach (var k in keys)
            if (k.rect.Contains(local)) { hovered = k; break; }
        return true;   // klavye zemini de ışını tutar (arkadaki pencereye tıklanmasın)
    }

    public override void Hover(bool on) { if (!on) hovered = null; }

    public override void Press(Ray ray, Vector3 point)
    {
        if (hovered == null) return;
        pressed = hovered;
        Sfx.Click();
        Activate(pressed);
        repeatAt = Time.time + 0.45f;
    }

    public override void Drag(Ray ray)
    {
        if (pressed == null || pressed.kind != Kind.Backspace) return;
        if (Time.time >= repeatAt)
        {
            repeatAt = Time.time + 0.08f;
            Activate(pressed);
        }
    }

    public override void Release() { pressed = null; }

    void Activate(Key k)
    {
        if (target == null) return;
        switch (k.kind)
        {
            case Kind.Char:
                target.Type(shift ? k.upper : k.lower);
                if (shift) { shift = false; RefreshLabels(); }
                break;
            case Kind.Text: target.Type(k.lower); break;
            case Kind.Space: target.Type(" "); break;
            case Kind.Backspace: target.Backspace(); break;
            case Kind.Enter: target.Enter(); break;
            case Kind.Shift: shift = !shift; RefreshLabels(); break;
            case Kind.Hide: target.Blur(); SetVisible(false, false); break;
        }
    }

    void RefreshLabels()
    {
        foreach (var k in keys)
            if (k.label != null) k.label.text = shift ? k.upper : k.lower;
    }
}
