using UnityEngine;

public class MovingPlatform : MonoBehaviour
{
    [Header("Cài đặt hướng và độ xa")]
    public Vector3 distance = new Vector3(0, 5, 0); 
    
    [Header("Tốc độ di chuyển")]
    public float speed = 0.5f;

    private Vector3 startPosition;
    private Vector3 endPosition;

    void Start()
    {
        startPosition = transform.position;
        endPosition = startPosition + distance;
    }

    void Update()
    {
        float lerpValue = Mathf.PingPong(Time.time * speed, 1f);
        transform.position = Vector3.Lerp(startPosition, endPosition, lerpValue);
    }
}