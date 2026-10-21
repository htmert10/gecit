using UnityEngine;

// Yeni pencereler: sahnede hazır bekleyen (kapalı) yedek pencereler. Bir site açılınca boş bir yedek alınır,
// adı ve adresi verilir, önüne getirilir. Kapatınca (X) yedek yeniden boşa çıkar. Her tarayıcı penceresi
// bellek ve işlemci yediği için toplam pencere sayısı sınırlı (YouTube + Google + yedekler).
public class WindowPool : MonoBehaviour
{
    public static WindowPool Instance;

    public Transform head;
    public Window[] spares;

    int openCounter;
    readonly System.Collections.Generic.Dictionary<Window, int> openedAt = new System.Collections.Generic.Dictionary<Window, int>();

    void Awake() { Instance = this; }

    // Siteyi aç: zaten açıksa önüne getir; değilse kapalı bir pencereyi ya da yedeği kullan
    public Window OpenSite(string title, string url)
    {
        foreach (var w in Window.Windows)
            if (w.Title == title)
            {
                if (!w.IsOpen) w.Open();
                PlaceInFront(w);
                return w;
            }

        Window pick = null;
        foreach (var s in spares) if (s != null && !s.gameObject.activeSelf) { pick = s; break; }
        if (pick == null)
        {
            // Hepsi açık: en eski açılan yedeği yeni site için kullan
            int oldest = int.MaxValue;
            foreach (var s in spares)
                if (s != null && openedAt.TryGetValue(s, out int at) && at < oldest) { oldest = at; pick = s; }
        }
        if (pick == null) return null;

        pick.name = "Pencere " + title;
        var browser = pick.GetComponentInChildren<VRBrowser>(true);
        browser.windowName = title;
        browser.OpenUrl(url);
        pick.Open();
        pick.RefreshTitle();
        openedAt[pick] = ++openCounter;
        PlaceInFront(pick);
        Debug.Log("GECIT PENCERE ac " + title + " " + url);
        return pick;
    }

    void PlaceInFront(Window w)
    {
        // Önünde, başka açık pencerenin üstüne binmeyen ilk yer (karşı, sağ, sol...)
        var spot = ViewFrame.FreeSpot(head, 2.0f, 46f, p =>
        {
            foreach (var o in Window.Windows)
                if (o != w && o.IsOpen && Vector3.Distance(o.transform.position, p) < 1.2f) return true;
            return false;
        });
        w.MoveTo(spot);
    }
}
