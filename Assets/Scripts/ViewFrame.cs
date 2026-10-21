using UnityEngine;

// Kullanıcının "önü" ve "yukarısı": dik dururken yatay yön + dünya yukarısı (pencereler dik durur);
// uzanırken / tavana ya da yere bakarken gerçek bakış yönü + ekranın yukarısı (pencereler bakışa eğik kurulur).
// Kullanıcı: yatınca her şey ayak ucunda kalıyor, çizgi hep aşağı bakıyor.
public static class ViewFrame
{
    const float TiltDeg = 35f;   // baş bu kadar yukarı/aşağı eğikse "uzanmış" say

    public static bool Tilted(Transform head) => Mathf.Abs(head.forward.y) > Mathf.Sin(TiltDeg * Mathf.Deg2Rad);

    public static Vector3 Forward(Transform head)
    {
        var f = head.forward;
        if (Tilted(head)) return f.normalized;
        f.y = 0f;
        return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
    }

    public static Vector3 Up(Transform head)
    {
        if (!Tilted(head)) return Vector3.up;
        var u = Vector3.ProjectOnPlane(head.up, head.forward);
        return u.sqrMagnitude > 1e-6f ? u.normalized : Vector3.up;
    }

    public static Vector3 Right(Transform head) => Vector3.Cross(Up(head), Forward(head)).normalized;

    // pos'taki bir panel kullanıcıya dönük, yazılar okunur yönde
    public static Quaternion Facing(Vector3 pos, Transform head)
    {
        var look = pos - head.position;
        if (Tilted(head)) return look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Up(head)) : Quaternion.identity;
        look.y = 0f;
        return look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity;
    }

    // Bakış yönüyle hedef arasındaki açı (derece)
    public static float AngleFromView(Transform head, Vector3 pos)
    {
        if (!Tilted(head))
        {
            var a = head.forward; a.y = 0f;
            var b = pos - head.position; b.y = 0f;
            if (a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f) return Vector3.Angle(a, b);
        }
        return Vector3.Angle(head.forward, pos - head.position);
    }

    // Önündeki boş yer: önce tam karşı, dolu ise sağ / sol (açık pencerelerle üst üste binmesin), aynı uzaklıkta yay üstünde
    public static Vector3 FreeSpot(Transform head, float distance, float stepDeg, System.Func<Vector3, bool> occupied)
    {
        var fwd = Forward(head);
        var up = Up(head);
        var down = -up * 0.05f;
        float[] steps = { 0f, 1f, -1f, 2f, -2f };
        foreach (var k in steps)
        {
            var p = head.position + Quaternion.AngleAxis(k * stepDeg, up) * fwd * distance + down;
            if (!occupied(p)) return p;
        }
        return head.position + fwd * distance + down;
    }
}
