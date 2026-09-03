using UnityEngine;
using DG.Tweening;

/// <summary>
/// Dedicated melee attack, independent of the weapon system. Sphere-cast from
/// the camera on swing to detect a hit, apply damage via IDamageable, and
/// knock the target back.
/// </summary>
public class PlayerMelee : MonoBehaviour
{
    [Header("Input")]
    public KeyCode meleeKey = KeyCode.Q;

    [Header("References")]
    [Tooltip("Defaults to Camera.main if left empty.")]
    public Transform playerCam;
    [Tooltip("Plays the slash animation. Optional — leave empty if using the DOTween swing below instead.")]
    public Animator meleeAnimator;
    [Tooltip("Weapon mesh — hidden except during the slash itself.")]
    public GameObject meleeMesh;
    [Tooltip("Gun holder (procedural weapon sway/IK) — its VISUALS are hidden during the " +
             "slash and shown again after. The GameObject itself is never deactivated, " +
             "because WeaponsController lives on/under it — deactivating it would stop " +
             "Update()/coroutines entirely and silently break \"shoot during melee\".")]
    public GameObject gunHolder;
    [Tooltip("Checked so melee can't start mid-reload.")]
    public WeaponsController activeWeapon;
    [Tooltip("Checked so melee can't start mid-switch.")]
    public WeaponSwitcherProcedural weaponSwitcher;
    [Tooltip("How long the mesh stays visible after the swing resolves.")]
    public float meshVisibleAfterHit = 0.2f;
    [Tooltip("Defaults to a CameraShaker found on playerCam if left empty.")]
    public CameraShaker cameraShaker;
    public CameraShaker.ShakePreset ExecuteShake = CameraShaker.ShakePreset.Heavy;
    [Tooltip("Optional — slices any Cuttable target the swing lands on. " +
             "Assign swingOrigin on it to a blade-tip child of meleeMesh (not the pivot) " +
             "so its position actually traces the swing arc.")]

    public class MeleeCutterBehaviour : DynamicMeshCutter.CutterBehaviour { }
    [Tooltip("Optional — slices any Cuttable target the swing lands on. " +
            "Assign swingOrigin on it to a blade-tip child of meleeMesh (not the pivot) " +
            "so its position actually traces the swing arc.")]
    public MeleeMeshCutter meleeMeshCutter;
    [Header("Swing Tween")]
    [Tooltip("meleeMesh's local rotation at the start of the swing (wind-up pose).")]
    public Vector3 swingFromEuler = new Vector3(15f, -50f, 0f);
    [Tooltip("meleeMesh's local rotation at the end of the swing (follow-through pose).")]
    public Vector3 swingToEuler   = new Vector3(-10f, 40f, 0f);
    public Ease swingEase = Ease.OutQuad;
    [Tooltip("Time to snap back to rest rotation after the swing lands.")]
    public float swingReturnTime = 0.1f;

    [Header("Slash VFX")]
    public GameObject[] SlashEffectPrefabs;
    public Transform SlashEffectSpawnPoint;
    public float SlashEffectLifetime = 1.5f;
    public Vector3 SlashEffectRotationOffset = Vector3.zero;

    [Header("Melee Audio")]
    public AudioSource meleeAudioSource;
    public AudioClip[] SwingSounds;
    public AudioClip[] HitSounds;

    /// <summary>True from key-press until the mesh hides again. Check this from
    /// WeaponsController/WeaponSwitcherProcedural before starting a reload or switch.</summary>
    public bool IsSwinging => _swinging;

    private static readonly int AnimSlash = Animator.StringToHash("Slash");

    [Header("Attack")]
    public float Damage        = 25f;
    public float Range         = 2.2f;
    public float SphereRadius  = 0.5f;
    public float KnockbackForce = 6f;
    public float Cooldown      = 0.6f;
    public LayerMask HitMask   = ~0;

