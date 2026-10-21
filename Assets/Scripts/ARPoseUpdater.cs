using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using InputSystem = UnityEngine.InputSystem.InputSystem;
using AttitudeSensor = UnityEngine.InputSystem.AttitudeSensor;
using UnityEngine.InputSystem.Android;

// Telefonun duruşunu kameraya uygular.
//  - Oda görünümü, ARCore takipte: doğrudan ARCore'un oda haritası (konum + yön), el görünürken de. ARCore odayı
//    yeniden tanıyıp kendini düzeltince pencereler gerçek yerlerine yumuşakça geri oturur.
//  - Takip yok (karanlık, düz duvar): jiroskop son yönden devam eder; konum "boyun modeli".
//  - Sanal dünya: jiroskop (gecikmesiz, 60 fps), yön yavaşça ARCore'a oturur (kayma birikmez); konum boyun modeli.
//  - Kaynak değişince ya da ARCore sıçrayınca aradaki fark yarım saniyede erir (görüntü hiç zıplamaz).
//  Jiroskop eksenleri telefon dururken ARCore ile karşılaştırılarak kendiliğinden bulunur.
[DefaultExecutionOrder(-900)]   // önce telefonun konumu, sonra el (-500), sonra imleç (PointerManager, 100)
public class ARPoseUpdater : MonoBehaviour
{
    public static bool LockPosition = false;
    public const float MaxStep = 0.08f;
    const float MaxTurnPerFrame = 25f;   // derece: tek karede bundan büyük dönme gerçek olamaz (ARCore sıfırlandı)

    public static Vector3 PivotOffset = new Vector3(0f, 0.04f, 0.22f);
    public static bool GyroReady;
    public static string Source = "-";
    public static string GyroDiag = "-";

    static bool HandInFront => HandSourceState.HasHand || Time.time - HandSourceState.LastSeen < 0.3f;
    const string RoomTrackingKey = "oda_takibi";
    const string GyroKey = "jiroskop_v4";

