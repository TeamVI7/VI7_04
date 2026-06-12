using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Grapple hook with animated rope (Spring simulation).
/// Reads rope origin from activeWeapon.leftHandBone if available.
/// 
/// EXTENDING:
///   - Subscribe to OnGrappleStart / OnGrappleLand / OnGrappleEnd for haptics, audio, VFX.
///   - Replace rope simulation with a LineRenderer trail by modifying DrawRope().
///   - Add multi-grapple by queueing grapple points.
/// 
/// DEBUG:
///   - Enable debugLog in Inspector.
///   - grapplePoint is public read-only via GetGrapplePoint().
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class Grappling : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public Transform     cam;
    public LayerMask     whatIsGrappleable;
    public LineRenderer  lr;

    [Header("Grapple Settings")]
    public float maxGrappleDistance = 30f;
    public float grappleDelayTime   = 0.1f;
    public float overshootYAxis     = 2f;

    [Header("Cooldown")]
    public float grapplingCooldown  = 1f;

    [Header("Input")]
    public KeyCode grappleKey = KeyCode.Mouse1;

    [Header("Active Weapon  (auto-set by WeaponSwitcher)")]
    public WeaponsController activeWeapon;

    [Header("Rope Simulation")]
    public int            quality    = 200;
    public float          damper     = 14f;
    public float          strength   = 800f;
    public float          velocity   = 15f;
    public float          waveCount  = 3f;
    public float          waveHeight = 1f;
    public AnimationCurve affectCurve;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fired the moment the grapple key is pressed and a target is found.</summary>
    public event Action<Vector3> OnGrappleStart;

    /// <summary>Fired when the arc launches (after grappleDelayTime).</summary>
    public event Action<Vector3> OnGrappleLand;

    /// <summary>Fired when grapple ends for any reason.</summary>
    public event Action OnGrappleEnd;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool    IsGrappling()        => _grappling;
    public Vector3 GetGrapplePoint()    => _grapplePoint;
    public float   CooldownRemaining    => Mathf.Max(0f, _cdTimer);

    // Read by Grappling.cs so WeaponSwitcher can swap it
    public Transform leftHandBone => activeWeapon != null ? activeWeapon.leftHandBone : null;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private
    // ─────────────────────────────────────────────────────────────────────────

    private PlayerMovement _pm;
    private bool           _grappling;
    private float          _cdTimer;
    private Vector3        _grapplePoint;
    private Coroutine      _stopCoroutine;

    // Rope spring simulation
    private Spring  _spring;
    private Vector3 _currentGrapplePos;

    private static readonly int AnimGrapple     = Animator.StringToHash("Grapple");
    private static readonly int AnimStopGrapple = Animator.StringToHash("StopGrapple");

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _spring = new Spring();
        _spring.SetTarget(0);
    }

    private void Start()
    {
        _pm = GetComponent<PlayerMovement>();

        if (lr != null)
        {
            lr.positionCount = 0;
            lr.enabled       = true;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(grappleKey)) TryGrapple();

        if (_cdTimer > 0f) _cdTimer -= Time.deltaTime;
    }

    private void LateUpdate()
    {
        DrawRope();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Grapple Logic
    // ─────────────────────────────────────────────────────────────────────────

    private void TryGrapple()
    {
        if (_cdTimer > 0f) { Log("Grapple blocked — on cooldown."); return; }
        StartGrapple();
    }

    private void StartGrapple()
    {
        _grappling = true;
        _pm.freeze = true;

        // Interrupt weapon actions
        if (activeWeapon != null)
        {
            if (activeWeapon.IsReloading)  activeWeapon.CancelReload();
            if (activeWeapon.IsInspecting) activeWeapon.CancelInspect();

            if (activeWeapon.gunAnimator != null)
            {
                activeWeapon.gunAnimator.ResetTrigger(AnimStopGrapple);
                activeWeapon.gunAnimator.SetTrigger(AnimGrapple);
            }
        }

        RaycastHit hit;
        if (Physics.Raycast(cam.position, cam.forward, out hit, maxGrappleDistance, whatIsGrappleable))
        {
            _grapplePoint = hit.point;
            OnGrappleStart?.Invoke(_grapplePoint);
            Log($"Grapple started → {_grapplePoint}");
            Invoke(nameof(ExecuteGrapple), grappleDelayTime);
        }
        else
        {
            _grapplePoint = cam.position + cam.forward * maxGrappleDistance;
            Log("Grapple — no target, aborting.");
            Invoke(nameof(StopGrapple), grappleDelayTime);
        }
    }

    private void ExecuteGrapple()
    {
        _pm.freeze = false;

        float relativeY   = _grapplePoint.y - (transform.position.y - 1f);
        float arcHeight   = relativeY < 0 ? overshootYAxis : relativeY + overshootYAxis;

        _pm.JumpToPosition(_grapplePoint, arcHeight);

        if (_stopCoroutine != null) StopCoroutine(_stopCoroutine);
        _stopCoroutine = StartCoroutine(Co_StopWhenArrived());

        OnGrappleLand?.Invoke(_grapplePoint);
        Log("Grapple executing arc.");
    }

    private IEnumerator Co_StopWhenArrived()
    {
        const float maxWait = 3f;
        float elapsed = 0f;
        while (elapsed < maxWait)
        {
            elapsed += Time.deltaTime;
            if (Vector3.Distance(transform.position, _grapplePoint) <= 2f) break;
            yield return null;
        }
        StopGrapple();
        _stopCoroutine = null;
    }

    public void StopGrapple()
    {
        if (_stopCoroutine != null) { StopCoroutine(_stopCoroutine); _stopCoroutine = null; }

        _pm.freeze  = false;
        _grappling  = false;
        _cdTimer    = grapplingCooldown;

        if (activeWeapon?.gunAnimator != null)
        {
            activeWeapon.gunAnimator.ResetTrigger(AnimGrapple);
            activeWeapon.gunAnimator.SetTrigger(AnimStopGrapple);
        }

        OnGrappleEnd?.Invoke();
        Log("Grapple ended.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Rope Drawing
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3 RopeOrigin =>
        leftHandBone != null ? leftHandBone.position : transform.position;

    private void DrawRope()
    {
        if (lr == null) return;

        if (!_grappling)
        {
            _currentGrapplePos = RopeOrigin;
            _spring.Reset();
            if (lr.positionCount > 0) lr.positionCount = 0;
            return;
        }

        if (lr.positionCount == 0)
        {
            _spring.SetVelocity(velocity);
            lr.positionCount = quality + 1;
        }

        _spring.SetDamper(damper);
        _spring.SetStrength(strength);
        _spring.Update(Time.deltaTime);

        Vector3 origin = RopeOrigin;
        Vector3 up     = Quaternion.LookRotation((_grapplePoint - origin).normalized) * Vector3.up;

        _currentGrapplePos = Vector3.Lerp(_currentGrapplePos, _grapplePoint, Time.deltaTime * 12f);

        for (int i = 0; i <= quality; i++)
        {
            float   delta  = i / (float)quality;
            Vector3 offset = up * waveHeight
                             * Mathf.Sin(delta * waveCount * Mathf.PI)
                             * _spring.Value
                             * affectCurve.Evaluate(delta);
            lr.SetPosition(i, Vector3.Lerp(origin, _currentGrapplePos, delta) + offset);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[Grappling] {msg}", this);
    }

    #endregion
}