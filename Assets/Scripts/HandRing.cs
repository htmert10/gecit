using UnityEngine;

// Eli saran halka (Meta tarzı geri bildirim): el görününce elin çevresinde ince bir halka belirir, çubuk
// (ışın) halkanın tepesinden çıkar. El modunda parmaklar birleşmeye yaklaştıkça halka daralıp parlar,
// tıklayınca maviye döner. Kumanda modunda OK'a basınca parlar.
[RequireComponent(typeof(LineRenderer))]
public class HandRing : MonoBehaviour
{
    public Camera arCamera;
    public HandTracker tracker;
    const int Segments = 48;

    LineRenderer line;
    float alpha;
    readonly Vector3[] points = new Vector3[Segments];

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.loop = true;
        line.positionCount = Segments;
        line.useWorldSpace = true;
        line.widthMultiplier = 0.0022f;
        line.numCornerVertices = 2;
    }

    void LateUpdate()
    {
        bool visible = HandTracker.Visible && HandTracker.HandClicks;   // kumanda modunda halka yok (görseldeki gibi sade)
        alpha = Mathf.MoveTowards(alpha, visible ? 1f : 0f, Time.deltaTime * (visible ? 6f : 10f));
        line.enabled = alpha > 0.01f;
        if (!line.enabled) return;

        float pinch = HandTracker.HandClicks ? HandTracker.PinchAmount : 0f;
        bool pressed = PointerInput.Pressed && PointerInput.Source == "el";
        float r = HandTracker.RingRadius * (pressed ? 0.85f : Mathf.Lerp(1f, 0.88f, pinch));
        var c = HandTracker.RingCenter;
        float aspect = arCamera.aspect;
        float depth = tracker.aimDepth;
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            var vp = new Vector3(c.x + Mathf.Cos(a) * r / aspect, c.y + Mathf.Sin(a) * r, depth);
            points[i] = arCamera.ViewportToWorldPoint(vp);
        }
        line.SetPositions(points);

        var col = pressed ? new Color(0.45f, 0.7f, 1f, 0.95f) : Color.Lerp(new Color(1f, 1f, 1f, 0.55f), new Color(0.8f, 0.9f, 1f, 0.95f), pinch);
        col.a *= alpha;
        line.startColor = line.endColor = col;
        line.widthMultiplier = pressed ? 0.0034f : Mathf.Lerp(0.0022f, 0.003f, pinch);
    }
}
