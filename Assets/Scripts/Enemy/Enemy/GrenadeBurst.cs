using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Grenade burst attack. Drop on Shielder or any enemy that lobs grenades.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public class GrenadeBurstBehaviour : MonoBehaviour
{
    [Header("Grenades")]
    public GameObject GrenadePrefab;
    public Transform  FirePoint;
    public float      AttackRange    = 35f;
    public int        BurstCount     = 4;
    public float      BurstInterval  = 0.25f;
    public float      BurstCooldown  = 2.5f;
    [Range(10f, 45f)]
    public float      ArcAngle       = 25f;

    public event Action OnBurstStarted;

    private EnemyBrain _brain;
    private float      _cooldownTimer;
    private bool       _bursting;
    private Coroutine  _burstRoutine;

    // Runtime multipliers — other components (ShieldBehaviour) can boost these
    [HideInInspector] public float CooldownMultiplier = 1f;
    [HideInInspector] public float IntervalMultiplier = 1f;

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();
        _brain.OnStateChanged += OnStateChanged;
    }

    private void OnDestroy() => _brain.OnStateChanged -= OnStateChanged;

    private void Update()
    {
        if (_brain.State != EnemyState.Aggro || _bursting) return;
        if (PlayerHealth.Transform == null) return;

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > AttackRange) return;

        _cooldownTimer += Time.deltaTime;
        if (_cooldownTimer >= BurstCooldown * CooldownMultiplier)
        {
            _cooldownTimer = 0f;
            _burstRoutine  = StartCoroutine(Burst());
        }
    }

    private IEnumerator Burst()
    {
        _bursting = true;
        OnBurstStarted?.Invoke();

        for (int i = 0; i < BurstCount; i++)
        {
            if (_brain.State == EnemyState.Dead || PlayerHealth.Transform == null) break;
            FireGrenade();
            yield return new WaitForSeconds(BurstInterval * IntervalMultiplier);
        }

        _bursting     = false;
        _burstRoutine = null;
    }

    private void FireGrenade()
    {
        if (GrenadePrefab == null) return;

        Vector3 origin  = FirePoint != null ? FirePoint.position : transform.position + Vector3.up * 1.6f;
        Vector3 target  = PlayerHealth.Transform.position + Vector3.up * 1f;
        Vector3 flatDir = new Vector3(target.x - origin.x, 0f, target.z - origin.z).normalized;
        if (flatDir == Vector3.zero) flatDir = transform.forward;

        Vector3 right   = Vector3.Cross(Vector3.up, flatDir).normalized;
        Vector3 fireDir = Quaternion.AngleAxis(-ArcAngle, right) * flatDir;

        var go = Instantiate(GrenadePrefab, origin, Quaternion.LookRotation(fireDir));
        go.GetComponent<Enemy.AcidProjectile>()?.Init(fireDir);
    }

    private void OnStateChanged(EnemyState state)
    {
        if (state == EnemyState.Dead || state == EnemyState.Staggered)
        {
            if (_burstRoutine != null) { StopCoroutine(_burstRoutine); _burstRoutine = null; }
            _bursting = false;
        }
    }
}