using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// GEÇİT Kumanda (Arduino + HC-06): telefonla eşleşmiş "HC-06" cihazına Bluetooth (seri port) ile bağlanır,
// gelen satırları işler. Bağlantı koparsa 3 sn'de bir yeniden dener.
//   Düğme 1 (Seç)        : basılı tutunca tıklar / sürükler (elin ya da bakışın gösterdiği yere)
//   Düğme 2 (Geri)       : imlecin üstündeki (ya da son kullanılan) pencerede geri
//   Düğme 3 (Menü)       : ana menüyü açar/kapatır
//   Düğme 4 (Önüme getir): açık pencereleri önüne getirir
//   Tekerlek (potansiyometre): döndürünce sayfayı kaydırır
// Kitteki IR kumanda (Arduino'daki IR alıcı üzerinden):
//   OK = Seç (basılı tut = sürükle)   ◄ = Geri   ► = Tam ekran   ▲/▼ = kaydır (basılı tut = akıcı)
//   * = Menü   # = Önüme getir   1 = YouTube   2 = Google   3 = Sanal dünya   4 = El modu (kıstırarak tıkla)
//   5 = Işın Kılıcı   6 = Oda takibi (yürüyerek)   7 = Saat paneli   9 = Fotoğraf çek   0 = Gözlük modu
public class GecitController : MonoBehaviour
{
    public static bool Connected;
    public static bool SelectHeld;
    public static string Status = "kumanda yok";

    public float scrollPerStep = 7f;   // tekerleğin her birimi kaç piksel kaydırır

    readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
    Thread thread;
    volatile bool running;
    volatile bool permitted;
    AndroidJavaObject socket;
    int lastPot = -1;
    bool buttonSelect, irSelect;
    int irScroll;             // basılı tutulan kaydırma tuşu: -1 yukarı, +1 aşağı, 0 yok
    float irScrollSince;

    enum IrAction { None, Select, Back, Fullscreen, ScrollUp, ScrollDown, Menu, Bring, YouTube, Google, Game, Stereo, World, HandMode, RoomTracking, Photo, Clock }

    // Kullanıcının kumandası (17 tuş, oklu) — kodlar Arduino'dan okunarak doğrulandı (4 Eki 2026)
    static IrAction MapIr(int code)
    {
        switch (code)
        {
            case 0x1C: return IrAction.Select;      // OK
            case 0x08: return IrAction.Back;        // ◄
            case 0x5A: return IrAction.Fullscreen;  // ►
            case 0x18: return IrAction.ScrollUp;    // ▲
            case 0x52: return IrAction.ScrollDown;  // ▼
            case 0x16: return IrAction.Menu;        // *
            case 0x0D: return IrAction.Bring;       // #
            case 0x45: return IrAction.YouTube;     // 1
            case 0x46: return IrAction.Google;      // 2
            case 0x40: return IrAction.Game;        // 5
            case 0x19: return IrAction.Stereo;      // 0
            case 0x47: return IrAction.World;       // 3
            case 0x44: return IrAction.HandMode;    // 4
            case 0x43: return IrAction.RoomTracking; // 6
            case 0x09: return IrAction.Photo;       // 9
            case 0x07: return IrAction.Clock;       // 7
        }
        return IrAction.None;
    }
    float lastSeen;
    bool wasConnected;

    const string SerialUuid = "00001101-0000-1000-8000-00805F9B34FB";
    const string ConnectPermission = "android.permission.BLUETOOTH_CONNECT";

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        permitted = Permission.HasUserAuthorizedPermission(ConnectPermission);
        if (!permitted) Permission.RequestUserPermission(ConnectPermission);
        running = true;
        thread = new Thread(Run) { IsBackground = true, Name = "Kumanda" };
        thread.Start();
#endif
    }

    void OnDestroy()
    {
        running = false;
        try { socket?.Call("close"); } catch { }
    }

    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!permitted && Time.frameCount % 30 == 0) permitted = Permission.HasUserAuthorizedPermission(ConnectPermission);
