using UnityEngine;

// Tek merkezden ışın yönetimi: tüm hedefler (pencereler, tuşlar, düğmeler) arasından en yakını seçer,
// halka imleci ve elden çıkan ışını çizer, basma/sürükleme/bırakmayı ilgili hedefe iletir.
[DefaultExecutionOrder(100)]   // telefon ve el güncellendikten sonra: imleç kamerayla aynı karede
public class PointerManager : MonoBehaviour
{
    public Transform head;
    public SpriteRenderer cursor;
    public Sprite ringSprite;
    public Sprite dotSprite;
    public RayBeam line;
    public bool secondary;   // ikinci elin işaretçisi (PointerInput.Second)

    // Hangi işaretçiden okunur
    bool InValid => secondary ? PointerInput.Second.Valid : PointerInput.Valid;
    Ray InRay => secondary ? PointerInput.Second.Ray : PointerInput.Ray;
    bool InPressed => secondary ? PointerInput.Second.Pressed : PointerInput.Pressed;
    string InSource => secondary ? PointerInput.Second.Source : PointerInput.Source;
    Vector3 InOrigin => secondary ? PointerInput.Second.VisualOrigin : PointerInput.VisualOrigin;
    bool InHasPressRay => secondary ? PointerInput.Second.HasPressRay : PointerInput.HasPressRay;
    Ray InPressRay => secondary ? PointerInput.Second.PressRay : PointerInput.PressRay;
    bool InPalmUp => secondary ? PointerInput.Second.PalmUp : HandTracker.PalmUp;
    float InPinch => secondary ? PointerInput.Second.PinchAmount : HandTracker.PinchAmount;
    public Material ringMaterial;   // Gecit/Overlay: kıstırma halkası ve tıklama dalgası

    SpriteRenderer pinchRing, burst;
    float ringK, burstT = 1f;
    bool ringWasPressed;
    static readonly Color Neon = new Color(0.3f, 0.82f, 1f, 1f);

    public static string HoverName = "-";
    static readonly Color CursorGray = Color.white;
    public static PointerTarget Hovered;

    PointerTarget hovered;
    PointerTarget active;
    bool wasPressed;

    void Start()
    {
        pinchRing = RingPart("Kistirma Halkasi");
        burst = RingPart("Tiklama Dalgasi");
    }

