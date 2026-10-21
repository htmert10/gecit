using UnityEngine;
using UnityEngine.UI;

// Quest tarzı ana menü: kumandadaki * tuşuyla önünde uygulama paneli açılır
// (YouTube, Google, Önüme getir, Işın Kılıcı, Sanal dünya, Gözlük, El ile tıkla).
public class HomeMenu : MonoBehaviour
{
    public Transform head;
    public SaberGame game;

    Transform dockRoot;
    CanvasGroup dockGroup;
    Text clock, gameLabel, vrLabel, worldLabel, handModeLabel, roomLabel;
    Image gameTile;
    bool open;
    MoveHandle dockHandle;
    float nextClock;

    const float Mpp = 0.001f;   // metre / piksel
    const float DockW = 880f, DockH = 240f, TileStep = 104f;
    const int Tiles = 8;
    Text dateText, batteryText;
    UiButton handChip, roomChip, followChip;
    static readonly Color ChipOn = new Color(0.22f, 0.45f, 0.95f, 0.95f);
    static readonly string[] Days = { "Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi" };
    static readonly string[] Months = { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };

    UiButton Chip(Transform parent, string label, string icon, Vector2 pos, System.Action action)
    {
        const float w = 130f, h = 40f;
        var bg = UiKit.Box(parent, label, pos, new Vector2(w, h), UiKit.Glass, null, 80f / h);
        var ic = UiKit.Box(bg.transform, "Simge", new Vector2(-w * 0.5f + 24f, 0f), new Vector2(20f, 20f), Color.white, UiKit.Ikon(icon));
        ic.preserveAspect = true;
        var l = UiKit.Label(bg.transform, label, 15, Color.white, new Vector2(14f, 0f), new Vector2(w - 40f, h));
        var b = bg.gameObject.AddComponent<UiButton>();
        b.background = bg;
        b.idle = UiKit.Glass;
        b.hover = UiKit.GlassHover;
        b.pressed = ChipOn;
        b.hoverScale = 1.04f;
        b.glowEdge = false;
        b.onClick = action;
        return b;
    }

    public static HomeMenu Instance;

    void Awake() { Instance = this; }

