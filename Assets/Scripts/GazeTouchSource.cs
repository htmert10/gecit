using UnityEngine;

// El görünmüyorken işaretçi YOK (kullanıcı: ekranın ortasındaki beyaz nokta olmasın, ekrana dokunma yok):
// imleç ve ışın yalnız el görününce çıkar. El kısa süre (0,6 sn) kaybolursa imleç son yerinde bekler,
// kumandanın OK'u o sırada yine tıklar; daha uzun kaybolursa işaretçi kapanır.
[DefaultExecutionOrder(-400)]
public class GazeTouchSource : MonoBehaviour
{
    public Transform head;

    void Update()
    {
        if (HandSourceState.HasHand) return;
        if (Time.time - HandSourceState.LastSeen < 0.6f)
        {
            PointerInput.Pressed = GecitController.SelectHeld;
            return;
        }
        PointerInput.Valid = false;
        PointerInput.Pressed = false;
        PointerInput.Source = "-";
    }
}

// El takibi durumu
public static class HandSourceState
{
    public static bool HasHand;
    public static float LastSeen = -10f;
}
