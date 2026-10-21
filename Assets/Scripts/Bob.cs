using UnityEngine;

// Nesneyi yavaşça yukarı aşağı süzdürür.
public class Bob : MonoBehaviour
{
    public float amplitude = 0.1f;
    public float speed = 1f;
    Vector3 start;
    float phase;

    void Start()
    {
        start = transform.localPosition;
        phase = Random.value * 10f;
    }

    void Update()
    {
        transform.localPosition = start + Vector3.up * Mathf.Sin((Time.time + phase) * speed) * amplitude;
    }
}
