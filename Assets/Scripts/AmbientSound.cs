using UnityEngine;

// Sanal dünyanın ortam sesi: gece kurbağa + cırcır böceği, gündüz kuşlar, sahil dalgalar, uzay derin uğultu
// (kodla üretilir). Ortam değişince yumuşak geçiş; odaya dönünce susar. Kaynaklar: Assets/Sounds_KAYNAKLAR.txt
public class AmbientSound : MonoBehaviour
{
    static AmbientSound instance;
    static readonly string[] Files = { "gece", "gunduz", null, "sahil", "" };   // "" = sessiz (sinema)
    static readonly float[] Volume = { 0.55f, 0.45f, 0.35f, 0.6f, 0f };

    AudioSource a, b;      // çapraz geçiş için iki kaynak
    AudioSource current;
    float target;
    AudioClip spaceClip;

    static AmbientSound Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("Ortam Sesi");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<AmbientSound>();
            }
            return instance;
        }
    }

    void Awake()
    {
        a = Make();
        b = Make();
    }

    AudioSource Make()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.volume = 0f;
        return s;
    }

    public static void Play(int env) => Instance.Switch(env);
    public static void Stop() => Instance.Switch(-1);

    void Switch(int env)
    {
        AudioClip clip = null;
        if (env >= 0)
        {
            if (Files[env] == "") clip = null;
            else if (Files[env] != null) clip = Resources.Load<AudioClip>("Ses/" + Files[env]);
            else clip = spaceClip != null ? spaceClip : (spaceClip = SpaceDrone());
        }
        var next = current == a ? b : a;
        if (clip != null)
        {
            next.clip = clip;
            next.volume = 0f;
            next.time = Random.Range(0f, Mathf.Max(0f, clip.length - 1f));   // her girişte farklı yerden
            next.Play();
            target = Volume[env];
            current = next;
        }
        else current = null;
    }

    void Update()
    {
        float k = Time.deltaTime / 1.2f;   // ~1 sn geçiş
        foreach (var s in new[] { a, b })
        {
            if (s == null) continue;
            float want = s == current ? target : 0f;
            s.volume = Mathf.MoveTowards(s.volume, want, k);
            if (s != current && s.volume <= 0f && s.isPlaying) s.Stop();
        }
    }

    // Uzay: derin, yavaş nefes alan uğultu + çok hafif "uzay rüzgarı" (kahverengi gürültü), dikişsiz döngü
    static AudioClip SpaceDrone()
    {
        const int rate = 22050;
        const float seconds = 24f;
        int n = (int)(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(7);
        float brown = 0f;
        // Döngüde tam tur atan frekanslar (dikiş olmasın): f * seconds tam sayı
        float[] f = { 55f, 82.5f, 110.25f, 164.75f };
        float[] amp = { 0.22f, 0.12f, 0.07f, 0.035f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float breath = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * t / seconds * 2f);
            float s = 0f;
            for (int k = 0; k < f.Length; k++)
                s += amp[k] * Mathf.Sin(2f * Mathf.PI * f[k] * t + k) * (0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * t / seconds * (k + 1)));
            brown = Mathf.Clamp(brown + ((float)rng.NextDouble() * 2f - 1f) * 0.02f, -1f, 1f) * 0.998f;
            data[i] = (s * breath + brown * 0.35f) * 0.8f;
        }
        // Kahverengi gürültünün başı ile sonu arasında yumuşak geçiş (dikişsiz)
        int fade = rate;
        for (int i = 0; i < fade; i++)
        {
            float w = i / (float)fade;
            data[i] = data[i] * w + data[n - fade + i] * (1f - w);
        }
        var clip = AudioClip.Create("Uzay Ugultusu", n - fade, 1, rate, false);
        var trimmed = new float[n - fade];
        System.Array.Copy(data, 0, trimmed, 0, n - fade);
        clip.SetData(trimmed, 0);
        return clip;
    }
}
