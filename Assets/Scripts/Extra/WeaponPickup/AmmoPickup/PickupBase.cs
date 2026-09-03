using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for all world pickups (weapons, ammo, health, etc).
/// Handles trigger detection, idle spin/bob animation, pickup FX/SFX, and
/// an optional respawn timer.
///
/// Override TryPickup() to define what the pickup actually grants the player.
/// Return false from TryPickup() to leave the pickup in the world uncollected
/// (e.g. an ammo crate when the player is already at max reserve).
///
/// A collected pickup is hidden and its collider disabled — it is never destroyed.
/// The save system has to be able to put it back: rewinding to a checkpoint taken
/// before the player grabbed an ammo crate must return that crate to the world, and
/// a destroyed GameObject cannot be un-destroyed. The cost is a hidden, collider-less
/// object that early-outs of Update, which is nothing next to losing the rewind.
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
    [Tooltip("If > 0 and respawnDelay is 0, the pickup removes itself after this many seconds if never collected.")]
    [SerializeField] private float despawnTime = 120f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    #region State

    private Collider   _collider;
    private Renderer[] _renderers;
    private bool       _collected;
    private Coroutine  _despawnRoutine;

    public bool IsCollected => _collected;

    /// <summary>Stable identity for the save file — see <see cref="SaveableId"/>.</summary>
    public string SaveId => SaveableId.Resolve(this);

    #endregion

    #region Save Registry

    // Every live pickup registers here so the save system can ask "which of these have
    // been taken?" without a FindObjectsOfType sweep, which would miss the ones already
    // hidden after collection.
    private static readonly List<PickupBase> Registry = new List<PickupBase>();

    public static IReadOnlyList<PickupBase> All => Registry;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => Registry.Clear();

    #endregion

    #region Unity Callbacks

    protected virtual void Awake()
    {
        _collider            = GetComponent<Collider>();
        _collider.isTrigger  = true;
        _renderers           = GetComponentsInChildren<Renderer>();
    }

    protected virtual void OnEnable()
    {
        if (!Registry.Contains(this)) Registry.Add(this);
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

    protected virtual void OnDestroy()
    {
        Registry.Remove(this);
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
        if (_despawnRoutine != null)
        {
            StopCoroutine(_despawnRoutine);
            _despawnRoutine = null;
        }

        if (pickupVFX != null)
            Destroy(Instantiate(pickupVFX, transform.position, Quaternion.identity), 3f);

        if (pickupSFX != null)
            AudioSource.PlayClipAtPoint(pickupSFX, transform.position, sfxVolume);

        SetCollected(true);

        Log("Collected by player.");

        if (respawnDelay > 0f) Invoke(nameof(Respawn), respawnDelay);
    }

    private void Respawn()
    {
        SetCollected(false);
        Log("Respawned.");
    }

    /// <summary>
    /// Puts this pickup back to a saved collected/uncollected state, with no FX, no
    /// sound and no respawn timer — a rewind is not a pickup event.
    /// </summary>
    public void RestoreCollected(bool collected)
    {
        CancelInvoke(nameof(Respawn));

        if (_despawnRoutine != null)
        {
            StopCoroutine(_despawnRoutine);
            _despawnRoutine = null;
        }

        SetCollected(collected);

        // Rewinding to before this pickup was taken puts it back in the world, and an
        // uncollected pickup with a despawn timer is supposed to be counting down.
        if (!collected && respawnDelay <= 0f && despawnTime > 0f && isActiveAndEnabled)
            _despawnRoutine = StartCoroutine(DespawnCountdown());
    }

    private void SetCollected(bool collected)
    {
        _collected = collected;

        if (_collider != null) _collider.enabled = !collected;
        SetVisible(!collected);
    }

    #endregion

    #region Despawn

    private IEnumerator DespawnCountdown()
    {
        yield return new WaitForSeconds(despawnTime);
        if (_collected) yield break;

        Log($"Despawn timer expired after {despawnTime}s.");
        OnDespawn();
        SetCollected(true);
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
