using UnityEngine;
using UnityEngine.UI;

// Çalışırken arayüz (klavye, menü, düğmeler) kurmak için küçük yardımcılar ve çizilmiş simgeler.
public static class UiKit
{
    public static Sprite Rounded;   // sahnedeki UiAssets doldurur
    public static Material HoloBase;   // Gecit/HoloFrame (UiAssets doldurur)
    public static readonly Color HoloColor = new Color(0.3f, 0.82f, 1f, 1f);   // neon camgöbeği
    static Font font;
    public static Font CustomFont;   // Inter (UiAssets doldurur); yoksa Unity'nin yazı tipi

    public static Font Font => CustomFont != null ? CustomFont : font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    public static Canvas WorldCanvas(string name, Transform parent, Vector2 size, float metersPerPixel)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * metersPerPixel;
        return canvas;
    }

    public static Image Box(Transform parent, string name, Vector2 position, Vector2 size, Color color, Sprite sprite = null, float roundness = 1f)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = sprite != null ? sprite : Rounded;
        img.color = color;
        if (sprite == null && Rounded != null)
        {
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = roundness;
        }
        img.raycastTarget = false;
        return img;
    }

    public static Text Label(Transform parent, string text, int size, Color color, Vector2 position, Vector2 box)
    {
        var go = new GameObject("Yazi", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = position;
        rt.sizeDelta = box;
        var t = go.GetComponent<Text>();
        t.font = Font;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        return t;
    }

    // Teknolojik ışıklı çerçeve: verilen dikdörtgenin (innerSize) kenarında neon çizgi + hale (+ dönen ışık).
    // Görüntü dikdörtgenden 'spread' kadar taşar (hale için). Rengin alfası parlaklıktır.
    static readonly System.Collections.Generic.Dictionary<string, Material> holoMats = new System.Collections.Generic.Dictionary<string, Material>();

    public static Image Holo(Transform parent, string name, Vector2 position, Vector2 innerSize, float radius, Color color,
        float spread = 22f, float sweep = 1f, float bracket = 0f)
    {
        var size = innerSize + Vector2.one * spread * 2f;
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        img.color = color;
        if (HoloBase == null) { img.enabled = false; return img; }
        if (bracket <= 0f) bracket = Mathf.Min(innerSize.x, innerSize.y) * 0.18f;
        string key = $"{size.x:F0}x{size.y:F0}r{radius:F0}i{spread:F0}s{sweep:F2}b{bracket:F0}";
        if (!holoMats.TryGetValue(key, out var mat))
        {
            mat = new Material(HoloBase) { name = "Holo " + key };
            mat.SetVector("_Size", new Vector4(size.x, size.y, bracket, 0f));
            mat.SetFloat("_Radius", radius);
            mat.SetFloat("_Inset", spread);
            mat.SetFloat("_Line", Mathf.Clamp(Mathf.Min(innerSize.x, innerSize.y) * 0.02f, 1.2f, 2.2f));
            mat.SetFloat("_Glow", Mathf.Clamp(spread * 0.5f, 5f, 16f));
            mat.SetFloat("_Sweep", sweep);
            holoMats[key] = mat;
        }
        img.material = mat;
        return img;
    }

    // İnce, yumuşak kenarlı halka (imleç kıstırma halkası, tıklama dalgası). 128 px, yarıçap 56.
    static Sprite thinRing;
    public static Sprite ThinRing
    {
        get
        {
            if (thinRing != null) return thinRing;
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f));
                    float d = Mathf.Abs(r - 56f);
                    float a = Mathf.Clamp01(3.2f - d) + 0.35f * Mathf.Exp(-d * d / 30f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Clamp;
            thinRing = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            return thinRing;
        }
    }

    // Düz beyaz (ince çizgiler için: dilimli köşeli görüntü 2 px'lik çizgide bozuk çiziyordu)
    static Sprite white;
    public static Sprite White => white != null ? white : (white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f));

    // Dolu daire (kenarı yumuşak) ve yumuşak gölge/parıltı lekesi (128 px)
    static Sprite disc, softDisc;
    public static Sprite Disc => disc != null ? disc : (disc = MakeRadial(r => Mathf.Clamp01(60f - r)));
    public static Sprite SoftDisc => softDisc != null ? softDisc : (softDisc = MakeRadial(r => Mathf.Pow(Mathf.Clamp01(1f - r / 64f), 2.2f)));

    static Sprite MakeRadial(System.Func<float, float> alpha)
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f));
                px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(r)) * 255));
            }
        tex.SetPixels32(px);
        tex.Apply(true);
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        t = Mathf.Clamp01(t);
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    // Paneldeki düğmeleri soldan sağa, yukarıdan aşağı sırayla zıplatarak getir
    public static void PopIn(Transform root, float step = 0.035f)
    {
        var buttons = root.GetComponentsInChildren<UiButton>(true);
        System.Array.Sort(buttons, (a, b) =>
        {
            var pa = a.transform.localPosition; var pb = b.transform.localPosition;
            int ra = Mathf.RoundToInt(-pa.y / 40f), rb = Mathf.RoundToInt(-pb.y / 40f);
            return ra != rb ? ra.CompareTo(rb) : pa.x.CompareTo(pb.x);
        });
        for (int i = 0; i < buttons.Length; i++) buttons[i].PopIn(i * step);
    }

    // RectTransform düzleminde ışın testi (kenarlara biraz pay bırakılır)
    public static bool HitRect(RectTransform rt, Ray ray, float padding, out float distance, out Vector3 point, out Vector2 local)
    {
        distance = 0f; point = Vector3.zero; local = Vector2.zero;
        var plane = new Plane(-rt.forward, rt.position);
        if (!plane.Raycast(ray, out distance)) return false;
        point = ray.GetPoint(distance);
        local = rt.InverseTransformPoint(point);
        var r = rt.rect;
        return local.x >= r.xMin - padding && local.x <= r.xMax + padding &&
               local.y >= r.yMin - padding && local.y <= r.yMax + padding;
    }

    // ---- Material simgeleri (Resources/Ikon, beyaz; Apache 2.0) ----
    static readonly System.Collections.Generic.Dictionary<string, Sprite> ikons = new System.Collections.Generic.Dictionary<string, Sprite>();

    public static Sprite Ikon(string name)
    {
        if (ikons.TryGetValue(name, out var sp)) return sp;
        var tex = Resources.Load<Texture2D>("Ikon/" + name);
        sp = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
        ikons[name] = sp;
        return sp;
    }

    // Profesyonel kutu (menüler): koyu cam, ince açık kenar, simge vurgu renginde, altında sade yazı
    public static readonly Color Glass = new Color(0.12f, 0.13f, 0.16f, 0.96f);
    public static readonly Color GlassHover = new Color(0.22f, 0.24f, 0.29f, 0.98f);

    public static UiButton ProTile(Transform parent, string label, Vector2 pos, float size, Color accent, Sprite icon, string letter,
        System.Action action, Sprite photo = null)
    {
        var bg = Box(parent, label, pos, new Vector2(size, size), Glass, null, 80f / (size * 0.22f));
        if (photo != null)
        {
            var mask = Box(bg.transform, "Maske", Vector2.zero, new Vector2(size - 6f, size - 6f), Color.white, null, 80f / (size * 0.2f));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            Box(mask.transform, "Foto", Vector2.zero, new Vector2(size - 6f, size - 6f), Color.white, photo);
        }
        if (icon != null)
        {
            // Simgenin arkasında renkli yumuşak daire (Quest'teki uygulama simgeleri gibi)
            Box(bg.transform, "Daire", new Vector2(0f, 4f), new Vector2(size * 0.58f, size * 0.58f), new Color(accent.r, accent.g, accent.b, 0.2f), Disc);
            var ic = Box(bg.transform, "Simge", new Vector2(0f, 4f), new Vector2(size * 0.38f, size * 0.38f), accent, icon);
            ic.preserveAspect = true;
        }
        if (letter != null)
        {
            var l = Label(bg.transform, letter, Mathf.RoundToInt(size * 0.36f), accent, new Vector2(0f, 4f), new Vector2(size, size));
            l.fontStyle = FontStyle.Bold;
        }
        Label(bg.transform, label, 14, new Color(0.82f, 0.85f, 0.9f, 0.9f), new Vector2(0f, -size * 0.5f - 15f), new Vector2(size + 40f, 22f));
        var b = bg.gameObject.AddComponent<UiButton>();
        b.background = bg;
        b.idle = Glass;
        b.hover = GlassHover;
        b.pressed = Color.Lerp(Glass, accent, 0.35f);
        b.hoverScale = 1.06f;
        b.padding = 10f;
        b.glowEdge = false;   // sade: neon kenar yok
        b.onClick = action;
        return b;
    }

    // ---- Çizilmiş simgeler (64x64, beyaz) ----
    public enum Icon { Back, Reload, Fullscreen, Close, Play, Recenter, Saber, Home, Exit, Ring, Apps, Camera, Goggles, Scan, Globe }

    static readonly System.Collections.Generic.Dictionary<Icon, Sprite> icons = new System.Collections.Generic.Dictionary<Icon, Sprite>();

    public static Sprite GetIcon(Icon icon)
    {
        if (icons.TryGetValue(icon, out var s)) return s;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = new Vector2(x + 0.5f, N - y - 0.5f);   // üstten aşağı koordinat
                float a = Mathf.Clamp01(Coverage(icon, p));
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply(true);
        tex.wrapMode = TextureWrapMode.Clamp;
        s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        icons[icon] = s;
        return s;
    }

    static float Coverage(Icon icon, Vector2 p)
    {
        const float w = 3.2f;   // çizgi yarı kalınlığı
        float d = 1e9f;
        switch (icon)
        {
            case Icon.Back:
                d = Mathf.Min(Seg(p, 18, 32, 46, 32), Seg(p, 18, 32, 31, 19), Seg(p, 18, 32, 31, 45));
                return w + 0.5f - d;
            case Icon.Reload:
            {
                var c = new Vector2(32, 33);
                float ang = Mathf.Atan2(-(p.y - c.y), p.x - c.x) * Mathf.Rad2Deg;
                if (ang < 0) ang += 360f;
                float ring = Mathf.Abs(Vector2.Distance(p, c) - 15f);
                if (ang < 30f || ang > 330f) ring = 1e9f;   // açıklık
                d = Mathf.Min(ring, Seg(p, 47, 13, 47, 26), Seg(p, 47, 26, 34, 26));
                return w + 0.5f - d;
            }
            case Icon.Fullscreen:
                d = Mathf.Min(Mathf.Min(Seg(p, 13, 24, 13, 13), Seg(p, 13, 13, 24, 13), Seg(p, 40, 13, 51, 13)),
                    Mathf.Min(Seg(p, 51, 13, 51, 24), Seg(p, 13, 40, 13, 51), Seg(p, 13, 51, 24, 51)),
                    Mathf.Min(Seg(p, 51, 40, 51, 51), Seg(p, 51, 51, 40, 51)));
                return w + 0.5f - d;
            case Icon.Close:
                d = Mathf.Min(Seg(p, 20, 20, 44, 44), Seg(p, 44, 20, 20, 44));
                return w + 0.5f - d;
            case Icon.Exit:
                d = Mathf.Min(Mathf.Min(Seg(p, 26, 14, 14, 14), Seg(p, 14, 14, 14, 50), Seg(p, 14, 50, 26, 50)),
                    Mathf.Min(Seg(p, 26, 32, 52, 32), Seg(p, 52, 32, 42, 22), Seg(p, 52, 32, 42, 42)));
                return w + 0.5f - d;
            case Icon.Play:
                return Triangle(p, new Vector2(22, 14), new Vector2(50, 32), new Vector2(22, 50));
            case Icon.Recenter:
            {
                var c = new Vector2(32, 32);
                float r = Vector2.Distance(p, c);
                return Mathf.Max(w + 0.5f - Mathf.Abs(r - 17f), 6.5f - r);
            }
            case Icon.Saber:
                d = Seg(p, 16, 50, 48, 14);
                float hilt = Seg(p, 12, 54, 19, 46);
                return Mathf.Max(2.6f + 0.5f - d, 5f + 0.5f - hilt);
            case Icon.Ring:
                return 3.5f + 0.5f - Mathf.Abs(Vector2.Distance(p, new Vector2(32, 32)) - 26f);
            case Icon.Apps:
            {
                float best = 1e9f;
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 3; b++)
                        best = Mathf.Min(best, Vector2.Distance(p, new Vector2(17f + a * 15f, 17f + b * 15f)));
                return 6f - best;
            }
            case Icon.Camera:
            {
                d = Mathf.Min(Mathf.Min(Seg(p, 10, 22, 54, 22), Seg(p, 54, 22, 54, 50), Seg(p, 54, 50, 10, 50)),
                    Mathf.Min(Seg(p, 10, 50, 10, 22), Seg(p, 22, 22, 26, 14), Seg(p, 26, 14, 38, 14)), Seg(p, 38, 14, 42, 22));
                float lens = Mathf.Abs(Vector2.Distance(p, new Vector2(32, 36)) - 8.5f);
                return w + 0.5f - Mathf.Min(d, lens);
            }
            case Icon.Goggles:
            {
                float l = Mathf.Abs(Vector2.Distance(p, new Vector2(21, 34)) - 9f);
                float r = Mathf.Abs(Vector2.Distance(p, new Vector2(43, 34)) - 9f);
                d = Mathf.Min(Mathf.Min(l, r), Mathf.Min(Seg(p, 30, 32, 34, 32), Seg(p, 6, 30, 12, 32), Seg(p, 52, 32, 58, 30)));
                return w + 0.5f - d;
            }
            case Icon.Scan:
                d = Mathf.Min(Mathf.Min(Seg(p, 10, 22, 10, 10), Seg(p, 10, 10, 22, 10), Seg(p, 42, 10, 54, 10)),
                    Mathf.Min(Seg(p, 54, 10, 54, 22), Seg(p, 10, 42, 10, 54), Seg(p, 10, 54, 22, 54)),
                    Mathf.Min(Mathf.Min(Seg(p, 54, 42, 54, 54), Seg(p, 54, 54, 42, 54)), Seg(p, 17, 32, 47, 32)));
                return w + 0.5f - d;
            case Icon.Globe:
            {
                var c = new Vector2(32, 32);
                float ring = Mathf.Abs(Vector2.Distance(p, c) - 19f);
                var q = new Vector2((p.x - c.x) / 8.5f, (p.y - c.y) / 19f);
                float meridian = Mathf.Abs(q.magnitude - 1f) * 8.5f;
                d = Mathf.Min(Mathf.Min(ring, meridian), Mathf.Min(Seg(p, 14, 32, 50, 32), Mathf.Min(Seg(p, 17, 22, 47, 22), Seg(p, 17, 42, 47, 42))));
                return 2.6f + 0.5f - d;
            }
            case Icon.Home:
            {
                var c = new Vector2(32, 32);
                float r = Vector2.Distance(p, c);
                return Mathf.Max(3f + 0.5f - Mathf.Abs(r - 17f), 7.5f - r);
            }
        }
        return 0f;
    }

    static float Seg(Vector2 p, float ax, float ay, float bx, float by)
    {
        var a = new Vector2(ax, ay);
        var ab = new Vector2(bx, by) - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float e1 = Edge(p, a, b), e2 = Edge(p, b, c), e3 = Edge(p, c, a);
        return Mathf.Min(e1, e2, e3) + 0.5f;
    }

    static float Edge(Vector2 p, Vector2 a, Vector2 b)
    {
        var n = new Vector2(b.y - a.y, -(b.x - a.x)).normalized;
        return -Vector2.Dot(p - a, n);
    }
}
