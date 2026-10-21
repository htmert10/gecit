using UnityEngine;
using UnityEngine.UI;

// Gözlük ayarı: gözlük modu açılınca önünde çıkar. Göz arası (mercek arası) −/+ ile 1 mm adımla ayarlanır;
// iki görüntü tek ve net görünene kadar. Boyut (merceğin odağı) −/+ ile: gerçek eşyalar gözlüksüzkiyle aynı boyda,
// başı çevirince oda kaymıyor. "Tamam" ile kapanır. Menüdeki "Gözlük" kutusundan yeniden açılır.
public class GogglesPanel : MonoBehaviour
{
    public static GogglesPanel Instance;
    public Transform head;

    Transform panel;
    CanvasGroup group;
    Text value, sizeValue;
    UiButton comfortBtn;
    static readonly Color On = new Color(0.22f, 0.45f, 0.95f, 0.95f);
    bool open;

    void Awake() { Instance = this; }

    void Start()
    {
        Build();
        if (PassthroughView.Stereo) Invoke(nameof(Show), 2f);   // gözlük modunda açıldıysa ayar paneli kendiliğinden
    }

    public void Show()
    {
        panel.position = head.position + ViewFrame.Forward(head) * 0.9f;
        panel.rotation = ViewFrame.Facing(panel.position, head);
        panel.gameObject.SetActive(true);
        open = true;
        UiKit.PopIn(panel);
    }

    void Update()
    {
        float a = Mathf.MoveTowards(group.alpha, open ? 1f : 0f, Time.deltaTime * 5f);
        if (a != group.alpha) group.alpha = a;
        bool act = a > 0.001f || open;
        if (panel.gameObject.activeSelf != act) panel.gameObject.SetActive(act);
        if (open && !PassthroughView.Stereo) open = false;
        var t = $"{PassthroughView.IpdMm:F0} mm";
        if (value != null && value.text != t) value.text = t;
        var s = $"%{Mathf.RoundToInt(PassthroughView.LensMm / 40f * 100f)}";
        if (sizeValue != null && sizeValue.text != s) sizeValue.text = s;
        if (comfortBtn != null) comfortBtn.SetIdle(EyeComfort.Enabled ? On : UiKit.Glass);
    }

    void Build()
    {
        panel = new GameObject("Gozluk Ayari").transform;
        const float W = 460f, H = 320f;
        var c = UiKit.WorldCanvas("Tuval", panel, new Vector2(W, H), 0.001f);
        group = c.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        var t = c.transform;
        UiKit.Box(t, "Kenar", Vector2.zero, new Vector2(W + 2f, H + 2f), new Color(1f, 1f, 1f, 0.10f), null, 1.35f);
        UiKit.Box(t, "Zemin", Vector2.zero, new Vector2(W, H), new Color(0.07f, 0.08f, 0.10f, 0.96f), null, 1.4f);
        var title = UiKit.Label(t, "Gözlük ayarı", 22, Color.white, new Vector2(0f, 128f), new Vector2(W, 30f));
        title.fontStyle = FontStyle.Bold;
        var sub = new Color(0.75f, 0.78f, 0.84f, 0.9f);
        UiKit.Label(t, "Göz arası: iki görüntü tek ve net olana kadar", 13, sub, new Vector2(0f, 98f), new Vector2(W, 22f));
        value = UiKit.Label(t, "", 30, Color.white, new Vector2(0f, 58f), new Vector2(160f, 50f));
        Btn(t, "−", new Vector2(-130f, 58f), () => PassthroughView.SetIpd(PassthroughView.IpdMm - 1f));
        Btn(t, "+", new Vector2(130f, 58f), () => PassthroughView.SetIpd(PassthroughView.IpdMm + 1f));
        UiKit.Label(t, "Boyut: başını çevirince oda kaymayana kadar", 13, sub, new Vector2(0f, 12f), new Vector2(W, 22f));
        sizeValue = UiKit.Label(t, "", 30, Color.white, new Vector2(0f, -28f), new Vector2(160f, 50f));
        Btn(t, "−", new Vector2(-130f, -28f), () => PassthroughView.SetLens(PassthroughView.LensMm - 1f));
        Btn(t, "+", new Vector2(130f, -28f), () => PassthroughView.SetLens(PassthroughView.LensMm + 1f));
        Btn(t, "Gözlüğü kapat", new Vector2(-140f, -112f), () => { open = false; PassthroughView.SetStereo(false); }, 150f);
        // Göz koruma: açıkken mavi (sayfalar / perde / gökyüzü kısık)
        comfortBtn = Btn(t, "Göz koruma", new Vector2(30f, -112f), () => EyeComfort.Set(!EyeComfort.Enabled), 150f);
        Btn(t, "Tamam", new Vector2(170f, -112f), () => open = false, 100f);
        panel.gameObject.SetActive(false);
    }

    UiButton Btn(Transform parent, string label, Vector2 pos, System.Action action, float w = 70f)
    {
        var bg = UiKit.Box(parent, label, pos, new Vector2(w, 54f), UiKit.Glass, null, 80f / 54f);
        UiKit.Label(bg.transform, label, label.Length == 1 ? 30 : 16, Color.white, Vector2.zero, new Vector2(w, 54f));
        var b = bg.gameObject.AddComponent<UiButton>();
        b.background = bg;
        b.idle = UiKit.Glass;
        b.hover = UiKit.GlassHover;
        b.pressed = new Color(0.22f, 0.45f, 0.95f, 0.95f);
        b.glowEdge = false;
        b.hoverScale = 1.06f;
        b.onClick = action;
        return b;
    }
}