    SpriteRenderer RingPart(string name)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(cursor.transform, false);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = UiKit.ThinRing;
        if (ringMaterial != null) sr.sharedMaterial = ringMaterial;
        sr.sortingOrder = 9;
        sr.enabled = false;
        return sr;
    }

    // Quest gibi: noktanın çevresinde kıstırdıkça daralan halka (ne kadar kaldığı görünür), tıklayınca dışa yayılan dalga
    void LateUpdate()
    {
        if (pinchRing == null) return;
        float dt = Time.deltaTime;
        bool show = cursor.enabled;
        bool hand = InSource == "el";
        bool pressedNow = show && InPressed;
        float k = pressedNow ? 1f : hand && HandTracker.HandClicks ? InPinch : 0f;
        ringK = Mathf.Lerp(ringK, k, 1f - Mathf.Exp(-dt * 25f));
        bool over = Hovered != null;
        pinchRing.enabled = show;
        if (show)
        {
            // Sade: halka yalnız kıstırmaya başlayınca belirir ve noktaya doğru daralır
            pinchRing.transform.localScale = Vector3.one * Mathf.Lerp(0.95f, 0.6f, ringK);
            var c = Color.Lerp(Color.white, Neon, ringK * 0.6f);
            c.a = Mathf.Clamp01(ringK * 1.2f) * 0.8f;
            pinchRing.color = c;
        }

        if (pressedNow && !ringWasPressed)
        {
            burstT = 0f;
            if (line != null) line.Pulse();
        }
        ringWasPressed = pressedNow;
        burstT = Mathf.MoveTowards(burstT, 1f, dt / 0.38f);
        burst.enabled = show && burstT < 1f;
        if (burst.enabled)
        {
            float e = 1f - (1f - burstT) * (1f - burstT);
            burst.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.6f, e);
            var bc = Color.Lerp(Color.white, Neon, 0.4f);
            bc.a = (1f - burstT) * 0.55f;
            burst.color = bc;
        }
    }

    void Update()
    {
        PointerTarget.CurrentPointer = secondary ? 1 : 0;
        if (!InValid)
        {
            if (active != null) { active.Release(); active = null; }
            SetHover(null);
            cursor.enabled = false;
            line.Visible = false;
            return;
        }

        if (InSource == "el" && InPalmUp)
        {
            // Avuç menüsü hareketi: ışın ve imleç gizli, hiçbir şeye tıklanmaz
            if (active != null) { active.Release(); active = null; }
            SetHover(null);
            cursor.enabled = false;
            line.Visible = false;
            wasPressed = false;
            return;
        }

        var ray = InRay;
        bool pressed = InPressed;
        Vector3? point = null;

        if (active != null)
        {
            if (pressed)
            {
                active.Drag(ray);
                point = active.GrabPoint(ray);
                if (point == null && active.Raycast(ray, out _, out var p)) point = p;
            }
            else
            {
                active.Release();
                active = null;
            }
        }

        if (active == null)
        {
            bool found = FindTarget(ray, out var best, out var bestPoint);
            SetHover(best);
            PointerTarget nearTarget = null;
            inMargin = false;
            if (found) point = bestPoint;
            else if (FindNear(ray, out var nearPoint, out nearTarget)) { point = nearPoint; inMargin = true; }   // hedefin yakınında

            if (pressed && !wasPressed)
            {
                // Basmadan hemen önceki ışın (kıstırırken / OK'a basarken el kıpırdadıysa tıklama kaymasın)
                var pressRay = (InSource == "el" && InHasPressRay) ? InPressRay : ray;
                if (!FindTarget(pressRay, out var pressTarget, out var pressPoint)) { pressTarget = best; pressPoint = bestPoint; }
                // Payın içinde basıldıysa en yakın DÜĞMEYE tıkla (nokta görünüyordu ama tıklamıyordu; kullanıcı
                // "tıklamayı algılama azalmış" dedi). Pencere içeriğine payda tıklanmaz.
                if (pressTarget == null && nearTarget != null && !(nearTarget is Window) && !(nearTarget is CurvedScreen))
                    nearTarget.Near(pressRay, out _, out pressPoint);
                if (pressTarget == null && nearTarget != null && !(nearTarget is Window) && !(nearTarget is CurvedScreen))
                    pressTarget = nearTarget;
                if (pressTarget != null)
                {
                    pressTarget.Raycast(pressRay, out _, out _);   // hedefin bölümünü bu ışına göre ayarla
                    active = pressTarget;
                    active.Press(pressRay, pressPoint);
                    if (active is Window) Sfx.Click();   // pencereye gerçek tıklama (düğmeler kendi sesini çalar)
                }
            }
        }

        // Nokta ve çizgi yalnız bir ekranın / düğmenin üstündeyken (kullanıcı: ekran yokken çizgi olmasın)
        cursor.enabled = point.HasValue;
        if (point.HasValue)
        {
            var p = point.Value;
            cursor.transform.position = p + (head.position - p).normalized * 0.01f;
            cursor.transform.rotation = Quaternion.LookRotation(cursor.transform.position - head.position);
            float squeeze = Mathf.Lerp(1f, 0.75f, pressed ? 1f : InPinch);
            cursor.transform.localScale = Vector3.one * Vector3.Distance(head.position, p) * 0.03f * squeeze;
            cursor.sprite = dotSprite;
            cursor.color = pressed ? new Color(0.35f, 0.6f, 1f, 1f) : inMargin ? new Color(1f, 1f, 1f, 0.45f) : CursorGray;
        }

        // Elden çıkan, sona doğru silikleşen ışın
        bool showRay = InSource == "el" && point.HasValue && !SaberGame.Playing;   // oyunda kılıç ışının yerini alır
        line.Visible = showRay;
        if (showRay)
        {
            // Kıstırdıkça ışın maviye döner (basmaya ne kadar kaldığı belli olsun)
            float glow = pressed ? 0.6f : 0f;   // sade: yalnız basılıyken hafif mavi
            line.color = Color.Lerp(Color.white, new Color(0.55f, 0.82f, 1f, 1f), glow);
            line.SetPosition(0, InOrigin);
            line.SetPosition(1, point.Value);
        }

        wasPressed = pressed;
    }

    // Mıknatıs KAPALI (kullanıcı: imleç kendi kendine düğmelere gidiyor, sabit durmuyor)
    const bool UseSnap = false;
    const float SnapMargin = 2.2f;   // derece: açılırsa düğmenin kenarına bu kadar yakın ışın düğmeye yapışır

    // En yakın doğrudan isabet; yoksa (ya da önde bir düğme varsa) mıknatısla yakındaki düğme
    static bool FindTarget(Ray ray, out PointerTarget target, out Vector3 point)
    {
        target = null; point = Vector3.zero;
        float bestDistance = float.MaxValue;
        for (int i = PointerTarget.All.Count - 1; i >= 0; i--)
        {
            var t = PointerTarget.All[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            float d; Vector3 p;
            try { if (!t.Raycast(ray, out d, out p)) continue; }
            catch (System.Exception e) { LogOnce(t, e); continue; }   // tek bir hata bütün tıklamaları durdurmasın
            if (d < bestDistance) { target = t; bestDistance = d; point = p; }
        }

        if (!UseSnap) return target != null;

        PointerTarget snap = null;
        float snapAngle = SnapMargin, snapDistance = 0f;
        Vector3 snapPoint = Vector3.zero;
        foreach (var t in PointerTarget.All)
        {
            if (!t.isActiveAndEnabled || !t.Snap(ray, out float a, out float d, out Vector3 p)) continue;
            if (a < snapAngle) { snap = t; snapAngle = a; snapDistance = d; snapPoint = p; }
        }
        if (snap != null && (target == null || target == snap || snapDistance < bestDistance - 0.05f))
        {
            target = snap;
            point = snapPoint;
        }
        return target != null;
    }

    bool inMargin;

    static readonly System.Collections.Generic.HashSet<string> logged = new System.Collections.Generic.HashSet<string>();
    static void LogOnce(PointerTarget t, System.Exception e)
    {
        string key = t.GetType().Name + e.GetType().Name;
        if (logged.Add(key)) Debug.LogWarning("GECIT HEDEF HATASI " + t.name + " (" + t.GetType().Name + "): " + e);
    }

    // Pay içindeki en yakın hedef: önce düğmeler (ekran payı düğmeyi örtmesin), sonra en yakın uzaklık
    static bool FindNear(Ray ray, out Vector3 point, out PointerTarget target)
    {
        point = Vector3.zero;
        target = null;
        float best = float.MaxValue;
        bool bestIsButton = false;
        for (int i = PointerTarget.All.Count - 1; i >= 0; i--)
        {
            var t = PointerTarget.All[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            float d; Vector3 p;
            try { if (!t.Near(ray, out d, out p)) continue; }
            catch (System.Exception e) { LogOnce(t, e); continue; }
            bool isButton = !(t is Window) && !(t is CurvedScreen);
            if ((isButton && !bestIsButton) || (isButton == bestIsButton && d < best))
            {
                best = d; point = p; target = t; bestIsButton = isButton;
            }
        }
        return target != null;
    }

    void SetHover(PointerTarget t)
    {
        if (t == hovered) return;
        if (!secondary)
        {
            HoverName = t != null ? t.name : "-";
            Hovered = t;
        }
        if (hovered != null) hovered.Hover(false);
        hovered = t;
        if (hovered != null) hovered.Hover(true);
    }
}
