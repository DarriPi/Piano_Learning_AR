using UnityEngine;

public class FallingNote : MonoBehaviour
{
    public float fallSpeed = 0.5f;

    void Update()
    {
        transform.Translate(Vector3.down * fallSpeed * Time.deltaTime);
    }
}