// HomingMissile.cs
using UnityEngine;

public class HomingMissile : MonoBehaviour, IDamageable
{
    public float speed = 18f;
    [Tooltip("Max turn rate in degrees/sec. Caps how sharply the missile can correct course -- low values let the player juke it.")]
    public float maxTurnRateDegrees = 90f;
    public float damage = 25f;
    public float explosionRadius = 3f;
    public float lifeTime = 6f;
    public GameObject hitEffectPrefab;

    [Header("Health")]
    public float maxHealth = 20f;
    public GameObject destroyedEffectPrefab;

    [Header("Audio")]
    [Tooltip("Optional. Looping thruster/whine while the missile flies, so the player can hear one coming from off-screen.")]
    public AudioClip thrusterLoopClip;
    [Tooltip("Optional. Detonation. Played detached, since the missile destroys itself the same frame.")]
    public AudioClip explosionClip;
    [Tooltip("Optional. Played instead when the player shoots this missile down.")]
    public AudioClip shotDownClip;
    [Range(0f, 1f)] public float explosionVolume = 0.8f;

    [Header("Layer")]
    [Tooltip("The missile's layer. Pick your 'Enemy' layer here so weapon hitscans (filtered by hitMask) can actually register hits on it.")]
    public LayerMask EnemyLayerMask = 0;

    private Transform _target;
    private Rigidbody _rb;
    private float _health;
    private bool _dead;

    public void Init(Transform target)
    {
        int resolvedLayer = LayerMaskToLayer(EnemyLayerMask);
        if (resolvedLayer == 0)
            Debug.LogWarning($"[HomingMissile] {gameObject.name}: EnemyLayerMask resolves to 'Default' -- " +
                              "weapon hitscans filtered by layer may miss this missile. " +
                              "Set EnemyLayerMask to your actual Enemy layer in the Inspector.", this);
        gameObject.layer = resolvedLayer;

        _target = target;
        _rb = GetComponent<Rigidbody>();
        _rb.linearVelocity = transform.forward * speed;
        _health = maxHealth;
        StartThrusterLoop();
        Destroy(gameObject, lifeTime);
    }

    private void StartThrusterLoop()
    {
        if (thrusterLoopClip == null) return;

        // Added rather than required on the prefab, so an existing missile prefab
        // picks the loop up just by having a clip dropped on it.
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = thrusterLoopClip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 4f;
        src.maxDistance = 45f;
        src.Play();
    }

    private static int LayerMaskToLayer(LayerMask mask)
    {
        int value = mask.value;
        if (value == 0) return 0;
        int layer = 0;
        while ((value & 1) == 0) { value >>= 1; layer++; }
        return layer;
    }

    private void FixedUpdate()
    {
        if (_target == null) return;

        Vector3 toTarget = (_target.position + Vector3.up - transform.position).normalized;
        Vector3 currentDir = _rb.linearVelocity.sqrMagnitude > 0.01f ? _rb.linearVelocity.normalized : transform.forward;
        Vector3 newDir = Vector3.RotateTowards(currentDir, toTarget, maxTurnRateDegrees * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);

        _rb.linearVelocity = newDir * speed;
        transform.rotation = Quaternion.LookRotation(newDir);
    }

    public void TakeDamage(float amount, Vector3 direction, Vector3 hitPoint)
    {
        if (_dead) return;
        _health -= amount;
        if (_health <= 0f) Detonate(false);
    }

    private void OnCollisionEnter(Collision col) => Detonate(true);

    private void Detonate(bool damagePlayer)
    {
        if (_dead) return;
        _dead = true;

        if (damagePlayer)
        {
            foreach (var h in Physics.OverlapSphere(transform.position, explosionRadius))
                if (h.TryGetComponent(out PlayerHealth ph)) ph.TakeDamage(damage);
        }

        GameObject fx = damagePlayer ? hitEffectPrefab : (destroyedEffectPrefab != null ? destroyedEffectPrefab : hitEffectPrefab);
        if (fx != null) Destroy(Instantiate(fx, transform.position, Quaternion.identity), 2f);

        AudioClip sfx = damagePlayer ? explosionClip : (shotDownClip != null ? shotDownClip : explosionClip);
        if (sfx != null) AudioSource.PlayClipAtPoint(sfx, transform.position, explosionVolume);

        Destroy(gameObject);
    }
}