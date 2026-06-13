using System;
using UnityEngine;

/// <summary>
/// Drone laser attack. Drop this on the Drone enemy.
/// Reads range, fires at player, emits event for audio.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public class LaserBehaviour : MonoBehaviour
{
    [Header("Laser")]
    public LineRenderer LaserPrefab;
    public Transform    FirePoint;
    public float        AttackRange          = 30f;
    public float        DamagePerSecond      = 15f;
    public LayerMask    ObstacleLayers;
    public float        TargetHeightOffset   = 0.5f;

    public event Action<bool> OnLaserToggled;

    private LineRenderer _laser;
    private bool         _active;
    private EnemyBrain   _brain;

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();
        _brain.OnStateChanged += OnStateChanged;

        if (LaserPrefab != null)
        {
            _laser = Instantiate(LaserPrefab, transform);
            _laser.positionCount = 2;
            _laser.useWorldSpace = true;
            _laser.enabled       = false;
        }
    }

    private void OnDestroy() => _brain.OnStateChanged -= OnStateChanged;

    private void LateUpdate()
    {
        if (_brain.State != EnemyState.Aggro) { SetLaser(false); return; }
        if (PlayerHealth.Transform == null)       { SetLaser(false); return; }

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > AttackRange) { SetLaser(false); return; }

        SetLaser(true);
        UpdateBeam();
    }

    private void UpdateBeam()
    {
        if (_laser == null || FirePoint == null) return;

        Vector3 origin = FirePoint.position;
        Vector3 target = PlayerHealth.Transform.position + Vector3.up * TargetHeightOffset;
        Vector3 dir    = (target - origin).normalized;

        _laser.SetPosition(0, origin);

        if (Physics.Raycast(origin, dir, out RaycastHit hit, AttackRange, ObstacleLayers))
        {
            _laser.SetPosition(1, hit.point);
            if (hit.collider.CompareTag("Player")) DealDamage();
        }
        else
        {
            _laser.SetPosition(1, target);
            if (Physics.Raycast(origin, dir, out RaycastHit ph, AttackRange))
                if (ph.collider.CompareTag("Player")) DealDamage();
        }
    }

    private void DealDamage() => PlayerHealth.Instance?.TakeDamage(DamagePerSecond * Time.deltaTime);

    private void SetLaser(bool on)
    {
        if (_active == on) return;
        _active = on;
        if (_laser != null) _laser.enabled = on;
        OnLaserToggled?.Invoke(on);
    }

    private void OnStateChanged(EnemyState state)
    {
        if (state == EnemyState.Dead || state == EnemyState.Staggered)
            SetLaser(false);
    }

    private void OnDisable() => SetLaser(false);
}