    public static void SetRoomTracking(bool on)
    {
        LockPosition = !on;
        PlayerPrefs.SetInt(RoomTrackingKey, on ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log("GECIT ODA TAKIBI " + (on ? "acik (konum + donme)" : "kapali (sabit, yalniz donme)"));
    }

    public static string DeviceName = "-";
    public static string Features = "-";
    public static string UsedPosition = "-";

    static readonly InputFeatureUsage<Vector3>[] PositionUsages =
    {
        CommonUsages.devicePosition, CommonUsages.colorCameraPosition, CommonUsages.centerEyePosition,
    };
    static readonly InputFeatureUsage<Quaternion>[] RotationUsages =
    {
        CommonUsages.deviceRotation, CommonUsages.colorCameraRotation, CommonUsages.centerEyeRotation,
    };

    readonly List<InputDevice> devices = new List<InputDevice>();
    readonly List<InputFeatureUsage> usages = new List<InputFeatureUsage>();
    InputDevice device;

    // Uygulanan duruş
    Vector3 pos;
    Quaternion rot = Quaternion.identity;
    bool started;
    Vector3 lastAr;
    bool hasLastAr;
    Quaternion lastRot = Quaternion.identity;
    Quaternion correction = Quaternion.identity;   // ARCore dünyası -> bizim dünya (sıfırlanınca değişir)
    bool usingGyro;
    bool gyroRefSet;
    float nextVrLog;
    string lastSrc = "-";
    bool yawInit, frameInit, hasPrevAr;
    const float VideoLag = 0.1f;
    readonly Quaternion[] attHist = new Quaternion[48];
    readonly float[] attHistT = new float[48];
    int attHistI;
    float goodSince = -1f, trackedSince = -1f;

    Quaternion AttAt(float t)
    {
        int best = -1;
        float bestDt = float.MaxValue;
        for (int i = 0; i < attHist.Length; i++)
        {
            float dt = Mathf.Abs(attHistT[i] - t);
            if (attHistT[i] > 0f && dt < bestDt) { bestDt = dt; best = i; }
        }
        return best >= 0 ? attHist[best] : attHist[(attHistI + attHist.Length - 1) % attHist.Length];
    }
    Vector3 vrPosFix, vrPrevAr;
    bool vrPosInit;
    float stillFor, accSmooth, movingUntil, winAr, winGy, winT;
    Quaternion prevGyroDelayed = Quaternion.identity;

    static float YawDeg(Quaternion a, Quaternion b)
    {
        var fa = a * Vector3.forward; var fb = b * Vector3.forward;
        return Mathf.DeltaAngle(Mathf.Atan2(fa.x, fa.z) * Mathf.Rad2Deg, Mathf.Atan2(fb.x, fb.z) * Mathf.Rad2Deg);
    }
    Quaternion vrFrame = Quaternion.identity;   // sanal dünyada ARCore dünyası -> bizim dünya (yalnız sağa-sola)
    bool vrFrameInit;   // sanal dünyaya girerken ARCore konumuyla bizim konum arasındaki fark (kesintisiz)
    Quaternion arYawFix = Quaternion.identity, prevArRaw = Quaternion.identity;
    float lastJumpAt = -10f;
    Quaternion arFrame = Quaternion.identity;
    Quaternion vrRot = Quaternion.identity, vrPrevAtt = Quaternion.identity, vrLogRot = Quaternion.identity;
    public static bool RateOk => UsingGameRotation;
    Quaternion rotOffset = Quaternion.identity, prevTargetRot = Quaternion.identity;
    Vector3 posOffset, prevTargetPos, pivotPos, arSmooth;
    Quaternion prevArUse;
    bool hasPrevArUse;

    // Jiroskop: telefonun kendi yön ölçümü (Android) kameranın yönüne çevrilir: G = L · dönüştür(att) · B.
    // Hangi L / dönüştürme / B doğru, telefon DURURKEN ölçülür: doğru eşleşmede G ile ARCore arasındaki fark
    // yalnız sağa-sola dönmedir (ikisi de yer çekimini bilir), ufuk eğimi ~0 ve hep aynı. Hareket sırasındaki
    // gecikme farkı bu ölçümü bozamaz (önceki hız karşılaştırması %40-72 uyumda kalıyor, yön karışıyordu).
    static readonly Quaternion[] Lefts = { Quaternion.Euler(90f, 0f, 0f), Quaternion.Euler(-90f, 0f, 0f) };
    static readonly Quaternion[] Rights = BuildRights();
    const int Bases = 3;
    static int gyroCand = -1;
    float[] candTilt, candYawS, candYawC;
    int stillSamples;
    Quaternion lastStillAr;
    float nextStill;
    Quaternion yawAlign = Quaternion.identity;

    static Quaternion[] BuildRights()
    {
        var list = new List<Quaternion>();
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                for (int z = 0; z < 4; z++)
                {
                    var q = Quaternion.Euler(x * 90f, y * 90f, z * 90f);
                    if (!list.Any(o => Quaternion.Angle(o, q) < 1f)) list.Add(q);
                }
        return list.ToArray();   // 24 eksen dönmesi
    }

    static Quaternion Base(int b, Quaternion a)
    {
        switch (b)
        {
            case 0: return new Quaternion(a.x, a.y, -a.z, -a.w);   // yaygın Unity dönüşümü (z aynası)
            case 1: return new Quaternion(a.x, -a.y, -a.z, a.w);   // x aynası
            default: return new Quaternion(-a.x, a.y, -a.z, a.w);  // y aynası
        }
    }

    static int CandCount => Lefts.Length * Bases * Rights.Length;

    // Telefonun kendi eksenindeki küçük dönme -> kameranın eksenindeki dönme (seçili adayla)
    static Quaternion CamDelta(Quaternion devDelta)
    {
        if (gyroCand < 0) return Quaternion.identity;
        var r = Rights[gyroCand % Rights.Length];
        int b = (gyroCand / Rights.Length) % Bases;
        return Quaternion.Inverse(r) * Base(b, devDelta) * r;
    }

    // Telefonun yön sensörü: Android "oyun dönme vektörü" (jiroskop + ivmeölçer, PUSULASIZ). Unity'nin eski
    // Input.gyro.attitude'u pusulalı sensörü kullanıyordu; evde metal eşyalar yüzünden durduğun yerde bile saniyede
    // 1-4° kayıyordu (kayıt). Yoksa eski sensöre düşülür.
    static AttitudeSensor grv;
    static float grvRetry;
    public static bool UsingGameRotation;

    static Quaternion ReadAtt()
    {
        if (grv == null && Time.time >= grvRetry)
        {
            grvRetry = Time.time + 2f;
            grv = InputSystem.GetDevice<AndroidGameRotationVector>();
            if (grv != null)
            {
                if (!grv.enabled) InputSystem.EnableDevice(grv);
                grv.samplingFrequency = 100f;
            }
        }
        if (grv != null && grv.enabled)
        {
            var q = grv.attitude.ReadValue();
            if (q != Quaternion.identity && q != new Quaternion(0f, 0f, 0f, 0f)) { UsingGameRotation = true; return q; }
        }
        UsingGameRotation = false;
        return Input.gyro.attitude;
    }

    static Quaternion G(int c, Quaternion att)
    {
        int r = c % Rights.Length;
        int b = (c / Rights.Length) % Bases;
        int l = c / (Rights.Length * Bases);
        return Lefts[l] * Base(b, att) * Rights[r];
    }

    // Dönme noktası ölçümü
    readonly float[] lsM = new float[9];
    readonly float[] lsV = new float[3];
    readonly float[] lsA = new float[9];

    void Awake()
    {
        candTilt = new float[CandCount];
        candYawS = new float[CandCount];
        candYawC = new float[CandCount];
        LockPosition = PlayerPrefs.GetInt(RoomTrackingKey, 1) == 0;
        Input.gyro.enabled = true;
        Input.gyro.updateInterval = 0.01f;
        gyroCand = PlayerPrefs.GetInt(GyroKey, -1);
        GyroReady = gyroCand >= 0 && gyroCand < CandCount;
    }

    void OnEnable() { Application.onBeforeRender += BeforeRender; }
    void OnDisable() { Application.onBeforeRender -= BeforeRender; }
    void Update()
    {
        UpdatePose(true);
        // Sanal dünyada dönme jiroskoptan geliyorsa kamera hızına (30) bağlı kalma: 60 fps çiz (dönerken gecikme
        // yarıya iner). Oda görünümünde görüntü kameradan geldiği için 30'da kalır.
        bool fast = VirtualWorld.Active && usingGyro;
        if (fast != fastMode)
        {
            fastMode = fast;
            if (session == null) session = FindAnyObjectByType<ARSession>();
            if (session != null) session.matchFrameRateRequested = !fast;
            Application.targetFrameRate = fast ? 60 : 30;
            Debug.Log("GECIT KARE HIZI " + (fast ? 60 : 30));
        }
    }

    bool fastMode;
    ARSession session;
    void BeforeRender() { UpdatePose(false); }

    static Vector3 RotVec(Quaternion q)
    {
        q.ToAngleAxis(out float deg, out Vector3 axis);
        if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return Vector3.zero;
        if (deg > 180f) deg -= 360f;
        return axis * (deg * Mathf.Deg2Rad);
    }

    static Quaternion FromRotVec(Vector3 v)
    {
        float a = v.magnitude;
        return a < 1e-7f ? Quaternion.identity : Quaternion.AngleAxis(a * Mathf.Rad2Deg, v / a);
    }

    void UpdatePose(bool isUpdate)
    {
        if (!device.isValid)
        {
            InputDevices.GetDevices(devices);
            device = devices.FirstOrDefault(d => d.name.Contains("ARCore"));
            if (!device.isValid && devices.Count > 0) device = devices[0];
            if (device.isValid)
            {
                DeviceName = device.name;
                if (device.TryGetFeatureUsages(usages))
                    Features = string.Join(", ", usages.Select(u => u.name));
            }
        }

        bool arTracking = ARSession.state == ARSessionState.SessionTracking;
        bool hasArRot = false, hasArPos = false;
        var arRot = Quaternion.identity;
        var arPos = Vector3.zero;
        if (device.isValid)
        {
            foreach (var u in RotationUsages)
                if (device.TryGetFeatureValue(u, out Quaternion r) && r != new Quaternion(0f, 0f, 0f, 0f)) { arRot = r; hasArRot = true; break; }
            foreach (var u in PositionUsages)
                if (device.TryGetFeatureValue(u, out Vector3 p) && p != Vector3.zero) { arPos = p; hasArPos = true; UsedPosition = u.name; break; }
        }
        var att = ReadAtt();
        bool gyroOk = SystemInfo.supportsGyroscope && att != new Quaternion(0f, 0f, 0f, 0f);

        // Ölçüm yalnız oda görünümünde ve ARCore iyi takip ederken (sanal dünyada ayar değişip görüntü kaymasın)
        bool goodAr = arTracking && hasArRot && ARSession.notTrackingReason == UnityEngine.XR.ARSubsystems.NotTrackingReason.None;
        if (isUpdate && !VirtualWorld.Active) LearnGyro(goodAr && gyroOk, arRot, att);

        // ARCore takibe döndükten sonraki ilk 3 sn "oturma": haritasını küçük adımlarla düzeltiyor (kayıt: kopmadan
        // dönünce 0,25 sn'de 4 düzeltme), bu adımlar pencereleri kaydırıyordu. O sürede jiroskop + boyun modeli
        // sürer; süre bitince fark tek seferde, görünmeden içeri alınır.
        bool arRaw = arTracking && hasArRot;
        if (!arRaw) trackedSince = -1f; else if (trackedSince < 0f) trackedSince = Time.time;
        bool tracked = arRaw && Time.time - trackedSince > 3f;
        var g = gyroOk && GyroReady ? G(gyroCand, att) : Quaternion.identity;

        // ---- Yön ----
        // Oda görünümü (ARCore takipte): ARCore'un yönü — kamera görüntüsüyle AYNI karede, pencereler odaya yapışık.
        //   (v5'te jiroskop kullanmıştım: görüntü birkaç kare geç geldiği için başı çevirince pencereler odanın
        //   önüne geçip geri kayıyordu, kullanıcı "eskiden daha sabitti" dedi.) ARCore haritasını düzeltip yön
        //   sıçrarsa sıçrama arYawFix'e emilir ve sakinken en fazla 0,5°/sn ile erir (paneller kendi kendine kaymaz).
        // Takip yok ve sanal dünya: kalibre pusulasız jiroskop (yawAlign·g), geçişte kesintisiz.
        bool gyroUse = GyroReady && gyroOk;
        float turnRate = Input.gyro.rotationRateUnbiased.magnitude;
        // Jiroskop geçmişi: oda görünümünde kamera görüntüsü ~0,1 sn geç gelir; takip yokken jiroskobun o kadar
        // önceki yönü kullanılır ki pencereler görüntüyle aynı anda dönsün (yüzmesin)
        attHistT[attHistI] = Time.time; attHist[attHistI] = att; attHistI = (attHistI + 1) % attHist.Length;
        // ARCore ne zamandır kesintisiz ve iyi takip ediyor (yalnız o zaman hizalanır; karanlıkta güvenilmez)
        bool goodNow = tracked && ARSession.notTrackingReason == UnityEngine.XR.ARSubsystems.NotTrackingReason.None;
        if (!goodNow) goodSince = -1f; else if (goodSince < 0f) goodSince = Time.time;
        bool stableAr = goodSince >= 0f && Time.time - goodSince > 8f && Time.time - lastJumpAt > 8f;
        // Telefon GERÇEKTEN duruyor mu (jiroskop dönme görmüyor, ivmeölçer hareket görmüyor). Duruyorsa ARCore'un
        // bildirdiği her değişiklik kendi hatasıdır: dünya kıpırdamaz (kullanıcı: "hala kayıyor").
        float acc = Input.gyro.userAcceleration.magnitude;
        if (turnRate < 0.06f && acc < 0.04f) stillFor += Time.deltaTime * (isUpdate ? 1f : 0f); else stillFor = 0f;
        bool still = stillFor > 0.25f;
        // Yürüyor mu: ivmeölçerin (yer çekimisiz) ortalaması. Yürümüyorsan ARCore'un konum değişimi ya hata ya
        // da elin görüntüye girmesinden (ARCore eli odanın parçası sanıp kayıyor): uygulanmaz, yalnız boyun modeli.
        if (isUpdate) accSmooth = Mathf.Lerp(accSmooth, acc, 1f - Mathf.Exp(-Time.deltaTime * 5f));
        if (accSmooth > 0.045f) movingUntil = Time.time + 0.6f;
        bool walking = Time.time < movingUntil;
        var oldFix = arYawFix;
        Quaternion targetRot;
        if (VirtualWorld.Active && gyroUse)
        {
            if (lastSrc != "sanal") yawAlign = YawOnly(rot * Quaternion.Inverse(g));
            targetRot = yawAlign * g;
        }
        else if (tracked)
        {
            if (hasPrevAr && lastSrc == "arcore" && Quaternion.Angle(prevArRaw, arRot) > MaxTurnPerFrame)
            {
                arYawFix = YawOnly(rot * Quaternion.Inverse(arRot));   // sıçramayı em: görüntü yerinde kalsın
                lastJumpAt = Time.time;
            }
            else if (lastSrc != "arcore" && lastSrc != "sabit" && started)
                arYawFix = YawOnly(rot * Quaternion.Inverse(arRot));   // jiroskoptan / sanal dünyadan dönüş: kesintisiz
            else if (lastSrc == "arcore" && gyroUse && Mathf.Abs((arRot * Vector3.forward).y) < 0.85f)
            {
                // ARCore'un sağa-sola dönüşü jiroskobunkiyle (görüntü anına geciktirilmiş) 0,2 sn'lik dilimlerde
                // karşılaştırılır; 2°'den fazla uyuşmazsa (el görüntüye girince ARCore şaşırıyor) jiroskobunki
                // geçerli olur. Durgunken ARCore hiç döndüremez. Dik yukarı/aşağı bakarken karşılaştırma yapılmaz.
                var gNow = G(gyroCand, AttAt(Time.time - VideoLag));
                float dAr = YawDeg(prevArRaw, arRot), dGy = YawDeg(prevGyroDelayed, gNow);
                if (still)
                {
                    if (Mathf.Abs(dAr) > 0f) arYawFix = Quaternion.AngleAxis(-dAr, Vector3.up) * arYawFix;
                    winAr = winGy = 0f; winT = Time.time;
                }
                else
                {
                    winAr += dAr; winGy += dGy;
                    if (Time.time - winT > 0.2f)
                    {
                        float diff = winGy - winAr;
                        if (Mathf.Abs(diff) > 2f) arYawFix = Quaternion.AngleAxis(diff, Vector3.up) * arYawFix;
                        winAr = winGy = 0f; winT = Time.time;
                    }
                }
            }
            targetRot = arYawFix * arRot;
            if (gyroUse) yawAlign = YawOnly(targetRot * Quaternion.Inverse(g));   // takip koparsa jiroskop buradan devam
        }
        else if (gyroUse)
        {
            var gd = VirtualWorld.Active ? g : G(gyroCand, AttAt(Time.time - VideoLag));
            if (lastSrc != "takipyok") yawAlign = YawOnly(rot * Quaternion.Inverse(gd));
            targetRot = yawAlign * gd;
        }
        else targetRot = rot;
        prevArRaw = arRot;
        hasPrevAr = hasArRot;
        if (gyroUse) prevGyroDelayed = G(gyroCand, AttAt(Time.time - VideoLag));
        arFrame = arYawFix;

        // ---- Konum ----
        string src;
        Vector3 targetPos;
        if (VirtualWorld.Active)
        {
            // Sanal dünya: dönme jiroskoptan; yürüyünce ilerlemek için KONUM ARCore'dan (kamera takip ettikçe).
            // ARCore'un yönü (dünyası) jiroskopunkinden farklı olabilir ve harita düzeltilince değişir; konum adımları
            // bu farkla çevrilmezse yanlış yöne gidilir (kayıt: düzeltmeden sonra "ileri yürüyorum, onlar geri gidiyor").
            // Fark her karede iki yönden ölçülür (vrFrame); sıçrarsa hemen güncellenir, konum kesintisiz devam eder.
            if (lastSrc != "sanal") { pivotPos = pos - rot * PivotOffset; vrLogRot = rot; vrFrameInit = false; vrPosInit = false; }
            if (tracked && hasArPos)
            {
                // ARCore'un yönü kamera karesinin anına ait (~0,1 sn eski): jiroskobun da o anki yönüyle karşılaştır,
                // yoksa hızlı dönerken fark "sıçrama" sanılır
                var gyroThen = yawAlign * G(gyroCand, AttAt(Time.time - VideoLag));
                var frameNow = YawOnly(gyroThen * Quaternion.Inverse(arRot));
                // Önceki yerin: ARCore'la takip ediyorsak son ARCore konumundan, yoksa (yeni başlıyorsa) boyun modelinden
                var oldFrame = vrFrame;
                Vector3 prevP = vrPosInit ? oldFrame * vrPrevAr + vrPosFix : pivotPos + targetRot * PivotOffset;
                if (!vrFrameInit || (Quaternion.Angle(vrFrame, frameNow) > 10f && turnRate < 1.5f)) { vrFrame = frameNow; vrFrameInit = true; }
                else if (turnRate < 0.6f) vrFrame = Quaternion.RotateTowards(vrFrame, frameNow, 2f * Time.deltaTime);
                // Yalnız telefon gerçekten hareket ediyorsa ARCore'un adımı uygulanır (eşleme değişimi konumu kaydırmaz);
                // durgunken ya da ARCore sıçrayınca bulunduğun yer AYNEN kalır
                var step = arPos - vrPrevAr;
                Vector3 neck = targetRot * PivotOffset - prevTargetRot * PivotOffset;
                Vector3 newP = (!vrPosInit || step.magnitude > MaxStep) ? prevP
                    : walking ? prevP + vrFrame * step     // yürüyorsun: ARCore'un adımı
                    : prevP + neck;                        // yürümüyorsun: yalnız baş dönmesi
                vrPosFix = newP - vrFrame * arPos;
                vrPrevAr = arPos;
                vrPosInit = true;
                pivotPos = newP - targetRot * PivotOffset;
            }
            else vrPosInit = false;   // takip yokken boyun modeli; dönünce bulunduğun yerden devam
            if (isUpdate && Time.time > nextVrLog)
            {
                nextVrLog = Time.time + 1f;
                if (turnRate < 0.03f)
                    Debug.Log($"GECIT VR durgun kayma={Quaternion.Angle(vrLogRot, targetRot):F2} derece/sn sensor={(UsingGameRotation ? "oyun-donme (pusulasiz)" : "eski")}");
                vrLogRot = targetRot;
            }
            targetPos = pivotPos + targetRot * PivotOffset;
            src = "sanal";
        }
        else if (tracked)
        {
            if (LockPosition)
            {
                if (lastSrc != "sabit") pivotPos = pos - rot * PivotOffset;
                targetPos = pivotPos + targetRot * PivotOffset;
                src = "sabit";
            }
            else
            {
                // El görünürken ARCore'un konum titremesini hafifçe yumuşat
                var p = arFrame * arPos;
                arSmooth = lastSrc == "arcore" && HandInFront ? Vector3.Lerp(arSmooth, p, 1f - Mathf.Exp(-Time.deltaTime * 10f)) : p;
                targetPos = hasArPos ? arSmooth : pos;
                src = "arcore";
            }
        }
        else
        {
            if (lastSrc != "takipyok") pivotPos = pos - rot * PivotOffset;
            targetPos = pivotPos + targetRot * PivotOffset;
            src = "takipyok";
        }

        // ---- Kesintisiz geçiş: kaynak değişince ya da ARCore haritasını düzeltince aradaki fark erir.
        //      Konumda yavaş (~4 sn: fark edilmez), jiroskopsuz yedek yönde yarım saniye ----
        if (!started) { rot = targetRot; pos = targetPos; started = true; }
        if (src == "arcore" && lastSrc == "arcore")
        {
            // Yürümüyorsan ARCore'un konum değişimi uygulanmaz (hata ya da elin etkisi); yalnız başın dönmesinin
            // boyun modeli kadar yer değişimi. Yürüyorsan ARCore'un adımı aynen (yön düzeltmesi değiştiyse konum sabit).
            if (!walking) posOffset += (prevTargetPos - targetPos) + (targetRot * PivotOffset - prevTargetRot * PivotOffset);
            else if (Quaternion.Angle(oldFix, arYawFix) > 0.01f) posOffset += oldFix * arPos - arYawFix * arPos;
        }
        bool jumpPos = src == lastSrc && (prevTargetPos - targetPos).magnitude > MaxStep;
        bool jumpRot = src == lastSrc && src != "arcore" && Quaternion.Angle(prevTargetRot, targetRot) > MaxTurnPerFrame;
        if (src != lastSrc || jumpPos || jumpRot)
        {
            rotOffset = rot * Quaternion.Inverse(targetRot);
            posOffset = pos - targetPos;
            if (jumpPos || jumpRot) lastJumpAt = Time.time;
            Debug.Log("GECIT DONME " + src + (jumpPos || jumpRot ? " (harita duzeltildi, yavas gecis)" : ""));
        }
        rotOffset = Quaternion.Slerp(rotOffset, Quaternion.identity, 1f - Mathf.Exp(-Time.deltaTime * 4f));
        // Konum farkı ERİTİLMEZ: eritmek kendisi kayma demekti (1 m fark = 20 sn süren kayma)
        prevTargetRot = targetRot;
        prevTargetPos = targetPos;
        lastSrc = src;
        Source = src;
        usingGyro = src == "sanal" || src == "takipyok";

        var newRot = rotOffset * targetRot;
        var newPos = targetPos + posOffset;
        if (isUpdate && tracked && src == "arcore" && !HandInFront && hasArPos && hasLastAr && (arPos - lastAr).magnitude < MaxStep)
            LearnPivot(lastRot, newRot, arPos - lastAr);
        if (hasArPos) { lastAr = arPos; hasLastAr = true; }
        if (LockPosition && !VirtualWorld.Active) UsedPosition = "sabit";

        rot = newRot;
        pos = newPos;
        lastRot = newRot;
        transform.localPosition = pos;
        transform.localRotation = rot;
    }

    static Quaternion YawOnly(Quaternion q)
    {
        var f = q * Vector3.forward;
        if (new Vector2(f.x, f.z).sqrMagnitude < 1e-6f) f = q * Vector3.up;
        return Quaternion.Euler(0f, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 0f);
    }

    // ---- Jiroskop ölçümü (telefon dururken) ----
    void LearnGyro(bool ok, Quaternion ar, Quaternion att)
    {
        if (!ok || Time.time < nextStill) return;
        bool still = Input.gyro.rotationRateUnbiased.magnitude < 0.12f && Quaternion.Angle(lastStillAr, ar) < 0.6f;
        lastStillAr = ar;
        if (!still) return;
        nextStill = Time.time + 0.15f;   // birbirine çok yakın örnekler aynı bilgi
        for (int c = 0; c < CandCount; c++)
        {
            var d = ar * Quaternion.Inverse(G(c, att));   // doğru adayda yalnız sağa-sola dönme
            float tilt = Vector3.Angle(d * Vector3.up, Vector3.up);
            var f = d * Vector3.forward;
            float yaw = Mathf.Atan2(f.x, f.z);
            const float k = 0.92f;   // unutkan ortalama (~12 örnek)
            if (stillSamples == 0) { candTilt[c] = tilt; candYawS[c] = Mathf.Sin(yaw); candYawC[c] = Mathf.Cos(yaw); }
            else
            {
                candTilt[c] = candTilt[c] * k + tilt * (1f - k);
                candYawS[c] = candYawS[c] * k + Mathf.Sin(yaw) * (1f - k);
                candYawC[c] = candYawC[c] * k + Mathf.Cos(yaw) * (1f - k);
            }
        }
        stillSamples++;
        if (stillSamples < 12) return;
        int best = -1;
        float bestScore = float.MaxValue;
        for (int c = 0; c < CandCount; c++)
        {
            float steady = Mathf.Sqrt(candYawS[c] * candYawS[c] + candYawC[c] * candYawC[c]);   // 1: yön farkı sabit
            float score = candTilt[c] + (1f - steady) * 200f;
            if (score < bestScore) { bestScore = score; best = c; }
        }
        float bestSteady = Mathf.Sqrt(candYawS[best] * candYawS[best] + candYawC[best] * candYawC[best]);
        GyroDiag = $"durgun={stillSamples} egim={candTilt[best]:F1} sabitlik={bestSteady:F3} aday={best}";
        // Eşdeğer adaylar arasında gidip gelmesin: mevcut aday hâlâ iyiyse değiştirme
        // Seçili aday hâlâ makulse değiştirme (eşdeğer adaylar arasında gidip gelmesin)
        bool currentOk = gyroCand >= 0 && candTilt[gyroCand] < 6f;
        if (candTilt[best] < 4f && bestSteady > 0.995f && !currentOk)
        {
            if (best != gyroCand)
            {
                gyroCand = best;
                GyroReady = true;
                PlayerPrefs.SetInt(GyroKey, gyroCand);
                PlayerPrefs.Save();
                Debug.Log($"GECIT JIROSKOP hazir aday={best} egim={candTilt[best]:F1} sabitlik={bestSteady:F3}");
            }
        }
    }

    // Δkonum = (R2 - R1) · o   (o: kamera ekseninde dönme noktasından kameraya)
    void LearnPivot(Quaternion r1, Quaternion r2, Vector3 step)
    {
        float ang = Quaternion.Angle(r1, r2);
        if (ang < 0.5f || ang > 15f) return;
        var m1 = Matrix4x4.Rotate(r1);
        var m2 = Matrix4x4.Rotate(r2);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                lsA[i * 3 + j] = m2[i, j] - m1[i, j];
        const float forget = 0.99f;
        for (int i = 0; i < 9; i++) lsM[i] *= forget;
        for (int i = 0; i < 3; i++) lsV[i] *= forget;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                float s = 0f;
                for (int k = 0; k < 3; k++) s += lsA[k * 3 + i] * lsA[k * 3 + j];
                lsM[i * 3 + j] += s;
            }
            float v = 0f;
            for (int k = 0; k < 3; k++) v += lsA[k * 3 + i] * step[k];
            lsV[i] += v;
        }
        var m = Matrix4x4.identity;
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] = lsM[i * 3 + j];
        if (Mathf.Abs(m.determinant) < 1e-6f) return;
        var o = m.inverse.MultiplyVector(new Vector3(lsV[0], lsV[1], lsV[2]));
        if (float.IsNaN(o.x) || o.magnitude > 0.6f) return;
        o.z = Mathf.Clamp(o.z, 0f, 0.5f);
        PivotOffset = Vector3.Lerp(PivotOffset, o, 0.05f);
    }
}
