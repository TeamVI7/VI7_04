using System;
using UnityEngine;

public class EnemyDrone : EnemyFodder
{
    [Header("Drone — Laser")]
    public LineRenderer LaserPrefab;
    public float DamagePerSecond = 15f;
    public float AttackRange     = 30f;
    public LayerMask ObstacleLayers;
    public float LaserTargetHeightOffset = 0.5f;

    // FIX: C# event — EnemySoundController subscribes instead of polling via reflection
    public event Action<bool> OnLaserToggled;

    private LineRenderer _spawnedLaser;
    private bool         _isLaserActive;

    protected override float GetAttackRange() => AttackRange;

    protected override void Awake()
    {
        base.Awake();
        if (LaserPrefab != null)
        {
            _spawnedLaser = Instantiate(LaserPrefab, transform);
            _spawnedLaser.positionCount = 2;
            _spawnedLaser.useWorldSpace = true;
            _spawnedLaser.enabled       = false;
        }
    }

    private void LateUpdate()
    {
        if (_nav == null || !_nav.enabled)
        {
            TurnOffLaser();
            return;
        }

        if (_isLaserActive)
        {
            if (_player != null) UpdateLaserBeam();
            else TurnOffLaser();
        }
    }

    protected override void TryFireProjectile(float dist)
    {
        if (dist > AttackRange) { TurnOffLaser(); return; }
        if (!_isLaserActive) TurnOnLaser();
    }

    private void UpdateLaserBeam()
    {
        if (_spawnedLaser == null || FirePoint == null || _player == null) return;

        Vector3 origin = FirePoint.position;
        _spawnedLaser.SetPosition(0, origin);

        Vector3 targetCenter = _player.position + Vector3.up * LaserTargetHeightOffset;
        Vector3 direction    = (targetCenter - origin).normalized;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, AttackRange, ObstacleLayers))
        {
            _spawnedLaser.SetPosition(1, hit.point);
            if (hit.collider.CompareTag("Player")) ApplyDamage(hit.collider.gameObject);
        }
        else
        {
            _spawnedLaser.SetPosition(1, targetCenter);
            if (Physics.Raycast(origin, direction, out RaycastHit playerHit, AttackRange))
                if (playerHit.collider.CompareTag("Player"))
                    ApplyDamage(playerHit.collider.gameObject);
        }
    }

    private void ApplyDamage(GameObject playerObj)
    {
        if (playerObj.TryGetComponent(out PlayerHealth health))
            health.TakeDamage(DamagePerSecond * Time.deltaTime);
    }

    private void TurnOnLaser()
    {
        _isLaserActive = true;
        if (_spawnedLaser != null) _spawnedLaser.enabled = true;
        OnLaserToggled?.Invoke(true);
    }

    private void TurnOffLaser()
    {
        if (!_isLaserActive) return;
        _isLaserActive = false;
        if (_spawnedLaser != null) _spawnedLaser.enabled = false;
        OnLaserToggled?.Invoke(false);
    }

    protected override void Die(Vector3 deathVelocityDirection)
    {
        TurnOffLaser();
        var droneAnim = GetComponentInChildren<OutOfBullet.Enemy.DroneAnimation>();
        if (droneAnim != null) droneAnim.enabled = false;
        base.Die(deathVelocityDirection);
    }

    private void OnDisable() => TurnOffLaser();
}