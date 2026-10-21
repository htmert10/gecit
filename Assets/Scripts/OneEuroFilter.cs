using UnityEngine;

// "1€ filtresi": yavaş harekette titremeyi yok eder, hızlı harekette gecikme yaratmaz.
// Quest/VR el takibinde yaygın kullanılan yumuşatma yöntemi.
public class OneEuroFilter
{
    readonly float minCutoff, beta, dCutoff;
    Vector3 x, dx;
    bool init;

    public OneEuroFilter(float minCutoff = 1.2f, float beta = 0.6f, float dCutoff = 1f)
    {
        this.minCutoff = minCutoff;
        this.beta = beta;
        this.dCutoff = dCutoff;
    }

    static float Alpha(float cutoff, float dt)
    {
        float tau = 1f / (2f * Mathf.PI * cutoff);
        return 1f / (1f + tau / dt);
    }

    public void Reset() { init = false; }

    public Vector3 Filter(Vector3 value, float dt)
    {
        if (!init) { x = value; dx = Vector3.zero; init = true; return value; }
        dt = Mathf.Max(dt, 1e-4f);
        var rawDx = (value - x) / dt;
        dx = Vector3.Lerp(dx, rawDx, Alpha(dCutoff, dt));
        float cutoff = minCutoff + beta * dx.magnitude;
        x = Vector3.Lerp(x, value, Alpha(cutoff, dt));
        return x;
    }
}
