using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// Saat / hava paneli: havada duran küçük cam panel — büyük saat, tarih, hava durumu ve pil.
// Hava durumu Open-Meteo'dan (ücretsiz, anahtar gerektirmez) 15 dakikada bir alınır; şehir ayarlanabilir.
// Alttaki çubuktan tutup taşınabilir. Menüden açılıp kapatılır.
public class InfoPanel : MonoBehaviour
{
    public static InfoPanel Instance;

    public Transform head;
    public string city = "İstanbul";
    public float latitude = 41.01f, longitude = 28.98f;

    Transform root;
    CanvasGroup group;
    Text timeText, dateText, weatherText, batteryText;
    bool visible = true;
    float nextClock;
    string weather = "Hava durumu alınıyor...";

    static readonly string[] Days = { "Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi" };
    static readonly string[] Months = { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    const float Mpp = 0.001f;

    void Awake() { Instance = this; }

    void Start()
    {
        root = new GameObject("Saat Paneli").transform;
        var c = UiKit.WorldCanvas("Tuval", root, new Vector2(300f, 130f), Mpp);
        group = c.GetComponent<CanvasGroup>();
        var t = c.transform;
        UiKit.Box(t, "Zemin", Vector2.zero, new Vector2(300f, 130f), new Color(0.07f, 0.08f, 0.1f, 0.78f), null, 1.6f);
        UiKit.Holo(t, "Isikli Cerceve", Vector2.zero, new Vector2(300f, 130f), 25f, new Color(0.3f, 0.82f, 1f, 0.45f), 20f, 0.6f);
        timeText = UiKit.Label(t, "", 48, Color.white, new Vector2(-62f, 22f), new Vector2(170f, 60f));
        dateText = UiKit.Label(t, "", 16, new Color(1f, 1f, 1f, 0.75f), new Vector2(-62f, -20f), new Vector2(170f, 24f));
        weatherText = UiKit.Label(t, "", 17, new Color(0.85f, 0.92f, 1f, 0.95f), new Vector2(82f, 18f), new Vector2(130f, 50f));
        weatherText.horizontalOverflow = HorizontalWrapMode.Wrap;
        batteryText = UiKit.Label(t, "", 15, new Color(1f, 1f, 1f, 0.7f), new Vector2(82f, -26f), new Vector2(130f, 24f));
        MoveHandle.Create(t, new Vector2(0f, -65f - 20f), 110f, root, head);

        // Başlangıç: önünde, solda ve biraz aşağıda (pencerelerin altında, görüşü kapatmaz)
        var fwd = head.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        var left = Vector3.Cross(fwd, Vector3.up);
        root.position = head.position + fwd * 1.25f + left * 0.75f + Vector3.down * 0.42f;
        root.rotation = Quaternion.LookRotation(root.position - head.position, Vector3.up);

        StartCoroutine(WeatherLoop());
    }

    public void Toggle() { visible = !visible; }
    public bool Visible => visible;

    void Update()
    {
        float a = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.deltaTime * 5f);
        if (a != group.alpha) group.alpha = a;
        if (root.gameObject.activeSelf != a > 0.001f) root.gameObject.SetActive(a > 0.001f);

        if (Time.time < nextClock) return;
        nextClock = Time.time + 1f;
        var now = System.DateTime.Now;
        Set(timeText, now.ToString("HH:mm"));
        Set(dateText, $"{now.Day} {Months[now.Month - 1]} {Days[(int)now.DayOfWeek]}");
        Set(weatherText, weather);
        int battery = Mathf.RoundToInt(SystemInfo.batteryLevel * 100f);
        bool charging = SystemInfo.batteryStatus == BatteryStatus.Charging;
        Set(batteryText, battery > 0 ? $"Pil %{battery}{(charging ? " (şarjda)" : "")}" : "");
    }

    static void Set(Text t, string s) { if (t.text != s) t.text = s; }

    IEnumerator WeatherLoop()
    {
        while (true)
        {
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                         "&longitude=" + longitude.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                         "&current=temperature_2m,weather_code,is_day&timezone=auto";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) Parse(req.downloadHandler.text);
                else if (weather.EndsWith("...")) weather = city + "\nhava alınamadı";
            }
            yield return new WaitForSeconds(15f * 60f);
        }
    }

    // {"current":{"temperature_2m":14.3,"weather_code":3,"is_day":0,...}}
    void Parse(string json)
    {
        float temp = Number(json, "\"temperature_2m\":");
        int code = Mathf.RoundToInt(Number(json, "\"weather_code\":"));
        bool day = Number(json, "\"is_day\":") > 0.5f;
        if (float.IsNaN(temp)) return;
        weather = $"{city}\n{Mathf.RoundToInt(temp)}°C  {Describe(code, day)}";
        Debug.Log("GECIT HAVA " + weather.Replace('\n', ' '));
    }

    static float Number(string json, string key)
    {
        // "current" bloğundaki değeri al (birimler bloğunda aynı anahtar metin olarak geçer)
        int cur = json.IndexOf("\"current\":");
        int i = json.IndexOf(key, cur >= 0 ? cur : 0);
        if (i < 0) return float.NaN;
        i += key.Length;
        int j = i;
        while (j < json.Length && "-0123456789.".IndexOf(json[j]) >= 0) j++;
        return float.TryParse(json.Substring(i, j - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : float.NaN;
    }

    static string Describe(int code, bool day)
    {
        if (code == 0) return day ? "Güneşli" : "Açık";
        if (code <= 2) return "Az bulutlu";
        if (code == 3) return "Kapalı";
        if (code == 45 || code == 48) return "Sisli";
        if (code >= 51 && code <= 57) return "Çisenti";
        if (code >= 61 && code <= 67) return "Yağmurlu";
        if (code >= 71 && code <= 77) return "Karlı";
        if (code >= 80 && code <= 82) return "Sağanak";
        if (code >= 85 && code <= 86) return "Kar sağanağı";
        if (code >= 95) return "Fırtına";
        return "";
    }
}