    [Header("Timing")]
    [Tooltip("Delay from key press to hit resolving, so it can line up with a swing animation.")]
    public float WindupTime = 0.15f;

    [Header("Execute")]
    [Tooltip("Hitting a Staggered enemy triggers EnemyHealth.Execute() instead of normal damage.")]
    public bool AllowExecute = true;
    [Tooltip("Time.timeScale to drop to on execute. 1 = no slow-mo, lower = more dramatic.")]
    [Range(0.01f, 1f)] public float ExecuteSlowMoScale = 0.15f;
    [Tooltip("How long the slow-mo holds, in REAL (unscaled) seconds — unaffected by timeScale.")]
    public float ExecuteSlowMoHold = 0.35f;
    [Tooltip("Real seconds to ease back up from slow-mo to normal speed.")]
    public float ExecuteSlowMoRecover = 0.15f;
    [Tooltip("Spawned at the hit point when an execute lands. Leave empty to skip.")]
    public GameObject ExecuteParticlePrefab;
    public float ExecuteParticleLifetime = 3f;

    [Header("Shield Break")]
    [Tooltip("Slow time down when a bash breaks a riot shield, the same way an execute does. " +
             "Reads as a beat of impact and gives the slice a moment to be seen.")]
    public bool SlowMoOnShieldBreak = true;
    [Tooltip("Time.timeScale to drop to on a shield break. Usually milder than an execute.")]
    [Range(0.01f, 1f)] public float ShieldBreakSlowMoScale = 0.3f;
    [Tooltip("How long the shield-break slow-mo holds, in REAL (unscaled) seconds.")]
    public float ShieldBreakSlowMoHold = 0.2f;
    [Tooltip("Real seconds to ease back up to normal speed after a shield break.")]
    public float ShieldBreakSlowMoRecover = 0.2f;
    public CameraShaker.ShakePreset ShieldBreakShake = CameraShaker.ShakePreset.Medium;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    [SerializeField] private bool drawGizmos = true;

    public event System.Action OnSwing;      // windup begins
    public event System.Action OnHit;        // normal hit landed
    public event System.Action OnMiss;       // windup finished, nothing hit
    public event System.Action<EnemyHealth> OnExecute; // execute landed, target about to die

    private float _cooldownTimer;
    private bool _swinging;
    private Tween _swingTween;
    private Quaternion _meshRestRotation;
    private bool _meshRestCached;
    private float _baseFixedDeltaTime;
    private Coroutine _slowMoRoutine;
    private Renderer[] _gunRenderers;

    private void Awake()
    {
        if (playerCam == null && Camera.main != null)
            playerCam = Camera.main.transform;

        if (meleeMesh != null)
        {
            _meshRestRotation = meleeMesh.transform.localRotation;
            _meshRestCached   = true;
            meleeMesh.SetActive(false);
        }

        if (gunHolder != null)
            _gunRenderers = gunHolder.GetComponentsInChildren<Renderer>(includeInactive: true);

        if (cameraShaker == null && playerCam != null)
            cameraShaker = playerCam.GetComponent<CameraShaker>();

        if (meleeAudioSource == null)
            meleeAudioSource = GetComponent<AudioSource>();

        _baseFixedDeltaTime = Time.fixedDeltaTime;
    }

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        if (Input.GetKeyDown(meleeKey) && _cooldownTimer <= 0f && !_swinging && CanSwing())
            StartCoroutine(Co_Swing());

