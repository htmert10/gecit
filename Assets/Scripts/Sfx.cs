using UnityEngine;

// Kodla üretilen kısa sesler (tık, kesme, ıska); dosya gerektirmez.
public static class Sfx
{
    static AudioSource source;
    static AudioClip click, hover, slice, miss, release, shutter;

    static AudioSource Source
    {
        get
        {
            if (source == null)
            {
                var go = new GameObject("Sesler", typeof(AudioSource));
                Object.DontDestroyOnLoad(go);
                source = go.GetComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            return source;
        }
    }

    public static void Click() => Play(click ??= Tone("Tik", 0.035f, 1900f, 1200f, 0.35f, 0f), 0.5f);
    public static void Release() => Play(release ??= Tone("Birak", 0.03f, 1300f, 1700f, 0.25f, 0f), 0.35f);
    public static void Hover() => Play(hover ??= Tone("Uzerinde", 0.02f, 2600f, 2600f, 0.15f, 0f), 0.25f);
    public static void Slice() => Play(slice ??= Tone("Kesme", 0.22f, 900f, 180f, 0.5f, 0.55f), 0.9f);
    public static void Shutter() => Play(shutter ??= Tone("Deklansor", 0.09f, 3200f, 700f, 0.45f, 0.7f), 0.8f);
    public static void Miss() => Play(miss ??= Tone("Iska", 0.25f, 220f, 110f, 0.5f, 0.1f), 0.6f);

    static AudioClip lastClip;
    static float lastTime;

    static void Play(AudioClip clip, float volume)
    {
        // Aynı ses aynı anda iki kez (kıstırma + düğme) çalmasın
        if (clip == lastClip && Time.unscaledTime - lastTime < 0.06f) return;
        lastClip = clip;
        lastTime = Time.unscaledTime;
        Source.PlayOneShot(clip, volume);
    }

    // f0 -> f1 kayan ton + isteğe bağlı gürültü, hızla sönümlenir
    static AudioClip Tone(string name, float seconds, float f0, float f1, float amp, float noise)
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(seconds * rate);
        var data = new float[n];
        var rng = new System.Random(7);
        float phase = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = Mathf.Lerp(f0, f1, t);
            phase += 2f * Mathf.PI * f / rate;
            float env = Mathf.Min(1f, i / (rate * 0.002f)) * Mathf.Pow(1f - t, 2.5f);
            lp = Mathf.Lerp(lp, (float)(rng.NextDouble() * 2 - 1), 0.35f);
            data[i] = (Mathf.Sin(phase) * (1f - noise) + lp * noise * 1.6f) * env * amp;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
