using UnityEngine;

// Çalışırken kurulan arayüzün kullandığı ortak görselleri (yuvarlak köşe) UiKit'e verir; her şeyden önce çalışır.
[DefaultExecutionOrder(-1000)]
public class UiAssets : MonoBehaviour
{
    public Sprite rounded;
    public Material holo;
    public Font font;

    void Awake() { UiKit.Rounded = rounded; UiKit.HoloBase = holo; if (font != null) UiKit.CustomFont = font; }
}
