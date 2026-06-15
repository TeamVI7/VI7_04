using UnityEngine;
using UnityEngine.Animations.Rigging;

public class WallHandIK : MonoBehaviour
{
    [Header("References")]
    public TwoBoneIKConstraint leftArmIK;
    public Transform ikTarget;
    public Transform handOrigin;

    [Header("Settings")]
    public float ikSpeed = 8f;
    public float wallRayDistance = 1.2f;
    public float groundRayDistance = 2.5f;

    [Header("Debug")]
    public bool debugMode = true;

    private WallRunning wr;
    private PlayerMovement pm;

    private bool _lastRayHit;
    private Vector3 _lastRayOrigin;
    private Vector3 _lastRayHitPoint;

    private void Start()
    {
        wr = GetComponent<WallRunning>();
        pm = GetComponent<PlayerMovement>();

        if (leftArmIK == null)  Debug.LogError("[WallHandIK] leftArmIK is NULL");
        if (ikTarget == null)   Debug.LogError("[WallHandIK] ikTarget is NULL");
        if (handOrigin == null) Debug.LogWarning("[WallHandIK] handOrigin is NULL — using player root");
        if (wr == null)         Debug.LogError("[WallHandIK] WallRunning not found");
        if (pm == null)         Debug.LogError("[WallHandIK] PlayerMovement not found");
    }

    /// <summary>
    /// Call from WeaponSwitcher after new weapon is drawn.
    /// e.g. wallHandIK.SetIK(newWeapon.leftArmIK);
    /// </summary>
    public void SetIK(TwoBoneIKConstraint newIK)
    {
        if (leftArmIK != null) leftArmIK.weight = 0f;
        leftArmIK = newIK;
    }

    private void Update()
    {
        if (leftArmIK == null) return;

        bool active = false;
        Vector3 rayOrigin = transform.position + Vector3.up * 0.3f;
        Debug.Log($"[WallHandIK] rayOrigin:{rayOrigin} state:{pm.state}");

        if (pm.wallrunning || pm.wallSliding)
        {
            Vector3 toWall = -wr.WallNormal;
            if (Physics.Raycast(transform.position, toWall, out RaycastHit hit, wallRayDistance, wr.whatIsWall))
            {
                ikTarget.position = hit.point;
                ikTarget.rotation = Quaternion.LookRotation(wr.WallNormal, Vector3.up);
                active = true;
                _lastRayHit = true;
                _lastRayHitPoint = hit.point;
            }
            else _lastRayHit = false;
        }
        else if (pm.state == PlayerMovement.MovementState.sliding)
        {
            _lastRayOrigin = rayOrigin;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRayDistance))
            {
                if (debugMode) Debug.Log($"[WallHandIK] slide ray HIT — {hit.collider.name} layer:{hit.collider.gameObject.layer}");
                ikTarget.position = hit.point;
                ikTarget.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                active = true;
                _lastRayHit = true;
                _lastRayHitPoint = hit.point;
            }
            else
            {
                _lastRayHit = false;
                if (debugMode) Debug.Log($"[WallHandIK] slide ray MISS — origin:{rayOrigin} dist:{groundRayDistance} layer:{pm.whatIsGround.value}");
            }
        }

        float targetWeight = active ? 1f : 0f;
        leftArmIK.weight = Mathf.MoveTowards(leftArmIK.weight, targetWeight, Time.deltaTime * ikSpeed);

        if (debugMode && pm.state == PlayerMovement.MovementState.sliding)
            Debug.Log($"[WallHandIK] sliding — rayHit:{_lastRayHit}  weight:{leftArmIK.weight:F2}  ikTargetPos:{ikTarget.position}");
    }

    private void OnDrawGizmos()
    {
        if (!debugMode) return;
        if (pm == null || wr == null) return;

        Vector3 rayOrigin = handOrigin != null ? handOrigin.position : transform.position;

        if (pm.state == PlayerMovement.MovementState.sliding)
        {
            Gizmos.color = _lastRayHit ? Color.green : Color.red;
            Gizmos.DrawLine(rayOrigin, rayOrigin + Vector3.down * groundRayDistance);
            if (_lastRayHit) Gizmos.DrawSphere(_lastRayHitPoint, 0.05f);
        }

        if (pm.wallrunning || pm.wallSliding)
        {
            Gizmos.color = _lastRayHit ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, transform.position + (-wr.WallNormal) * wallRayDistance);
            if (_lastRayHit) Gizmos.DrawSphere(_lastRayHitPoint, 0.05f);
        }
    }
}