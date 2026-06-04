using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Primary weapon controller for an FPS character.
/// All tunable values live in a <see cref="WeaponData"/> ScriptableObject.
///
/// ── Reload event order ───────────────────────────────────────────────────
///   OnReloadStart  →  OnMagOut  →  OnMagIn  →  OnChamberRound  →  OnReloadComplete
///   Any phase can be subscribed to independently for SFX, VFX, UI, IK, etc.
///
/// ── Inspect ──────────────────────────────────────────────────────────────
///   Press F (default) to inspect the weapon from any state.
///   Firing or reloading will cancel the inspect coroutine immediately.
///   Wire an "Inspect" trigger in your Animator and add AnimEvent_InspectEnd
///   on the last frame of your inspect clip.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class WeaponsController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Weapon Data")]
    [Tooltip("ScriptableObject containing all stats for this weapon. " +
             "Create one via Right-click > FPS > Weapon Data.")]
    public WeaponData weaponData;

    [Header("References")]
    public Animator        gunAnimator;
    public PlayerMovement  playerMovement;
    public LayerMask       aimColliderLayerMask;

    [Header("Transform Points")]
    public Transform muzzleFlashPoint;
    public Transform casingEjectPoint;
    public Transform spawnBulletPosition;

    [Header("Prefabs")]
    public GameObject bulletCasingPrefab;
    public GameObject bulletTrailPrefab;
    public GameObject muzzleFlashPrefab;

    [Header("Reload Mode")]
    [Tooltip("TRUE  → reload/inspect phases are triggered by Animation Events via AnimationEventReceiver.\n" +
             "FALSE → reload phases are driven by the timed delays in WeaponData (no Animation Events needed).")]
    public bool animationDrivenReload = true;

    [Header("Inspect")]
    [Tooltip("Keyboard key that triggers the weapon inspect animation.")]
    public KeyCode inspectKey = KeyCode.I;
    [Tooltip("Fallback inspect duration used in timed mode (when animationDrivenReload is false).")]
    public float inspectDuration = 2.5f;
    [Header("Switch Animations (Timed fallback — used when animationDrivenReload = false)")]
    [Tooltip("Duration of the Holster animation clip when no AnimEvent_HolsterEnd is wired.")]
    public float holsterDuration = 0.4f;
    [Tooltip("Duration of the Draw animation clip when no AnimEvent_DrawEnd is wired.")]
    public float drawDuration    = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs    = true;
    [SerializeField] private bool showSpreadGizmo  = true;
    [SerializeField] private bool showRaycastGizmo = true;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Reload Events
    // ─────────────────────────────────────────────────────────────────────────
    // Subscribe to any of these from UI, animation rigs, IK controllers, etc.
    // All callbacks run on the main thread.

    /// <summary>Fired the moment the player triggers a reload.</summary>
    public event Action OnReloadStart;

    /// <summary>Fired when the magazine physically leaves the weapon.
    /// Hook this to mag-drop VFX, mag-out IK pose, etc.</summary>
    public event Action OnMagOut;

    /// <summary>Fired when the fresh magazine seats into the weapon.
    /// Hook this to mag-in IK pose, mag-in VFX, etc.</summary>
    public event Action OnMagIn;

    /// <summary>Fired when the bolt/slide chambers a round.
    /// Hook this to bolt-pull animation, chamber sound, etc.
    /// Only fires when <see cref="WeaponData.requiresManualChamber"/> is true
    /// or when chambering after an empty-gun reload.</summary>
    public event Action OnChamberRound;

    /// <summary>Fired after all reload phases complete and ammo is replenished.</summary>
    public event Action OnReloadComplete;

    /// <summary>Fired if reload is interrupted before it finishes.</summary>
    public event Action OnReloadCancelled;
    /// <summary>Fired by WeaponSwitcher after draw completes and weapon is live.</summary>
    public event Action OnEquipped;

    /// <summary>Fired by WeaponSwitcher after holster completes and GO is deactivated.</summary>
    public event Action OnUnequipped;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region General Weapon Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fired on every successful shot. Payload: world-space hit point
    /// (Vector3.zero if no surface was hit within range).</summary>
    public event Action<Vector3> OnWeaponFired;

    /// <summary>Fired whenever clip or reserve ammo changes.</summary>
    public event Action<int, int> OnAmmoChanged; // (currentClip, reserve)

    /// <summary>Fired when the trigger is pulled with no ammo.</summary>
    public event Action OnDryFire;

    /// <summary>Fired when the clip empties after the last shot.</summary>
    public event Action OnWeaponEmpty;

    /// <summary>Fired when the player begins the inspect animation.</summary>
    public event Action OnInspectStart;

    /// <summary>Fired when the inspect animation finishes.</summary>
    public event Action OnInspectEnd;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Animation-Driven Entry Points
    // ─────────────────────────────────────────────────────────────────────────
    // AnimationEventReceiver calls these public methods when each animation
    // event fires. They signal the coroutines to advance to the next phase
    // instead of using timed delays.

    // ── Reload signals ────────────────────────────────────────────────────────
    private bool _animSignal_MagOut;
    private bool _animSignal_MagIn;
    private bool _animSignal_ChamberRound;
    private bool _animSignal_ReloadEnd;

    // ── Inspect signal ────────────────────────────────────────────────────────
    private bool _animSignal_InspectEnd;

    // ── Switch signals ────────────────────────────────────────────────────────
    private bool _animSignal_HolsterEnd;
    private bool _animSignal_DrawEnd;

    /// <summary>Called by <see cref="AnimationEventReceiver.AnimEvent_ReloadStart"/>.</summary>
    public void OnReloadStart_AnimDriven()   { /* start already handled by coroutine */ }

    /// <summary>Called by <see cref="AnimationEventReceiver.AnimEvent_MagOut"/>.</summary>
    public void OnMagOut_AnimDriven()        { _animSignal_MagOut       = true; }

    /// <summary>Called by <see cref="AnimationEventReceiver.AnimEvent_MagIn"/>.</summary>
    public void OnMagIn_AnimDriven()         { _animSignal_MagIn        = true; }

    /// <summary>Called by <see cref="AnimationEventReceiver.AnimEvent_ChamberRound"/>.</summary>
    public void OnChamberRound_AnimDriven()  { _animSignal_ChamberRound = true; }

    /// <summary>Called by <see cref="AnimationEventReceiver.AnimEvent_ReloadEnd"/>.</summary>
    public void OnReloadEnd_AnimDriven()     { _animSignal_ReloadEnd    = true; }

    public void OnInspectMagOut_AnimDriven()
    {
        PlaySound(weaponData.magOutSound, weaponData.reloadVolume);
    }

    public void OnInspectMagIn_AnimDriven()
    {
        PlaySound(weaponData.magInSound, weaponData.reloadVolume);
    }

    /// <summary>Called by <see cref="AnimationEventReceiver.AnimEvent_InspectEnd"/>.</summary>
    public void OnInspectEnd_AnimDriven()    { _animSignal_InspectEnd   = true; }

    /// <summary>Called by AnimationEventReceiver.AnimEvent_HolsterEnd.</summary>
    public void OnHolsterEnd_AnimDriven()    { _animSignal_HolsterEnd   = true; }

    /// <summary>Called by AnimationEventReceiver.AnimEvent_DrawEnd.</summary>
    public void OnDrawEnd_AnimDriven()       { _animSignal_DrawEnd      = true; }

    private void ResetAnimSignals()
    {
        _animSignal_MagOut       = false;
        _animSignal_MagIn        = false;
        _animSignal_ChamberRound = false;
        _animSignal_ReloadEnd    = false;
        _animSignal_InspectEnd   = false;
        _animSignal_HolsterEnd   = false;
        _animSignal_DrawEnd      = false;
    }

    /// <summary>
    /// Yields until <paramref name="signal"/> becomes true or
    /// <paramref name="timeout"/> seconds pass (failsafe against missing events).
    /// </summary>
    private IEnumerator WaitForAnimSignal(System.Func<bool> signal, float timeout, string signalName)
    {
        float elapsed = 0f;
        while (!signal() && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (elapsed >= timeout)
            LogWarning($"Timed out waiting for anim signal '{signalName}'. " +
                       "Is the Animation Event added in the FBX importer? " +
                       "Is AnimationEventReceiver on the Animator GameObject?");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Weapon State Machine
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>All possible states the weapon can be in at any time.</summary>
    public enum WeaponState
    {
        Idle,
        Firing,
        Reloading,
        Empty,       // clip empty, no reserve — weapon is dead until pickup
        Switching
    }

    /// <summary>Current weapon state. Read-only externally; drives all logic gating.</summary>
    public WeaponState CurrentState { get; private set; } = WeaponState.Idle;

    private void SetState(WeaponState next)
    {
        if (CurrentState == next) return;
        Log($"WeaponState: {CurrentState} → {next}");
        CurrentState = next;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Animator Parameter Hashes
    // ─────────────────────────────────────────────────────────────────────────
    // Cached at startup — avoids per-frame string allocations and typos.

    private static readonly int AnimIsWalking   = Animator.StringToHash("isWalking");
    private static readonly int AnimIsShooting  = Animator.StringToHash("IsShooting");
    private static readonly int AnimReload      = Animator.StringToHash("Reload");
    private static readonly int AnimReloadEmpty = Animator.StringToHash("ReloadEmpty");
    private static readonly int AnimInspect     = Animator.StringToHash("Inspect");
    private static readonly int AnimHolster     = Animator.StringToHash("Holster");
    private static readonly int AnimDraw        = Animator.StringToHash("Draw");

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private int   _currentAmmoInClip;
    private int   _ammoInReserve;
    private bool  _roundInChamber;
    private bool  _canShoot;
    private bool  _isInspecting;       // true while Co_Inspect is running
    private bool  _isSwitching;        // true while holstering or drawing
    private bool  _holsterComplete;    // set true by Co_WaitHolster
    private bool  _drawComplete;       // set true by Co_WaitDraw

    private float _currentSpreadBuildup;
    private float _dryFireCooldown;
    private const float DryFireCooldownTime = 0.3f;

    private Coroutine  _inspectCoroutine;
    private AudioSource        _weaponAudioSource;
    private Camera             _mainCamera;
    private static AudioSource _sharedImpactAudio;

    // Last gizmo data — written each shot, read by OnDrawGizmosSelected
    private Vector3 _gizmoRayOrigin;
    private Vector3 _gizmoRayEnd;
    private float   _gizmoSpread;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        ValidateSetup();
        InitAmmo();
        InitAudio();
        EnsureSharedImpactAudio();
    }

    private void Update()
    {
        HandleInput();
        TickSpreadRecovery();
        TickDryFireCooldown();
        TickAnimations();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Initialisation
    // ─────────────────────────────────────────────────────────────────────────

    private void InitAmmo()
    {
        _currentAmmoInClip = weaponData.clipSize;
        _ammoInReserve     = weaponData.reservedAmmoCapacity;
        _roundInChamber    = true;
        _canShoot          = true;
    }

    private void InitAudio()
    {
        _weaponAudioSource = GetComponent<AudioSource>();
        _weaponAudioSource.spatialBlend = 1f;
        _weaponAudioSource.playOnAwake  = false;
    }

    /// <summary>
    /// Warns in the console about any missing critical references.
    /// Runs once at Start so you catch problems immediately on Play.
    /// </summary>
    private void ValidateSetup()
    {
        if (weaponData          == null) LogWarning("weaponData is not assigned!");
        if (gunAnimator         == null) LogWarning("gunAnimator is not assigned — animations will be skipped.");
        if (playerMovement      == null) LogWarning("playerMovement is not assigned — spread multipliers won't apply.");
        if (spawnBulletPosition == null) LogWarning("spawnBulletPosition is not assigned — hitscan will use transform.position.");
        if (muzzleFlashPoint    == null) LogWarning("muzzleFlashPoint is not assigned — muzzle flash will be skipped.");
        if (casingEjectPoint    == null) LogWarning("casingEjectPoint is not assigned — casing eject will be skipped.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Input Handling
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleInput()
    {
        if (_isSwitching) return;   // block all input while switching

        // ── Primary fire ────────────────────────────────────────────────────
        if (Input.GetMouseButtonDown(0))
        {
            if (_canShoot && CurrentState == WeaponState.Idle && (_currentAmmoInClip > 0 || _roundInChamber))
            {
                _canShoot = false;
                StartCoroutine(Co_Fire());
            }
            else if (CurrentState != WeaponState.Reloading && _currentAmmoInClip <= 0 && !_roundInChamber)
            {
                TriggerDryFire();
            }
        }

        // ── Reload ──────────────────────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.R))
        {
            TryStartReload();
        }

        // ── Inspect ─────────────────────────────────────────────────────────
        if (Input.GetKeyDown(inspectKey))
        {
            TryStartInspect();
        }
    }

    private void TryStartReload()
    {
        if (CurrentState == WeaponState.Reloading) return;
        if (_currentAmmoInClip >= weaponData.clipSize) return;
        if (_ammoInReserve <= 0)                       return;

        StartCoroutine(Co_Reload());
    }

    private void TriggerDryFire()
    {
        if (_dryFireCooldown > 0f) return;

        Log("Dry fire.");
        PlaySound(weaponData.dryFireSound, weaponData.dryFireVolume);
        _dryFireCooldown = DryFireCooldownTime;
        OnDryFire?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inspect
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the weapon inspect animation from any weapon state.
    /// If an inspect is already playing it is ignored (no double-trigger).
    /// Firing or reloading will cancel the inspect automatically.
    /// </summary>
    private void TryStartInspect()
    {
        if (_isInspecting) return;
        _inspectCoroutine = StartCoroutine(Co_Inspect());
    }

    public void CancelInspect()
    {
        if (!_isInspecting) return;
        if (_inspectCoroutine != null) StopCoroutine(_inspectCoroutine);
        _isInspecting           = false;
        _animSignal_InspectEnd  = false;
        Log("Inspect cancelled.");
        OnInspectEnd?.Invoke();  // notify listeners the inspect ended early
    }

    private IEnumerator Co_Inspect()
    {
        _isInspecting          = true;
        _animSignal_InspectEnd = false;
        Log("Inspect start.");

        SetAnimatorTrigger(AnimInspect);
        OnInspectStart?.Invoke();

        if (animationDrivenReload)
        {
            // Wait for AnimEvent_InspectEnd on the last frame of the inspect clip.
            // safetyTimeout: inspectDuration + 1 s so a missing event doesn't hang forever.
            yield return WaitForAnimSignal(
                () => _animSignal_InspectEnd,
                inspectDuration + 1f,
                "AnimEvent_InspectEnd");

            _animSignal_InspectEnd = false; // consume
        }
        else
        {
            yield return new WaitForSeconds(inspectDuration);
        }

        Log("Inspect end.");
        OnInspectEnd?.Invoke();
        _isInspecting = false;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Firing
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator Co_Fire()
    {
        // Cancel any ongoing inspect — firing takes priority
        CancelInspect();

        if (!_roundInChamber)
        {
            _canShoot = true;
            yield break;
        }

        SetState(WeaponState.Firing);

        // Consume ammo
        _roundInChamber = false;
        if (_currentAmmoInClip > 0)
        {
            _currentAmmoInClip--;
            _roundInChamber = true;
        }

        // Trigger animation
        SetAnimatorTrigger(AnimIsShooting);

        // Visuals
        SpawnMuzzleFlash();
        SpawnBulletCasing();

        // Hitscan
        Vector3 aimDir   = CalculateAimWithSpread();
        Vector3 hitPoint = PerformHitscan(aimDir);

        // Trail
        SpawnBulletTrail(aimDir, hitPoint);

        // Audio
        PlayRandomSound(weaponData.shootSounds, weaponData.shootVolume);

        // Broadcast
        OnWeaponFired?.Invoke(hitPoint);
        NotifyAmmoChanged();

        // Auto-reload prompt / empty state
        if (_currentAmmoInClip <= 0 && !_roundInChamber)
        {
            Log("Clip empty.");
            OnWeaponEmpty?.Invoke();
            SetState(WeaponState.Empty);

            // Optional: auto-reload when empty
            if (_ammoInReserve > 0)
                StartCoroutine(Co_Reload());
        }
        else
        {
            SetState(WeaponState.Idle);
        }

        yield return new WaitForSeconds(weaponData.fireRate);
        _canShoot = true;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Reload — Phased Coroutine
    // ─────────────────────────────────────────────────────────────────────────
    //
    //  Timeline (all delays configurable in WeaponData):
    //
    //  t=0                  t=MagOut          t=MagIn           t=Chamber      t=Total
    //  |── OnReloadStart ───|── OnMagOut ─────|── OnMagIn ──────|─ OnChamber ──|── OnReloadComplete
    //  |                    |  (audio/VFX)     |  (audio/VFX)    |  (optional)  |  (ammo replenished)
    //
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator Co_Reload()
    {
        if (CurrentState == WeaponState.Reloading) yield break;

        // Cancel any ongoing inspect — reloading takes priority
        CancelInspect();

        SetState(WeaponState.Reloading);
        ResetAnimSignals();

        bool wasEmpty       = !_roundInChamber && _currentAmmoInClip == 0;
        bool needsChamber   = wasEmpty || weaponData.requiresManualChamber;
        float safetyTimeout = weaponData.reloadTotalTime + 1f; // generous fallback

        Log($"Reload started | mode={(animationDrivenReload ? "AnimEvent" : "Timed")} | wasEmpty={wasEmpty}");

        // ── Phase 0: Kick off the animation ──────────────────────────────────
        // Use a dedicated empty-reload clip when the gun ran dry so the animator
        // can play a slide-lock → rack variant instead of the normal reload.
        SetAnimatorTrigger(wasEmpty ? AnimReloadEmpty : AnimReload);
        OnReloadStart?.Invoke();

        // ─────────────────────────────────────────────────────────────────────
        if (animationDrivenReload)
        {
            // ══ ANIMATION-DRIVEN MODE ════════════════════════════════════════
            // Each phase waits for an Animation Event signal from
            // AnimationEventReceiver instead of a fixed delay.
            // Signals are consumed (set back to false) after each wait so that
            // events arriving early don't cause the next phase to skip instantly.

            // Phase 1 — wait for AnimEvent_MagOut
            yield return WaitForAnimSignal(() => _animSignal_MagOut, safetyTimeout, "AnimEvent_MagOut");
            _animSignal_MagOut = false; // consume
            Log("Mag out (anim-driven).");
            PlaySound(weaponData.magOutSound, weaponData.reloadVolume);
            OnMagOut?.Invoke();

            // Phase 2 — wait for AnimEvent_MagIn
            yield return WaitForAnimSignal(() => _animSignal_MagIn, safetyTimeout, "AnimEvent_MagIn");
            _animSignal_MagIn = false;  // consume
            Log("Mag in (anim-driven).");
            PlaySound(weaponData.magInSound, weaponData.reloadVolume);
            OnMagIn?.Invoke();

            // Phase 3 — wait for AnimEvent_ChamberRound (conditional)
            if (needsChamber)
            {
                yield return WaitForAnimSignal(() => _animSignal_ChamberRound, safetyTimeout, "AnimEvent_ChamberRound");
                _animSignal_ChamberRound = false; // consume
                Log("Chamber round (anim-driven).");
                PlaySound(weaponData.chamberRoundSound, weaponData.chamberVolume);
                OnChamberRound?.Invoke();
            }

            // Phase 4 — wait for AnimEvent_ReloadEnd before applying ammo
            yield return WaitForAnimSignal(() => _animSignal_ReloadEnd, safetyTimeout, "AnimEvent_ReloadEnd");
            _animSignal_ReloadEnd = false; // consume
        }
        else
        {
            // ══ TIMED MODE ═══════════════════════════════════════════════════
            // Falls back to the delay values in WeaponData.
            // Useful when you have no Animation Events yet.

            // Phase 1 — Mag Out
            yield return new WaitForSeconds(weaponData.reloadMagOutDelay);
            Log("Mag out (timed).");
            PlaySound(weaponData.magOutSound, weaponData.reloadVolume);
            OnMagOut?.Invoke();

            // Phase 2 — Mag In
            yield return new WaitForSeconds(weaponData.reloadMagInDelay);
            Log("Mag in (timed).");
            PlaySound(weaponData.magInSound, weaponData.reloadVolume);
            OnMagIn?.Invoke();

            // Phase 3 — Chamber Round (conditional)
            if (needsChamber && weaponData.reloadChamberDelay > 0f)
            {
                yield return new WaitForSeconds(weaponData.reloadChamberDelay);
                Log("Chamber round (timed).");
                PlaySound(weaponData.chamberRoundSound, weaponData.chamberVolume);
                OnChamberRound?.Invoke();
            }

            // Phase 4 — Wait out the rest of the animation
            float consumed = weaponData.reloadMagOutDelay
                           + weaponData.reloadMagInDelay
                           + (needsChamber ? weaponData.reloadChamberDelay : 0f);
            float remaining = weaponData.reloadTotalTime - consumed;
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);
        }

        // ── Phase 5: Apply ammo math (same for both modes) ───────────────────
        int ammoNeeded = weaponData.clipSize - _currentAmmoInClip;
        int ammoToAdd  = Mathf.Min(ammoNeeded, _ammoInReserve);

        _currentAmmoInClip += ammoToAdd;
        _ammoInReserve     -= ammoToAdd;
        _roundInChamber     = _currentAmmoInClip > 0;

        Log($"Reload complete. Clip={_currentAmmoInClip}/{weaponData.clipSize}  Reserve={_ammoInReserve}");

        NotifyAmmoChanged();
        OnReloadComplete?.Invoke();

        SetState(_currentAmmoInClip > 0 || _roundInChamber ? WeaponState.Idle : WeaponState.Empty);
    }

    /// <summary>
    /// Call this externally or from another system to hard-cancel a reload
    /// mid-animation (e.g. player dies, weapon swapped).
    /// </summary>
    public void CancelReload()
    {
        if (CurrentState != WeaponState.Reloading) return;

        StopAllCoroutines();
        Log("Reload cancelled.");
        OnReloadCancelled?.Invoke();
        SetState(WeaponState.Idle);
        _canShoot = true;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Hitscan & Spread
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires a raycast along <paramref name="aimDir"/> and applies damage to
    /// anything implementing <see cref="IDamageable"/>.
    /// </summary>
    /// <returns>World-space impact point, or Vector3.zero on miss.</returns>
    private Vector3 PerformHitscan(Vector3 aimDir)
    {
        if (spawnBulletPosition == null) return Vector3.zero;

        Vector3 origin = spawnBulletPosition.position;

        if (Physics.Raycast(origin, aimDir, out RaycastHit hit, weaponData.maxRange,
                            aimColliderLayerMask, QueryTriggerInteraction.Ignore))
        {
            Log($"Hit: {hit.collider.name}  distance={hit.distance:F1}m");

            // IDamageable — implement this interface on any damageable object.
            if (hit.collider.TryGetComponent(out IDamageable damageable))
                damageable.TakeDamage(weaponData.weaponDamage, aimDir, hit.point);

            // Physics impulse (works even without IDamageable)
            if (hit.rigidbody != null)
                hit.rigidbody.AddForceAtPosition(aimDir * weaponData.hitImpulseForce,
                                                  hit.point, ForceMode.Impulse);

            // Gizmo data
            _gizmoRayOrigin = origin;
            _gizmoRayEnd    = hit.point;
            return hit.point;
        }

        _gizmoRayOrigin = origin;
        _gizmoRayEnd    = origin + aimDir * weaponData.maxRange;
        return Vector3.zero;
    }

    private Vector3 CalculateAimWithSpread()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null)
            return spawnBulletPosition != null ? spawnBulletPosition.forward : Vector3.forward;

        // Camera-centre ray → world target point
        Ray camRay = _mainCamera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        Vector3 targetPoint = camRay.origin + camRay.direction * 100f;

        if (Physics.Raycast(camRay, out RaycastHit hit, weaponData.maxRange,
                            aimColliderLayerMask, QueryTriggerInteraction.Ignore))
            targetPoint = hit.point;

        Vector3 origin = spawnBulletPosition != null ? spawnBulletPosition.position : transform.position;
        Vector3 aimDir = (targetPoint - origin).normalized;

        // ── Spread multiplier from movement state ─────────────────────────
        float spread = weaponData.baseSpread + _currentSpreadBuildup;

        if (playerMovement != null)
        {
            spread *= playerMovement.state switch
            {
                PlayerMovement.MovementState.standing  => weaponData.standSpreadMultiplier,
                PlayerMovement.MovementState.walking   => weaponData.moveSpreadMultiplier,
                PlayerMovement.MovementState.crouching => weaponData.crouchSpreadMultiplier,
                _                                      => 1f
            };
        }

        _gizmoSpread = spread;

        // ── Apply spread cone ─────────────────────────────────────────────
        if (spread > 0f)
        {
            Vector3 right = Vector3.Cross(Vector3.up, aimDir).normalized;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.Cross(Vector3.forward, aimDir).normalized;
            Vector3 up = Vector3.Cross(aimDir, right).normalized;

            aimDir = (aimDir
                    + right * Random.Range(-spread, spread)
                    + up    * Random.Range(-spread, spread)).normalized;
        }

        _currentSpreadBuildup = Mathf.Min(_currentSpreadBuildup + weaponData.spreadBuildPerShot,
                                           weaponData.maxSpreadBuildup);
        return aimDir;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Visuals
    // ─────────────────────────────────────────────────────────────────────────

    private void SpawnMuzzleFlash()
    {
        if (muzzleFlashPrefab == null || muzzleFlashPoint == null) return;
        Destroy(Instantiate(muzzleFlashPrefab, muzzleFlashPoint.position, muzzleFlashPoint.rotation),
                0.1f);
    }

    private void SpawnBulletCasing()
    {
        if (bulletCasingPrefab == null || casingEjectPoint == null) return;

        GameObject casing = Instantiate(bulletCasingPrefab, casingEjectPoint.position, casingEjectPoint.rotation);
        IgnorePlayerColliders(casing);

        if (casing.TryGetComponent(out Rigidbody rb))
        {
            rb.AddForce(casingEjectPoint.right * weaponData.casingEjectForce
                      + Vector3.up * (weaponData.casingEjectForce * 0.5f), ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * weaponData.casingEjectForce, ForceMode.Impulse);
        }

        Destroy(casing, weaponData.casingDestroyTime);
    }

    private void SpawnBulletTrail(Vector3 aimDir, Vector3 hitPoint)
    {
        if (bulletTrailPrefab == null || spawnBulletPosition == null) return;

        Vector3 startPos = spawnBulletPosition.position;
        Vector3 endPos   = hitPoint != Vector3.zero ? hitPoint : startPos + aimDir * weaponData.maxRange;

        GameObject trailObj = Instantiate(bulletTrailPrefab, startPos, Quaternion.identity);
        if (trailObj.TryGetComponent(out TrailRenderer trail))
            StartCoroutine(Co_MoveTrail(trail, startPos, endPos));
        else
            LogWarning("bulletTrailPrefab has no TrailRenderer component.");
    }

    private IEnumerator Co_MoveTrail(TrailRenderer trail, Vector3 start, Vector3 end)
    {
        float t = 0f;
        float duration = Mathf.Max(trail.time, 0.001f);

        while (t < 1f)
        {
            trail.transform.position = Vector3.Lerp(start, end, t);
            t += Time.deltaTime / duration;
            yield return null;
        }

        trail.transform.position = end;
        Destroy(trail.gameObject, trail.time);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Animation
    // ─────────────────────────────────────────────────────────────────────────

    private void TickAnimations()
    {
        if (gunAnimator == null || playerMovement == null) return;
        gunAnimator.SetBool(AnimIsWalking, playerMovement.state == PlayerMovement.MovementState.walking);
    }

    private void SetAnimatorTrigger(int hash)
    {
        if (gunAnimator != null) gunAnimator.SetTrigger(hash);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Audio
    // ─────────────────────────────────────────────────────────────────────────

    private void PlaySound(AudioClip clip, float volume)
    {
        if (clip == null) return;
        _weaponAudioSource.pitch = Random.Range(0.95f, 1.05f);
        _weaponAudioSource.PlayOneShot(clip, volume);
    }

    private void PlayRandomSound(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0) return;
        PlaySound(clips[Random.Range(0, clips.Length)], volume);
    }

    private static void EnsureSharedImpactAudio()
    {
        if (_sharedImpactAudio != null) return;
        var go = new GameObject("BulletImpactAudio_Shared");
        _sharedImpactAudio = go.AddComponent<AudioSource>();
        _sharedImpactAudio.spatialBlend = 1f;
        _sharedImpactAudio.playOnAwake  = false;
        DontDestroyOnLoad(go);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Tick Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void TickSpreadRecovery()
    {
        if (weaponData == null) return;
        _currentSpreadBuildup = Mathf.Max(0f, _currentSpreadBuildup
                                            - weaponData.spreadRecoveryRate * Time.deltaTime);
    }

    private void TickDryFireCooldown()
    {
        if (_dryFireCooldown > 0f) _dryFireCooldown -= Time.deltaTime;
    }

    private void NotifyAmmoChanged() => OnAmmoChanged?.Invoke(_currentAmmoInClip + (_roundInChamber ? 1 : 0), _ammoInReserve);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Utility
    // ─────────────────────────────────────────────────────────────────────────

    private void IgnorePlayerColliders(GameObject obj)
    {
        if (obj == null) return;
        Collider[] objCols    = obj.GetComponentsInChildren<Collider>();
        Collider[] playerCols = transform.root.GetComponentsInChildren<Collider>();
        foreach (var a in objCols)
            foreach (var b in playerCols)
                Physics.IgnoreCollision(a, b);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug Logging
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[{name}] {msg}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogWarning(string msg) => Debug.LogWarning($"[{name}] ⚠ {msg}", this);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Gizmos (Editor Only)
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // ── Raycast line ──────────────────────────────────────────────────────
        if (showRaycastGizmo && _gizmoRayOrigin != _gizmoRayEnd)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(_gizmoRayOrigin, _gizmoRayEnd);
            Gizmos.DrawSphere(_gizmoRayEnd, 0.05f);
        }

        // ── Spread cone at muzzle ─────────────────────────────────────────────
        if (showSpreadGizmo && spawnBulletPosition != null && _gizmoSpread > 0f)
        {
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
            float coneLength = 5f;
            float radius     = Mathf.Tan(_gizmoSpread) * coneLength;

            Vector3 tip   = spawnBulletPosition.position;
            Vector3 fwd   = spawnBulletPosition.forward;
            Vector3 base_ = tip + fwd * coneLength;

            // Draw 8 lines around the cone perimeter
            Vector3 right = Vector3.Cross(fwd, Vector3.up).normalized;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 2f / 8f;
                Vector3 offset = (Mathf.Cos(angle) * right
                                + Mathf.Sin(angle) * Vector3.up) * radius;
                Gizmos.DrawLine(tip, base_ + offset);
            }

            // Circle at the base of the cone
            UnityEditor.Handles.color = new Color(1f, 0.4f, 0f, 0.5f);
            UnityEditor.Handles.DrawWireDisc(base_, fwd, radius);
        }
    }
#endif

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    // Read-only state for HUD / other systems
    public int         CurrentAmmo     => _currentAmmoInClip;
    public int         ReserveAmmo     => _ammoInReserve;
    public bool        RoundInChamber  => _roundInChamber;
    public bool        IsReloading     => CurrentState == WeaponState.Reloading;
    public bool        IsInspecting    => _isInspecting;
    public float       SpreadBuildup   => _currentSpreadBuildup;

    /// <summary>True when the holster animation has finished. Polled by WeaponSwitcher.</summary>
    public bool HolsterComplete => _holsterComplete;

    /// <summary>True when the draw animation has finished. Polled by WeaponSwitcher.</summary>
    public bool DrawComplete    => _drawComplete;

    /// <summary>
    /// Immediately stops all ongoing activity (fire / reload / inspect) and
    /// returns the weapon to Idle.  Called by WeaponSwitcher before StartHolster().
    /// </summary>
    public void ForceIdle()
    {
        StopAllCoroutines();
        _isInspecting    = false;
        _isSwitching     = false;
        _holsterComplete = false;
        _drawComplete    = false;
        _canShoot        = true;
        ResetAnimSignals();
        SetState(WeaponState.Idle);
        Log("ForceIdle (weapon switcher).");
    }

    /// <summary>
    /// Triggers the Holster animation.  Poll <see cref="HolsterComplete"/>
    /// (or subscribe to <see cref="OnUnequipped"/>) to know when it is safe
    /// to deactivate the GameObject.
    /// </summary>
    public void StartHolster()
    {
        _isSwitching     = true;
        _holsterComplete = false;
        SetState(WeaponState.Switching);
        SetAnimatorTrigger(AnimHolster);
        StartCoroutine(Co_WaitHolster());
        Log("Holster started.");
    }

    /// <summary>
    /// Triggers the Draw animation.  Poll <see cref="DrawComplete"/> to know
    /// when the weapon is ready to accept input.
    /// </summary>
    public void StartDraw()
    {
        _isSwitching  = true;
        _drawComplete = false;
        SetState(WeaponState.Switching);
        SetAnimatorTrigger(AnimDraw);
        StartCoroutine(Co_WaitDraw());
        Log("Draw started.");
    }

    /// <summary>
    /// Called by WeaponSwitcher once the draw is confirmed complete.
    /// Marks the weapon as live and ready to fire.
    /// </summary>
    public void NotifyEquipped()
    {
        _isSwitching = false;
        _canShoot    = true;
        SetState(WeaponState.Idle);
        OnEquipped?.Invoke();
        Log("Equipped.");
    }

    /// <summary>
    /// Called by WeaponSwitcher after the GameObject is deactivated.
    /// </summary>
    public void NotifyUnequipped()
    {
        _isSwitching = false;
        OnUnequipped?.Invoke();
        Log("Unequipped.");
    }

    private IEnumerator Co_WaitHolster()
    {
        if (animationDrivenReload)
        {
            // Wait for AnimEvent_HolsterEnd fired by AnimationEventReceiver.
            yield return WaitForAnimSignal(
                () => _animSignal_HolsterEnd,
                holsterDuration + 1f,
                "AnimEvent_HolsterEnd");
            _animSignal_HolsterEnd = false;
        }
        else
        {
            yield return new WaitForSeconds(holsterDuration);
        }

        _holsterComplete = true;
        Log("Holster complete.");
    }

    private IEnumerator Co_WaitDraw()
    {
        if (animationDrivenReload)
        {
            // Wait for AnimEvent_DrawEnd fired by AnimationEventReceiver.
            yield return WaitForAnimSignal(
                () => _animSignal_DrawEnd,
                drawDuration + 1f,
                "AnimEvent_DrawEnd");
            _animSignal_DrawEnd = false;
        }
        else
        {
            yield return new WaitForSeconds(drawDuration);
        }

        _drawComplete = true;
        Log("Draw complete.");
    }

    #endregion
}