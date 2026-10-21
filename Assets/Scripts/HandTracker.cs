using System.Collections.Generic;
using Mediapipe;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// El takibi (Meta tarzı). Telefon kamerasının görüntüsü MediaPipe'a verilir; elin 21 noktası hem görüntüde
// hem de metre cinsinden 3B şekil olarak gelir.
//  - Uzaklık: elin 3B şekli ile görüntüdeki boyu karşılaştırılarak bulunur (el yan dönse de doğru).
//  - Işın: gözün biraz altından, işaret parmağı kökü ile baş parmak kökü arasından geçer. Uzaklık tahminine
//    bağlı değildir, bu yüzden titremez; imleç parmaklarının hemen üstünde durur.
//  - Kıstırma: 3B parmak ucu mesafesinden; açma/kapama için iki ayrı eşik + art arda iki sonuç.
//  - Yumuşatma yalnız yeni sonuç gelince yapılır, aradaki karelerde akıcı geçiş.
[DefaultExecutionOrder(-500)]
public class HandTracker : MonoBehaviour
{
    public static string Status = "-";
    public static bool Visible;
    public static bool Pinched;
    public static float PinchAmount;          // 0 = açık, 1 = tam kıstırılmış
    public static float PinchRatio;
    public static readonly Vector3[] Joints = new Vector3[21];
    // Sanal el için: modelin metre cinsinden 3B el şekli (parmak boyları sabit, titremez) + elin konumu
    public static readonly Vector3[] ShapeJoints = new Vector3[21];
    public static Vector3 PalmCenter;
    public static Vector3 HandUp = Vector3.up;   // bilekten orta parmak köküne (dünya)
    public static float Rate;                  // saniyedeki el sonucu
    public static bool HandClicks = false;     // el modu: baş+işaret birleşince tıklar. Kapalı: tıklama kumandadan
    public static Vector2 RingCenter;          // eli saran halkanın merkezi (ekran/viewport)
    public static float PalmFacing;            // +1: avuç içi sana (kameraya) dönük, -1: elin sırtı dönük
    public static bool PalmUp;
    public static float PalmTurnSpeed;          // elin dönme hızı (derece/sn)                 // avuç menüsü hareketi: avuç sana dönük (ışın gizlenir, kıstırma menüyü açar)
    public static Vector2 PinchViewport;       // baş ve işaret parmağı uçlarının ortası (ekranda)
    public static string Handedness = "-";
    public static float RingRadius;            // yarıçap (ekran yüksekliği biriminde)
    const string HandModeKey = "el_modu";

    public static void SetHandClicks(bool on)
    {
        if (!on && !GecitController.Connected)
        {
            Debug.Log("GECIT EL MODU kapatilmadi: kumanda bagli degil (yoksa hic tiklanamaz)");
            on = true;
        }
        HandClicks = on;
        PlayerPrefs.SetInt(HandModeKey, on ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log("GECIT EL MODU " + (on ? "acik (kistirinca tiklar)" : "kapali (kumanda)"));
    }

    public ARCameraManager cameraManager;
    public Camera arCamera;
    public Transform head;
    public int targetWidth = 640;
    public float pinchOn = 0.85f, pinchOff = 1.2f, pinchSure = 0.55f;   // birleşik puan eşikleri
    public float aimDepth = 0.35f;             // ışın başlangıcının (elin) varsayılan uzaklığı
    public float pressLookBack = 0.1f;

    public bool useLiteModel = true;           // hafif model: ~2 kat hızlı
    const int MaxInFlight = 2;                 // aynı anda işlenen kare (biri işlenirken sıradaki hazırlanır)
    const int Slots = 3;

    HandLandmarker landmarker;      // tek el: hızlı (el takip edilirken arama yapmaz)
    HandLandmarker landmarker2;     // iki el: ikinci el yokken her karede arar, yavaş -> yalnız iki el görünürken
    // Kayıt: hep iki el modunda el takibi 30 -> 15 Hz, gecikme 50 -> 110 ms oldu (kullanıcı: "elde aşırı gecikme").
    // Normalde tek el; saniyede bir kare iki el modeline yollanır, iki el bulursa iki el moduna geçilir,
    // ikinci el kaybolunca tek ele dönülür.
    volatile bool twoHandMode;
    long probeTs = -1;
    int singleResults;
    float nextProbe;
    readonly NativeArray<byte>[] buffers = new NativeArray<byte>[Slots];
    int nextSlot;
    readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
    readonly object pendingLock = new object();
    readonly object sendLock = new object();
    readonly Queue<long> pending = new Queue<long>();   // gönderilip sonucu beklenen kareler (zaman damgası)
    long lastQueuedTs = -1, lastSentTs = -1, lastActivityMs;
    double lastImageTimestamp = -1;
    float latencyMs;
    string modelName = "-";

    readonly object resultLock = new object();
    readonly Vector3[][] landmarksN = { new Vector3[21], new Vector3[21] };
    readonly Vector3[][] worldN = { new Vector3[21], new Vector3[21] };
    readonly string[] labelN = { "", "" };
    int handCountRaw;
    readonly Vector3[][] uvIn = { new Vector3[21], new Vector3[21] };
    readonly Vector3[][] worldIn = { new Vector3[21], new Vector3[21] };
    readonly string[] labelIn = { "", "" };
    string handLabel = "";
    // İki elin sırası: hangi sonuç birinci el (menü, avuç), hangisi ikinci (yalnız işaretçi). Bilekten izlenir.
    readonly Vector2[] slotWrist = new Vector2[2];
    readonly float[] slotSeen = { -10f, -10f };
    readonly int[] slotMiss = new int[2];
    readonly SecondState second = new SecondState();
    Vector2 pinchVpTarget;
    readonly Vector3[] uv = new Vector3[21];
    readonly Vector3[] world = new Vector3[21];
    bool hasResult;
    long resultTime;
    int resultSeq, processedSeq;
    int imageWidth, imageHeight;

    Matrix4x4 displayMatrix;
    bool hasDisplayMatrix;

    bool pinched;
    int pinchVotes;
    float lastResultAt = -1f;
    int rateCount; float rateSince;
    float nextDiag;

    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
        return (p - (a + ab * t)).magnitude;
    }

    static float SegDist2(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
        return (p - (a + ab * t)).magnitude;
    }

