using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Primary weapon controller for an FPS character.
/// All tunable values live in a <see cref="WeaponData"/> ScriptableObject.
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
    public AnimatorOverrideController weaponAnimOverride;
    public PlayerMovement             playerMovement;
    public LayerMask                  aimColliderLayerMask;
    public Transform leftHandBone;
    public TwoBoneIKConstraint leftArmIK;

    [Header("Transform Points")]
    public Transform muzzleFlashPoint;
    public Transform spawnBulletPosition;

    [Header("Prefabs")]
    public GameObject bulletTrailPrefab;
    public GameObject muzzleFlashPrefab;

    [Header("Casing")]
    [Tooltip("Assign the CasingEjector component on this weapon. Leave null to skip casing.")]
    public CasingEjector casingEjector;

    [Header("Reload Mode")]
    public bool animationDrivenReload = true;
    public bool IsEmpty => _currentAmmoInClip <= 0 && !_roundInChamber;

    [Header("Inspect")]
    public KeyCode inspectKey    = KeyCode.I;
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

    public event Action              OnReloadStart;
    public event Action              OnMagOut;
    public event Action              OnMagIn;
    public event Action              OnChamberRound;
    public event Action              OnReloadComplete;
    public event Action              OnReloadCancelled;
    public event Action              OnEquipped;
    public event Action              OnUnequipped;
    public event Action<Vector3>     OnWeaponFired;
    public event Action<int, int>    OnAmmoChanged;   // (clip, reserve)
    public event Action              OnDryFire;
    public event Action              OnWeaponEmpty;
    public event Action              OnInspectStart;
    public event Action              OnInspectEnd;
    public event Action              OnBoltOut;
    public event Action              OnBoltIn;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Animation-Driven Entry Points
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
    public void OnReloadStart_AnimDriven()   { }
    public void OnMagOut_AnimDriven()        { _animSignal_MagOut       = true; }
    public void OnMagIn_AnimDriven()         { _animSignal_MagIn        = true; }
    public void OnChamberRound_AnimDriven()  { _animSignal_ChamberRound = true; }
    public void OnReloadEnd_AnimDriven()     { _animSignal_ReloadEnd    = true; }
    public void OnInspectEnd_AnimDriven()    { _animSignal_InspectEnd   = true; }
    public void OnHolsterEnd_AnimDriven()    { _animSignal_HolsterEnd   = true; }
    public void OnDrawEnd_AnimDriven()       { _animSignal_DrawEnd      = true; }

    public void OnInspectMagOut_AnimDriven() => PlaySound(weaponData.magOutSound,  weaponData.reloadVolume);
    public void OnInspectMagIn_AnimDriven()  => PlaySound(weaponData.magInSound,   weaponData.reloadVolume);

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

    /// <summary>
    /// Bolt cycle: waits for anim-driven signals when available,
    /// falls back to timed delays if no animation events are set up.
    /// </summary>
    private IEnumerator Co_BoltCycle()
    {
        SetState(WeaponState.BoltCycling);

        // ── Bolt out ──────────────────────────────────────────────────────
        if (animationDrivenReload)
        {
            yield return WaitForAnimSignal(
                () => _animSignal_BoltOut,
                weaponData.boltOutDuration + 0.5f,
                "AnimEvent_BoltOut");
            _animSignal_BoltOut = false;
        }
        else
        {
            yield return new WaitForSeconds(weaponData.boltOutDuration);
        }

        PlayRandomSound(weaponData.boltOutSounds, weaponData.boltOutVolume);
        OnBoltOut?.Invoke();

        // ── Bolt in ───────────────────────────────────────────────────────
        if (animationDrivenReload)
        {
            yield return WaitForAnimSignal(
                () => _animSignal_BoltIn,
                weaponData.boltInDuration + 0.5f,
                "AnimEvent_BoltIn");
            _animSignal_BoltIn = false;
        }
        else
        {
            yield return new WaitForSeconds(weaponData.boltInDuration);
        }

        PlaySound(weaponData.boltInSound, weaponData.boltInVolume);
        OnBoltIn?.Invoke();

        _canShoot = true;
        SetState((_currentAmmoInClip <= 0 && !_roundInChamber) ? WeaponState.Empty : WeaponState.Idle);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Weapon State Machine
    // ─────────────────────────────────────────────────────────────────────────

    public enum WeaponState { Idle, Firing, Reloading, Empty, Switching, BoltCycling }

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

    private Grappling  _grapplingModule;

    private int   _currentAmmoInClip;
    private int   _ammoInReserve;
    private bool  _roundInChamber;
    private bool  _canShoot;
    private bool  _isInspecting;
    private bool  _isSwitching;
    private bool  _holsterComplete;
    private bool  _drawComplete;

    // ADS state — set externally by ProceduralWeaponAnimator via SetADS()
    private bool  _isADS;

    private float _currentSpreadBuildup;
    private float _dryFireCooldown;
    private const float DryFireCooldownTime = 0.3f;

    private Coroutine   _inspectCoroutine;
    private Coroutine   _reloadCoroutine;
    private AudioSource _weaponAudioSource;
    private Camera      _mainCamera;

    private Vector3 _gizmoRayOrigin;
    private Vector3 _gizmoRayEnd;
    private float   _gizmoSpread;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()  => InitAmmo();

    private void Start()
    {
        _grapplingModule = GetComponentInParent<Grappling>();
        ValidateSetup();
        ApplyAnimationOverride();
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

    private void ApplyAnimationOverride()
    {
        if (gunAnimator == null || weaponAnimOverride == null) return;
        gunAnimator.runtimeAnimatorController = weaponAnimOverride;
        for (int i = 1; i < gunAnimator.layerCount; i++)
            gunAnimator.SetLayerWeight(i, 1f);
    }

    public void SwapAnimationClips() => ApplyAnimationOverride();

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
        if (weaponData          == null) LogWarning("weaponData is not assigned!");
        if (gunAnimator         == null) LogWarning("gunAnimator is not assigned — animations will be skipped.");
        if (playerMovement      == null) LogWarning("playerMovement is not assigned — spread multipliers won't apply.");
        if (spawnBulletPosition == null) LogWarning("spawnBulletPosition is not assigned — hitscan will use transform.position.");
        if (muzzleFlashPoint    == null) LogWarning("muzzleFlashPoint is not assigned — muzzle flash will be skipped.");
        if (casingEjector       == null) LogWarning("casingEjector is not assigned — casings will not spawn.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public ADS Bridge
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ProceduralWeaponAnimator every frame to keep spread in sync with ADS state.
    /// </summary>
    public void SetADS(bool isADS) => _isADS = isADS;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Input Handling
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleInput()
    {
        if (_isSwitching) return;

        bool fireInput = weaponData.isFullAuto
            ? Input.GetMouseButton(0)
            : Input.GetMouseButtonDown(0);

        if (fireInput)
        {
            if (_canShoot && CurrentState == WeaponState.Idle && (_currentAmmoInClip > 0 || _roundInChamber))
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

        if (Input.GetKeyDown(KeyCode.R))       TryStartReload();
        if (Input.GetKeyDown(inspectKey))       TryStartInspect();
    }

    private void TryStartReload()
    {
        if (_grapplingModule != null && _grapplingModule.IsGrappling()) return;
        if (playerMovement.sliding) return;
        if (playerMovement.wallrunning) return;
        if (playerMovement.wallSliding) return;
        if (CurrentState == WeaponState.BoltCycling)  return;
        if (CurrentState == WeaponState.Reloading)    return;
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
        if (_isInspecting) return;
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

    private IEnumerator Co_Inspect()
    {
        _isInspecting          = true;
        _animSignal_InspectEnd = false;
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

        // ── Casing via pooled ejector ──────────────────────────────────────
        casingEjector?.Eject();

        Vector3 aimDir   = CalculateAimWithSpread();
        Vector3 hitPoint = PerformHitscan(aimDir);

        SpawnBulletTrail(aimDir, hitPoint);
        PlayRandomSound(weaponData.shootSounds, weaponData.shootVolume);

        OnWeaponFired?.Invoke(hitPoint);
        NotifyAmmoChanged();

        if (_currentAmmoInClip <= 0 && !_roundInChamber)
        {
            Log("Clip empty.");
            OnWeaponEmpty?.Invoke();
            SetState(WeaponState.Empty);

            bool isGrappling = _grapplingModule != null && _grapplingModule.IsGrappling();
            if (_ammoInReserve > 0 && !isGrappling)
                _reloadCoroutine = StartCoroutine(Co_Reload());
        }
        else
        {
            SetState(WeaponState.Idle);
        }

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
    #region Reload — Phased Coroutine
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator Co_Reload()
    {
        if (CurrentState == WeaponState.Reloading) yield break;

        CancelInspect();
        SetState(WeaponState.Reloading);
        ResetAnimSignals();

        bool wasEmpty       = !_roundInChamber && _currentAmmoInClip == 0;
        bool needsChamber   = wasEmpty || weaponData.requiresManualChamber;
        float safetyTimeout = weaponData.reloadTotalTime + 1f;

        Log($"Reload started | mode={(animationDrivenReload ? "AnimEvent" : "Timed")} | wasEmpty={wasEmpty}");

        SetAnimatorTrigger(wasEmpty ? AnimReloadEmpty : AnimReload);
        OnReloadStart?.Invoke();

        if (animationDrivenReload)
        {
            yield return WaitForAnimSignal(() => _animSignal_MagOut, safetyTimeout, "AnimEvent_MagOut");
            _animSignal_MagOut = false;
            Log("Mag out (anim-driven).");
            PlaySound(weaponData.magOutSound, weaponData.reloadVolume);
            OnMagOut?.Invoke();

            yield return WaitForAnimSignal(() => _animSignal_MagIn, safetyTimeout, "AnimEvent_MagIn");
            _animSignal_MagIn = false;
            Log("Mag in (anim-driven).");
            PlaySound(weaponData.magInSound, weaponData.reloadVolume);
            OnMagIn?.Invoke();

            if (needsChamber)
            {
                yield return WaitForAnimSignal(() => _animSignal_ChamberRound, safetyTimeout, "AnimEvent_ChamberRound");
                _animSignal_ChamberRound = false;
                Log("Chamber round (anim-driven).");
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
                Log("Chamber round (timed).");
                PlaySound(weaponData.chamberRoundSound, weaponData.chamberVolume);
                OnChamberRound?.Invoke();
            }
        }

        // Wait out whatever time remains in the animation
        float consumed = weaponData.reloadMagOutDelay
                       + weaponData.reloadMagInDelay
                       + (needsChamber ? weaponData.reloadChamberDelay : 0f);
        float remaining = weaponData.reloadTotalTime - consumed;
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        int ammoNeeded     = weaponData.clipSize - _currentAmmoInClip;
        int ammoToAdd      = Mathf.Min(ammoNeeded, _ammoInReserve);
        _currentAmmoInClip += ammoToAdd;
        _ammoInReserve     -= ammoToAdd;
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
            Log($"Hit: {hit.collider.name}  distance={hit.distance:F1}m");

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

        // ── Spread — movement state + ADS ─────────────────────────────────
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

        // ADS tightens spread — now actually wired up
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
    #region Animation
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

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    public int   CurrentAmmo    => _currentAmmoInClip;
    public int   ReserveAmmo    => _ammoInReserve;
    public bool  RoundInChamber => _roundInChamber;
    public bool  IsReloading    => CurrentState == WeaponState.Reloading;
    public bool  IsInspecting   => _isInspecting;
    public float SpreadBuildup  => _currentSpreadBuildup;
    public bool  IsADS          => _isADS;
    public bool  HolsterComplete => _holsterComplete;
    public bool  DrawComplete    => _drawComplete;

    public void PlayBoltOutSound() => PlayRandomSound(weaponData.boltOutSounds, weaponData.boltOutVolume);
    public void PlayBoltInSound()  => PlaySound(weaponData.boltInSound, weaponData.boltInVolume);

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
        Log("ForceIdle.");
    }

    public void StartHolster()
    {
        _isSwitching     = true;
        _holsterComplete = false;
        SetState(WeaponState.Switching);
        SetAnimatorTrigger(AnimHolster);
        StartCoroutine(Co_WaitHolster());
        Log("Holster started.");
    }

    public void StartDraw()
    {
        _isSwitching  = true;
        _drawComplete = false;
        SetState(WeaponState.Switching);
        SetAnimatorTrigger(AnimDraw);
        StartCoroutine(Co_WaitDraw());
        Log("Draw started.");
    }

    public void NotifyEquipped()
    {
        _isSwitching = false;
        _canShoot    = true;
        SetState(WeaponState.Idle);
        OnEquipped?.Invoke();
        Log("Equipped.");
    }

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
}