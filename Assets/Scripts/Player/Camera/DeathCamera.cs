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

    // Called by CheckpointManager once the screen has been black for a beat.
    public void Respawn(Vector3 pos, Quaternion rot)
    {
        if (cam.TryGetComponent<Rigidbody>(out var rb))      Destroy(rb);
        if (cam.TryGetComponent<SphereCollider>(out var sc))  Destroy(sc);

        cam.transform.SetParent(_camOrigParent);
        cam.transform.localPosition = _camOrigLocalPos;
        cam.transform.localRotation = _camOrigLocalRot;

        if (player)
        {
            player.transform.SetPositionAndRotation(pos, rot);

            // Rigidbody caches its own internal position separately from Transform.
            // Setting transform alone gets overwritten by the Rigidbody's stale
            // (pre-death) position on the next physics step — must set rb directly too.
            if (player.TryGetComponent<Rigidbody>(out var playerRb))
            {
                playerRb.position         = pos;
                playerRb.rotation         = rot;
                playerRb.linearVelocity   = Vector3.zero;
                playerRb.angularVelocity  = Vector3.zero;
            }

            player.SetActive(true);
        }

        if (mouseLook) mouseLook.enabled = true;

        PlayerHealth.Instance?.Respawn();

        // Hard reset, not just "release Dead" — if a coroutine got silently killed by
        // deactivation while the player was dead (weapon mid-reload, mid-switch, etc.),
        // this guarantees respawn always hands back a completely clean, unblocked state
        // instead of inheriting a stuck lock from whatever was happening at the moment of death.
        PlayerActionLock.Instance.ClearAll();

        if (deathFade) deathFade.DOFade(0f, fadeDuration);
    }
}