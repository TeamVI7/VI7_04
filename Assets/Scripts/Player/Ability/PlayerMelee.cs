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

    [Header("Swing Tween")]
    [Tooltip("meleeMesh's local rotation at the start of the swing (wind-up pose).")]
    public Vector3 swingFromEuler = new Vector3(15f, -50f, 0f);
    [Tooltip("meleeMesh's local rotation at the end of the swing (follow-through pose).")]
    public Vector3 swingToEuler   = new Vector3(-10f, 40f, 0f);
    public Ease swingEase = Ease.OutQuad;
    [Tooltip("Time to snap back to rest rotation after the swing lands.")]
    public float swingReturnTime = 0.1f;

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

        _baseFixedDeltaTime = Time.fixedDeltaTime;
    }

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        if (Input.GetKeyDown(meleeKey) && _cooldownTimer <= 0f && !_swinging && CanSwing())
            StartCoroutine(Co_Swing());
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

    private void ResolveHit()
    {
        if (playerCam == null)
        {
            LogWarning("No playerCam assigned/found — cannot resolve melee hit.");
            OnMiss?.Invoke();
            return;
        }

        if (Physics.SphereCast(playerCam.position, SphereRadius, playerCam.forward,
                out RaycastHit hit, Range, HitMask, QueryTriggerInteraction.Ignore))
        {
            // Hitboxes live on child colliders (EnemyLimbHitbox) — the brain/health
            // that actually track Staggered state sit on the root, so search up.
            if (AllowExecute)
            {
                var brain  = hit.collider.GetComponentInParent<EnemyBrain>();
                var health = hit.collider.GetComponentInParent<EnemyHealth>();

                if (brain != null && brain.State == EnemyState.Staggered
                    && health != null && health.IsAlive)
                {
                    health.Execute(transform);
                    TriggerExecuteSlowMo();
                    cameraShaker?.Shake(ExecuteShake);

                    Log($"Executed {health.name}.");
                    OnExecute?.Invoke(health);
                    return;
                }
            }

            if (hit.collider.TryGetComponent(out IDamageable damageable))
            {
                Vector3 hitDir = playerCam.forward;

                damageable.TakeDamage(Damage, hitDir, hit.point);

                if (hit.rigidbody != null)
                    hit.rigidbody.AddForce(hitDir * KnockbackForce, ForceMode.Impulse);

                Log($"Hit {hit.collider.name} for {Damage} dmg.");
                OnHit?.Invoke();
                return;
            }
        }

        Log("Swing missed.");
        OnMiss?.Invoke();
    }

    private void TriggerExecuteSlowMo()
    {
        if (_slowMoRoutine != null) StopCoroutine(_slowMoRoutine);
        _slowMoRoutine = StartCoroutine(Co_ExecuteSlowMo());
    }

    private System.Collections.IEnumerator Co_ExecuteSlowMo()
    {
        Time.timeScale      = ExecuteSlowMoScale;
        Time.fixedDeltaTime = _baseFixedDeltaTime * Time.timeScale;

        yield return new WaitForSecondsRealtime(ExecuteSlowMoHold);

        float t = 0f;
        while (t < ExecuteSlowMoRecover)
        {
            t += Time.unscaledDeltaTime;
            Time.timeScale      = Mathf.Lerp(ExecuteSlowMoScale, 1f, Mathf.Clamp01(t / ExecuteSlowMoRecover));
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