using UnityEngine;

// Avuç menüsü (Meta'daki gibi, kumandasız): avucunu kendine çevirince baş ve işaret parmağının arasında hareketli
// bir logo belirir (yaylanarak büyür, dış halkası döner, nefes alır gibi parlar). Avuç sana dönükken baş ve işaret
// parmağını birleştirince menü açılır / kapanır. Bu sırada ışın ve imleç gizlenir, kıstırma başka yere tıklamaz.
public class PalmMenu : MonoBehaviour
{
    public Camera arCamera;
    public Material material;   // Gecit/Overlay
    public float size = 0.032f;
    public float depth = 0.35f;

    Transform root, spin;
    SpriteRenderer core, ring, glow, disc;
    float show, flash;
    bool wasPinched;
    float shownSince = -1f;
    float lastToggle = -10f;

    void Start()
    {
        // Quest'teki gibi sade menü düğmesi: beyaz daire içinde koyu "uygulamalar" simgesi, altında yumuşak gölge
        root = new GameObject("Avuc Logosu").transform;
        root.SetParent(transform, false);
        glow = Part("Golge", UiKit.SoftDisc, new Color(0f, 0f, 0f, 0.35f), 1.7f, root);
        glow.sortingOrder = 18;
        spin = new GameObject("Halka Kok").transform;
        spin.SetParent(root, false);
        ring = Part("Halka", UiKit.ThinRing, new Color(1f, 1f, 1f, 0.6f), 1.25f, spin);
        ring.sortingOrder = 19;
        var discSr = Part("Daire", UiKit.Disc, new Color(0.97f, 0.97f, 0.98f, 1f), 1f, root);
        disc = discSr;
        core = Part("Simge", UiKit.Ikon("apps") ?? UiKit.GetIcon(UiKit.Icon.Home), new Color(0.12f, 0.13f, 0.16f, 1f), 0.42f, root);
        core.sortingOrder = 21;
        root.gameObject.SetActive(false);
    }

    SpriteRenderer Part(string name, Sprite sprite, Color color, float scale, Transform parent)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * scale;
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sharedMaterial = material;
        sr.color = color;
        sr.sortingOrder = 20;
        return sr;
    }

    void LateUpdate()
    {
        bool want = HandTracker.Visible && HandTracker.PalmUp;   // oyun sırasında da: menüden "Oyundan çık"
        show = Mathf.MoveTowards(show, want ? 1f : 0f, Time.deltaTime * (want ? 4f : 6f));
        bool visible = show > 0.01f;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (want && shownSince < 0f) shownSince = Time.time;
        if (!want) shownSince = -1f;

        // Kıstırınca menü (logo tam görünür olduktan sonra). El geri çevrilirken parmaklar görüntüde üst üste
        // biniyor, ikinci bir kıstırma sanılıp menü hemen kapanıyordu (kullanıcı): avuç sabit ve iyice sana dönük
        // olmalı, el dönmüyor olmalı, son açma/kapamadan bu yana 1,5 sn geçmiş olmalı.
        bool pinched = HandTracker.Pinched;
        bool steady = HandTracker.PalmFacing > 0.65f && HandTracker.PalmTurnSpeed < 90f;
        if (want && show > 0.8f && pinched && !wasPinched && steady && Time.time - shownSince > 0.35f && Time.time - lastToggle > 1.5f)
        {
            lastToggle = Time.time;
            if (HomeMenu.Instance != null) HomeMenu.Instance.Toggle();
            Sfx.Click();
            flash = 1f;
            Debug.Log("GECIT AVUC MENU");
        }
        wasPinched = pinched;
        if (!visible) return;

        // Baş ve işaret parmağının arasının biraz üstünde, kameraya dönük
        var vp = HandTracker.PinchViewport + new Vector2(0f, 0.035f);
        var cam = arCamera.transform;
        root.position = arCamera.ViewportToWorldPoint(new Vector3(vp.x, vp.y, depth));
        root.rotation = Quaternion.LookRotation(root.position - cam.position, cam.up);

        // Animasyon: yaylanarak büyüme; kıstırmaya yaklaştıkça daire maviye döner ve hafif küçülür
        float pinch = Mathf.Max(HandTracker.PinchAmount, flash);
        float s = EaseOutBack(show) * size * (1f - 0.15f * HandTracker.PinchAmount) * (1f + 0.2f * flash);
        root.localScale = Vector3.one * s;
        float breathe = 0.85f + 0.15f * Mathf.Sin(Time.time * 3f);
        glow.color = new Color(0f, 0f, 0f, 0.35f * show);
        ring.color = new Color(1f, 1f, 1f, 0.55f * show * breathe * (1f - pinch));
        spin.localScale = Vector3.one * Mathf.Lerp(1.15f, 0.95f, pinch);
        disc.color = Color.Lerp(new Color(0.97f, 0.97f, 0.98f, show), new Color(0.35f, 0.6f, 1f, show), pinch);
        core.color = Color.Lerp(new Color(0.12f, 0.13f, 0.16f, show), new Color(1f, 1f, 1f, show), pinch);
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 3f);
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        t = Mathf.Clamp01(t);
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
