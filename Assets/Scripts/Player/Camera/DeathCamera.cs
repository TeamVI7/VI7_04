using UnityEngine;
using DG.Tweening;

public class DeathCamera : MonoBehaviour
{
    [SerializeField] GameObject    player;
    [SerializeField] Camera        cam;
    [SerializeField] MonoBehaviour mouseLook;
    [SerializeField] float         colliderRadius = 0.15f;
    [SerializeField] float         rollTorque     = 3f;
    [SerializeField] float         fadeDelay      = 2f;
    [SerializeField] float         fadeDuration   = 0.8f;
    [SerializeField] CanvasGroup   deathFade;

    Transform  _camOrigParent;
    Vector3    _camOrigLocalPos;
    Quaternion _camOrigLocalRot;
    bool       _cachedOrigin;

    public static DeathCamera Instance { get; private set; }

    // Total time from death to fully black — CheckpointManager waits this long before respawning.
    public float DeathToBlackDuration => fadeDelay + fadeDuration;

    void Awake()
    {
        Instance = this;
        if (cam) CacheCamOrigin();
    }

    // FIX: Start/OnDestroy — stays subscribed even when camera GameObject is disabled.
    // OnEnable/OnDisable was wrong: ComputerInteraction disables the camera GO,
    // which fired OnDisable and silently unsubscribed from OnDied.
    void Start()     => PlayerHealth.OnDied += Play;
    void OnDestroy()
    {
        PlayerHealth.OnDied -= Play;
        if (Instance == this) Instance = null;
    }

    void CacheCamOrigin()
    {
        _camOrigParent   = cam.transform.parent;
        _camOrigLocalPos = cam.transform.localPosition;
        _camOrigLocalRot = cam.transform.localRotation;
        _cachedOrigin    = true;
    }

    void Play()
    {
        // Raise this first, before anything else — every other system (weapon fire,
        // reload, melee, switching) checks PlayerActionLock.IsDead through IsInputBlocked
        // / CanMelee / CanSwitch, so this one line is what stops all player actions on death.
        PlayerActionLock.Instance?.SetLock(PlayerActionLock.LockReason.Dead, true);

        if (!cam) cam = Camera.main;
        if (!_cachedOrigin && cam) CacheCamOrigin();

        // FIX: Re-enable camera in case ComputerInteraction had it disabled.
        if (cam != null && !cam.gameObject.activeSelf)
            cam.gameObject.SetActive(true);

        if (mouseLook) mouseLook.enabled = false;

        cam.transform.SetParent(null);

        var rb = cam.gameObject.AddComponent<Rigidbody>();
        var sc = cam.gameObject.AddComponent<SphereCollider>();
        sc.radius = colliderRadius;

        var playerRb = player ? player.GetComponent<Rigidbody>() : null;
        if (playerRb) rb.linearVelocity = playerRb.linearVelocity;

        rb.angularVelocity = cam.transform.right * rollTorque;

        if (player) player.SetActive(false);

        if (deathFade)
            deathFade.DOFade(1f, fadeDuration).SetDelay(fadeDelay);
    }

    /// <summary>
    /// Undoes the death presentation: the camera stops being a physics object, goes back
    /// on its rig, and the player GameObject comes back on.
    ///
    /// This is deliberately only the *view*. Where the player ends up, how much health
    /// they have and what is in their magazine all come from the checkpoint snapshot,
    /// which CheckpointManager applies immediately after this returns. Reactivating the
    /// player has to happen first: a disabled Rigidbody silently drops position writes,
    /// so restoring the snapshot into a still-dead player would place them nowhere.
    /// </summary>
    public void BeginRespawn()
    {
        if (cam.TryGetComponent<Rigidbody>(out var rb))      Destroy(rb);
        if (cam.TryGetComponent<SphereCollider>(out var sc))  Destroy(sc);

        cam.transform.SetParent(_camOrigParent);
        cam.transform.localPosition = _camOrigLocalPos;
        cam.transform.localRotation = _camOrigLocalRot;

        if (player) player.SetActive(true);

        if (mouseLook) mouseLook.enabled = true;

        // Hard reset, not just "release Dead" — if a coroutine got silently killed by
        // deactivation while the player was dead (weapon mid-reload, mid-switch, etc.),
        // this guarantees respawn always hands back a completely clean, unblocked state
        // instead of inheriting a stuck lock from whatever was happening at the moment of death.
        PlayerActionLock.Instance.ClearAll();
    }

    /// <summary>Fades the black screen back out. Called after the snapshot has been
    /// applied, so the player never sees the world mid-rewind.</summary>
    public void FinishRespawn()
    {
        if (deathFade) deathFade.DOFade(0f, fadeDuration);
    }
}