using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Primary weapon controller for an FPS character.
/// All tunable values live in a <see cref="WeaponData"/> ScriptableObject.
///
/// EXTENDING:
///   - Subscribe to any public event (OnWeaponFired, OnMagOut, OnAmmoChanged, etc.)
///     from external components — no modifications needed here.
///   - Add new input locks by checking <see cref="IsInputBlocked"/> before any action.
///   - New fire modes: add a FireMode enum to WeaponData and branch inside Co_Fire().
///   - New reload phases: extend Co_Reload() with additional anim-signal wait steps.
///
/// DEBUG:
///   - Toggle showDebugLogs in Inspector to see state transitions and events.
///   - showSpreadGizmo / showRaycastGizmo visualize spread cone and hit ray in Scene view.
///   - Use FPSDebug.GlobalEnabled to silence all FPS logs in builds.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class WeaponsController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Weapon Data")]
    public WeaponData    weaponData;
    public RecoilProfile recoilProfile;

    [Header("References")]
    public Animator                   gunAnimator;
    public PlayerMovement             playerMovement;
    public LayerMask                  aimColliderLayerMask;
    public Transform                  leftHandBone;
    public TwoBoneIKConstraint        leftArmIK;

    [Header("Transform Points")]
    public Transform muzzleFlashPoint;
    public Transform spawnBulletPosition;

    [Header("Prefabs")]
    public GameObject bulletTrailPrefab;
    public GameObject muzzleFlashPrefab;

    [Header("Reload Mode")]
    public bool animationDrivenReload = true;
    public bool IsEmpty => _currentAmmoInClip <= 0 && !_roundInChamber;

    [Header("Inspect")]
    public KeyCode inspectKey     = KeyCode.I;
    public float   inspectDuration = 2.5f;

    [Header("Switch Animations")]
    public float holsterDuration = 0.4f;
    public float drawDuration    = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs   = true;
#if UNITY_EDITOR
    [SerializeField] private bool showSpreadGizmo  = true;
    [SerializeField] private bool showRaycastGizmo = true;
