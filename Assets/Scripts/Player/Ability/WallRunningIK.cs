using UnityEngine;
using UnityEngine.Animations.Rigging;

public class WallHandIK : MonoBehaviour
{
    [Header("References")]
    public Grappling grappling; // already has activeWeapon ref
    public Transform wallIKTarget;

    [Header("Settings")]
    public float ikSpeed = 8f;
    public float wallRayDistance = 1.2f;

    private WallRunning wr;
    private PlayerMovement pm;
    private TwoBoneIKConstraint _currentIK;

    private void Start()
    {
        wr = GetComponent<WallRunning>();
        pm = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        // grab current weapon's IK constraint
        _currentIK = grappling.activeWeapon != null ? grappling.activeWeapon.leftArmIK : null;
        if (_currentIK == null) return;

        bool active = false;

        if (pm.wallrunning || pm.wallSliding)
        {
            Vector3 toWall = -wr.WallNormal;
            if (Physics.Raycast(transform.position, toWall, out RaycastHit hit, wallRayDistance, wr.whatIsWall))
            {
                wallIKTarget.position = hit.point;
                wallIKTarget.rotation = Quaternion.LookRotation(wr.WallNormal, Vector3.up);
                active = true;
            }
        }

        float targetWeight = active ? 1f : 0f;
        _currentIK.weight = Mathf.MoveTowards(_currentIK.weight, targetWeight, Time.deltaTime * ikSpeed);
    }
}