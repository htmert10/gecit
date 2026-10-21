using System.Collections.Generic;
using UnityEngine;

// Işınla etkileşebilen her şeyin (pencere, klavye tuşu, menü düğmesi) ortak tabanı.
// PointerManager en yakın hedefi seçer; basınca o hedef bırakılana kadar "aktif" kalır.
public abstract class PointerTarget : MonoBehaviour
{
    public static readonly List<PointerTarget> All = new List<PointerTarget>();
    public static int CurrentPointer;   // şu an çağıran işaretçi: 0 birinci el / kumanda, 1 ikinci el

    protected virtual void OnEnable() { All.Add(this); }
    protected virtual void OnDisable() { All.Remove(this); }

    public abstract bool Raycast(Ray ray, out float distance, out Vector3 point);

    // Hedefe değmiyor ama yakınında mı (çevresindeki pay): nokta ve çizgi görünmeye devam eder, tıklamaz.
    // Kullanıcı: ekrandan çıkar çıkmaz kayboluyor, nerede olduğu karışıyor.
    public virtual bool Near(Ray ray, out float distance, out Vector3 point)
    {
        distance = 0f; point = Vector3.zero;
        return false;
    }
    public virtual void Hover(bool on) { }
    public abstract void Press(Ray ray, Vector3 point);
    public virtual void Drag(Ray ray) { }
    public virtual void Release() { }

    // Taşıma/boyutlandırma sırasında imlecin nerede duracağı (yoksa null)
    public virtual Vector3? GrabPoint(Ray ray) => null;

    // Mıknatıs: ışın bu hedefe yakınsa (kenarına açısal uzaklık, derece) imleç ortasına oturur.
    // Küçük düğmeler bunu açar; pencere içeriği gibi büyük yüzeyler açmaz.
    public virtual bool Snap(Ray ray, out float angle, out float distance, out Vector3 point)
    {
        angle = distance = 0f; point = Vector3.zero;
        return false;
    }

    // RectTransform için ortak mıknatıs hesabı
    protected static bool SnapRect(RectTransform rt, Ray ray, out float angle, out float distance, out Vector3 point)
    {
        point = rt.TransformPoint(rt.rect.center);
        var to = point - ray.origin;
        distance = to.magnitude;
        angle = 180f;
        if (distance < 1e-3f || Vector3.Dot(to, ray.direction) <= 0f) return false;
        float radius = 0.5f * Mathf.Min(rt.rect.width * rt.lossyScale.x, rt.rect.height * rt.lossyScale.y);
        angle = Vector3.Angle(ray.direction, to) - Mathf.Atan2(radius, distance) * Mathf.Rad2Deg;
        return true;
    }
}