    void Start()
    {
        // Ana düğme YOK (kullanıcı: başı takip edip sürekli karşıma çıkıyor, tıklamayı engelliyor).
        // Menü kumandadaki * tuşuyla açılır.

        // Uygulama paneli (Quest'in ana menüsü gibi sade): üstte saat + tarih, hızlı ayarlar, pil / Wi-Fi;
        // altta uygulama kutuları. Koyu cam, ince açık kenar (neon yok: kullanıcı "amatörce" dedi).
        dockRoot = new GameObject("Uygulama Paneli").transform;
        var dock = UiKit.WorldCanvas("Tuval", dockRoot, new Vector2(DockW, DockH), Mpp);
        dockGroup = dock.GetComponent<CanvasGroup>();
        dockGroup.alpha = 0f;
        var t = dock.transform;
        UiKit.Box(t, "Kenar", Vector2.zero, new Vector2(DockW + 2f, DockH + 2f), new Color(1f, 1f, 1f, 0.10f), null, 1.35f);
        UiKit.Box(t, "Zemin", Vector2.zero, new Vector2(DockW, DockH), new Color(0.07f, 0.08f, 0.10f, 0.95f), null, 1.4f);
        UiKit.Box(t, "Ayrac", new Vector2(0f, 52f), new Vector2(DockW - 60f, 1.5f), new Color(1f, 1f, 1f, 0.07f), UiKit.White);

        clock = UiKit.Label(t, "", 30, new Color(1f, 1f, 1f, 0.95f), new Vector2(-DockW * 0.5f + 110f, 82f), new Vector2(160f, 40f));
        clock.alignment = TextAnchor.MiddleLeft;
        clock.rectTransform.anchoredPosition = new Vector2(-DockW * 0.5f + 115f, 84f);
        dateText = UiKit.Label(t, "", 15, new Color(0.75f, 0.78f, 0.84f, 0.9f), new Vector2(-DockW * 0.5f + 205f, 82f), new Vector2(200f, 30f));
        dateText.alignment = TextAnchor.MiddleLeft;
        dateText.rectTransform.anchoredPosition = new Vector2(-DockW * 0.5f + 230f, 82f);

        // Hızlı ayarlar: simgeli küçük düğmeler; açıkken mavi
        handChip = Chip(t, "El ile tıkla", "pan_tool", new Vector2(-30f, 82f), () => HandTracker.SetHandClicks(!HandTracker.HandClicks));
        roomChip = Chip(t, "Oda takibi", "my_location", new Vector2(105f, 82f), () => ARPoseUpdater.SetRoomTracking(ARPoseUpdater.LockPosition));
        // Takip: ekranlar arkandan yumuşakça gelir (kapalıyken odaya sabit)
        followChip = Chip(t, "Takip", "center_focus_strong", new Vector2(240f, 82f), () => FollowFrame.SetEnabled(!FollowFrame.Enabled));
        if (FollowFrame.Instance == null) new GameObject("Takip Cercevesi").AddComponent<FollowFrame>().head = head;

        // Wi-Fi + pil
        var wifi = UiKit.Box(t, "WiFi", new Vector2(DockW * 0.5f - 120f, 82f), new Vector2(22f, 22f), new Color(1f, 1f, 1f, 0.8f), UiKit.Ikon("wifi"));
        wifi.preserveAspect = true;
        var bat = UiKit.Box(t, "Pil", new Vector2(DockW * 0.5f - 90f, 82f), new Vector2(22f, 22f), new Color(1f, 1f, 1f, 0.8f), UiKit.Ikon("battery_full"));
        bat.preserveAspect = true;
        batteryText = UiKit.Label(t, "", 15, new Color(1f, 1f, 1f, 0.8f), new Vector2(DockW * 0.5f - 50f, 82f), new Vector2(60f, 24f));

        // Uygulama kutuları
        float x0 = -(Tiles - 1) * 0.5f * TileStep;
        int k = 0;
        Tile(t, "Uygulamalar", new Vector2(x0 + TileStep * k++, -20f), new Color(0.35f, 0.62f, 1f, 1f), UiKit.Icon.Apps, null, OpenGrid, false);
        Tile(t, "Sinema", new Vector2(x0 + TileStep * k++, -20f), new Color(1f, 0.38f, 0.38f, 1f), null, null, () => { if (CinemaHall.Instance != null) CinemaHall.Instance.Enter(); }, true, null, 84f, "theaters");
        var world = Tile(t, "Sanal dünya", new Vector2(x0 + TileStep * k++, -20f), new Color(0.68f, 0.52f, 1f, 1f), UiKit.Icon.Globe, null, OpenEnvGrid, false);
        foreach (var lbl in world.GetComponentsInChildren<Text>(true)) if (lbl.text == "Sanal dünya") worldLabel = lbl;
        Tile(t, "Önüme getir", new Vector2(x0 + TileStep * k++, -20f), new Color(0.85f, 0.88f, 0.94f, 1f), UiKit.Icon.Recenter, null, BringAll);
        var g = Tile(t, "Işın Kılıcı", new Vector2(x0 + TileStep * k++, -20f), new Color(0.75f, 0.45f, 1f, 1f), UiKit.Icon.Saber, null, ToggleGame);
        Tile(t, "Oda tara", new Vector2(x0 + TileStep * k++, -20f), new Color(0.3f, 0.85f, 0.9f, 1f), UiKit.Icon.Scan, null, () => { if (RoomScan.Instance != null) RoomScan.Instance.StartScan(); });
        var vr = Tile(t, "Gözlük", new Vector2(x0 + TileStep * k++, -20f), new Color(0.35f, 0.9f, 0.65f, 1f), UiKit.Icon.Goggles, null, () => { if (!PassthroughView.Stereo) PassthroughView.SetStereo(true); if (GogglesPanel.Instance != null) GogglesPanel.Instance.Show(); });   // açıkken ayar panelini açar (içinde "Gözlüğü kapat")
        foreach (var lbl in vr.GetComponentsInChildren<Text>(true)) if (lbl.text == "Gözlük") vrLabel = lbl;
        Tile(t, "Fotoğraf", new Vector2(x0 + TileStep * k++, -20f), new Color(1f, 0.72f, 0.3f, 1f), UiKit.Icon.Camera, null, () => Screenshot.Take());
        gameTile = g.background;
        gameLabel = g.transform.GetComponentInChildren<Text>(true);
        foreach (var lbl in g.GetComponentsInChildren<Text>(true)) if (lbl.text == "Işın Kılıcı") gameLabel = lbl;

        dockHandle = MoveHandle.Create(t, new Vector2(0f, -DockH * 0.5f - 24f), 160f, dockRoot, head);

        BuildGrid();
        BuildEnvGrid();

    }

    // Profesyonel kutu: koyu cam zemin, simge / harf kutunun renginde (vurgu), altta ince renk çizgisi.
    // Rengarenk dolu kutular çocuksu duruyordu (kullanıcı).
    static readonly Color Glass = new Color(0.1f, 0.12f, 0.16f, 0.94f);
    static readonly Color GlassHover = new Color(0.18f, 0.22f, 0.29f, 0.97f);

