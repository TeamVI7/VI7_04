// MechBarrageMissile.cs
// The projectile half of MechMissileBarrageAttack. Three beats: climb near-
// vertically, pick and mark a ground impact point around the player at apex,
// then dive straight down onto it.
//
// Transform-driven rather than physics-driven (unlike HomingMissile) so the arc
// is exactly as authored and 14 of them can't shove each other off course. The
// prefab still wants a Collider — and a *kinematic* Rigidbody if you want
// OnCollision-based VFX — so the player's hitscans can shoot them down.
using System.Collections;
using UnityEngine;

public class BarrageMissile : MonoBehaviour, IDamageable
{
    [Header("Ascent")]
    public float ascendSpeed = 32f;
    public float ascendDuration = 0.9f;
    [Tooltip("Random cone off straight-up, in degrees. Gives the volley a spray instead of 14 missiles on one line.")]
    [Range(0f, 60f)] public float ascendSpreadAngle = 22f;

    [Header("Turn Over")]
    [Tooltip("Seconds at the top of the arc sliding over the chosen impact point and nosing down. The ground marker appears at the start of this, so this plus the dive is the player's dodge window.")]
    public float turnOverDuration = 0.5f;

    [Header("Dive")]
    public float diveSpeed = 45f;
    [Tooltip("Safety cap — the missile detonates where it is if the dive somehow never lands.")]
    public float maxDiveDuration = 6f;
    [Tooltip("Detonate if the player passes this close mid-dive, instead of only on ground contact.")]
    public float proximityFuse = 1.5f;

    [Header("Impact")]
    public float damage = 28f;
    public float explosionRadius = 4f;
    [Tooltip("Surfaces the dive stops on, and what the impact point is probed against. Set this to your ground/level geometry layers — the missile's own layer is excluded automatically.")]
    public LayerMask groundMask = ~0;
    public GameObject explosionPrefab;
    [Tooltip("Optional ground decal/VFX spawned at the impact point for the whole descent, so a 14-missile volley is actually dodgeable. Spawned aligned to the ground normal.")]
    public GameObject impactMarkerPrefab;

    [Header("Health")]
    [Tooltip("Missiles are shootable — with a dozen in the air this is the player's main counterplay. Keep it low.")]
    public float maxHealth = 12f;
    public GameObject destroyedEffectPrefab;

    [Header("Audio")]
    [Tooltip("Optional. Looping thruster/whine for the whole flight, so a volley overhead is audible before it lands.")]
    public AudioClip thrusterLoopClip;
    [Tooltip("Optional. One-shot as the missile noses over and starts falling — the audio half of the impact marker.")]
    public AudioClip diveWhistleClip;
    [Tooltip("Volume for the in-flight loop and the dive whistle. A volley is a dozen of these at once, so keep it well under the explosion.")]
    [Range(0f, 1f)] public float flightVolume = 0.4f;
    [Tooltip("Optional. Detonation on the ground. Played detached, since the missile destroys itself the same frame.")]
    public AudioClip explosionClip;
    [Tooltip("Optional. Played instead when the player shoots this missile down — the reward cue for intercepting one.")]
    public AudioClip shotDownClip;
    [Tooltip("A volley is a dozen of these going off close together, so leave headroom.")]
    [Range(0f, 1f)] public float explosionVolume = 0.7f;

    [Header("Layer")]
    [Tooltip("The missile's layer. Pick your 'Enemy' layer here so weapon hitscans (filtered by hitMask) can actually register hits on it.")]
    public LayerMask EnemyLayerMask = 0;

    private Transform _target;
    private float _scatterRadius;
    private Vector3 _targetPoint;
    private Vector3 _groundNormal = Vector3.up;
    private GameObject _marker;
    private int _diveMask;
    private float _health;
    private bool _dead;
    private AudioSource _flight;

    public void Init(Transform target, float scatterRadius)
    {
        int resolvedLayer = LayerMaskToLayer(EnemyLayerMask);
        if (resolvedLayer == 0)
            Debug.LogWarning($"[BarrageMissile] {gameObject.name}: EnemyLayerMask resolves to 'Default' — " +
                              "weapon hitscans filtered by layer may miss this missile. " +
                              "Set EnemyLayerMask to your actual Enemy layer in the Inspector.", this);
        gameObject.layer = resolvedLayer;

        _target = target;
        _scatterRadius = scatterRadius;
        _health = maxHealth;

        // Strip our own layer out of the dive mask — otherwise a groundMask left
        // at Everything makes the downward raycast hit this missile's own
        // collider and detonate it the instant the dive starts.
        _diveMask = groundMask & ~(1 << gameObject.layer);

        StartFlightLoop();
        StartCoroutine(Co_Fly());
    }

