using UnityEngine;

// Sanal dünyadaki eller (iki el): iskeletli el modeli (WebXR Input Profiles "generic-hand", MIT lisansı).
// Kemik boyları MODELİN KENDİ boyunda kalır, yalnız eklemler döner (önceden kemikler doğrudan kameranın bulduğu
// noktalara çekiliyordu; parmaklar uzayıp kısalıyor, eklemlerde kırılıp kaba görünüyordu — kullanıcı şikâyeti):
//  - bilek: elin yeri + avucun yönü (bilekten orta parmak köküne, elin sırtı)
//  - avuç kemikleri: bileğe bağlı, modeldeki gibi
//  - parmak kemikleri: kameranın bulduğu eklemden bir sonrakine doğru döner, uzunluğu modelinki
// Yalnız sanal dünyada ve el modunda görünür (kumanda modunda kumanda modeli çıkar).
public class ModelHand : MonoBehaviour
{
    public GameObject leftModel, rightModel;
    public Material material;   // Gecit/QuestHand
    public float shrink = 0.92f;
    const float Size = 0.92f;   // sanal el gerçeğinden biraz küçük (kullanıcı isteği)

    static readonly string[] Names =
    {
        "wrist",
        "thumb-metacarpal", "thumb-phalanx-proximal", "thumb-phalanx-distal", "thumb-tip",
        "index-finger-metacarpal", "index-finger-phalanx-proximal", "index-finger-phalanx-intermediate", "index-finger-phalanx-distal", "index-finger-tip",
        "middle-finger-metacarpal", "middle-finger-phalanx-proximal", "middle-finger-phalanx-intermediate", "middle-finger-phalanx-distal", "middle-finger-tip",
        "ring-finger-metacarpal", "ring-finger-phalanx-proximal", "ring-finger-phalanx-intermediate", "ring-finger-phalanx-distal", "ring-finger-tip",
        "pinky-finger-metacarpal", "pinky-finger-phalanx-proximal", "pinky-finger-phalanx-intermediate", "pinky-finger-phalanx-distal", "pinky-finger-tip",
    };
    // Her model kemiğinin karşılığı olan MediaPipe noktası (avuç kemiklerinde bilek)
    static readonly int[] Src = { 0, 1, 2, 3, 4, 0, 5, 6, 7, 8, 0, 9, 10, 11, 12, 0, 13, 14, 15, 16, 0, 17, 18, 19, 20 };

    class Rig
    {
        public GameObject go;
        public bool isLeft;
        public readonly Transform[] bones = new Transform[25];
        public readonly Quaternion[] offset = new Quaternion[25];   // (çerçeve)^-1 * kemik, dinlenme duruşunda
        public readonly Vector3[] restLocal = new Vector3[25];      // bilek çerçevesinde dinlenme konumu
        public readonly float[] length = new float[25];             // kemikten bir sonrakine (modelde)
        public bool ok;
    }

    // [el yuvası][0 sol model, 1 sağ model]
    readonly Rig[,] rigs = new Rig[2, 2];
    readonly Rig[] shown = new Rig[2];
    readonly float[] alpha = new float[2];
    readonly Material[] mats = new Material[2];
    readonly Vector3[] j = new Vector3[21];
    readonly Vector3[] rest = new Vector3[25];

    static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    static readonly int WristId = Shader.PropertyToID("_Wrist");
    static readonly int DirId = Shader.PropertyToID("_Dir");
    static readonly int TipAId = Shader.PropertyToID("_TipA");
    static readonly int TipBId = Shader.PropertyToID("_TipB");
    static readonly int PinchId = Shader.PropertyToID("_Pinch");

    void Start()
    {
        for (int slot = 0; slot < 2; slot++)
        {
            mats[slot] = new Material(material);
            rigs[slot, 0] = Make(leftModel, true, mats[slot]);
            rigs[slot, 1] = Make(rightModel, false, mats[slot]);
        }
    }