    static Color Accent(Color c)
    {
        float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (m < 0.25f) return new Color(0.92f, 0.94f, 0.97f, 1f);
        var a = new Color(c.r / m, c.g / m, c.b / m, 1f);
        return Color.Lerp(a, Color.white, 0.18f);
    }

    static string IkonName(UiKit.Icon icon)
    {
        switch (icon)
        {
            case UiKit.Icon.Apps: return "apps";
            case UiKit.Icon.Recenter: return "center_focus_strong";
            case UiKit.Icon.Saber: return "sports_esports";
            case UiKit.Icon.Globe: return "public";
            case UiKit.Icon.Scan: return "view_in_ar";
            case UiKit.Icon.Goggles: return "vrpano";
            case UiKit.Icon.Camera: return "photo_camera";
            case UiKit.Icon.Exit: return "logout";
            case UiKit.Icon.Home: return "home";
        }
        return null;
    }

    UiButton Tile(Transform parent, string label, Vector2 pos, Color color, UiKit.Icon? icon, string letter, System.Action action,
        bool closeAfter = true, Sprite image = null, float size = 84f, string ikon = null)
    {
        Sprite sp = null;
        if (ikon != null) sp = UiKit.Ikon(ikon);
        else if (icon.HasValue) sp = UiKit.Ikon(IkonName(icon.Value)) ?? UiKit.GetIcon(icon.Value);
        var accent = color.grayscale < 0.2f ? new Color(0.92f, 0.94f, 0.97f, 1f) : color;
        return UiKit.ProTile(parent, label, pos, size, accent, sp, letter, () => { if (closeAfter) Close(); action(); }, image);
    }

    void Update()
    {
        if (gridRoot != null)
        {
            float ga = Mathf.MoveTowards(gridGroup.alpha, gridOpen ? 1f : 0f, Time.deltaTime * 6f);
            if (ga != gridGroup.alpha) gridGroup.alpha = ga;
            if (gridRoot.gameObject.activeSelf != (ga > 0.001f || gridOpen)) gridRoot.gameObject.SetActive(ga > 0.001f || gridOpen);
            if (gridOpen && ViewFrame.AngleFromView(head, gridRoot.position) > 65f) CloseGrid();
        }
        if (envRoot != null)
        {
            float ea = Mathf.MoveTowards(envGroup.alpha, envOpen ? 1f : 0f, Time.deltaTime * 6f);
            if (ea != envGroup.alpha) envGroup.alpha = ea;
            if (envRoot.gameObject.activeSelf != (ea > 0.001f || envOpen)) envRoot.gameObject.SetActive(ea > 0.001f || envOpen);
            if (envOpen && ViewFrame.AngleFromView(head, envRoot.position) > 65f) envOpen = false;
        }
        float da = Mathf.MoveTowards(dockGroup.alpha, open ? 1f : 0f, Time.deltaTime * 6f);
        if (da != dockGroup.alpha)
        {
            dockGroup.alpha = da;
            dockRoot.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, UiKit.EaseOutBack(da));
        }
        dockRoot.gameObject.SetActive(dockGroup.alpha > 0.001f || open);

        if (open)
        {
            if (Time.time >= nextClock)
            {
                nextClock = Time.time + 1f;
                var now = System.DateTime.Now;
                var tt = now.ToString("HH:mm");
                if (clock.text != tt) clock.text = tt;
                var dt = $"{Days[(int)now.DayOfWeek]}, {now.Day} {Months[now.Month - 1]}";
                if (dateText.text != dt) dateText.text = dt;
                int battery = Mathf.RoundToInt(SystemInfo.batteryLevel * 100f);
                var bt = battery > 0 ? $"%{battery}" : "";
                if (batteryText.text != bt) batteryText.text = bt;
            }
            handChip.SetIdle(HandTracker.HandClicks ? ChipOn : UiKit.Glass);
            roomChip.SetIdle(!ARPoseUpdater.LockPosition ? ChipOn : UiKit.Glass);
            followChip.SetIdle(FollowFrame.Enabled ? ChipOn : UiKit.Glass);
            // Kullanıcı başını çok çevirirse panel kapanır
            var toDock = dockRoot.position - head.position;
            if (!dockHandle.Grabbed && ViewFrame.AngleFromView(head, dockRoot.position) > 65f) Close();
        }

