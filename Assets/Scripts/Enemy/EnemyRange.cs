using System;
using UnityEngine;

/// <summary>
/// Shared gating for enemy hitscan-style attacks: Aggro-check, range-check, and a
/// fire-rate cooldown, plus a single OnFired event for EnemyAudio to hook into.
/// Concrete weapons only implement Fire() — the actual raycast/damage/trail logic.
///
/// Extracted from SMGAttackBehaviour / ShotgunAttackBehaviour, which were ~80%
/// identical boilerplate around the same "am I allowed to shoot right now" check.
///
/// NOT used by SniperAttackBehaviour (charge/lock/fire state machine — different
/// enough that forcing it through Fire() would just be an awkward override) or
/// LaserBehaviour (continuous beam, not a discrete cooldown-gated shot).
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public abstract class EnemyRangedAttackBehaviour : MonoBehaviour
{
    [Header("Base Setup")]
    public Transform FirePoint;
    public float     AttackRange = 20f;
    public float     FireRate    = 0.1f;
    public LayerMask ObstacleLayers;

    /// <summary>Fired the instant a shot is taken — EnemyAudio subscribes to this.</summary>
    public event Action OnFired;

    protected EnemyBrain Brain { get; private set; }
    private float _nextFireTimer;

    protected virtual void Awake()
    {
        Brain = GetComponent<EnemyBrain>();
    }

    protected virtual void Update()
    {
        if (Brain.State != EnemyState.Aggro) return;
        if (PlayerHealth.Transform == null)  return;
        if (FirePoint == null)               return;

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > AttackRange) return;

        _nextFireTimer += Time.deltaTime;
        if (_nextFireTimer >= FireRate)
        {
            _nextFireTimer = 0f;
            OnFired?.Invoke();
            Fire();
        }
    }

    /// <summary>Implement the actual shot here — raycast(s), damage, trail VFX.</summary>
    protected abstract void Fire();

    protected Vector3 GetTargetPoint(float heightOffset = 0.5f) =>
        PlayerHealth.Transform.position + Vector3.up * heightOffset;
}