#endif

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    // ── Reload ────────────────────────────────────────────────────────────────
    public event Action           OnReloadStart;
    public event Action           OnMagOut;
    public event Action           OnMagIn;
    public event Action           OnChamberRound;
    public event Action           OnReloadComplete;
    public event Action           OnReloadCancelled;

    // ── Equip / Unequip ───────────────────────────────────────────────────────
    public event Action           OnEquipped;
    public event Action           OnUnequipped;

    // ── Firing ────────────────────────────────────────────────────────────────
    public event Action<Vector3>  OnWeaponFired;     // hitPoint
    public event Action<int, int> OnAmmoChanged;     // (clip, reserve)
    public event Action           OnDryFire;
    public event Action           OnWeaponEmpty;

    // ── Inspect ───────────────────────────────────────────────────────────────
    public event Action           OnInspectStart;
    public event Action           OnInspectEnd;

    // ── Bolt Action ───────────────────────────────────────────────────────────
    public event Action           OnBoltOut;
    public event Action           OnBoltIn;

    // ── Switch (fired by WeaponSwitcherProcedural, not internally) ────────────
    // No internal events here — switcher owns the switch lifecycle.

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Weapon State Machine
    // ─────────────────────────────────────────────────────────────────────────

    public enum WeaponState
    {
        Idle,
        Firing,
        Reloading,
        Empty,
        Switching,      // weapon is holstering or being drawn — ALL input blocked
        BoltCycling,
        Inspecting,     // separated from _isInspecting bool for clarity
    }

    public WeaponState CurrentState { get; private set; } = WeaponState.Idle;

    private void SetState(WeaponState next)
    {
        if (CurrentState == next) return;
        Log($"WeaponState: {CurrentState} → {next}");
        CurrentState = next;
    }

    /// <summary>
    /// True whenever the player cannot interact with the weapon.
    /// Gate ALL new input paths through this instead of duplicating checks.
    /// </summary>
    public bool IsInputBlocked =>
        _isSwitching
        || CurrentState == WeaponState.Switching;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Animation-Driven Entry Points  (called by AnimationEventReceiver)
    // ─────────────────────────────────────────────────────────────────────────

    private bool _animSignal_MagOut;
    private bool _animSignal_MagIn;
    private bool _animSignal_ChamberRound;
    private bool _animSignal_ReloadEnd;
    private bool _animSignal_InspectEnd;
    private bool _animSignal_HolsterEnd;
    private bool _animSignal_DrawEnd;
    private bool _animSignal_BoltOut;
    private bool _animSignal_BoltIn;

    public void OnBoltOut_AnimDriven()       { _animSignal_BoltOut      = true; OnBoltOut?.Invoke(); }
    public void OnBoltIn_AnimDriven()        { _animSignal_BoltIn       = true; OnBoltIn?.Invoke(); }
    public void OnReloadStart_AnimDriven()   { /* hook point — subscribe via event if needed */ }
    public void OnMagOut_AnimDriven()        { _animSignal_MagOut       = true; }
    public void OnMagIn_AnimDriven()         { _animSignal_MagIn        = true; }
    public void OnChamberRound_AnimDriven()  { _animSignal_ChamberRound = true; }
    public void OnReloadEnd_AnimDriven()     { _animSignal_ReloadEnd    = true; }
    public void OnInspectEnd_AnimDriven()    { _animSignal_InspectEnd   = true; }
    public void OnHolsterEnd_AnimDriven()    { _animSignal_HolsterEnd   = true; }
    public void OnDrawEnd_AnimDriven()       { _animSignal_DrawEnd      = true; }

    // Inspect-specific sound hooks
    public void OnInspectMagOut_AnimDriven() => PlaySound(weaponData.magOutSound, weaponData.reloadVolume);
    public void OnInspectMagIn_AnimDriven()  => PlaySound(weaponData.magInSound,  weaponData.reloadVolume);

    private void ResetAnimSignals()
    {
        _animSignal_MagOut       = false;
        _animSignal_MagIn        = false;
        _animSignal_ChamberRound = false;
        _animSignal_ReloadEnd    = false;
        _animSignal_InspectEnd   = false;
        _animSignal_HolsterEnd   = false;
        _animSignal_DrawEnd      = false;
        _animSignal_BoltOut      = false;
        _animSignal_BoltIn       = false;
    }

    /// <summary>
    /// Waits until <paramref name="signal"/> returns true, or until
    /// <paramref name="timeout"/> seconds pass (safety net when no anim events are wired).
    /// </summary>
    private IEnumerator WaitForAnimSignal(Func<bool> signal, float timeout, string signalName)
    {
        float elapsed = 0f;
        while (!signal() && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (elapsed >= timeout)
            LogWarning($"Timed out waiting for anim signal '{signalName}'.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Animator Parameter Hashes
    // ─────────────────────────────────────────────────────────────────────────

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
    private bool  _isInspecting;
    private bool  _isSwitching;         // set by StartHolster / StartDraw / ForceIdle
    private bool  _holsterComplete;
    private bool  _drawComplete;
    private bool  _isADS;               // set externally by ProceduralWeaponAnimator

    private float _currentSpreadBuildup;
    private float _dryFireCooldown;
    private const float DryFireCooldownTime = 0.3f;

    private Coroutine   _inspectCoroutine;
    private Coroutine   _reloadCoroutine;
    private AudioSource _weaponAudioSource;
    private Camera      _mainCamera;

    // Gizmo data (editor only)
    private Vector3 _gizmoRayOrigin;
    private Vector3 _gizmoRayEnd;
    private float   _gizmoSpread;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake() => InitAmmo();

    private void Start()
    {
        ValidateSetup();
        InitAudio();
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
        _weaponAudioSource              = GetComponent<AudioSource>();
        _weaponAudioSource.spatialBlend = 1f;
        _weaponAudioSource.playOnAwake  = false;
    }

    private void ValidateSetup()
    {
        if (weaponData          == null) LogWarning("weaponData not assigned.");
        if (gunAnimator         == null) LogWarning("gunAnimator not assigned — animations skipped.");
        if (playerMovement      == null) LogWarning("playerMovement not assigned — spread multipliers won't apply.");
        if (spawnBulletPosition == null) LogWarning("spawnBulletPosition not assigned — hitscan uses transform.position.");
        if (muzzleFlashPoint    == null) LogWarning("muzzleFlashPoint not assigned — muzzle flash skipped.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public ADS Bridge
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Called by ProceduralWeaponAnimator every frame to sync ADS state.</summary>
    public void SetADS(bool isADS) => _isADS = isADS;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Input Handling
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleInput()
    {
        // ── MASTER GATE — no input while switching ──────────────────────────
        // IsInputBlocked covers both _isSwitching and WeaponState.Switching,
        // so a single check here locks everything below.
        if (IsInputBlocked) return;

        HandleFireInput();
        HandleReloadInput();
        HandleInspectInput();
    }

    private void HandleFireInput()
    {
        bool fireInput = weaponData.isFullAuto
            ? Input.GetMouseButton(0)
            : Input.GetMouseButtonDown(0);

        if (fireInput)
        {
            bool canFire = _canShoot
                        && CurrentState == WeaponState.Idle
                        && (_currentAmmoInClip > 0 || _roundInChamber);

            if (canFire)
            {
                _canShoot = false;
                StartCoroutine(Co_Fire());
            }
            else if (Input.GetMouseButtonDown(0)
                  && CurrentState != WeaponState.Reloading
                  && _currentAmmoInClip <= 0 && !_roundInChamber)
            {
                TriggerDryFire();
            }
        }
    }

    private void HandleReloadInput()
    {
        if (Input.GetKeyDown(KeyCode.R)) TryStartReload();
    }

    private void HandleInspectInput()
    {
        if (Input.GetKeyDown(inspectKey)) TryStartInspect();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void TryStartReload()
    {
        if (playerMovement != null)
        {
            if (playerMovement.sliding)     return;
            if (playerMovement.wallrunning) return;
            if (playerMovement.wallSliding) return;
        }

        if (CurrentState == WeaponState.BoltCycling) return;
        if (CurrentState == WeaponState.Reloading)   return;
        if (_currentAmmoInClip >= weaponData.clipSize) return;
        if (_ammoInReserve <= 0)                       return;

        _reloadCoroutine = StartCoroutine(Co_Reload());
    }

    private void TriggerDryFire()
    {
        if (_dryFireCooldown > 0f) return;
        Log("Dry fire.");
        PlaySound(weaponData.dryFireSound, weaponData.dryFireVolume);
        _dryFireCooldown = DryFireCooldownTime;
        OnDryFire?.Invoke();
    }

    private void TryStartInspect()
    {
        if (_isInspecting)               return;
        if (CurrentState != WeaponState.Idle) return;
        _inspectCoroutine = StartCoroutine(Co_Inspect());
    }

    public void CancelInspect()
    {
        if (!_isInspecting) return;
        if (_inspectCoroutine != null) StopCoroutine(_inspectCoroutine);
        _isInspecting          = false;
        _animSignal_InspectEnd = false;
        Log("Inspect cancelled.");
        OnInspectEnd?.Invoke();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Inspect Coroutine
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator Co_Inspect()
    {
        _isInspecting          = true;
        _animSignal_InspectEnd = false;
        SetState(WeaponState.Inspecting);
        Log("Inspect start.");
        SetAnimatorTrigger(AnimInspect);
        OnInspectStart?.Invoke();

        if (animationDrivenReload)
        {
            yield return WaitForAnimSignal(
                () => _animSignal_InspectEnd,
                inspectDuration + 1f, "AnimEvent_InspectEnd");
            _animSignal_InspectEnd = false;
        }
        else
        {
            yield return new WaitForSeconds(inspectDuration);
        }

        Log("Inspect end.");
        OnInspectEnd?.Invoke();
        _isInspecting     = false;
        _inspectCoroutine = null;
        SetState(WeaponState.Idle);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Firing
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator Co_Fire()
    {
        _animSignal_BoltOut = false;
        _animSignal_BoltIn  = false;
        CancelInspect();

        if (!_roundInChamber) { _canShoot = true; yield break; }

        SetState(WeaponState.Firing);

        _roundInChamber = false;
        if (_currentAmmoInClip > 0)
        {
            _currentAmmoInClip--;
            _roundInChamber = true;
        }

        SetAnimatorTrigger(AnimIsShooting);
        SpawnMuzzleFlash();

        Vector3 aimDir   = CalculateAimWithSpread();
        Vector3 hitPoint = PerformHitscan(aimDir);

        SpawnBulletTrail(aimDir, hitPoint);
        PlayRandomSound(weaponData.shootSounds, weaponData.shootVolume);

        OnWeaponFired?.Invoke(hitPoint);
        NotifyAmmoChanged();

        bool clipEmpty = _currentAmmoInClip <= 0 && !_roundInChamber;
        SetState(clipEmpty ? WeaponState.Empty : WeaponState.Idle);
        if (clipEmpty) OnWeaponEmpty?.Invoke();

        if (weaponData.isBoltAction)
            yield return Co_BoltCycle();
        else
        {
            yield return new WaitForSeconds(weaponData.fireRate);
            _canShoot = true;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Bolt Cycle
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles the bolt-action cycle after firing.
    /// If the player starts sliding AFTER the bolt-out signal, the animator
    /// freezes at that frame and resumes the moment the slide ends.
    /// </summary>
    private IEnumerator Co_BoltCycle()
    {
        SetState(WeaponState.BoltCycling);

        // ── Bolt out ───────────────────────────────────────────────────────
        if (animationDrivenReload)
        {
            yield return WaitForAnimSignal(
                () => _animSignal_BoltOut,
                weaponData.boltOutDuration + 0.5f, "AnimEvent_BoltOut");
            _animSignal_BoltOut = false;
        }
        else
        {
            yield return new WaitForSeconds(weaponData.boltOutDuration);
        }

        PlayRandomSound(weaponData.boltOutSounds, weaponData.boltOutVolume);
        OnBoltOut?.Invoke();

        // ── Slide freeze — hold animation at bolt-out frame ────────────────
        // If sliding, freeze the animator exactly here (bolt is visually open)
        // and wait until the slide ends before continuing to bolt-in.
        if (playerMovement != null && playerMovement.sliding)
        {
            Log("Bolt frozen at bolt-out — waiting for slide to end.");
            FreezeAnimator();

            while (playerMovement.sliding)
                yield return null;

            ResumeAnimator();
            Log("Slide ended — resuming bolt-in.");
        }

        // ── Bolt in ────────────────────────────────────────────────────────
        if (animationDrivenReload)
        {
            yield return WaitForAnimSignal(
                () => _animSignal_BoltIn,
                weaponData.boltInDuration + 0.5f, "AnimEvent_BoltIn");
            _animSignal_BoltIn = false;
        }
        else
        {
            yield return new WaitForSeconds(weaponData.boltInDuration);
        }

        PlaySound(weaponData.boltInSound, weaponData.boltInVolume);
        OnBoltIn?.Invoke();

        _canShoot = true;
        bool isEmpty = _currentAmmoInClip <= 0 && !_roundInChamber;
        SetState(isEmpty ? WeaponState.Empty : WeaponState.Idle);
    }

    /// <summary>Freezes the gun Animator in place (Animator.speed = 0).</summary>
    private void FreezeAnimator()
    {
        if (gunAnimator != null) gunAnimator.speed = 0f;
    }

    /// <summary>Restores the gun Animator to normal playback speed.</summary>
    private void ResumeAnimator()
    {
        if (gunAnimator != null) gunAnimator.speed = 1f;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Reload — Phased Coroutine
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator Co_Reload()
    {
        if (CurrentState == WeaponState.Reloading) yield break;

        CancelInspect();
        SetState(WeaponState.Reloading);
        ResetAnimSignals();

        bool wasEmpty     = !_roundInChamber && _currentAmmoInClip == 0;
        bool needsChamber = wasEmpty || weaponData.requiresManualChamber;
        float timeout     = weaponData.reloadTotalTime + 1f;

        Log($"Reload start | anim={animationDrivenReload} | empty={wasEmpty}");
        SetAnimatorTrigger(wasEmpty ? AnimReloadEmpty : AnimReload);
        OnReloadStart?.Invoke();

        if (animationDrivenReload)
        {
            yield return WaitForAnimSignal(() => _animSignal_MagOut, timeout, "AnimEvent_MagOut");
            _animSignal_MagOut = false;
            Log("Mag out (anim).");
            PlaySound(weaponData.magOutSound, weaponData.reloadVolume);
            OnMagOut?.Invoke();

            yield return WaitForAnimSignal(() => _animSignal_MagIn, timeout, "AnimEvent_MagIn");
            _animSignal_MagIn = false;
            Log("Mag in (anim).");
            PlaySound(weaponData.magInSound, weaponData.reloadVolume);
            OnMagIn?.Invoke();

            if (needsChamber)
            {
                yield return WaitForAnimSignal(() => _animSignal_ChamberRound, timeout, "AnimEvent_ChamberRound");
                _animSignal_ChamberRound = false;
                Log("Chamber (anim).");
                PlaySound(weaponData.chamberRoundSound, weaponData.chamberVolume);
                OnChamberRound?.Invoke();
            }
        }
        else
        {
            yield return new WaitForSeconds(weaponData.reloadMagOutDelay);
            Log("Mag out (timed).");
            PlaySound(weaponData.magOutSound, weaponData.reloadVolume);
            OnMagOut?.Invoke();

            yield return new WaitForSeconds(weaponData.reloadMagInDelay);
            Log("Mag in (timed).");
            PlaySound(weaponData.magInSound, weaponData.reloadVolume);
            OnMagIn?.Invoke();

            if (needsChamber && weaponData.reloadChamberDelay > 0f)
            {
                yield return new WaitForSeconds(weaponData.reloadChamberDelay);
                Log("Chamber (timed).");
                PlaySound(weaponData.chamberRoundSound, weaponData.chamberVolume);
                OnChamberRound?.Invoke();
            }
        }

        // Wait out any remaining animation time
        float consumed  = weaponData.reloadMagOutDelay + weaponData.reloadMagInDelay
                        + (needsChamber ? weaponData.reloadChamberDelay : 0f);
        float remaining = weaponData.reloadTotalTime - consumed;
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        // Apply ammo
        int needed         = weaponData.clipSize - _currentAmmoInClip;
        int toAdd          = Mathf.Min(needed, _ammoInReserve);
        _currentAmmoInClip += toAdd;
        _ammoInReserve     -= toAdd;
        _roundInChamber     = _currentAmmoInClip > 0;

        Log($"Reload complete. Clip={_currentAmmoInClip}/{weaponData.clipSize}  Reserve={_ammoInReserve}");
        NotifyAmmoChanged();
        OnReloadComplete?.Invoke();
        SetState(_currentAmmoInClip > 0 || _roundInChamber ? WeaponState.Idle : WeaponState.Empty);
    }

    public void CancelReload()
    {
        if (CurrentState != WeaponState.Reloading) return;
        if (_reloadCoroutine != null) { StopCoroutine(_reloadCoroutine); _reloadCoroutine = null; }
        ResumeAnimator();       // safety in case reload also gets a freeze mechanic later
        ResetAnimSignals();
        Log("Reload cancelled.");
        OnReloadCancelled?.Invoke();
        SetState(WeaponState.Idle);
        _canShoot = true;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Hitscan & Spread
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3 PerformHitscan(Vector3 aimDir)
    {
        if (spawnBulletPosition == null) return Vector3.zero;

        Vector3 origin = spawnBulletPosition.position;
        if (Physics.Raycast(origin, aimDir, out RaycastHit hit, weaponData.maxRange,
                            aimColliderLayerMask, QueryTriggerInteraction.Ignore))
        {
            Log($"Hit: {hit.collider.name}  dist={hit.distance:F1}m");

            if (hit.collider.TryGetComponent(out IDamageable damageable))
                damageable.TakeDamage(weaponData.weaponDamage, aimDir, hit.point);

            if (hit.rigidbody != null)
                hit.rigidbody.AddForceAtPosition(aimDir * weaponData.hitImpulseForce,
                                                  hit.point, ForceMode.Impulse);
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

        Ray     camRay      = _mainCamera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        Vector3 targetPoint = camRay.origin + camRay.direction * 100f;

        if (Physics.Raycast(camRay, out RaycastHit hit, weaponData.maxRange,
                            aimColliderLayerMask, QueryTriggerInteraction.Ignore))
            targetPoint = hit.point;

        Vector3 origin = spawnBulletPosition != null ? spawnBulletPosition.position : transform.position;
        Vector3 aimDir = (targetPoint - origin).normalized;

        // ── Spread ────────────────────────────────────────────────────────
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

        if (_isADS) spread *= weaponData.adsSpreadMultiplier;

        _gizmoSpread = spread;

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

        _currentSpreadBuildup = Mathf.Min(
            _currentSpreadBuildup + weaponData.spreadBuildPerShot,
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
        Destroy(Instantiate(muzzleFlashPrefab, muzzleFlashPoint.position, muzzleFlashPoint.rotation), 0.1f);
    }

    private void SpawnBulletTrail(Vector3 aimDir, Vector3 hitPoint)
    {
        if (bulletTrailPrefab == null || spawnBulletPosition == null) return;

        Vector3 start = spawnBulletPosition.position;
        Vector3 end   = hitPoint != Vector3.zero ? hitPoint : start + aimDir * weaponData.maxRange;

        GameObject trailObj = Instantiate(bulletTrailPrefab, start, Quaternion.identity);
        if (trailObj.TryGetComponent(out TrailRenderer trail))
            StartCoroutine(Co_MoveTrail(trail, start, end));
        else
            LogWarning("bulletTrailPrefab has no TrailRenderer.");
    }

    private IEnumerator Co_MoveTrail(TrailRenderer trail, Vector3 start, Vector3 end)
    {
        float t        = 0f;
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
    #region Animation Tick
    // ─────────────────────────────────────────────────────────────────────────

    private void TickAnimations()
    {
        if (gunAnimator == null || playerMovement == null) return;
        gunAnimator.SetBool(AnimIsWalking,
            playerMovement.state == PlayerMovement.MovementState.walking);
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

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Tick Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void TickSpreadRecovery()
    {
        if (weaponData == null) return;
        _currentSpreadBuildup = Mathf.Max(0f,
            _currentSpreadBuildup - weaponData.spreadRecoveryRate * Time.deltaTime);
    }

    private void TickDryFireCooldown()
    {
        if (_dryFireCooldown > 0f) _dryFireCooldown -= Time.deltaTime;
    }

    private void NotifyAmmoChanged() =>
        OnAmmoChanged?.Invoke(_currentAmmoInClip + (_roundInChamber ? 1 : 0), _ammoInReserve);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    public int   CurrentAmmo     => _currentAmmoInClip;
    public int   ReserveAmmo     => _ammoInReserve;
    public bool  RoundInChamber  => _roundInChamber;
    public bool  IsReloading     => CurrentState == WeaponState.Reloading;
    public bool  IsInspecting    => _isInspecting;
    public bool  IsSwitching     => _isSwitching;
    public float SpreadBuildup   => _currentSpreadBuildup;
    public bool  IsADS           => _isADS;
    public bool  HolsterComplete => _holsterComplete;
    public bool  DrawComplete    => _drawComplete;

    public void PlayBoltOutSound() => PlayRandomSound(weaponData.boltOutSounds, weaponData.boltOutVolume);
    public void PlayBoltInSound()  => PlaySound(weaponData.boltInSound, weaponData.boltInVolume);

    /// <summary>
    /// Hard-resets all state — called by WeaponSwitcherProcedural on outgoing weapon.
    /// Also restores animator speed in case it was frozen during a bolt-slide hold.
    /// </summary>
    public void ForceIdle()
    {
        StopAllCoroutines();
        ResumeAnimator();       // safety — restore speed if frozen mid-bolt
        _isInspecting    = false;
        _isSwitching     = false;
        _holsterComplete = false;
        _drawComplete    = false;
        _canShoot        = true;
        ResetAnimSignals();
        SetState(WeaponState.Idle);
        Log("ForceIdle.");
    }

    /// <summary>
    /// Immediately locks all input without stopping coroutines or resetting ammo state.
    /// Call from WeaponSwitcherProcedural the instant a switch is requested — this is
    /// the very first thing that happens so there is zero frame gap before input is blocked.
    /// </summary>
    public void SetSwitching(bool on)
    {
        _isSwitching = on;
        if (on)
        {
            SetState(WeaponState.Switching);
            Log("Input locked — switch initiated.");
        }
    }

    /// <summary>Begins holster sequence. Blocks all input until <see cref="NotifyEquipped"/> is called.</summary>
    public void StartHolster()
    {
        _isSwitching     = true;
        _holsterComplete = false;
        SetState(WeaponState.Switching);
        SetAnimatorTrigger(AnimHolster);
        StartCoroutine(Co_WaitHolster());
        Log("Holster started.");
    }

    /// <summary>Begins draw sequence. Input remains blocked until <see cref="NotifyEquipped"/> is called.</summary>
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
    /// Call after the weapon is fully drawn and ready to use.
    /// Clears the switch lock and fires <see cref="OnEquipped"/>.
    /// </summary>
    public void NotifyEquipped()
    {
        _isSwitching = false;
        _canShoot    = true;
        SetState(WeaponState.Idle);
        OnEquipped?.Invoke();
        Log("Equipped.");
    }

    /// <summary>Call when this weapon is no longer the active weapon.</summary>
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
            yield return WaitForAnimSignal(
                () => _animSignal_HolsterEnd, holsterDuration + 1f, "AnimEvent_HolsterEnd");
            _animSignal_HolsterEnd = false;
        }
        else yield return new WaitForSeconds(holsterDuration);

        _holsterComplete = true;
        Log("Holster complete.");
    }

    private IEnumerator Co_WaitDraw()
    {
        if (animationDrivenReload)
        {
            yield return WaitForAnimSignal(
                () => _animSignal_DrawEnd, drawDuration + 1f, "AnimEvent_DrawEnd");
            _animSignal_DrawEnd = false;
        }
        else yield return new WaitForSeconds(drawDuration);

        _drawComplete = true;
        Log("Draw complete.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[{name}] {msg}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogWarning(string msg) => Debug.LogWarning($"[{name}] ⚠ {msg}", this);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (showRaycastGizmo && _gizmoRayEnd != Vector3.zero)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(_gizmoRayOrigin, _gizmoRayEnd);
        }
        if (showSpreadGizmo && _gizmoSpread > 0f && spawnBulletPosition != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
            Gizmos.DrawWireSphere(spawnBulletPosition.position, _gizmoSpread);
        }
    }
#endif

    #endregion
}