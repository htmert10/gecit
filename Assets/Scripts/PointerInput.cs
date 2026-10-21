using UnityEngine;

// Ortak "işaretçi": ışın + basılı mı. Bakış+dokunma (GazeTouchSource) ya da el (HandTracker) doldurur.
public static class PointerInput
{
    public static bool Valid;
    public static Ray Ray;
    public static bool Pressed;
    public static Vector3 VisualOrigin;   // ışının çizildiği başlangıç (el ya da göğüs hizası)
    public static string Source = "-";

    // Kıstırma başladığı andaki "sabit" ışın: kıstırırken elin oynamasıyla tıklama kaymasın diye
    // birkaç kare önceki ışın (Meta'daki yöntem).
    public static bool HasPressRay;
    public static Ray PressRay;

    // İkinci el (sol/sağ, hangisi ikinciyse) için ayrı işaretçi
    public static readonly PointerState Second = new PointerState();
}

public class PointerState
{
    public bool Valid;
    public Ray Ray;
    public bool Pressed;
    public Vector3 VisualOrigin;
    public string Source = "-";
    public bool HasPressRay;
    public Ray PressRay;
    public bool PalmUp;
    public float PinchAmount;
}
