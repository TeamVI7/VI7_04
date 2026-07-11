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
    [Header("Detection")]
    [SerializeField] protected string playerTag = "Player";

    [Header("Idle Animation")]
    [SerializeField] private bool  spin      = true;
    [SerializeField] private float spinSpeed = 90f;
    [SerializeField] private bool  bob       = true;
    [SerializeField] private float bobHeight = 0.15f;
    [SerializeField] private float bobSpeed  = 2f;

    [Header("Pickup FX")]
    [SerializeField] private GameObject pickupVFX;
    [SerializeField] private AudioClip  pickupSFX;
    [SerializeField] private float      sfxVolume = 1f;

    [Header("Respawn")]
    [Tooltip("If > 0, the pickup reappears after this many seconds instead of being destroyed. " +
             "Useful for ammo/health crates on a loop; leave at 0 for one-time weapon pickups.")]
    [SerializeField] private float respawnDelay = 0f;

    private Vector3    _startPos;
    private Collider   _collider;
    private Renderer[] _renderers;
    private bool       _collected;

    protected virtual void Awake()
    {
        _startPos            = transform.position;
        _collider             = GetComponent<Collider>();
        _collider.isTrigger  = true;
        _renderers            = GetComponentsInChildren<Renderer>();
    }

    protected virtual void Update()
    {
        if (_collected) return;

        if (spin) transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        if (bob)
        {
            float y = _startPos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_collected) return;
        if (!other.CompareTag(playerTag)) return;

        if (TryPickup(other)) Collect();
    }

    /// <summary>
    /// Attempt to apply this pickup's effect to the player who touched it.
    /// Return true if something was actually granted.
    /// </summary>
    protected abstract bool TryPickup(Collider player);

    private void Collect()
    {
        _collected = true;

        if (pickupVFX != null)
            Destroy(Instantiate(pickupVFX, transform.position, Quaternion.identity), 3f);

        if (pickupSFX != null)
            AudioSource.PlayClipAtPoint(pickupSFX, transform.position, sfxVolume);

        SetVisible(false);
        _collider.enabled = false;

        if (respawnDelay > 0f) Invoke(nameof(Respawn), respawnDelay);
        else Destroy(gameObject, 0.05f); // tiny delay so VFX/SFX still play this frame
    }

    private void Respawn()
    {
        _collected         = false;
        _collider.enabled  = true;
        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        foreach (var r in _renderers) r.enabled = visible;
    }
}
