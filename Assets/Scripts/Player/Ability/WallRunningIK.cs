using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Places the left hand on walls during wall-run, wall-slide, and sliding.
///
/// SETUP:
///   Assign leftArmIK, ikTarget, handOrigin in Inspector.
///   Call SetIK() from WeaponSwitcher after each weapon draw.
///
/// EXTEND:
///   - Add right-hand IK target for two-handed wall bracing.
///   - Subscribe to WallRunning events instead of polling pm.state for cleaner coupling.
///
/// DEBUG:
///   - Toggle debugMode in Inspector. No logs fire in builds regardless.
/// </summary>
public class WallHandIK : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public TwoBoneIKConstraint leftArmIK;
    public Transform ikTarget;
    public Transform handOrigin;

    [Header("Settings")]
    public float ikSpeed          = 8f;
    public float wallRayDistance  = 1.2f;
    public float groundRayDistance = 2.5f;

    [Header("Debug")]
    public bool debugMode = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private WallRunning    _wr;
    private PlayerMovement _pm;

    // Gizmo state — only written in editor
    private bool    _lastRayHit;
    private Vector3 _lastRayOrigin;
    private Vector3 _lastRayHitPoint;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        _wr = GetComponent<WallRunning>();
        _pm = GetComponent<PlayerMovement>();

        if (leftArmIK  == null) Debug.LogError("[WallHandIK] leftArmIK not assigned.", this);
        if (ikTarget   == null) Debug.LogError("[WallHandIK] ikTarget not assigned.", this);
        if (handOrigin == null) Debug.LogWarning("[WallHandIK] handOrigin not assigned — using player root.", this);
        if (_wr        == null) Debug.LogError("[WallHandIK] WallRunning component not found.", this);
        if (_pm        == null) Debug.LogError("[WallHandIK] PlayerMovement component not found.", this);
    }

    private void Update()
    {
        if (leftArmIK == null) return;

        bool    active    = false;
        Vector3 rayOrigin = transform.position + Vector3.up * 0.3f;

        Log($"rayOrigin:{rayOrigin} state:{_pm.state}");

        if (_pm.wallrunning || _pm.wallSliding)
        {
            active = TryWallRay();
        }
        else if (_pm.state == PlayerMovement.MovementState.sliding)
        {
            _lastRayOrigin = rayOrigin;
            active = TryGroundRay(rayOrigin);
        }

        float targetWeight = active ? 1f : 0f;
        leftArmIK.weight = Mathf.MoveTowards(leftArmIK.weight, targetWeight, Time.deltaTime * ikSpeed);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Ray Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private bool TryWallRay()
    {
        Vector3 toWall = -_wr.WallNormal;
        if (Physics.Raycast(transform.position, toWall, out RaycastHit hit, wallRayDistance, _wr.whatIsWall))
        {
            ikTarget.position = hit.point;
            ikTarget.rotation = Quaternion.LookRotation(_wr.WallNormal, Vector3.up);
            _lastRayHit       = true;
            _lastRayHitPoint  = hit.point;
            return true;
        }

        _lastRayHit = false;
        return false;
    }

    private bool TryGroundRay(Vector3 origin)
    {
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRayDistance))
        {
            LogSlide($"slide ray HIT — {hit.collider.name} layer:{hit.collider.gameObject.layer}");
            ikTarget.position = hit.point;
            ikTarget.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            _lastRayHit       = true;
            _lastRayHitPoint  = hit.point;
            return true;
        }

        _lastRayHit = false;
        LogSlide($"slide ray MISS — origin:{origin} dist:{groundRayDistance}");
        return false;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Call from WeaponSwitcher after new weapon is drawn.
    /// e.g. wallHandIK.SetIK(newWeapon.leftArmIK);
    /// </summary>
    public void SetIK(TwoBoneIKConstraint newIK)
    {
        if (leftArmIK != null) leftArmIK.weight = 0f;
        leftArmIK = newIK;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugMode) Debug.Log($"[WallHandIK] {msg}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogSlide(string msg)
    {
        if (debugMode && _pm != null && _pm.state == PlayerMovement.MovementState.sliding)
            Debug.Log($"[WallHandIK] {msg}", this);
    }

    private void OnDrawGizmos()
    {
        if (!debugMode || _pm == null || _wr == null) return;

        Vector3 origin = handOrigin != null ? handOrigin.position : transform.position;

        if (_pm.state == PlayerMovement.MovementState.sliding)
        {
            Gizmos.color = _lastRayHit ? Color.green : Color.red;
            Gizmos.DrawLine(origin, origin + Vector3.down * groundRayDistance);
            if (_lastRayHit) Gizmos.DrawSphere(_lastRayHitPoint, 0.05f);
        }

        if (_pm.wallrunning || _pm.wallSliding)
        {
            Gizmos.color = _lastRayHit ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, transform.position + (-_wr.WallNormal) * wallRayDistance);
            if (_lastRayHit) Gizmos.DrawSphere(_lastRayHitPoint, 0.05f);
        }
    }

    #endregion
}