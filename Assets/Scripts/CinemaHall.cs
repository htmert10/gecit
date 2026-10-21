using UnityEngine;

// Özel sinema (Meta'daki gibi): menüden "Sinema" -> karanlık salon, tek koltuk (sen), karşında seni saran KAVİSLİ
// dev perde. Önünde seçim paneli: YouTube / Netflix / Google. Seçilen site kavisli perdede görünür ve ona
// dokunulur (perde tarayıcı penceresinin görüntüsünü gösterir; pencere perdenin arkasında gizli çalışır).
// Salondan çıkınca pencere normal boyuna ve önüne döner.
public class CinemaHall : MonoBehaviour
{
    public static CinemaHall Instance;
    public Transform head;
    public Material screenMaterial;   // Gecit/CurvedScreen
    CurvedScreen curved;

    Transform panel;
    CanvasGroup group;
    bool chooserOpen;
    Window screen;


    void Awake() { Instance = this; }

    void Start()
    {
        BuildChooser();
        curved = new GameObject("Kavisli Perde").AddComponent<CurvedScreen>();
        curved.material = screenMaterial;
    }

    public void Enter()
    {
        if (VirtualWorld.Instance == null) return;
        VirtualWorld.Instance.SetEnvironment(VirtualWorld.Env.Sinema);
        Invoke(nameof(ShowChooser), 0.7f);   // karartma geçişi bitince
        Debug.Log("GECIT SINEMA SALONU");
    }

    void ShowChooser()
    {
        panel.position = head.position + ViewFrame.Forward(head) * 1.0f - ViewFrame.Up(head) * 0.12f;
        panel.rotation = ViewFrame.Facing(panel.position, head);
        panel.gameObject.SetActive(true);
        chooserOpen = true;
        UiKit.PopIn(panel);
    }

    void Choose(string title, string url)
    {
        chooserOpen = false;
        if (WindowPool.Instance == null) return;
        var w = WindowPool.Instance.OpenSite(title, url);
        if (w == null) return;
        if (screen != null && screen != w) Restore(screen);
        screen = w;
        // Salonun karşısı (ortam kullanıcının baktığı yöne kurulur)
        var fwd = VirtualWorld.Instance != null ? VirtualWorld.Instance.WorldForward : ViewFrame.Forward(head);
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        // Pencere kavisli perdenin ARKASINDA çalışır (görünmez, tarayıcı güncellenir); görüntü perdeye taşınır
        w.MoveTo(head.position + fwd * (curved.radius + 1.5f) + Vector3.up * curved.lift);
        w.transform.localScale = Vector3.one * 2f;
        curved.Show(w, head.position, fwd);
        Debug.Log("GECIT SINEMA perde " + title);
    }

    void Restore(Window w)
    {
        if (curved != null) curved.Hide();
        if (w == null) return;
        w.transform.localScale = Vector3.one;
        if (w.IsOpen) w.MoveTo(head.position + ViewFrame.Forward(head) * 2.0f - ViewFrame.Up(head) * 0.05f);
    }

    void Update()
    {
        bool inHall = VirtualWorld.Active && VirtualWorld.Current == VirtualWorld.Env.Sinema;
        if (!inHall)
        {
            chooserOpen = false;
            if (screen != null) { Restore(screen); screen = null; }
        }
        float a = Mathf.MoveTowards(group.alpha, chooserOpen ? 1f : 0f, Time.deltaTime * 5f);
        if (a != group.alpha) group.alpha = a;
        bool act = a > 0.001f || chooserOpen;
        if (panel.gameObject.activeSelf != act) panel.gameObject.SetActive(act);
        if (chooserOpen && ViewFrame.AngleFromView(head, panel.position) > 70f) chooserOpen = false;
    }

    public void ToggleChooser()
    {
        if (chooserOpen) chooserOpen = false; else ShowChooser();
    }

    void BuildChooser()
    {
        panel = new GameObject("Sinema Secimi").transform;
        const float W = 520f, H = 250f;
        var c = UiKit.WorldCanvas("Tuval", panel, new Vector2(W, H), 0.001f);
        group = c.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        var t = c.transform;
        UiKit.Box(t, "Kenar", Vector2.zero, new Vector2(W + 2f, H + 2f), new Color(1f, 1f, 1f, 0.10f), null, 1.35f);
        UiKit.Box(t, "Zemin", Vector2.zero, new Vector2(W, H), new Color(0.07f, 0.08f, 0.10f, 0.95f), null, 1.4f);
        var title = UiKit.Label(t, "Sinema", 24, Color.white, new Vector2(0f, 95f), new Vector2(W, 34f));
        title.fontStyle = FontStyle.Bold;
        UiKit.Label(t, "Ne izlemek istersin?", 15, new Color(0.75f, 0.78f, 0.84f, 0.9f), new Vector2(0f, 68f), new Vector2(W, 24f));
        const float size = 110f, step = 150f;
        UiKit.ProTile(t, "YouTube", new Vector2(-step, -15f), size, new Color(1f, 0.25f, 0.25f, 1f), UiKit.Ikon("play_arrow"), null,
            () => Choose("YouTube", "https://m.youtube.com"));
        UiKit.ProTile(t, "Netflix", new Vector2(0f, -15f), size, new Color(0.9f, 0.1f, 0.12f, 1f), null, "N",
            () => Choose("Netflix", "https://www.netflix.com"));
        UiKit.ProTile(t, "Google", new Vector2(step, -15f), size, new Color(0.35f, 0.62f, 1f, 1f), null, "G",
            () => Choose("Google", "https://www.google.com"));
        // Salondan çık
        var exit = UiKit.Box(t, "Cik", new Vector2(W * 0.5f - 70f, 95f), new Vector2(110f, 32f), UiKit.Glass, null, 80f / 32f);
        UiKit.Label(exit.transform, "Salondan çık", 13, new Color(1f, 1f, 1f, 0.85f), Vector2.zero, new Vector2(110f, 32f));
        var eb = exit.gameObject.AddComponent<UiButton>();
        eb.background = exit;
        eb.idle = UiKit.Glass;
        eb.hover = UiKit.GlassHover;
        eb.glowEdge = false;
        eb.hoverScale = 1.04f;
        eb.onClick = () => { chooserOpen = false; if (VirtualWorld.Instance != null) VirtualWorld.Instance.ReturnToRoom(); };
        // Perdedeki sayfada geri
        var back = UiKit.Box(t, "Geri", new Vector2(-W * 0.5f + 60f, 95f), new Vector2(80f, 32f), UiKit.Glass, null, 80f / 32f);
        var bi = UiKit.Box(back.transform, "Simge", new Vector2(-20f, 0f), new Vector2(18f, 18f), Color.white, UiKit.Ikon("arrow_back"));
        bi.preserveAspect = true;
        UiKit.Label(back.transform, "Geri", 13, new Color(1f, 1f, 1f, 0.85f), new Vector2(10f, 0f), new Vector2(50f, 32f));
        var bb = back.gameObject.AddComponent<UiButton>();
        bb.background = back;
        bb.idle = UiKit.Glass;
        bb.hover = UiKit.GlassHover;
        bb.glowEdge = false;
        bb.hoverScale = 1.04f;
        bb.onClick = () => { if (screen != null && screen.Browser != null) screen.Browser.Back(); };
        panel.gameObject.SetActive(false);
    }
}
