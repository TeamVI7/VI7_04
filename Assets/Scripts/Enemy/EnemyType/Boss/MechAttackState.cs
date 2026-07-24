// MechAttackBehaviour.cs
using UnityEngine;

public abstract class MechAttackBehaviour : MonoBehaviour
{
    [Header("Range")]
    public float minRange = 0f;
    public float maxRange = 10f;

    [Header("Cooldown / Weight")]
    public float cooldown = 5f;
    [Range(0f, 10f)] public float weight = 1f;

    protected MechBossBrain brain;
    private float _cooldownTimer;

    public bool IsExecuting { get; protected set; }
    public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);

    protected virtual void Awake() => brain = GetComponent<MechBossBrain>();

    protected virtual void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
    }

    public bool IsAvailable(float distanceToPlayer) =>
        !IsExecuting && _cooldownTimer <= 0f
        && distanceToPlayer >= minRange && distanceToPlayer <= maxRange;

    public void BeginCooldown() => _cooldownTimer = cooldown;

    public abstract void Execute(System.Action onComplete);
}