using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(EnemyBrain))]
public abstract class EnemyRangedAttackBehaviour : MonoBehaviour
{
    [Header("Base Setup")]
    public Transform FirePoint;
    public float     AttackRange = 20f;
    public float     FireRate    = 0.1f;
    public LayerMask ObstacleLayers;

    [Header("Telegraph")]
    [Tooltip("Seconds of visible wind-up before the shot actually fires, once the fire-rate timer trips. 0 = instant/legacy hitscan.")]
    public float TelegraphTime = 0f;
    [Tooltip("Optional — enabled for the duration of the telegraph, disabled the instant the shot fires or is cancelled. Hook a muzzle glow, laser dot, aim pose, whatever reads as 'about to shoot'.")]
    public GameObject TelegraphVFX;

    /// <summary>Fired the instant a shot is taken — EnemyAudio subscribes to this.</summary>
    public event Action OnFired;
    /// <summary>Fired the instant the telegraph wind-up begins, before the shot lands.</summary>
    public event Action OnTelegraphStart;
    /// <summary>Fired if a telegraph is interrupted (target lost, left range, no longer Aggro) before it resolves into a shot.</summary>
    public event Action OnTelegraphCancelled;

    protected EnemyBrain Brain { get; private set; }
    protected bool IsTelegraphing { get; private set; }

    private float     _nextFireTimer;
    private Coroutine _telegraphRoutine;

    protected virtual void Awake()
    {
        Brain = GetComponent<EnemyBrain>();
    }

    protected virtual void Update()
    {
        if (Brain.State != EnemyState.Aggro) { CancelTelegraph(); return; }
        if (PlayerHealth.Transform == null)  { CancelTelegraph(); return; }
        if (FirePoint == null)               { CancelTelegraph(); return; }

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > AttackRange) { CancelTelegraph(); return; }

        if (IsTelegraphing) return; // wind-up coroutine already owns the shot this cycle

        _nextFireTimer += Time.deltaTime;
        if (_nextFireTimer >= FireRate)
        {
            _nextFireTimer = 0f;

            if (TelegraphTime > 0f)
                _telegraphRoutine = StartCoroutine(Co_TelegraphThenFire());
            else
                FireNow();
        }
    }

    private IEnumerator Co_TelegraphThenFire()
    {
        IsTelegraphing = true;
        if (TelegraphVFX != null) TelegraphVFX.SetActive(true);
        OnTelegraphStart?.Invoke();

        float t = 0f;
        while (t < TelegraphTime)
        {
            if (Brain.State != EnemyState.Aggro || PlayerHealth.Transform == null ||
                Vector3.Distance(transform.position, PlayerHealth.Transform.position) > AttackRange)
            {
                CancelTelegraph();
                yield break;
            }

            t += Time.deltaTime;
            yield return null;
        }

        if (TelegraphVFX != null) TelegraphVFX.SetActive(false);
        IsTelegraphing    = false;
        _telegraphRoutine = null;

        FireNow();
    }

    private void FireNow()
    {
        OnFired?.Invoke();
        Fire();
    }

    private void CancelTelegraph()
    {
        if (_telegraphRoutine != null)
        {
            StopCoroutine(_telegraphRoutine);
            _telegraphRoutine = null;
        }

        if (IsTelegraphing)
        {
            IsTelegraphing = false;
            if (TelegraphVFX != null) TelegraphVFX.SetActive(false);
            OnTelegraphCancelled?.Invoke();
        }
    }

    /// <summary>Implement the actual shot here — raycast(s), damage, trail VFX.</summary>
    protected abstract void Fire();

    protected Vector3 GetTargetPoint(float heightOffset = 0.5f) =>
        PlayerHealth.Transform.position + Vector3.up * heightOffset;
}