    // Added rather than required on the prefab, so an existing barrage missile
    // prefab picks the loop up just by having a clip dropped on it.
    private void StartFlightLoop()
    {
        if (thrusterLoopClip == null && diveWhistleClip == null) return;

        _flight = gameObject.AddComponent<AudioSource>();
        _flight.playOnAwake = false;
        _flight.spatialBlend = 1f;
        _flight.rolloffMode = AudioRolloffMode.Linear;
        _flight.minDistance = 5f;
        _flight.maxDistance = 60f;
        _flight.volume = flightVolume;

        if (thrusterLoopClip == null) return;
        _flight.clip = thrusterLoopClip;
        _flight.loop = true;
        _flight.Play();
    }

    private static int LayerMaskToLayer(LayerMask mask)
    {
        int value = mask.value;
        if (value == 0) return 0;
        int layer = 0;
        while ((value & 1) == 0) { value >>= 1; layer++; }
        return layer;
    }

    private IEnumerator Co_Fly()
    {
        // --- Climb ---
        Vector2 lateral = Random.insideUnitCircle;
        Vector3 dir = (Vector3.up + new Vector3(lateral.x, 0f, lateral.y) * Mathf.Tan(ascendSpreadAngle * Mathf.Deg2Rad)).normalized;
        transform.rotation = Quaternion.LookRotation(dir);

        float t = 0f;
        while (t < ascendDuration)
        {
            transform.position += dir * ascendSpeed * Time.deltaTime;
            t += Time.deltaTime;
            yield return null;
        }

        if (_target == null) { Destroy(gameObject); yield break; }

        // --- Commit to an impact point and telegraph it ---
        PickImpactPoint();
        SpawnMarker();

        // Layered over the loop rather than replacing it: the whistle is the cue
        // that this particular missile has picked its spot and is coming down.
        if (_flight != null && diveWhistleClip != null) _flight.PlayOneShot(diveWhistleClip, flightVolume);

        Vector3 apex = transform.position;
        Vector3 over = new Vector3(_targetPoint.x, apex.y, _targetPoint.z);
        Quaternion startRot = transform.rotation;
        Quaternion downRot = Quaternion.LookRotation(Vector3.down);

        t = 0f;
        while (t < turnOverDuration)
        {
            float k = t / turnOverDuration;
            transform.position = Vector3.Lerp(apex, over, Mathf.SmoothStep(0f, 1f, k));
            transform.rotation = Quaternion.Slerp(startRot, downRot, k);
            t += Time.deltaTime;
            yield return null;
        }
        transform.position = over;
        transform.rotation = downRot;

        // --- Dive ---
        t = 0f;
        while (t < maxDiveDuration)
        {
            float step = diveSpeed * Time.deltaTime;

            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, step + 0.5f, _diveMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;
                Detonate(true);
                yield break;
            }

            transform.position += Vector3.down * step;

            if (transform.position.y <= _targetPoint.y) { Detonate(true); yield break; }

            if (_target != null && Vector3.Distance(transform.position, _target.position + Vector3.up) <= proximityFuse)
            {
                Detonate(true);
                yield break;
            }

            t += Time.deltaTime;
            yield return null;
        }

        Detonate(true);
    }

    private void PickImpactPoint()
    {
        Vector2 offset = Random.insideUnitCircle * _scatterRadius;
        Vector3 flat = _target.position + new Vector3(offset.x, 0f, offset.y);

        // Probe from just above the player's own height so the impact point lands
        // on whatever floor they're actually standing on, not a roof above them.
        Vector3 probeStart = flat + Vector3.up * 3f;
        if (Physics.Raycast(probeStart, Vector3.down, out RaycastHit hit, 60f, _diveMask, QueryTriggerInteraction.Ignore))
        {
            _targetPoint = hit.point;
            _groundNormal = hit.normal;
        }
        else
        {
            _targetPoint = new Vector3(flat.x, _target.position.y, flat.z);
            _groundNormal = Vector3.up;
        }
    }

    private void SpawnMarker()
    {
        if (impactMarkerPrefab == null) return;
        _marker = Instantiate(
            impactMarkerPrefab,
            _targetPoint + _groundNormal * 0.05f,
            Quaternion.FromToRotation(Vector3.up, _groundNormal));
    }

    public void TakeDamage(float amount, Vector3 direction, Vector3 hitPoint)
    {
        if (_dead) return;
        _health -= amount;
        // Shot down: no player damage, so intercepting a missile actually saves you.
        if (_health <= 0f) Detonate(false);
    }

    private void Detonate(bool damagePlayer)
    {
        if (_dead) return;
        _dead = true;

        if (_marker != null) Destroy(_marker);

        if (damagePlayer)
        {
            foreach (var h in Physics.OverlapSphere(transform.position, explosionRadius))
                if (h.TryGetComponent(out PlayerHealth ph)) ph.TakeDamage(damage);
        }

        GameObject fx = damagePlayer ? explosionPrefab : (destroyedEffectPrefab != null ? destroyedEffectPrefab : explosionPrefab);
        if (fx != null) Destroy(Instantiate(fx, transform.position, Quaternion.identity), 3f);

        AudioClip sfx = damagePlayer ? explosionClip : (shotDownClip != null ? shotDownClip : explosionClip);
        if (sfx != null) AudioSource.PlayClipAtPoint(sfx, transform.position, explosionVolume);

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_marker != null) Destroy(_marker);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
