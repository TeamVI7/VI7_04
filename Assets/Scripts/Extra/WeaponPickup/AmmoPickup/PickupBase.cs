using System.Collections;
using UnityEngine;

/// <summary>
/// Base class for all world pickups (weapons, ammo, health, etc).
/// Handles trigger detection, idle spin/bob animation, pickup FX/SFX, and
/// an optional respawn timer instead of a hard destroy.
///
/// Override TryPickup() to define what the pickup actually grants the player.
/// Return false from TryPickup() to leave the pickup in the world uncollected
/// (e.g. an ammo crate when the player is already at max reserve).
/// </summary>
[RequireComponent(typeof(Collider))]
public abstract class PickupBase : MonoBehaviour
{
    #region Inspector

    [Header("Detection")]
    [SerializeField] protected string playerTag = "Player";

    [Header("Idle Animation")]
    [SerializeField] private bool  spin      = true;
    [SerializeField] private float spinSpeed = 90f;

    [Header("Pickup FX")]
    [SerializeField] private GameObject pickupVFX;
    [SerializeField] private AudioClip  pickupSFX;
    [SerializeField] private float      sfxVolume = 1f;

    [Header("Respawn")]
    [Tooltip("If > 0, the pickup reappears after this many seconds instead of being destroyed/despawned. Leave at 0 for one-time pickups.")]
    [SerializeField] private float respawnDelay = 0f;

    [Header("Despawn")]
    [Tooltip("If > 0 and respawnDelay is 0, the pickup is destroyed after this many seconds if never collected.")]
    [SerializeField] private float despawnTime = 120f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    #region State

    private Collider   _collider;
    private Renderer[] _renderers;
    private bool       _collected;
    private Coroutine  _despawnRoutine;

    #endregion

    #region Unity Callbacks

    protected virtual void Awake()
    {
        _collider            = GetComponent<Collider>();
        _collider.isTrigger  = true;
        _renderers           = GetComponentsInChildren<Renderer>();
    }

    protected virtual void Start()
    {
        if (respawnDelay <= 0f && despawnTime > 0f)
            _despawnRoutine = StartCoroutine(DespawnCountdown());
    }

    protected virtual void Update()
    {
        if (_collected) return;
        if (spin) transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    protected virtual void OnDisable()
    {
        if (_despawnRoutine != null)
        {
            StopCoroutine(_despawnRoutine);
            _despawnRoutine = null;
        }
    }

    #endregion

    #region Pickup Flow

    private void OnTriggerEnter(Collider other)
    {
        if (_collected) return;
        if (!other.CompareTag(playerTag)) return;

        if (TryPickup(other)) Collect();
    }

    protected abstract bool TryPickup(Collider player);

    private void Collect()
    {
        _collected = true;

        if (_despawnRoutine != null)
        {
            StopCoroutine(_despawnRoutine);
            _despawnRoutine = null;
        }

        if (pickupVFX != null)
            Destroy(Instantiate(pickupVFX, transform.position, Quaternion.identity), 3f);

        if (pickupSFX != null)
            AudioSource.PlayClipAtPoint(pickupSFX, transform.position, sfxVolume);

        SetVisible(false);
        _collider.enabled = false;

        Log($"Collected by player.");

        if (respawnDelay > 0f) Invoke(nameof(Respawn), respawnDelay);
        else Destroy(gameObject, 0.05f);
    }

    private void Respawn()
    {
        _collected        = false;
        _collider.enabled = true;
        SetVisible(true);
        Log("Respawned.");
    }

    #endregion

    #region Despawn

    private IEnumerator DespawnCountdown()
    {
        yield return new WaitForSeconds(despawnTime);
        if (_collected) yield break;

        Log($"Despawn timer expired after {despawnTime}s.");
        OnDespawn();
        Destroy(gameObject);
    }

    protected virtual void OnDespawn() { }

    #endregion

    #region Helpers

    private void SetVisible(bool visible)
    {
        foreach (var r in _renderers) r.enabled = visible;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[{name}] {msg}", this);
    }

    #endregion
}
