using UnityEngine;

[RequireComponent(typeof(TrailRenderer))]
public class BulletTracer : MonoBehaviour
{
    [Header("Tracer Settings")]
    public float Speed = 300f;
    public float MaxDistance = 200f;

    private TrailRenderer _trail;
    private Vector3 _targetPos;
    private float _traveled;

    private void Awake()
    {
        _trail = GetComponent<TrailRenderer>();
    }

    public void Fire(Vector3 origin, Vector3 target)
    {
        transform.position = origin;
        _targetPos = target;
        _traveled = 0f;

        _trail.Clear();
        gameObject.SetActive(true);
    }

    private void Update()
    {
        float step = Speed * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, _targetPos, step);
        _traveled += step;

        if (transform.position == _targetPos || _traveled >= MaxDistance)
        {
            Destroy(gameObject, _trail.time); // let trail fade before destroying
            enabled = false;
        }
    }
}