using UnityEngine;

// Kısa dokunuşta küp grubunu kameranın baktığı yöne, 1 m ileriye taşır.
public class TapToPlace : MonoBehaviour
{
    public Transform arCamera;
    public float distance = 1.0f;
    public float maxTapSeconds = 0.4f;

    float touchStart;

    void Update()
    {
        if (Input.touchCount == 0) return;
        var t = Input.GetTouch(0);
        if (t.phase == TouchPhase.Began) touchStart = Time.time;
        if (t.phase != TouchPhase.Ended || Time.time - touchStart > maxTapSeconds) return;

        var forward = arCamera.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        transform.position = arCamera.position + forward * distance;
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
}