        bool playing = (game != null && game.Active) || MiniGames.Playing;
        var gl = playing ? "Oyundan çık" : "Işın Kılıcı";
        if (gameLabel.text != gl) gameLabel.text = gl;
        var vl = PassthroughView.Stereo ? "Gözlük: açık" : "Gözlük: kapalı";
        if (vrLabel != null && vrLabel.text != vl) vrLabel.text = vl;
        var wl = "Sanal dünya";
        if (worldLabel != null && worldLabel.text != wl) worldLabel.text = wl;
    }

    public void Toggle()
    {
        if (gridOpen || envOpen) { CloseGrid(); return; }
        if (open) Close(); else Open();
    }

    // ---- Uygulamalar: site kısayolları (yeni pencerede açılır) ----
    static readonly (string title, string url, Color color, string letter)[] Sites =
    {
        ("YouTube", "https://m.youtube.com", new Color(0.9f, 0.13f, 0.13f, 1f), "YT"),
        ("Google", "https://www.google.com", new Color(0.97f, 0.97f, 0.98f, 1f), "G"),
        ("Instagram", "https://www.instagram.com", new Color(0.78f, 0.22f, 0.55f, 1f), "IG"),
        ("Twitch", "https://m.twitch.tv", new Color(0.55f, 0.3f, 0.95f, 1f), "T"),
        ("Spotify", "https://open.spotify.com", new Color(0.12f, 0.73f, 0.33f, 1f), "S"),
        ("Netflix", "https://www.netflix.com", new Color(0.08f, 0.08f, 0.08f, 1f), "N"),
        ("Vikipedi", "https://tr.m.wikipedia.org", new Color(0.55f, 0.57f, 0.6f, 1f), "W"),
        ("Haberler", "https://news.google.com/?hl=tr&gl=TR&ceid=TR:tr", new Color(0.2f, 0.45f, 0.9f, 1f), "H"),
    };

    Transform gridRoot;
    CanvasGroup gridGroup;
    bool gridOpen;

    void BuildGrid()
    {
        gridRoot = new GameObject("Uygulamalar Paneli").transform;
        var c = UiKit.WorldCanvas("Tuval", gridRoot, new Vector2(460f, 300f), Mpp);
        gridGroup = c.GetComponent<CanvasGroup>();
        gridGroup.alpha = 0f;
        var t = c.transform;
        UiKit.Box(t, "Kenar", Vector2.zero, new Vector2(462f, 302f), new Color(1f, 1f, 1f, 0.10f), null, 1.35f);
        UiKit.Box(t, "Zemin", Vector2.zero, new Vector2(460f, 300f), new Color(0.07f, 0.08f, 0.10f, 0.95f), null, 1.4f);
        UiKit.Label(t, "Uygulamalar", 22, new Color(1f, 1f, 1f, 0.9f), new Vector2(0f, 120f), new Vector2(300f, 30f));
        for (int i = 0; i < Sites.Length; i++)
        {
            var s = Sites[i];
            var pos = new Vector2(-165f + (i % 4) * 110f, 45f - (i / 4) * 115f);
            var site = s;
            Tile(t, s.title, pos, s.color, null, s.letter, () =>
            {
                CloseGrid();
                if (WindowPool.Instance != null) WindowPool.Instance.OpenSite(site.title, site.url);
            }, false);
        }
        var back = UiKit.Box(t, "Geri", new Vector2(0f, -132f), new Vector2(120f, 30f), new Color(0.2f, 0.22f, 0.28f, 1f), null, 80f / 30f);
        UiKit.Label(back.transform, "Geri", 17, Color.white, Vector2.zero, new Vector2(120f, 30f));
        var bb = back.gameObject.AddComponent<UiButton>();
        bb.background = back;
        bb.hoverScale = 1.05f;
        bb.onClick = () => { CloseGrid(); Open(); };
        gridRoot.gameObject.SetActive(false);
    }

    void OpenGrid()
    {
        Close();
        gridOpen = true;
        gridRoot.position = dockRoot.position;
        gridRoot.rotation = dockRoot.rotation;
        gridRoot.gameObject.SetActive(true);
        UiKit.PopIn(gridRoot);
    }

    void CloseGrid() { gridOpen = false; envOpen = false; }

    // ---- Ortamlar: sanal dünya seçimi ----
    static readonly (string title, Color color, string letter)[] Envs =
    {
        ("Gece vadisi", new Color(0.12f, 0.14f, 0.32f, 1f), "Ay"),
        ("Gündüz vadisi", new Color(0.30f, 0.62f, 0.35f, 1f), "Gün"),
        ("Uzay", new Color(0.08f, 0.06f, 0.14f, 1f), "Uz"),
        ("Sahil", new Color(0.95f, 0.5f, 0.3f, 1f), "Su"),
    };

    Transform envRoot;
    CanvasGroup envGroup;
    bool envOpen;

    void BuildEnvGrid()
    {
        envRoot = new GameObject("Ortamlar Paneli").transform;
        var c = UiKit.WorldCanvas("Tuval", envRoot, new Vector2(560f, 210f), Mpp);
        envGroup = c.GetComponent<CanvasGroup>();
        envGroup.alpha = 0f;
        var t = c.transform;
        UiKit.Box(t, "Kenar", Vector2.zero, new Vector2(562f, 212f), new Color(1f, 1f, 1f, 0.10f), null, 1.35f);
        UiKit.Box(t, "Zemin", Vector2.zero, new Vector2(560f, 210f), new Color(0.07f, 0.08f, 0.10f, 0.95f), null, 1.4f);
        UiKit.Label(t, "Sanal dünya", 22, new Color(1f, 1f, 1f, 0.9f), new Vector2(0f, 78f), new Vector2(300f, 30f));
        for (int i = 0; i < Envs.Length; i++)
        {
            var e = Envs[i];
            int index = i;
            var thumb = Resources.Load<Texture2D>("Panorama/kucuk_" + VirtualWorld.PanoNames[i]);
            var sprite = thumb != null ? Sprite.Create(thumb, new Rect(0, 0, thumb.width, thumb.height), new Vector2(0.5f, 0.5f), 100f) : null;
            Tile(t, e.title, new Vector2(-215f + i * 105f, 0f), e.color, null, sprite != null ? null : e.letter, () =>
            {
                envOpen = false;
                if (VirtualWorld.Instance != null) VirtualWorld.Instance.SetEnvironment((VirtualWorld.Env)index);
            }, false, sprite, 92f);
        }
        Tile(t, "Odaya dön", new Vector2(215f, 0f), new Color(0.25f, 0.28f, 0.36f, 1f), UiKit.Icon.Exit, null, () =>
        {
            envOpen = false;
            if (VirtualWorld.Instance != null) VirtualWorld.Instance.ReturnToRoom();
        }, false, null, 92f);
        var back = UiKit.Box(t, "Geri", new Vector2(0f, -88f), new Vector2(120f, 30f), new Color(0.2f, 0.22f, 0.28f, 1f), null, 80f / 30f);
        UiKit.Label(back.transform, "Geri", 17, Color.white, Vector2.zero, new Vector2(120f, 30f));
        var bb = back.gameObject.AddComponent<UiButton>();
        bb.background = back;
        bb.hoverScale = 1.05f;
        bb.onClick = () => { envOpen = false; Open(); };
        envRoot.gameObject.SetActive(false);
    }

    void OpenEnvGrid()
    {
        Close();
        envOpen = true;
        envRoot.position = dockRoot.position;
        envRoot.rotation = dockRoot.rotation;
        envRoot.gameObject.SetActive(true);
        UiKit.PopIn(envRoot);
    }

    void Open()
    {
        open = true;
        dockRoot.position = head.position + ViewFrame.Forward(head) * 0.7f - ViewFrame.Up(head) * 0.06f;
        dockRoot.rotation = ViewFrame.Facing(dockRoot.position, head);
        UiKit.PopIn(dockRoot);
    }

    void Close() { open = false; }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    // ---- Menü işlevleri ----
    public void OpenWindow(string title)
    {
        if (game != null && game.Active) game.Stop();
        foreach (var w in Window.Windows)
        {
            if (w.Title != title) continue;
            w.Open();
            w.MoveTo(head.position + ViewFrame.Forward(head) * 2.0f - ViewFrame.Up(head) * 0.05f);
        }
    }

    public void BringAll()
    {
        if (game != null && game.Active) game.Stop();
        var opened = Window.Windows.FindAll(w => w.IsOpen);
        if (FollowFrame.Instance != null) FollowFrame.Instance.SnapToHead();   // takip çerçevesi de tam önüne
        var up = ViewFrame.Up(head);
        var fwd = ViewFrame.Forward(head);
        for (int i = 0; i < opened.Count; i++)
        {
            float deg = (i - (opened.Count - 1) * 0.5f) * 46f;   // yay üstünde yan yana
            opened[i].MoveTo(head.position + Quaternion.AngleAxis(deg, up) * fwd * 2.1f - up * 0.05f);
        }
    }

    // Oyundaysan (hangisi olursa) çık; değilsen ışın kılıcını başlat
    public void ToggleGame()
    {
        if (MiniGames.StopActive()) return;
        if (game != null) game.Begin();
    }
}