    Rig Make(GameObject prefab, bool isLeft, Material mat)
    {
        var rig = new Rig { isLeft = isLeft };
        if (prefab == null) return rig;
        rig.go = Instantiate(prefab, transform);
        rig.go.name = isLeft ? "Sol El Modeli" : "Sag El Modeli";
        rig.go.transform.localPosition = Vector3.zero;
        rig.go.transform.localRotation = Quaternion.identity;
        rig.go.transform.localScale = Vector3.one;
        var all = rig.go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < 25; i++)
            foreach (var t in all)
                if (t.name == Names[i]) { rig.bones[i] = t; break; }
        foreach (var smr in rig.go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.receiveShadows = false;
        }
        rig.ok = System.Array.TrueForAll(rig.bones, b => b != null);
        if (rig.ok)
        {
            foreach (var b in rig.bones) b.localScale = Vector3.one * Size;
            for (int i = 0; i < 25; i++) rest[i] = rig.bones[i].position;
            var dorsal = Dorsal25(rest, isLeft);
            var wristFrame = Frame(rest[11] - rest[0], dorsal);
            for (int i = 0; i < 25; i++)
            {
                rig.offset[i] = Quaternion.Inverse(Frame(BoneDir(rest, i), dorsal)) * rig.bones[i].rotation;
                rig.restLocal[i] = Quaternion.Inverse(wristFrame) * (rest[i] - rest[0]) * Size;
                rig.length[i] = IsTip(i) ? 0f : (rest[i + 1] - rest[i]).magnitude * Size;
            }
        }
        else Debug.Log("GECIT ELMODELI kemik eksik " + rig.go.name);
        rig.go.SetActive(false);
        return rig;
    }

    static bool IsTip(int i) => i % 5 == 4 && i > 0;
    static bool IsMeta(int i) => i == 5 || i == 10 || i == 15 || i == 20;

    // Elin sırtı (25 eklemden): (işaret kökü - serçe kökü) x (bilek -> orta kök); sol el modelinde işaret ters
    static Vector3 Dorsal25(Vector3[] q, bool isLeft)
    {
        var f = Vector3.Cross(q[6] - q[21], q[11] - q[0]);
        if (f.sqrMagnitude < 1e-12f) return Vector3.up;
        return isLeft ? -f.normalized : f.normalized;
    }

    static Vector3 BoneDir(Vector3[] q, int i)
    {
        if (i == 0) return q[11] - q[0];
        if (IsTip(i)) return q[i] - q[i - 1];
        return q[i + 1] - q[i];
    }

    // -Z bir sonraki ekleme doğru, +Y elin sırtı
    static Quaternion Frame(Vector3 dir, Vector3 dorsal)
    {
        if (dir.sqrMagnitude < 1e-10f) dir = Vector3.forward;
        var up = Vector3.ProjectOnPlane(dorsal, dir);
        if (up.sqrMagnitude < 1e-10f) up = Vector3.up;
        return Quaternion.LookRotation(-dir.normalized, up.normalized);
    }

    void LateUpdate()
    {
        bool baseWant = VirtualWorld.Active && HandTracker.HandClicks;
        UpdateSlot(0, baseWant && HandTracker.Visible, HandTracker.ShapeJoints, HandTracker.Handedness,
            HandTracker.HandClicks ? HandTracker.PinchAmount : 0f);
        UpdateSlot(1, baseWant && HandTracker.Second.Visible, HandTracker.Second.ShapeJoints, HandTracker.Second.Handedness,
            PointerInput.Second.PinchAmount);
    }

    void UpdateSlot(int slot, bool want, Vector3[] src, string handedness, float pinch)
    {
        alpha[slot] = Mathf.MoveTowards(alpha[slot], want ? 1f : 0f, Time.deltaTime * (want ? 6f : 8f));
        // "sag" etiketli el, avuç kuralımıza göre sol el modelinin yönelimine sahip (HandTracker'daki avuç yönü)
        var rig = rigs[slot, handedness == "sag" ? 0 : 1];
        var cur = shown[slot];
        if (cur != null && (cur != rig || alpha[slot] <= 0.01f) && cur.go != null) { cur.go.SetActive(false); shown[slot] = null; }
        if (alpha[slot] <= 0.01f || rig == null || !rig.ok) return;
        if (shown[slot] != rig) { rig.go.SetActive(true); shown[slot] = rig; }
        Pose(rig, src, mats[slot], alpha[slot], pinch);
    }

    void Pose(Rig rig, Vector3[] src, Material mat, float a, float pinch)
    {
        var c = (src[0] + src[5] + src[9] + src[13] + src[17]) * 0.2f;
        for (int i = 0; i < 21; i++) j[i] = c + (src[i] - c) * shrink;

        // Bilek: yer ve avucun yönü
        var dorsal = Vector3.Cross(j[5] - j[17], j[9] - j[0]);
        dorsal = dorsal.sqrMagnitude > 1e-12f ? (rig.isLeft ? -dorsal.normalized : dorsal.normalized) : Vector3.up;
        var wristFrame = Frame(j[9] - j[0], dorsal);
        var wristPos = j[0];
        var bones = rig.bones;
        bones[0].SetPositionAndRotation(wristPos, Frame(j[9] - j[0], dorsal) * rig.offset[0]);

        // Parmaklar: zincir halinde, modelin kemik boylarıyla
        for (int f = 0; f < 5; f++)
        {
            int start = 1 + f * 5 - (f > 0 ? 1 : 0);   // baş parmak 1..4, diğerleri 5..9, 10..14, ...
            int end = f == 0 ? 4 : start + 4;
            var p = wristPos + wristFrame * rig.restLocal[start];
            for (int i = start; i <= end; i++)
            {
                Vector3 dir;
                if (IsMeta(i)) dir = wristFrame * (rig.restLocal[i + 1] - rig.restLocal[i]);   // avuç: bileğe bağlı
                else if (IsTip(i)) dir = bones[i - 1].rotation * Quaternion.Inverse(rig.offset[i - 1]) * Vector3.back;
                else dir = j[Src[i + 1]] - j[Src[i]];
                if (dir.sqrMagnitude < 1e-10f) dir = wristFrame * Vector3.back;
                bones[i].SetPositionAndRotation(p, Frame(dir, dorsal) * rig.offset[i]);
                p += dir.normalized * rig.length[i];
            }
        }

        mat.SetFloat(AlphaId, a);
        mat.SetVector(WristId, wristPos);
        mat.SetVector(DirId, (j[9] - j[0]).normalized);
        mat.SetVector(TipAId, bones[4].position);
        mat.SetVector(TipBId, bones[9].position);
        mat.SetFloat(PinchId, Mathf.InverseLerp(0.3f, 1f, pinch));
    }

    // Editör önizlemesi
    public void BuildNow() { Start(); }
    public void PoseNow(bool rightHand)
    {
        var rig = rigs[0, rightHand ? 0 : 1];
        if (rig == null || !rig.ok) return;
        rig.go.SetActive(true);
        Pose(rig, HandTracker.ShapeJoints, mats[0], 1f, 0f);
    }
}
