using UnityEngine;

/// <summary>
/// Visual-only hover bob and spin for drone enemies.
/// Reads EnemyHealth to stop on death automatically.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class HoverAnimation : MonoBehaviour
{
    [Header("Hover")]
    public float HoverSpeed  = 2f;
    public float HoverAmount = 0.2f;

    [Header("Spin")]
    public Vector3 SpinSpeed = new Vector3(0, 50f, 0);

    [Header("Core")]
    public Transform CoreVisual;
    public float     CoreSpinSpeed = 150f;

    private EnemyHealth _health;
    private Vector3     _startLocalPos;
    private float       _hoverTimer;
    private Vector3     _recoilOffset;

    private void Awake()
    {
        _health        = GetComponent<EnemyHealth>();
        _startLocalPos = transform.localPosition;
        _hoverTimer    = Random.Range(0f, 100f);

        _health.OnDied += _ => enabled = false;
    }

    private void Update()
    {
        _hoverTimer += Time.deltaTime * HoverSpeed;
        float y = Mathf.Sin(_hoverTimer) * HoverAmount;
        transform.localPosition = _startLocalPos + new Vector3(0, y, 0) + _recoilOffset;

        transform.Rotate(SpinSpeed * Time.deltaTime);
        CoreVisual?.Rotate(Vector3.up * CoreSpinSpeed * Time.deltaTime);

        _recoilOffset = Vector3.MoveTowards(_recoilOffset, Vector3.zero, Time.deltaTime * 5f);
    }

    public void PlayFireRecoil() => _recoilOffset = -transform.forward * 0.15f;
}