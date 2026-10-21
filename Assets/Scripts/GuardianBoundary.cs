using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

// Quest'teki "guardian" gibi: başlangıç noktasının etrafında güvenli bir çember.
// Çemberin dışına çıkınca VR kapanır, telefon kamerasının görüntüsü açılır.
public class GuardianBoundary : MonoBehaviour
{
    public static bool Outside;

    public Transform head;
    public Camera arCamera;
    public ARCameraBackground cameraBackground;
    public Camera leftEye;
    public Camera rightEye;
    public GameObject warning;
    public float radius = 1.5f;
    public float floorHeight = -1.5f;
    public int segments = 64;

    void Start()
    {
        var line = gameObject.AddComponent<LineRenderer>();
        line.loop = true;
        line.positionCount = segments;
        line.widthMultiplier = 0.03f;
        line.useWorldSpace = true;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = new Color(0f, 1f, 1f, 0.8f);
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, floorHeight + 0.01f, Mathf.Sin(a) * radius));
        }
        SetOutside(false);
    }

    void Update()
    {
        var p = head.position;
        bool outside = new Vector2(p.x, p.z).magnitude > radius;
        if (outside != Outside) SetOutside(outside);
    }

    void SetOutside(bool outside)
    {
        Outside = outside;
        leftEye.enabled = !outside;
        rightEye.enabled = !outside;
        cameraBackground.enabled = outside;
        arCamera.depth = outside ? 5 : -10;
        if (warning != null) warning.SetActive(outside);
    }
}
