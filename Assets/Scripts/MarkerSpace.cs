using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Merkez işareti (poster): uygulama işareti tanıdığında pencerelerin yerini ona göre kaydeder.
// Takip kopsa, yer değişse ya da uygulama kapanıp açılsa bile işaret görülünce pencereler
// bırakıldıkları yere geri oturur.
public class MarkerSpace : MonoBehaviour
{
    public static bool Visible;
    public static Pose MarkerPose;
    public static MarkerSpace Instance;

    public ARTrackedImageManager imageManager;
    public string imageName = "GecitIsaret";

    readonly Dictionary<Window, Pose> relative = new Dictionary<Window, Pose>();
    bool loaded;

    void Awake() { Instance = this; }

    void Update()
    {
        Visible = false;
        if (!WindowAnchor.UseAnchors) return;   // çapasız (offset'li) konumda merkez işareti kullanılmaz
        foreach (var img in imageManager.trackables)
        {
            if (img.referenceImage.name != imageName || img.trackingState != TrackingState.Tracking) continue;
            Visible = true;
            // Görüntünün yüzeyi: posterin üstü "up" olsun diye görüntünün ileri yönünü kullan
            MarkerPose = new Pose(img.transform.position, img.transform.rotation);
        }
        if (!Visible) return;

        if (!loaded) LoadOrCapture();

        foreach (var pair in relative)
        {
            var w = pair.Key;
            if (w == null || w.IsGrabbed) continue;
            var target = ToWorld(pair.Value);
            float drift = Vector3.Distance(w.transform.position, target.position);
            if (drift > 0.015f)
            {
                w.transform.position = Vector3.Lerp(w.transform.position, target.position, 0.25f);
                w.transform.rotation = Quaternion.Slerp(w.transform.rotation, target.rotation, 0.25f);
            }
        }
    }

    // Pencere bırakıldığında yeni yerini işarete göre kaydet
    public void Remember(Window w)
    {
        if (!Visible && !loaded) return;
        var rel = ToRelative(new Pose(w.transform.position, w.transform.rotation));
        relative[w] = rel;
        Save(w.name, rel);
    }

    void LoadOrCapture()
    {
        loaded = true;
        foreach (var w in FindObjectsByType<Window>(FindObjectsSortMode.None))
        {
            if (TryLoad(w.name, out Pose rel))
            {
                relative[w] = rel;
            }
            else
            {
                relative[w] = ToRelative(new Pose(w.transform.position, w.transform.rotation));
                Save(w.name, relative[w]);
            }
        }
        Debug.Log("GECIT ISARET bulundu, pencere sayisi=" + relative.Count);
    }

    static Pose ToRelative(Pose world)
    {
        var inv = Quaternion.Inverse(MarkerPose.rotation);
        return new Pose(inv * (world.position - MarkerPose.position), inv * world.rotation);
    }

    static Pose ToWorld(Pose rel) =>
        new Pose(MarkerPose.position + MarkerPose.rotation * rel.position, MarkerPose.rotation * rel.rotation);

    static void Save(string key, Pose p)
    {
        PlayerPrefs.SetString("isaret_" + key, JsonUtility.ToJson(new SavedPose { p = p.position, r = p.rotation }));
        PlayerPrefs.Save();
    }

    static bool TryLoad(string key, out Pose pose)
    {
        pose = default;
        var json = PlayerPrefs.GetString("isaret_" + key, "");
        if (string.IsNullOrEmpty(json)) return false;
        var s = JsonUtility.FromJson<SavedPose>(json);
        pose = new Pose(s.p, s.r);
        return true;
    }

    [System.Serializable]
    struct SavedPose { public Vector3 p; public Quaternion r; }
}
