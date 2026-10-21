using System.Collections;
using UnityEngine;

// Sinema modu: bir pencerede video tam ekran yapılınca (YouTube'un kendi tam ekran düğmesi, pencerenin
// Tam Ekran düğmesi ya da kumandada ►) etraf yumuşakça kararır, pencere 3 m öteye dev bir perde olarak geçer.
// Tam ekrandan çıkınca (◄ ya da aynı düğme) pencere eski yerine ve boyutuna döner, ortam aydınlanır.
// Sayfadaki betik bu nesneye ("Sinema") Enter/Exit mesajı gönderir.
public class CinemaMode : MonoBehaviour
{
    public static bool Active;

    public Transform head;
    public Material dimMaterial;
    public float distance = 3f;
    public float scale = 2.2f;
    [Range(0f, 1f)] public float darkness = 0.9f;

    Renderer dim;
    Window current;
    Vector3 savedPos, savedScale;
    Quaternion savedRot;
    Coroutine anim;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Start()
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(s.GetComponent<Collider>());
        s.name = "Sinema Karartmasi";
        s.transform.SetParent(head, false);
        s.transform.localScale = Vector3.one * 16f;   // 8 m yarıçap: perde (3 m) önde kalır
        dim = s.GetComponent<Renderer>();
        dim.sharedMaterial = dimMaterial;
        SetDim(0f);
    }

    // ---- Sayfadaki betikten (UnitySendMessage) ----
    public void Enter(string windowName)
    {
        // Sinema salonundaysan perde zaten dev ve kavisli: sayfanın tam ekranı pencereyi büyütmesin (kullanıcı)
        if (VirtualWorld.Active && VirtualWorld.Current == VirtualWorld.Env.Sinema) return;
        Window w = null;
        foreach (var x in Window.Windows) if (x.Title == windowName && x.IsOpen) w = x;
        if (w == null || current == w) return;
        if (current != null) Restore();
        current = w;
        savedPos = w.transform.position;
        savedRot = w.transform.rotation;
        savedScale = w.transform.localScale;

        w.MoveTo(head.position + ViewFrame.Forward(head) * distance);
        w.transform.localScale = savedScale * scale;
        Active = true;
        Fade(darkness);
        Debug.Log("GECIT SINEMA ac " + windowName);
    }

    public void Exit(string windowName)
    {
        if (current == null || current.Title != windowName) return;
        Restore();
        Active = false;
        Fade(0f);
        Debug.Log("GECIT SINEMA kapat " + windowName);
    }

    void Restore()
    {
        if (current == null) return;
        current.transform.localScale = savedScale;
        current.MoveTo(savedPos);
        current.transform.rotation = savedRot;
        current = null;
    }

    void Fade(float to)
    {
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(FadeTo(to));
    }

    IEnumerator FadeTo(float to)
    {
        float from = dimMaterial.GetColor(ColorId).a;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
        {
            SetDim(Mathf.SmoothStep(from, to, t));
            yield return null;
        }
        SetDim(to);
    }

    void SetDim(float a)
    {
        dim.enabled = a > 0.001f;
        dimMaterial.SetColor(ColorId, new Color(0f, 0f, 0f, a));
    }
}