    // Yeni sonuçta güncellenen hedefler
    Vector2 aimTarget, aimShown;
    readonly Vector3[] jointTarget = new Vector3[21];   // kameraya göre (yerel)
    readonly Vector3[] jointShown = new Vector3[21];
    readonly Vector3[] shapeTarget = new Vector3[21];   // kameraya göre (yerel)
    readonly Vector3[] shapeShown = new Vector3[21];
    readonly OneEuroFilter[] shapeFilters = new OneEuroFilter[21];
    readonly OneEuroFilter anchorFilter = new OneEuroFilter(1.2f, 4f, 1.5f);
    float depth = 0.45f;
    readonly OneEuroFilter aimFilter = new OneEuroFilter(1.2f, 14f, 2.0f);   // hızlı harekette daha az gecikme  // 21 noktanın ortası zaten sakin: hafif yumuşatma
    const float AimDeadZone = 0.004f;   // ekranın binde 4'ünden küçük kıpırdamalar imleci oynatmaz
    const float PinchDeadZone = 0.025f; // el modunda parmaklar birleşirken imleç donar (tıklama kaymasın)
    float ringRadiusTarget;
    Vector2 aimVelocity, aimHeld;
    float aimAt;
    readonly OneEuroFilter depthFilter = new OneEuroFilter(0.6f, 1.5f);
    readonly OneEuroFilter[] jointFilters = new OneEuroFilter[21];
    readonly Queue<(float time, Vector2 aim)> aimHistory = new Queue<(float, Vector2)>();

    void Awake()
    {
        Instance = this;
        for (int i = 0; i < 21; i++) jointFilters[i] = new OneEuroFilter(2.5f, 3f, 2.5f);
        for (int i = 0; i < 21; i++) shapeFilters[i] = new OneEuroFilter(2f, 6f, 2f);
    }

    void OnEnable() { cameraManager.frameReceived += OnFrame; }
    void OnDisable() { cameraManager.frameReceived -= OnFrame; }

    void OnFrame(ARCameraFrameEventArgs args)
    {
        if (args.displayMatrix.HasValue)
        {
            displayMatrix = args.displayMatrix.Value;
            hasDisplayMatrix = true;
        }
    }

    void Start()
    {
        clock.Start();
        // El ile tıklama varsayılan AÇIK. Kumanda bağlı değilken kapalı kalamaz: kapalıysa elle yeniden açmanın yolu
        // yoktu (tıklamak için tıklamak gerekiyordu; kullanıcı: "hiçbir yerde tıklamıyor").
        HandClicks = PlayerPrefs.GetInt(HandModeKey, 1) == 1 || !GecitController.Connected;
        Debug.Log("GECIT EL MODU baslangic " + (HandClicks ? "acik" : "kapali"));
        if (useLiteModel) { landmarker = Create("hand_landmarker_lite", 1); landmarker2 = Create("hand_landmarker_lite", 2); }
        if (landmarker == null) { landmarker = Create("hand_landmarker", 1); landmarker2 = Create("hand_landmarker", 2); }
        Status = "el modeli hazir: " + modelName;
        Debug.Log("GECIT EL MODELI " + modelName);
    }

    HandLandmarker Create(string resource, int hands)
    {
        try
        {
            var model = Resources.Load<TextAsset>(resource);
            if (model == null) return null;
            var options = new HandLandmarkerOptions(
                new Mediapipe.Tasks.Core.BaseOptions(Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU, modelAssetBuffer: model.bytes),
                runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                numHands: hands,
                minHandDetectionConfidence: 0.5f,
                minHandPresenceConfidence: 0.4f,
                minTrackingConfidence: 0.3f,   // takibi kolay bırakmasın (yeniden arama yavaş; kumandayı tutarken 10 Hz'e düşüyordu)
                resultCallback: OnResult);
            var lm = HandLandmarker.CreateFromOptions(options);
            modelName = resource;
            return lm;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("GECIT EL MODELI acilamadi " + resource + ": " + e.Message);
            return null;
        }
    }

    void OnDestroy()
    {
        lock (sendLock)
        {
            landmarker?.Close();
            landmarker = null;
            landmarker2?.Close();
            landmarker2 = null;
        }
        foreach (var b in buffers) if (b.IsCreated) b.Dispose();
    }

    void OnResult(HandLandmarkerResult result, Mediapipe.Image image, long timestamp)
    {
        long now = clock.ElapsedMilliseconds;
        lock (pendingLock)
        {
            while (pending.Count > 0 && pending.Peek() <= timestamp) pending.Dequeue();
            lastActivityMs = now;
        }
        lock (resultLock)
        {
            int n = result.handLandmarks != null && result.handWorldLandmarks != null
                ? Mathf.Min(2, Mathf.Min(result.handLandmarks.Count, result.handWorldLandmarks.Count)) : 0;
            int got = 0;
            for (int k = 0; k < n; k++)
            {
                var list = result.handLandmarks[k].landmarks;
                var wl = result.handWorldLandmarks[k].landmarks;
                if (list.Count < 21 || wl.Count < 21) continue;
                for (int i = 0; i < 21; i++)
                {
                    landmarksN[got][i] = new Vector3(list[i].x, list[i].y, list[i].z);
                    worldN[got][i] = new Vector3(wl[i].x, wl[i].y, wl[i].z);
                }
                labelN[got] = result.handedness != null && result.handedness.Count > k && result.handedness[k].categories != null && result.handedness[k].categories.Count > 0
                    ? result.handedness[k].categories[0].categoryName : "";
                got++;
            }
            handCountRaw = got;
            if (timestamp == probeTs) { if (got >= 2) { twoHandMode = true; singleResults = 0; } }
            else if (twoHandMode)
            {
                if (got < 2) { if (++singleResults >= 8) twoHandMode = false; }
                else singleResults = 0;
            }
            hasResult = true;   // el yoksa da sonuç: "el yok" hemen anlaşılsın
            if (got > 0)
            {
                resultTime = now;
                latencyMs = Mathf.Lerp(latencyMs, now - timestamp, 0.2f);
            }
            resultSeq++;
        }
    }

    void Update()
    {
        SubmitFrame();
        ApplyResult();
    }

    // Kamera görüntüsü arka planda RGBA'ya çevrilip MediaPipe'a verilir. Aynı anda iki kare yolda olabilir:
    // biri işlenirken sıradaki hazır bekler, böylece model hiç boş kalmaz.
    float nextIdleSubmit;
    float nextLoadSubmit;
    // Yüke göre (örnek videodaki gibi): telefon ağır ısınınca (3+) ya da kare hızı 24'ün altına düşünce el modeli
    // saniyede 20 kareye iner, telefon soğuyunca (2-) ve hız toparlanınca (27+) tam hıza döner.
    public static bool LoadLimited { get; private set; }