#endif
        if (Connected != wasConnected)
        {
            wasConnected = Connected;
            Debug.Log("GECIT KUMANDA " + (Connected ? "baglandi" : "koptu") + " (" + Status + ")");
            if (!Connected) { SelectHeld = buttonSelect = irSelect = false; irScroll = 0; }
            Sfx.Click();
        }
        while (lines.TryDequeue(out var line)) Handle(line);

        // IR kaydırma tuşu basılı tutulursa akıcı kaydır
        if (irScroll != 0 && Time.time - irScrollSince > 0.3f) Scroll(irScroll * 1100f * Time.deltaTime);
        SelectHeld = buttonSelect || irSelect;
    }

    void Handle(string line)
    {
        if (line.Length == 0) return;
        lastSeen = Time.time;
        char c = line[0];
        if ((c == 'D' || c == 'U') && line.Length >= 2)
        {
            bool down = c == 'D';
            int button = line[1] - '0';
            Debug.Log("GECIT KUMANDA dugme " + button + (down ? " basildi" : " birakildi"));
            switch (button)
            {
                case 1:
                    if (down) UsePressRayBefore();
                    buttonSelect = down;
                    SelectHeld = buttonSelect || irSelect;
                    break;
                case 2: if (down) Back(); break;
                case 3: if (down && HomeMenu.Instance != null) HomeMenu.Instance.Toggle(); break;
                case 4: if (down && HomeMenu.Instance != null) HomeMenu.Instance.BringAll(); break;
            }
        }
        else if (c == 'I' && int.TryParse(line.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out int code))
        {
            var action = MapIr(code);
            Debug.Log($"GECIT KUMANDA IR kod={code:X2} -> {action}");
            irScroll = 0;
            switch (action)
            {
                case IrAction.Select:
                    UsePressRayBefore();
                    irSelect = true;
                    SelectHeld = true;
                    break;
                case IrAction.Back: Back(); break;
                case IrAction.Fullscreen: { var w = TargetWindow(); if (w != null) w.Browser.ToggleFullscreen(); } break;
                case IrAction.ScrollUp: Scroll(-260f); irScroll = -1; irScrollSince = Time.time; break;
                case IrAction.ScrollDown: Scroll(260f); irScroll = 1; irScrollSince = Time.time; break;
                case IrAction.Menu: if (HomeMenu.Instance != null) HomeMenu.Instance.Toggle(); break;
                case IrAction.Bring: if (HomeMenu.Instance != null) HomeMenu.Instance.BringAll(); break;
                case IrAction.YouTube: if (WindowPool.Instance != null) WindowPool.Instance.OpenSite("YouTube", "https://m.youtube.com"); break;
                case IrAction.Google: if (WindowPool.Instance != null) WindowPool.Instance.OpenSite("Google", "https://www.google.com"); break;
                case IrAction.Game: if (HomeMenu.Instance != null) HomeMenu.Instance.ToggleGame(); break;
                case IrAction.Stereo: PassthroughView.SetStereo(!PassthroughView.Stereo); break;
                case IrAction.World: if (VirtualWorld.Instance != null) VirtualWorld.Instance.Toggle(); break;
                case IrAction.HandMode: HandTracker.SetHandClicks(!HandTracker.HandClicks); break;
                case IrAction.RoomTracking: ARPoseUpdater.SetRoomTracking(ARPoseUpdater.LockPosition); break;
                case IrAction.Photo: Screenshot.Take(); break;
                case IrAction.Clock: if (InfoPanel.Instance != null) InfoPanel.Instance.Toggle(); break;
            }
        }
        else if (c == 'J')
        {
            irSelect = false;
            irScroll = 0;
            SelectHeld = buttonSelect;
        }
        else if (c == 'P' && int.TryParse(line.Substring(1), out int pot))
        {
            if (lastPot >= 0)
            {
                int delta = pot - lastPot;
                if (Mathf.Abs(delta) < 300) Scroll(delta * scrollPerStep);   // büyük sıçrama = ilk okuma
            }
            lastPot = pot;
        }
    }

    // OK'a basarken el kıpırdar: tıklama, basmadan ~80 ms önceki ışınla yapılsın (el ışını varsa)
    static void UsePressRayBefore()
    {
        if (PointerInput.Source == "el" && HandTracker.Instance != null && HandTracker.Instance.isActiveAndEnabled)
        {
            PointerInput.PressRay = HandTracker.Instance.RayBefore(0.08f);
            PointerInput.HasPressRay = true;
        }
        else PointerInput.HasPressRay = false;
    }

    static Window TargetWindow()
    {
        if (PointerManager.Hovered is Window w && w.IsOpen) return w;
        if (Window.LastUsed != null && Window.LastUsed.IsOpen) return Window.LastUsed;
        foreach (var x in Window.Windows) if (x.IsOpen) return x;
        return null;
    }

    static void Back()
    {
        if (MiniGames.StopActive()) return;   // oyundaysan oyundan çık
        var w = TargetWindow();
        if (w != null) w.Browser.Back();
    }

    static void Scroll(float pixels)
    {
        var w = TargetWindow();
        if (w == null || Mathf.Abs(pixels) < 1f) return;
        w.Browser.container.browser.EvaluateJS("window.scrollBy(0," + Mathf.RoundToInt(pixels) + ")");
    }

    // ---- Bluetooth (arka plan iş parçacığı) ----
    void Run()
    {
        AndroidJNI.AttachCurrentThread();
        try
        {
            while (running)
            {
                try { ConnectAndRead(); }
                catch (System.Exception e) { Status = "baglanamadi: " + e.Message; }
                Connected = false;
                try { socket?.Call("close"); } catch { }
                socket = null;
                for (int i = 0; i < 30 && running; i++) Thread.Sleep(100);
            }
        }
        finally { AndroidJNI.DetachCurrentThread(); }
    }

    void ConnectAndRead()
    {
        if (!permitted) { Status = "Bluetooth izni bekleniyor"; return; }
        using var adapterClass = new AndroidJavaClass("android.bluetooth.BluetoothAdapter");
        using var adapter = adapterClass.CallStatic<AndroidJavaObject>("getDefaultAdapter");
        if (adapter == null) { Status = "Bluetooth yok"; return; }
        if (!adapter.Call<bool>("isEnabled")) { Status = "Bluetooth kapali"; return; }

        AndroidJavaObject device = null;
        string deviceName = null;
        using (var bonded = adapter.Call<AndroidJavaObject>("getBondedDevices"))
        using (var it = bonded.Call<AndroidJavaObject>("iterator"))
        {
            while (it.Call<bool>("hasNext"))
            {
                var d = it.Call<AndroidJavaObject>("next");
                string n = d.Call<string>("getName") ?? "";
                string u = n.ToUpperInvariant();
                if (u.Contains("HC-0") || u.Contains("HC0") || u.Contains("GECIT")) { device = d; deviceName = n; break; }
                d.Dispose();
            }
        }
        if (device == null) { Status = "eslesmis HC-06 yok (telefonun Bluetooth ayarindan eslestir, sifre 1234)"; return; }

        using (device)
        using (var uuidClass = new AndroidJavaClass("java.util.UUID"))
        using (var uuid = uuidClass.CallStatic<AndroidJavaObject>("fromString", SerialUuid))
        {
            Status = deviceName + " baglaniyor";
            try
            {
                socket = device.Call<AndroidJavaObject>("createRfcommSocketToServiceRecord", uuid);
                socket.Call("connect");
            }
            catch
            {
                try { socket?.Call("close"); } catch { }
                socket = device.Call<AndroidJavaObject>("createInsecureRfcommSocketToServiceRecord", uuid);
                socket.Call("connect");
            }
        }

        using var input = socket.Call<AndroidJavaObject>("getInputStream");
        Connected = true;
        Status = deviceName + " bagli";
        var sb = new StringBuilder();
        while (running)
        {
            int b = input.Call<int>("read");   // bekler
            if (b < 0) break;
            if (b == '\n') { lines.Enqueue(sb.ToString().Trim()); sb.Clear(); }
            else if (b != '\r' && sb.Length < 32) sb.Append((char)b);
        }
    }
}
