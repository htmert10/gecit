using UnityEngine;

public class Spin : MonoBehaviour
{
    public float degreesPerSecond = 45f;

    void Update()
    {
        transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.World);
    }
}
