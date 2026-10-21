using UnityEngine;

// Pencereleri "arkandan yumuşakça takip" ettiren gövde çerçevesi (Quest'teki tembel takip gibi).
//  - Başını 30°'den fazla çevirince çerçeve yumuşakça o yöne döner (Quaternion.Slerp); küçük baş hareketlerinde
//    yerinde kalır, ekranlar seninle titremez. Hızlı çapraz dönüşlerde de hedef her karede yumuşatılır, sıçramaz.
//  - 1 m'den fazla yürüyünce seninle gelir (Vector3.Lerp); ekrana birkaç adım yaklaşabilirsin.
//  - Uzanırken (baş çok yukarı/aşağı) çerçeve bakış yönüne eğilir.
// Ekranlar kameranın alt nesnesi DEĞİL: her ekran bu çerçeveye göre yerini (FollowItem) korur ve her karede hedefe
// yumuşakça gider. Kamera takibi sıçrasa da ekranlar zıplamaz, yumuşakça yerine gelir.
// VARSAYILAN KAPALI: ekranlar haritaya (odaya) sabit (kullanıcı: "sabit kalacak haritada"). Takip menüden açılır. Sinema salonunda takip yok (perde salona sabit).
[DefaultExecutionOrder(-100)]   // kamera duruşundan (-900) sonra, ekranlardan önce
public class FollowFrame : MonoBehaviour
{
    public static FollowFrame Instance;
    public static bool Enabled = false;
    const string Key = "pencere_takip2";   // eski anahtar varsayılan açıktı

    public Transform head;
    public float turnThreshold = 30f;   // bu kadar dönünce çerçeve döner
    public float turnSpeed = 3f;        // dönüş yumuşaklığı
    public float moveThreshold = 1f;    // bu kadar uzaklaşınca çerçeve gelir
    public float moveSpeed = 3f;        // yürüyünce takip yumuşaklığı

    Vector3 pos;
    Quaternion rot = Quaternion.identity;
    bool init, turning, moving;

    public Quaternion Rotation => rot;

    public static bool Active =>
        Enabled && Instance != null && Instance.init &&
        !(VirtualWorld.Active && VirtualWorld.Current == VirtualWorld.Env.Sinema);

    void Awake()
    {
        Instance = this;
        Enabled = PlayerPrefs.GetInt(Key, 0) == 1;
    }

    public static void SetEnabled(bool on)
    {
        Enabled = on;
        PlayerPrefs.SetInt(Key, on ? 1 : 0);
        PlayerPrefs.Save();
        if (on && Instance != null) Instance.SnapToHead();
        foreach (var f in FollowItem.All) f.Recapture();   // bulundukları yerden devam
        Debug.Log("GECIT TAKIP " + (on ? "acik (ekranlar arkandan gelir)" : "kapali (ekranlar odaya sabit)"));
    }

    Quaternion HeadFrame()
    {
        var f = ViewFrame.Forward(head);
        return Quaternion.LookRotation(f, ViewFrame.Up(head));
    }

    void Update()
    {
        if (head == null) return;
        var target = HeadFrame();
        if (!init) { SnapToHead(); init = true; return; }
        float ang = Quaternion.Angle(rot, target);
        if (ang > turnThreshold) turning = true;
        if (turning && ang < 1f) turning = false;
        float dist = Vector3.Distance(pos, head.position);
        if (dist > moveThreshold || turning) moving = true;   // dönerken merkez de başa gelsin
        if (moving && dist < 0.02f) moving = false;
        float dt = Time.deltaTime;
        if (turning) rot = Quaternion.Slerp(rot, target, 1f - Mathf.Exp(-dt * turnSpeed));
        if (moving) pos = Vector3.Lerp(pos, head.position, 1f - Mathf.Exp(-dt * moveSpeed));
    }

    // Hemen önüne al (menü "Önüme getir")
    public void SnapToHead()
    {
        if (head == null) return;
        pos = head.position;
        rot = HeadFrame();
        turning = moving = false;
    }

    public Vector3 ToWorld(Vector3 local) => pos + rot * local;
    public Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(rot) * (world - pos);
}

// Takip eden ekran: çerçeveye göre yerini saklar, her karede hedefe Lerp/Slerp ile gider.
// Ekran elle taşınırken (Hold) takip durur; bırakınca yeni yeri saklanır.
public class FollowItem : MonoBehaviour
{
    public static readonly System.Collections.Generic.List<FollowItem> All = new System.Collections.Generic.List<FollowItem>();
    public float smooth = 8f;

    Vector3 localPos;
    Quaternion localRot = Quaternion.identity;
    bool captured, held;

    void OnEnable() { All.Add(this); captured = false; }
    void OnDisable() { All.Remove(this); }

    // Elle taşıma başladı / bitti
    public void Hold(bool on)
    {
        held = on;
        captured = false;
    }

    // Ekran kodla yeni yere kondu: o yeri sakla
    public void Recapture() { captured = false; }

    void LateUpdate()
    {
        var f = FollowFrame.Instance;
        if (held || !FollowFrame.Active) { captured = false; return; }
        if (!captured)
        {
            localPos = f.ToLocal(transform.position);
            localRot = Quaternion.Inverse(f.Rotation) * transform.rotation;
            captured = true;
            return;
        }
        float k = 1f - Mathf.Exp(-Time.deltaTime * smooth);
        var p = f.ToWorld(localPos);
        var r = f.Rotation * localRot;
        if ((transform.position - p).sqrMagnitude < 1e-8f && Quaternion.Angle(transform.rotation, r) < 0.01f) return;
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, p, k), Quaternion.Slerp(transform.rotation, r, k));
    }
}