        meleeMeshCutter?.RegisterSwingSample();
    }

    private bool CanSwing()
    {
        // Central gate first — covers Dead/Switching/Reloading/Meleeing in one call and is
        // kept in sync automatically even if new abilities register new lock reasons later.
        if (PlayerActionLock.Instance != null && !PlayerActionLock.Instance.CanMelee)
        {
            Log("Blocked — action lock active (dead/switching/reloading).");
            return false;
        }
        if (activeWeapon != null && activeWeapon.IsReloading)
        {
            Log("Blocked — weapon is reloading.");
            return false;
        }
        if (weaponSwitcher != null && weaponSwitcher.IsSwitching)
        {
            Log("Blocked — weapon is switching.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Hides/shows the gun's VISUALS only — never deactivates gunHolder itself.
    /// WeaponsController (and everything else on gunHolder) keeps running the whole time,
    /// which is what lets fire/reload logic keep functioning correctly during a swing.
    /// </summary>
    private void SetGunVisible(bool visible)
    {
        if (_gunRenderers == null) return;
        foreach (var r in _gunRenderers)
        {
            if (r != null) r.enabled = visible;
        }
    }

    private System.Collections.IEnumerator Co_Swing()
    {
        _swinging = true;
        _cooldownTimer = Cooldown;
        PlayerActionLock.Instance?.SetLock(PlayerActionLock.LockReason.Meleeing, true);

        if (meleeMesh != null) meleeMesh.SetActive(true);
        if (gunHolder != null) SetGunVisible(false);
        if (meleeAnimator != null) meleeAnimator.SetTrigger(AnimSlash);

        _swingTween?.Kill();
        if (meleeMesh != null)
        {
            if (!_meshRestCached)
            {
                _meshRestRotation = meleeMesh.transform.localRotation;
                _meshRestCached   = true;
            }
            meleeMesh.transform.localRotation = Quaternion.Euler(swingFromEuler);
            _swingTween = meleeMesh.transform
                .DOLocalRotate(swingToEuler, Mathf.Max(0.01f, WindupTime))
                .SetEase(swingEase)
                .SetUpdate(true);
        }

        OnSwing?.Invoke();
        SpawnSlashEffect();
        PlayRandomClip(SwingSounds);
        Log("Swing start.");

        if (WindupTime > 0f)
            yield return new WaitForSeconds(WindupTime);

        ResolveHit();

        if (meleeMesh != null)
        {
            _swingTween?.Kill();
            _swingTween = meleeMesh.transform
                .DOLocalRotateQuaternion(_meshRestRotation, swingReturnTime)
                .SetUpdate(true);
        }

        if (meshVisibleAfterHit > 0f)
            yield return new WaitForSeconds(meshVisibleAfterHit);

        if (meleeMesh != null) meleeMesh.SetActive(false);
        if (gunHolder != null) SetGunVisible(true);
        _swinging = false;
        PlayerActionLock.Instance?.SetLock(PlayerActionLock.LockReason.Meleeing, false);
    }

    private struct MeleeHitInfo
    {
        public Collider collider;
        public Vector3 point;
        public Vector3 normal;
        public Rigidbody rigidbody;
    }

    private bool TryGetMeleeHit(out MeleeHitInfo hit)
    {
        Vector3 origin = playerCam.position;
        Vector3 dir = playerCam.forward;

        Collider[] overlaps = Physics.OverlapSphere(origin, SphereRadius, HitMask, QueryTriggerInteraction.Ignore);
        if (overlaps.Length > 0)
        {
            Collider closest = null;
            float closestDist = float.MaxValue;
            foreach (var col in overlaps)
            {
                float d = Vector3.Distance(origin, col.ClosestPoint(origin));
                if (d < closestDist)
                {
                    closestDist = d;
                    closest = col;
                }
            }

            hit = new MeleeHitInfo
            {
                collider = closest,
                point = closest.ClosestPoint(origin),
                normal = -dir,
                rigidbody = closest.attachedRigidbody
            };
            return true;
        }

        if (Physics.SphereCast(origin, SphereRadius, dir, out RaycastHit rayHit, Range, HitMask, QueryTriggerInteraction.Ignore))
        {
            hit = new MeleeHitInfo
            {
                collider = rayHit.collider,
                point = rayHit.point,
                normal = rayHit.normal,
                rigidbody = rayHit.rigidbody
            };
            return true;
        }

        hit = default;
        return false;
    }

    private void ResolveHit()
    {
        if (playerCam == null)
        {
            LogWarning("No playerCam assigned/found — cannot resolve melee hit.");
            OnMiss?.Invoke();
            return;
        }

        if (TryGetMeleeHit(out MeleeHitInfo hit))
        {
            // Hitboxes live on child colliders (EnemyLimbHitbox) — the brain/health
            // that actually track Staggered state sit on the root, so search up.
            if (AllowExecute)
            {
                var brain  = hit.collider.GetComponentInParent<EnemyBrain>();
                var health = hit.collider.GetComponentInParent<EnemyHealth>();

                // Downed counts on its own, without consulting EnemyBrain — the boss is driven
                // by MechBossBrain and its own state enum, so requiring EnemyBrain.Staggered
                // would make the boss's last stand unfinishable.
                bool staggered = brain  != null && brain.State == EnemyState.Staggered;
                bool downed    = health != null && health.IsDowned;

                if (health != null && health.IsAlive && (staggered || downed))
                {
                    health.Execute(transform);
                    TriggerExecuteSlowMo();
                    cameraShaker?.Shake(ExecuteShake);
                    SpawnExecuteParticle(hit.point);
                    meleeMeshCutter?.TryCutHit(hit.collider, hit.point, hit.normal);
                    PlayRandomClip(HitSounds);

                    Log($"Executed {health.name}.");
                    OnExecute?.Invoke(health);
                    return;
                }
            }

            Vector3 hitDir = playerCam.forward;
            bool damageApplied = false;

            if (hit.collider.TryGetComponent(out IMeleeDamageable meleeDamageable))
            {
                bool landed = meleeDamageable.TakeMeleeDamage(Damage, hitDir, hit.point);

                if (landed && hit.rigidbody != null)
                    hit.rigidbody.AddForce(hitDir * KnockbackForce, ForceMode.Impulse);

                Log(landed
                    ? $"Melee-damageable hit landed on {hit.collider.name}."
                    : $"Melee-damageable hit blocked (not exposed) on {hit.collider.name}.");
                damageApplied = landed;

                // A bash that breaks a riot shield gets its own beat of slow-mo — the cut
                // itself is queued a few lines below and resolves during the slow-down, so
                // the halves come apart while time is still stretched.
                if (landed && SlowMoOnShieldBreak
                    && hit.collider.TryGetComponent(out RiotShieldBehaviour shield)
                    && shield.IsDestroyed)
                {
                    TriggerSlowMo(ShieldBreakSlowMoScale, ShieldBreakSlowMoHold, ShieldBreakSlowMoRecover);
                    cameraShaker?.Shake(ShieldBreakShake);
                    Log("Shield break slow-mo.");
                }
            }
            else if (hit.collider.TryGetComponent(out IDamageable damageable))
            {
                damageable.TakeDamage(Damage, hitDir, hit.point);

                if (hit.rigidbody != null)
                    hit.rigidbody.AddForce(hitDir * KnockbackForce, ForceMode.Impulse);

                Log($"Hit {hit.collider.name} for {Damage} dmg.");
                damageApplied = true;
            }

            // Runs regardless of which damage path fired above — also catches
            // non-damageable Cuttable props (crates, scenery) on their own.
            bool cutApplied = meleeMeshCutter != null
                && meleeMeshCutter.TryCutHit(hit.collider, hit.point, hit.normal);

            if (damageApplied || cutApplied)
            {
                PlayRandomClip(HitSounds);
                OnHit?.Invoke();
                return;
            }
        }

        Log("Swing missed.");
        OnMiss?.Invoke();
    }

    private void SpawnSlashEffect()
    {
        if (SlashEffectPrefabs == null || SlashEffectPrefabs.Length == 0) return;

        Transform origin = SlashEffectSpawnPoint != null ? SlashEffectSpawnPoint : playerCam;
        if (origin == null) return;

        Quaternion rot = origin.rotation * Quaternion.Euler(SlashEffectRotationOffset);
        GameObject prefab = SlashEffectPrefabs[Random.Range(0, SlashEffectPrefabs.Length)];
        var fx = Instantiate(prefab, origin.position, rot);
        Destroy(fx, SlashEffectLifetime);
    }

    private void PlayRandomClip(AudioClip[] clips)
    {
        if (meleeAudioSource == null || clips == null || clips.Length == 0) return;
        meleeAudioSource.PlayOneShot(clips[Random.Range(0, clips.Length)]);
    }

    private void SpawnExecuteParticle(Vector3 point)
    {
        if (ExecuteParticlePrefab == null) return;

        Quaternion rot = playerCam != null ? Quaternion.LookRotation(-playerCam.forward) : Quaternion.identity;
        var fx = Instantiate(ExecuteParticlePrefab, point, rot);
        Destroy(fx, ExecuteParticleLifetime);
    }

    private void TriggerExecuteSlowMo() =>
        TriggerSlowMo(ExecuteSlowMoScale, ExecuteSlowMoHold, ExecuteSlowMoRecover);

    /// <summary>
    /// Drops Time.timeScale to <paramref name="scale"/>, holds for <paramref name="hold"/>
    /// REAL seconds, then eases back to 1 over <paramref name="recover"/> real seconds.
    /// A second call cancels the first, so overlapping hits can't stack the slow-down or
    /// leave the game stuck below normal speed.
    /// </summary>
    public void TriggerSlowMo(float scale, float hold, float recover)
    {
        if (_slowMoRoutine != null) StopCoroutine(_slowMoRoutine);
        _slowMoRoutine = StartCoroutine(Co_SlowMo(scale, hold, recover));
    }

    private System.Collections.IEnumerator Co_SlowMo(float scale, float hold, float recover)
    {
        scale = Mathf.Clamp(scale, 0.01f, 1f);

        Time.timeScale      = scale;
        Time.fixedDeltaTime = _baseFixedDeltaTime * Time.timeScale;

        yield return new WaitForSecondsRealtime(hold);

        float t = 0f;
        while (t < recover)
        {
            t += Time.unscaledDeltaTime;
            Time.timeScale      = Mathf.Lerp(scale, 1f, Mathf.Clamp01(t / recover));
            Time.fixedDeltaTime = _baseFixedDeltaTime * Time.timeScale;
            yield return null;
        }

        Time.timeScale      = 1f;
        Time.fixedDeltaTime = _baseFixedDeltaTime;
        _slowMoRoutine      = null;
    }

    private void OnDisable()
    {
        // Safety net — if this object gets disabled/destroyed mid slow-mo, don't
        // leave the whole game stuck at ExecuteSlowMoScale forever.
        if (_slowMoRoutine != null)
        {
            Time.timeScale      = 1f;
            Time.fixedDeltaTime = _baseFixedDeltaTime;
            _slowMoRoutine      = null;
        }

        // Safety net — if this gets disabled mid-swing (e.g. player died while swinging),
        // don't leave the Meleeing lock stuck on forever, and don't leave the gun's
        // renderers switched off with no code path left to turn them back on.
        if (_swinging)
        {
            _swinging = false;
            PlayerActionLock.Instance?.SetLock(PlayerActionLock.LockReason.Meleeing, false);
            SetGunVisible(true);
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[PlayerMelee] {msg}", this);
    }

    private void LogWarning(string msg) => Debug.LogWarning($"[PlayerMelee] {msg}", this);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || playerCam == null) return;
        Gizmos.color = _cooldownTimer > 0f ? new Color(1f, 0.5f, 0f, 0.6f) : Color.red;
        Gizmos.DrawWireSphere(playerCam.position + playerCam.forward * Range, SphereRadius);
    }
#endif
}