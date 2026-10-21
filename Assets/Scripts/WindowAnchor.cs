using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Pencereyi ARCore çapasına (anchor) bağlar: telefon odayı yeniden tanıdığında pencere doğru yerine oturur.
// Taşırken askıya alınır (Suspend), bırakınca yeni konumda yeniden bağlanır (Resume).
public class WindowAnchor : MonoBehaviour
{
    public ARAnchorManager anchorManager;
    public static bool UseAnchors = false;
    public bool IsAnchored => current != null;

    ARAnchor current;
    bool pending;
    bool suspended;
    float readySince = -1f;
    FollowItem follow;

    void Awake()
    {
        follow = GetComponent<FollowItem>();
        if (follow == null) follow = gameObject.AddComponent<FollowItem>();
    }

    void Update()
    {
        // Çapa yok: ARCore haritası düzeltilirken pencereler kaymasın (kayıtta 1,7 m kaydı)
        if (!UseAnchors) { if (current != null) Detach(); return; }
        bool tracking = ARSession.state == ARSessionState.SessionTracking;
        if (!tracking) { readySince = -1f; return; }
        if (readySince < 0f) readySince = Time.time;
        if (!suspended && current == null && !pending && Time.time - readySince > 2f) Anchor();
    }

    public void Suspend()
    {
        suspended = true;
        if (follow != null) follow.Hold(true);
        Detach();
    }

    public void Resume()
    {
        suspended = false;
        if (follow != null) follow.Hold(false);
        if (UseAnchors) Anchor();
    }

    void Detach()
    {
        if (current == null) return;
        transform.SetParent(null, true);
        anchorManager.TryRemoveAnchor(current);
        current = null;
    }

    async void Anchor()
    {
        Detach();
        pending = true;
        var result = await anchorManager.TryAddAnchorAsync(new Pose(transform.position, transform.rotation));
        pending = false;
        if (suspended) { if (result.status.IsSuccess() && result.value != null) anchorManager.TryRemoveAnchor(result.value); return; }
        if (result.status.IsSuccess() && result.value != null)
        {
            current = result.value;
            transform.SetParent(current.transform, true);
        }
    }
}