    void SubmitFrame()
    {
        if (landmarker == null) return;
        // El 1,5 sn'dir görünmüyorsa saniyede 5 kare bak (el gelince hemen tam hıza döner)
        if (Time.time - HandSourceState.LastSeen > 1.5f)
        {
            if (Time.time < nextIdleSubmit) return;
            nextIdleSubmit = Time.time + 0.2f;
        }
        int heat = DebugHUD.LastThermal;
        bool limit = LoadLimited ? (heat >= 3 || DebugHUD.LastFps < 27f) : (heat >= 3 || DebugHUD.LastFps < 24f);
        if (limit != LoadLimited)
        {
            LoadLimited = limit;
            Debug.Log($"GECIT EL HIZI {(limit ? "20 Hz (telefon isindi/yavasladi)" : "tam")} isi={heat} fps={DebugHUD.LastFps:F0}");
        }
        if (LoadLimited)
        {
            if (Time.time < nextLoadSubmit) return;
            nextLoadSubmit = Time.time + 0.05f;
        }
        long now = clock.ElapsedMilliseconds;
        lock (pendingLock)
        {
            if (pending.Count > 0 && now - lastActivityMs > 1000) pending.Clear();   // takılırsa sıfırla
            if (pending.Count >= MaxInFlight) return;
        }
        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage)) return;

        using (cpuImage)
        {
            if (cpuImage.timestamp == lastImageTimestamp) return;   // aynı kareyi iki kez gönderme
            lastImageTimestamp = cpuImage.timestamp;
            int factor = Mathf.Max(1, Mathf.RoundToInt((float)cpuImage.width / targetWidth));
            var conversion = new XRCpuImage.ConversionParams(cpuImage, TextureFormat.RGBA32, XRCpuImage.Transformation.None)
            {
                outputDimensions = new Vector2Int(cpuImage.width / factor, cpuImage.height / factor)
            };
            long ts = System.Math.Max(now, lastQueuedTs + 1);
            lastQueuedTs = ts;
            // Hangi model: iki el modu ya da saniyede bir "ikinci el var mı" yoklaması
            bool probe = !twoHandMode && landmarker2 != null && HandSourceState.HasHand && Time.time >= nextProbe;
            if (probe) { nextProbe = Time.time + 1f; probeTs = ts; }
            bool useTwo = (twoHandMode || probe) && landmarker2 != null;
            int slot = nextSlot;
            nextSlot = (nextSlot + 1) % Slots;
            lock (pendingLock)
            {
                pending.Enqueue(ts);
                lastActivityMs = now;
            }
            cpuImage.ConvertAsync(conversion, (st, p, data) => OnConverted(st, p, data, ts, slot, useTwo));
        }
    }

    void OnConverted(XRCpuImage.AsyncConversionStatus status, XRCpuImage.ConversionParams conversion, NativeArray<byte> data, long ts, int slot, bool useTwo)
    {
        lock (sendLock)
        {
            if (status != XRCpuImage.AsyncConversionStatus.Ready || landmarker == null || ts <= lastSentTs)
            {
                Forget(ts);
                return;
            }
            var buffer = buffers[slot];
            if (!buffer.IsCreated || buffer.Length != data.Length)
            {
                if (buffer.IsCreated) buffer.Dispose();
                buffer = buffers[slot] = new NativeArray<byte>(data.Length, Allocator.Persistent);
            }
            buffer.CopyFrom(data);
            int w = conversion.outputDimensions.x, h = conversion.outputDimensions.y;
            imageWidth = w;
            imageHeight = h;
            lastSentTs = ts;
            try
            {
                using var image = new Mediapipe.Image(ImageFormat.Types.Format.Srgba, w, h, w * 4, buffer);
                (useTwo ? landmarker2 : landmarker).DetectAsync(image, ts);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("GECIT EL gonderilemedi: " + e.Message);
                Forget(ts);
            }
        }
    }

    void Forget(long ts)
    {
        lock (pendingLock)
        {
            int n = pending.Count;
            for (int i = 0; i < n; i++)
            {
                long t = pending.Dequeue();
                if (t != ts) pending.Enqueue(t);
            }
        }
    }

    void ApplyResult()
    {
        bool recent, fresh;
        int count = 0;
        lock (resultLock)
        {
            recent = hasResult && clock.ElapsedMilliseconds - resultTime < 300;
            fresh = hasResult && resultSeq != processedSeq;
            if (fresh)
            {
                processedSeq = resultSeq;
                count = handCountRaw;
                for (int k = 0; k < count; k++)
                {
                    System.Array.Copy(landmarksN[k], uvIn[k], 21);
                    System.Array.Copy(worldN[k], worldIn[k], 21);
                    labelIn[k] = labelN[k];
                }
            }
        }

        bool ready = recent && hasDisplayMatrix && imageWidth > 0;
        int primaryIdx = -1, secondIdx = -1;
        if (fresh && ready) AssignSlots(count, out primaryIdx, out secondIdx);

        // Birinci el
        if (fresh)
        {
            if (primaryIdx >= 0) slotMiss[0] = 0; else slotMiss[0]++;
            if (secondIdx >= 0) slotMiss[1] = 0; else slotMiss[1]++;
        }
        if (!ready || slotMiss[0] >= 3)
        {
            if (Visible) { Lost(); Status = "el yok"; Rate = 0f; }
        }
        else
        {
            if (primaryIdx >= 0)
            {
                System.Array.Copy(uvIn[primaryIdx], uv, 21);
                System.Array.Copy(worldIn[primaryIdx], world, 21);
                handLabel = labelIn[primaryIdx];
                ProcessResult();
            }
            if (lastResultAt >= 0f) Present();
        }

        // İkinci el
        if (!ready || slotMiss[1] >= 3) { if (second.visible) LostSecond(); }
        else
        {
            if (secondIdx >= 0) ProcessSecond(uvIn[secondIdx], worldIn[secondIdx], labelIn[secondIdx]);
            if (second.visible) PresentSecond();
        }
    }

    // Sonuçtaki elleri birinci / ikinci yuvaya dağıt: bileklerin son yerine en yakın eşleşme; tek el varsa ve
    // birinci el bir süredir yoksa o el birinci olur (menü, avuç hep çalışsın)
    void AssignSlots(int count, out int primary, out int secondary)
    {
        primary = secondary = -1;
        float now = Time.time;
        bool p0 = now - slotSeen[0] < 0.5f, p1 = now - slotSeen[1] < 0.5f;
        Vector2 W(int k) => new Vector2(uvIn[k][0].x, uvIn[k][0].y);
        if (count == 1)
        {
            if (p1 && (!p0 || (W(0) - slotWrist[1]).sqrMagnitude < (W(0) - slotWrist[0]).sqrMagnitude)) secondary = 0;
            else primary = 0;
        }
        else if (count >= 2 && (W(0) - W(1)).sqrMagnitude < 0.08f * 0.08f)
        {
            // İki "el" neredeyse aynı yerde: aynı elin kopyası (iki elle büyütme sanılıp pencere tıklaması
            // iptal oluyordu; kullanıcı: "el ile tıklama sadece menüde çalışıyor"). Tek el say.
            primary = (p0 && (W(1) - slotWrist[0]).sqrMagnitude < (W(0) - slotWrist[0]).sqrMagnitude) ? 1 : 0;
        }
        else if (count >= 2)
        {
            float keep = (p0 ? (W(0) - slotWrist[0]).sqrMagnitude : 0f) + (p1 ? (W(1) - slotWrist[1]).sqrMagnitude : 0f);
            float swap = (p0 ? (W(1) - slotWrist[0]).sqrMagnitude : 0f) + (p1 ? (W(0) - slotWrist[1]).sqrMagnitude : 0f);
            if (!p0 && !p1)
            {
                // ikisi birden yeni: sağdaki el birinci (çoğu kişi sağ elini kullanır)
                bool zeroRight = ImageToViewport(W(0)).x >= ImageToViewport(W(1)).x;
                primary = zeroRight ? 0 : 1; secondary = zeroRight ? 1 : 0;
            }
            else if (keep <= swap) { primary = 0; secondary = 1; }
            else { primary = 1; secondary = 0; }
        }
        if (primary >= 0) { slotWrist[0] = W(primary); slotSeen[0] = now; }
        if (secondary >= 0) { slotWrist[1] = W(secondary); slotSeen[1] = now; }
    }

    void Lost()
    {
        Visible = false;
        PalmUp = false;
        Pinched = pinched = false;
        pinchVotes = 0;
        HandSourceState.HasHand = false;
        PointerInput.Pressed = false;
        PointerInput.HasPressRay = false;
        aimFilter.Reset();
        depthFilter.Reset();
        anchorFilter.Reset();
        foreach (var f in jointFilters) f.Reset();
        foreach (var f in shapeFilters) f.Reset();
        aimHistory.Clear();
        lastResultAt = -1f;
    }

    // Yeni MediaPipe sonucu: uzaklık, eklemler, kıstırma, nişan noktası
    void ProcessResult()
    {
        float now = Time.time;
        float dt = lastResultAt < 0f ? 0.05f : Mathf.Clamp(now - lastResultAt, 0.01f, 0.2f);
        bool first = lastResultAt < 0f;
        lastResultAt = now;

        rateCount++;
        if (now - rateSince >= 1f) { Rate = rateCount / (now - rateSince); rateCount = 0; rateSince = now; }

        // Görüntü eksenlerinin ekrandaki karşılığı (kamera yönü/aynalama ne olursa olsun)
        var o = ImageToViewport(new Vector2(0.5f, 0.5f));
        var ax = Snap(ImageToViewport(new Vector2(0.6f, 0.5f)) - o);
        var ay = Snap(ImageToViewport(new Vector2(0.5f, 0.6f)) - o);

        // Uzaklık: 3B el şeklinin yanal boyu / görüntüdeki boyu  (z = fx * W / piksel)
        float fx = imageWidth * 0.8f;
        if (cameraManager.TryGetIntrinsics(out XRCameraIntrinsics intr) && intr.resolution.x > 0)
            fx = intr.focalLength.x * imageWidth / intr.resolution.x;
        Vector2 pc = Vector2.zero, wc = Vector2.zero;
        float wcz = 0f;
        for (int i = 0; i < 21; i++)
        {
            pc += new Vector2(uv[i].x * imageWidth, uv[i].y * imageHeight);
            wc += new Vector2(world[i].x, world[i].y);
            wcz += world[i].z;
        }
        pc /= 21f; wc /= 21f; wcz /= 21f;
        float sumW = 0f, sumP = 0f;
        for (int i = 0; i < 21; i++)
        {
            sumW += (new Vector2(world[i].x, world[i].y) - wc).magnitude;
            sumP += (new Vector2(uv[i].x * imageWidth, uv[i].y * imageHeight) - pc).magnitude;
        }
        float rawDepth = Mathf.Clamp(fx * sumW / Mathf.Max(sumP, 1f), 0.15f, 1.1f);
        depth = depthFilter.Filter(new Vector3(rawDepth, 0f, 0f), dt).x;

        // Eklemler: görüntüdeki yerinden geçen ışın üzerinde, 3B şekle göre ön/arka
        for (int i = 0; i < 21; i++)
        {
            var vp = ImageToViewport(new Vector2(uv[i].x, uv[i].y));
            float z = Mathf.Clamp(depth + (world[i].z - wcz), 0.08f, 1.3f);
            var p = arCamera.ViewportToWorldPoint(new Vector3(vp.x, vp.y, z));
            jointTarget[i] = jointFilters[i].Filter(arCamera.transform.InverseTransformPoint(p), dt);
        }

        // Elin "yukarı" yönü (kılıç için): 3B şekilden, kamera eksenlerine çevrilmiş
        var w0 = CamSpace(world[0], ax, ay);
        var w9 = CamSpace(world[9], ax, ay);
        // Avuç yönü: elin sırtının baktığı yön = (işaret kökü - serçe kökü) x (bilek -> orta kök); sol elde ters.
        // Arka kamera aynalı olmadığı için MediaPipe'ın etiketi terstir: "Left" = gerçekte sağ el.
        var w5c = CamSpace(world[5], ax, ay);
        var w17c = CamSpace(world[17], ax, ay);
        var back = Vector3.Cross(w5c - w17c, w9 - w0);
        // Sağ/sol etiketi ara ara tek karelik yanlış geliyor (kayıt: sag/sol gidip geliyor): ancak 8 kare üst üste
        // farklı gelirse değiştir (nokta elin tarafına göre ayarlandığı için zıplamasın)
        string rawHand = handLabel == "Left" ? "sag" : "sol";
        if (first || Handedness == "-") { Handedness = rawHand; handVotes = 0; }
        else if (rawHand != Handedness) { if (++handVotes >= 8) { Handedness = rawHand; handVotes = 0; } }
        else handVotes = 0;
        bool actualRight = Handedness == "sag";
        if (!actualRight) back = -back;
        // Telefonda denendi: işaret tersti (avuç aşağıdayken çıkıyordu) -> çevrildi
        float facing = back.sqrMagnitude > 1e-12f ? -back.normalized.z : 0f;   // +1: avuç içi sana (yukarı) dönük
        // Elin dönme hızı (derece/sn): el çevrilirken baş-işaret parmağı görüntüde üst üste biner, kıstırma sanılır
        var backN = back.sqrMagnitude > 1e-12f ? back.normalized : lastBack;
        if (!first && dt > 1e-4f) palmTurnSpeed = Mathf.Lerp(palmTurnSpeed, Vector3.Angle(lastBack, backN) / dt, 0.3f);
        lastBack = backN;
        PalmTurnSpeed = palmTurnSpeed;
        PalmFacing = first ? facing : Mathf.Lerp(PalmFacing, facing, 0.5f);
        PalmUp = PalmUp ? PalmFacing > 0.35f : PalmFacing > 0.6f;
        var pv = ImageToViewport(new Vector2((uv[4].x + uv[8].x) * 0.5f, (uv[4].y + uv[8].y) * 0.5f));
        pinchVpTarget = pv;
        if (first) PinchViewport = pv;
        var up = arCamera.transform.TransformDirection(w9 - w0);
        if (up.sqrMagnitude > 1e-6f) HandUp = Vector3.Slerp(HandUp, up.normalized, first ? 1f : 0.6f);

        // Kıstırma (Meta gibi ped-ped): baş parmak ucunun işaret parmağının uç iki boğumuna en yakın mesafesi
        // (3B, metre) / avuç boyu. Uç uca değil, çünkü insan baş parmağını işaret parmağının yastığına bastırır.
        float palm = Mathf.Max(1e-3f, (world[9] - world[0]).magnitude);
        float tipRatio = (world[4] - world[8]).magnitude / palm;
        float segRatio = Mathf.Min(SegDist(world[4], world[6], world[7]), SegDist(world[4], world[7], world[8])) / palm;
        // Görüntüdeki (2B) aynı ölçü: kullanıcı kaydında kıstırma ile açık el arasını en net ayıran ölçü
        Vector2 P(int i) => new Vector2(uv[i].x * imageWidth, uv[i].y * imageHeight);
        float palm2 = Mathf.Max(1f, (P(9) - P(0)).magnitude);
        float r2 = Mathf.Min((P(4) - P(8)).magnitude, Mathf.Min(SegDist2(P(4), P(6), P(7)), SegDist2(P(4), P(7), P(8)))) / palm2;
        // Birleşik puan (1 = sınır). Kayıt (4 Eki): kıstırınca ped 0,17 / 2B 0,06; açık el ped 0,44 / 2B 0,55
        float ratio = 0.5f * (segRatio / 0.26f + r2 / 0.24f);
        PinchRatio = ratio;
        PinchAmount = Mathf.InverseLerp(2f, pinchOn, ratio);
        bool wasPinched = pinched;
        if (!pinched)
        {
            pinchVotes = ratio < pinchOn ? pinchVotes + 1 : 0;
            if (ratio < pinchSure || pinchVotes >= 2) { pinched = true; pinchVotes = 0; CapturePressRay(); }
        }
        else
        {
            pinchVotes = ratio > pinchOff ? pinchVotes + 1 : 0;
            if (pinchVotes >= 3) { pinched = false; pinchVotes = 0; }   // bırakma 3 sonuç: tıklama ortada kopmasın
        }
        // Ses YALNIZ gerçek tıklamada (PointerManager / düğmeler çalar). Önceden kıstırma algılanınca çalıyordu;
        // tıklama engellense de ses geliyordu (kullanıcı: "tıklama sesi geliyor ama tıklamıyor").
        if (!HandClicks) PinchAmount = 0f;                  // imleç parmak hareketiyle küçülmesin

        // Ölçüm kaydı (eşik ayarı için): saniyede ~8
        if (now >= nextDiag)
        {
            nextDiag = now + 0.12f;
            Debug.Log($"GECIT EL uc={tipRatio:F2} ped={segRatio:F2} 2b={r2:F2} puan={ratio:F2} basili={(pinched ? 1 : 0)} uzak={depth:F2} avuc={PalmFacing:F2} elX={aimShown.x:F2} elY={aimShown.y:F2} el={Handedness}({handLabel}) hedef={PointerManager.HoverName}");
        }

        // Nişan: elin TAMAMI (21 noktanın ortası) — tek noktaya göre çok daha sakin; Meta gibi el bir bütün.
        var aimUv = Vector3.zero;
        // Nişan noktası: AVUÇ (bilek + 4 parmak kökü). Parmaklar kıstırırken / oynarken nokta kaymasın
        // (önceden 21 noktanın ortasıydı; kullanıcı: "el kontrolü hassas değil")
        foreach (int i in PalmPoints) aimUv += uv[i];
        aimUv /= PalmPoints.Length;
        var aimVp = ImageToViewport(new Vector2(aimUv.x, aimUv.y));

        // Sanal el şekli: el merkezinin yeri (görüntüdeki ortası + tahmini uzaklık, ayrıca yumuşatılır) +
        // modelin metrik 3B şekli (kameranın eksenlerine çevrilmiş). Parmak boyları her karede aynı kalır.
        var anchorWorld = arCamera.ViewportToWorldPoint(new Vector3(aimVp.x, aimVp.y, depth));
        var anchorLocal = anchorFilter.Filter(arCamera.transform.InverseTransformPoint(anchorWorld), dt);
        var wmean = Vector3.zero;
        for (int i = 0; i < 21; i++) wmean += world[i];
        wmean /= 21f;
        for (int i = 0; i < 21; i++)
            shapeTarget[i] = anchorLocal + shapeFilters[i].Filter(CamSpace(world[i] - wmean, ax, ay), dt);
        // Halka yarıçapı: avuç boyundan (bilek -> orta parmak kökü); parmaklar kapanınca küçülmez
        var v0 = ImageToViewport(new Vector2(uv[0].x, uv[0].y));
        var v9 = ImageToViewport(new Vector2(uv[9].x, uv[9].y));
        var dp = v9 - v0;
        float palmLen = Mathf.Sqrt(dp.x * dp.x * arCamera.aspect * arCamera.aspect + dp.y * dp.y);
        ringRadiusTarget = Mathf.Clamp(palmLen * 0.35f, 0.025f, 0.05f);   // avuçta küçük işaret (ekranı kapatmasın)
        if (first) RingRadius = ringRadiusTarget;
        handVpTarget = aimVp;

        var prevAim = aimTarget;
        aimTarget = aimFilter.Filter(new Vector3(aimVp.x, aimVp.y, 0f), dt);
        if (first) { aimShown = aimHeld = aimTarget; aimVelocity = Vector2.zero; handShown = handVpTarget; }
        else aimVelocity = Vector2.Lerp(aimVelocity, (aimTarget - prevAim) / dt, 0.5f);
        aimAt = now;
        Status = $"el var {(twoHandMode ? "2el" : "1el")} {Rate:F0}Hz gecikme {latencyMs:F0}ms {(modelName.EndsWith("lite") ? "hafif" : "tam")}, uzaklik {depth:F2} m, kistirma {ratio:F2}{(pinched ? " BASILI" : "")}";
        aimHistory.Enqueue((now, aimTarget));
        while (aimHistory.Count > 0 && now - aimHistory.Peek().time > 0.4f) aimHistory.Dequeue();
    }

    // Her kare: hedeflere akıcı geçiş + ışını yayınla
    void Present()
    {
        // Ölü bölge: hedef, tutulan noktadan biraz uzaklaşmadıkça kıpırdama; uzaklaşınca farkı kadar izle.
        // (İleri tahmin yok: titremeyi büyütüyordu.)
        var off = aimTarget - aimHeld;
        float dist = off.magnitude;
        float dz = Mathf.Lerp(AimDeadZone, PinchDeadZone, HandClicks && !pinched ? PinchAmount : 0f);
        if (dist > dz) aimHeld += off * ((dist - dz) / dist);
        // Hızlı harekette biraz ileriyi tahmin et (gecikme hissi azalır); yavaşta/dururken tahmin yok (titreme olmasın)
        float speed = aimVelocity.magnitude;
        float ahead = Mathf.Min(Time.time - aimAt, 0.05f) + 0.06f;   // algılama gecikmesini (~50 ms) telafi
        var aimGoal = aimHeld + aimVelocity * ahead * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.6f, speed));
        aimShown = Vector2.Lerp(aimShown, aimGoal, 1f - Mathf.Exp(-Time.deltaTime * 70f));
        RingRadius = Mathf.Lerp(RingRadius, ringRadiusTarget, 1f - Mathf.Exp(-Time.deltaTime * 4f));
        handShown = Vector2.Lerp(handShown, handVpTarget, 1f - Mathf.Exp(-Time.deltaTime * 30f));
        RingCenter = handShown;
        float k = 1f - Mathf.Exp(-Time.deltaTime * 30f);
        var camT = arCamera.transform;
        for (int i = 0; i < 21; i++)
        {
            jointShown[i] = Visible ? Vector3.Lerp(jointShown[i], jointTarget[i], k) : jointTarget[i];
            Joints[i] = camT.TransformPoint(jointShown[i]);
            shapeShown[i] = Visible ? Vector3.Lerp(shapeShown[i], shapeTarget[i], k) : shapeTarget[i];
            ShapeJoints[i] = camT.TransformPoint(shapeShown[i]);
        }
        PalmCenter = (Joints[0] + Joints[5] + Joints[9] + Joints[17]) * 0.25f;

        UpdateCursor();
        var ray = AimRay(CursorViewport);
        var pinchPoint = (Joints[4] + Joints[8]) * 0.5f;

        Visible = true;
        Pinched = pinched;
        HandSourceState.HasHand = true;
        HandSourceState.LastSeen = Time.time;
        PointerInput.Valid = true;
        PointerInput.Ray = ray;
        // Kendiliğinden tıklama olmasın (kullanıcı: menüyü açıp elimi çevirince kendi kendine tıklıyor):
        //  - avuç menüsünden sonra 0,6 sn, el hızlı dönerken ve avuç yarı yarıya sana dönükken YENİ tıklama başlamaz
        //  - bu sırada yapılan kıstırma "harcanır": parmaklar ayrılıp yeniden birleşince tıklar
        if (PalmUp) clickBlockUntil = Time.time + 0.9f;
        if (palmTurnSpeed > 260f) clickBlockUntil = Mathf.Max(clickBlockUntil, Time.time + 0.3f);
        bool clickBlocked = Time.time < clickBlockUntil || PalmFacing > 0.2f;
        if (pinched && !pressingByHand && (PalmUp || clickBlocked)) pinchConsumed = true;
        if (!pinched) pinchConsumed = false;
        pressingByHand = HandClicks && pinched && !PalmUp && !pinchConsumed;
        PointerInput.Pressed = (HandClicks && pinched && !PalmUp && !pinchConsumed) || GecitController.SelectHeld;
        PinchViewport = Vector2.Lerp(PinchViewport, pinchVpTarget, 1f - Mathf.Exp(-Time.deltaTime * 25f));
        PointerInput.VisualOrigin = arCamera.ViewportToWorldPoint(new Vector3(handShown.x, handShown.y, aimDepth));   // elin ortası
        PointerInput.Source = "el";
    }

    // İmleç FARE GİBİ (göreli): el göründüğü an imleç ekranın ortasında belirir; sonra elin hareketi kadar
    // (kazançla) kayar. Elin ekranda nerede durduğu önemli değil, imleç ekrandan hiç çıkmaz (kenarda durur).
    // Böylece el aşağıda rahat durur, küçük hareketle tüm ekran gezilir (kullanıcı: "yuvarlak görünmüyor"du;
    // sabit dinlenme noktasıyla imleç ekranın altına kaçıyordu).
    public float gainX = 1.45f, gainY = 1.85f;   // elini az oynatarak pencerenin her köşesine ulaş (kamerayı eğmeden)

    public static Vector2 CursorViewport = new Vector2(0.5f, 0.5f);
    bool pinchConsumed, pressingByHand;
    float clickBlockUntil, palmTurnSpeed;
    Vector3 lastBack = Vector3.forward;
    // Basarken imleç donar (el kayması tıklamayı kaydırmaya çevirmesin); belirgin hareket olursa serbest kalır
    bool pressLatched, cursorLocked;
    Vector2 pressDrift, pressAnchor;
    const float PressUnlock = 0.03f;   // el bu kadar (ekranın %3'ü) oynarsa sürükleme başlar
    float lastCursorUpdate = -10f;
    readonly Queue<(float time, Vector2 cursor)> cursorHistory = new Queue<(float, Vector2)>();

    // Meta gibi SABİT eşleme: aynı el konumu hep aynı noktayı gösterir (göreli/fare gibi değil — kayma birikmez).
    // El göründüğü anki konumu "merkez" olur, imleç ortada belirir; el oynayınca imleç kazançla gider, el eski yerine
    // gelince imleç de eski yerine döner. El 3 sn'den uzun kaybolursa (duruş değişmiş olabilir) merkez yeniden alınır.
    Vector2 handCenter;
    const float RestY = 0.12f;  // elin bu yüksekliği (ekranın alt kenarına yakın) = nokta ortada
    static readonly int[] PalmPoints = { 0, 1, 5, 9, 13, 17 };   // nişan: avuç (bilek, baş parmak kökü, 4 parmak kökü)
    const float RestX = 0.7f;   // sağ yanda duran elin rahat yeri (sol yanda: 0,3)
    float restSideX = 0.7f;
    int handVotes;
    Vector2 handVpTarget, handShown;
    const float MaxRestY = 0.28f;

    void UpdateCursor()
    {
        if (Time.time - lastCursorUpdate > 3f)
        {
            // Dinlenme yüksekliği en fazla ekranın alt üçte biri: el kameranın üstünde görünse de (yatarken eli
            // kaldırınca) nokta elin ALTINA düşmez; çizgi Quest'teki gibi hep elden yukarı/ileri gider
            handCenter = new Vector2(aimShown.x, Mathf.Min(aimShown.y, MaxRestY));

            cursorHistory.Clear();
        }
        lastCursorUpdate = Time.time;
        // Yatayda nokta elin tarafında (elin sağdaysa nokta sağda: Quest gibi elin yönü);
        // dikeyde elin rahat durduğu yükseklik ekranın ortasına denk gelir (eli kaldırmak gerekmez)
        // Sabit dinlenme noktası, Quest gibi: sağ el sağ altta (sol el sol altta) dururken nokta ekranın ortasında.
        // Böylece çizgi elden ortaya ÇAPRAZ gider (önceden nokta hep elin tam üstündeydi, çizgi dimdik kalıyordu).
        // El kalkınca yukarı, sağa-sola gidince o yana; hep aynı el yeri = aynı nokta.
        // Dinlenme yeri ELE göre (önceden elin ilk belirdiği yana göreydi; sağ el ortadan girince nokta sağa fırlıyordu).
        // Kayıt: MediaPipe'ın etiketi arka kamerada DOĞRU ("Right" = sağ el, ekranda x≈0,7); bizim Handedness adımız
        // eskiden ters konduğu için sağ elde "sol" yazar.
        restSideX = Handedness == "sol" ? RestX : 1f - RestX;
        // Gözlükte her göz görüntünün yalnız ortasını görür: nokta o alanda kalsın
        float vf = PassthroughView.VisibleFracX;
        var target = new Vector2(Mathf.Clamp(0.5f + (aimShown.x - restSideX) * gainX * vf, 0.5f - 0.47f * vf, 0.5f + 0.47f * vf),
                                 Mathf.Clamp(0.5f + (aimShown.y - RestY) * gainY, 0.04f, 0.96f));

        // Basarken imleç donar (el kayması tıklamayı bozmasın); belirgin hareket olursa serbest kalır
        bool pressNow = (HandClicks && pinched && !PalmUp && !pinchConsumed) || GecitController.SelectHeld;
        if (pressNow && !pressLatched) { pressLatched = true; cursorLocked = true; pressDrift = Vector2.zero; pressAnchor = aimShown; }
        if (!pressNow) { pressLatched = false; cursorLocked = false; }
        if (cursorLocked && (aimShown - pressAnchor).magnitude < PressUnlock)
        {
            cursorHistory.Enqueue((Time.time, CursorViewport));
            while (cursorHistory.Count > 0 && Time.time - cursorHistory.Peek().time > 0.4f) cursorHistory.Dequeue();
            return;   // imleç yerinde
        }
        cursorLocked = false;
        CursorViewport = target;
        cursorHistory.Enqueue((Time.time, CursorViewport));
        while (cursorHistory.Count > 0 && Time.time - cursorHistory.Peek().time > 0.4f) cursorHistory.Dequeue();
    }

    // Işın gözden imlecin ekrandaki yerinden geçer (çizgi elden imlece çizilir)
    Ray AimRay(Vector2 cursor) => arCamera.ViewportPointToRay(new Vector3(cursor.x, cursor.y, 0f));

    public static HandTracker Instance;

    // Bir an önceki ışın (kumandanın OK'una basarken el kıpırdarsa tıklama kaymasın)
    public Ray RayBefore(float seconds) => AimRay(CursorBefore(seconds));

    Vector2 CursorBefore(float seconds)
    {
        float target = Time.time - seconds;
        Vector2 chosen = CursorViewport;
        foreach (var (time, c) in cursorHistory)
        {
            chosen = c;
            if (time >= target) break;
        }
        return chosen;
    }

    void CapturePressRay()
    {
        PointerInput.PressRay = AimRay(CursorBefore(pressLookBack));
        PointerInput.HasPressRay = true;
    }

    static Vector2 Snap(Vector2 d)
    {
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return new Vector2(Mathf.Sign(d.x), 0f);
        return new Vector2(0f, Mathf.Sign(d.y));
    }

    // MediaPipe 3B (x sağ, y aşağı, z uzak; görüntü eksenleri) -> kamera uzayı (x sağ, y yukarı, z ileri)
    static Vector3 CamSpace(Vector3 w, Vector2 ax, Vector2 ay)
    {
        var xy = ax * w.x + ay * w.y;   // ekran (viewport) yönleri: y yukarı
        return new Vector3(xy.x, xy.y, w.z);
    }

    // Kamera görüntüsü koordinatı (sol üst başlangıç) -> ekran (viewport): displayMatrix'in tersi
    Vector2 ImageToViewport(Vector2 p)
    {
        var m = displayMatrix;
        float a = m.m00, b = m.m10, c = m.m01, d = m.m11;
        float du = p.x - m.m20, dv = p.y - m.m21;
        float det = a * d - b * c;
        if (Mathf.Abs(det) < 1e-6f) return p;
        return new Vector2((d * du - b * dv) / det, (-c * du + a * dv) / det);
    }

    // ================= İkinci el (yalnız işaretçi: nokta, çizgi, tıklama; sanal el) =================
    class SecondState
    {
        public bool visible;
        public float lastAt = -1f;
        public readonly OneEuroFilter aimF = new OneEuroFilter(1.2f, 14f, 2f);
        public readonly OneEuroFilter depthF = new OneEuroFilter(0.6f, 1.5f);
        public readonly OneEuroFilter anchorF = new OneEuroFilter(1.2f, 4f, 1.5f);
        public readonly OneEuroFilter[] shapeF = new OneEuroFilter[21];
        public readonly Vector3[] shapeTarget = new Vector3[21], shapeShown = new Vector3[21];
        public Vector2 aimTarget, aimHeld, aimShown, handTarget, handShown, cursor, pressAnchor;
        public float depth = 0.45f, restSideX = 0.3f, palmFacing, clickBlockUntil, pinchAmount;
        public bool pinched, pinchConsumed, pressing, palmUp, locked, shapeInit;
        public int votes, handVotes;
        public string hand = "-";
        public SecondState() { for (int i = 0; i < 21; i++) shapeF[i] = new OneEuroFilter(2f, 6f, 2f); }
    }

    public static class Second
    {
        public static bool Visible;
        public static readonly Vector3[] ShapeJoints = new Vector3[21];
        public static string Handedness = "-";
    }

    void ProcessSecond(Vector3[] u, Vector3[] w, string label)
    {
        var h = second;
        float now = Time.time;
        float dt = h.lastAt < 0f ? 0.05f : Mathf.Clamp(now - h.lastAt, 0.01f, 0.2f);
        bool first = h.lastAt < 0f;
        h.lastAt = now;
        var o = ImageToViewport(new Vector2(0.5f, 0.5f));
        var ax = Snap(ImageToViewport(new Vector2(0.6f, 0.5f)) - o);
        var ay = Snap(ImageToViewport(new Vector2(0.5f, 0.6f)) - o);

        // Uzaklık (birinci elle aynı yöntem)
        float fx = imageWidth * 0.8f;
        if (cameraManager.TryGetIntrinsics(out XRCameraIntrinsics intr) && intr.resolution.x > 0)
            fx = intr.focalLength.x * imageWidth / intr.resolution.x;
        Vector2 pc = Vector2.zero, wc = Vector2.zero;
        for (int i = 0; i < 21; i++) { pc += new Vector2(u[i].x * imageWidth, u[i].y * imageHeight); wc += new Vector2(w[i].x, w[i].y); }
        pc /= 21f; wc /= 21f;
        float sumW = 0f, sumP = 0f;
        for (int i = 0; i < 21; i++)
        {
            sumW += (new Vector2(w[i].x, w[i].y) - wc).magnitude;
            sumP += (new Vector2(u[i].x * imageWidth, u[i].y * imageHeight) - pc).magnitude;
        }
        h.depth = h.depthF.Filter(new Vector3(Mathf.Clamp(fx * sumW / Mathf.Max(sumP, 1f), 0.15f, 1.1f), 0f, 0f), dt).x;

        var aimUv = Vector3.zero;
        foreach (int i in PalmPoints) aimUv += u[i];
        aimUv /= PalmPoints.Length;
        var aimVp = ImageToViewport(new Vector2(aimUv.x, aimUv.y));
        var anchorLocal = h.anchorF.Filter(arCamera.transform.InverseTransformPoint(arCamera.ViewportToWorldPoint(new Vector3(aimVp.x, aimVp.y, h.depth))), dt);
        var wmean = Vector3.zero;
        for (int i = 0; i < 21; i++) wmean += w[i];
        wmean /= 21f;
        for (int i = 0; i < 21; i++) h.shapeTarget[i] = anchorLocal + h.shapeF[i].Filter(CamSpace(w[i] - wmean, ax, ay), dt);

        // Sağ/sol (kararlı) ve avuç yönü (birinci elle aynı kural)
        string raw = label == "Left" ? "sag" : "sol";
        if (first || h.hand == "-") { h.hand = raw; h.handVotes = 0; }
        else if (raw != h.hand) { if (++h.handVotes >= 8) { h.hand = raw; h.handVotes = 0; } }
        else h.handVotes = 0;
        Second.Handedness = h.hand;
        var back = Vector3.Cross(CamSpace(w[5], ax, ay) - CamSpace(w[17], ax, ay), CamSpace(w[9], ax, ay) - CamSpace(w[0], ax, ay));
        if (h.hand != "sag") back = -back;
        float facing = back.sqrMagnitude > 1e-12f ? -back.normalized.z : 0f;
        h.palmFacing = first ? facing : Mathf.Lerp(h.palmFacing, facing, 0.5f);
        h.palmUp = h.palmUp ? h.palmFacing > 0.35f : h.palmFacing > 0.6f;

        // Kıstırma (birinci elle aynı ölçü ve eşikler)
        float palm = Mathf.Max(1e-3f, (w[9] - w[0]).magnitude);
        float segRatio = Mathf.Min(SegDist(w[4], w[6], w[7]), SegDist(w[4], w[7], w[8])) / palm;
        Vector2 P(int i) => new Vector2(u[i].x * imageWidth, u[i].y * imageHeight);
        float palm2 = Mathf.Max(1f, (P(9) - P(0)).magnitude);
        float r2 = Mathf.Min((P(4) - P(8)).magnitude, Mathf.Min(SegDist2(P(4), P(6), P(7)), SegDist2(P(4), P(7), P(8)))) / palm2;
        float ratio = 0.5f * (segRatio / 0.26f + r2 / 0.24f);
        h.pinchAmount = HandClicks ? Mathf.InverseLerp(2f, pinchOn, ratio) : 0f;
        if (!h.pinched)
        {
            h.votes = ratio < pinchOn ? h.votes + 1 : 0;
            if (ratio < pinchSure || h.votes >= 2) { h.pinched = true; h.votes = 0; }
        }
        else
        {
            h.votes = ratio > pinchOff ? h.votes + 1 : 0;
            if (h.votes >= 3) { h.pinched = false; h.votes = 0; }
        }

        h.handTarget = aimVp;
        h.aimTarget = h.aimF.Filter(new Vector3(aimVp.x, aimVp.y, 0f), dt);
        if (first)
        {
            h.aimShown = h.aimHeld = h.aimTarget;
            h.handShown = aimVp;
        }
        h.visible = true;
        Second.Visible = true;
    }

    void PresentSecond()
    {
        var h = second;
        var off = h.aimTarget - h.aimHeld;
        float dist = off.magnitude;
        float dz = Mathf.Lerp(AimDeadZone, PinchDeadZone, HandClicks && !h.pinched ? h.pinchAmount : 0f);
        if (dist > dz) h.aimHeld += off * ((dist - dz) / dist);
        h.aimShown = Vector2.Lerp(h.aimShown, h.aimHeld, 1f - Mathf.Exp(-Time.deltaTime * 70f));
        h.handShown = Vector2.Lerp(h.handShown, h.handTarget, 1f - Mathf.Exp(-Time.deltaTime * 30f));
        float k = 1f - Mathf.Exp(-Time.deltaTime * 30f);
        var camT = arCamera.transform;
        for (int i = 0; i < 21; i++)
        {
            h.shapeShown[i] = h.shapeInit ? Vector3.Lerp(h.shapeShown[i], h.shapeTarget[i], k) : h.shapeTarget[i];
            Second.ShapeJoints[i] = camT.TransformPoint(h.shapeShown[i]);
        }
        h.shapeInit = true;

        // Tıklama: avuç menüsü hareketinde ya da avuç yarı dönükken yeni tıklama yok
        if (h.palmUp) h.clickBlockUntil = Time.time + 0.9f;
        bool blocked = Time.time < h.clickBlockUntil || h.palmFacing > 0.2f;
        if (h.pinched && !h.pressing && (h.palmUp || blocked)) h.pinchConsumed = true;
        if (!h.pinched) h.pinchConsumed = false;
        bool pressNow = HandClicks && h.pinched && !h.palmUp && !h.pinchConsumed;

        h.restSideX = h.hand == "sol" ? RestX : 1f - RestX;   // ele göre (bkz. birinci el)
        float vf = PassthroughView.VisibleFracX;
        var target = new Vector2(Mathf.Clamp(0.5f + (h.aimShown.x - h.restSideX) * gainX * vf, 0.5f - 0.47f * vf, 0.5f + 0.47f * vf),
                                 Mathf.Clamp(0.5f + (h.aimShown.y - RestY) * gainY, 0.04f, 0.96f));
        if (pressNow && !h.pressing) { h.locked = true; h.pressAnchor = h.aimShown; }
        if (!pressNow) h.locked = false;
        if (!(h.locked && (h.aimShown - h.pressAnchor).magnitude < PressUnlock)) { h.locked = false; h.cursor = target; }
        h.pressing = pressNow;

        var st = PointerInput.Second;
        st.Valid = true;
        st.Source = "el";
        st.Ray = AimRay(h.cursor);
        if (pressNow && !st.Pressed) { st.PressRay = st.Ray; st.HasPressRay = true; }
        st.Pressed = pressNow;
        st.PalmUp = h.palmUp;
        st.PinchAmount = h.pinchAmount;
        st.VisualOrigin = arCamera.ViewportToWorldPoint(new Vector3(h.handShown.x, h.handShown.y, aimDepth));
    }

    void LostSecond()
    {
        var h = second;
        h.visible = false;
        h.lastAt = -1f;
        h.pinched = h.pressing = h.palmUp = h.locked = h.shapeInit = false;
        h.votes = 0;
        h.aimF.Reset(); h.depthF.Reset(); h.anchorF.Reset();
        foreach (var f in h.shapeF) f.Reset();
        Second.Visible = false;
        var st = PointerInput.Second;
        st.Valid = false;
        st.Pressed = false;
        st.HasPressRay = false;
    }
}
