using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Grappling : MonoBehaviour
{
    [Header("References")]
    private PlayerMovement pm;
    public Transform cam;
    public Transform gunTip;
    public LayerMask whatIsGrappleable;
    public LineRenderer lr;

    [Header("Grappling")]
    public float maxGrappleDistance;
    public float grappleDelayTime;
    public float overshootYAxis;

    private Vector3 grapplePoint;

    [Header("Cooldown")]
    public float grapplingCd;
    private float grapplingCdTimer;

    [Header("Input")]
    public KeyCode grappleKey = KeyCode.Mouse1;

    [Header("Active Weapon")]
    public WeaponsController activeWeapon;

    private bool grappling;
    private Coroutine _stopGrappleCoroutine;

    private static readonly int AnimGrapple = Animator.StringToHash("Grapple");
    private static readonly int AnimStopGrapple = Animator.StringToHash("StopGrapple");

    private void Start()
    {
        pm = GetComponent<PlayerMovement>();
        if (lr != null)
        {
            lr.positionCount = 2;
            lr.enabled = false;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(grappleKey)) StartGrapple();

        if (grapplingCdTimer > 0)
            grapplingCdTimer -= Time.deltaTime;
    }

    private void LateUpdate()
    {
        if (!grappling || lr == null) return;

        lr.SetPosition(0, gunTip.position);
        lr.SetPosition(1, grapplePoint);
    }

    private void StartGrapple()
    {
        if (grapplingCdTimer > 0) return;

        grappling = true;
        pm.freeze = true;

        if (activeWeapon != null)
        {
            if (activeWeapon.IsReloading) activeWeapon.CancelReload();
            if (activeWeapon.IsInspecting) activeWeapon.CancelInspect();
            
            if (activeWeapon.gunAnimator != null)
            {
                // Clear any leftover stop signals, then fire the start trigger
                activeWeapon.gunAnimator.ResetTrigger(AnimStopGrapple);
                activeWeapon.gunAnimator.SetTrigger(AnimGrapple);
            }
        }

        RaycastHit hit;
        if (Physics.Raycast(cam.position, cam.forward, out hit, maxGrappleDistance, whatIsGrappleable))
        {
            grapplePoint = hit.point;
            Invoke(nameof(ExecuteGrapple), grappleDelayTime);
        }
        else
        {
            grapplePoint = cam.position + cam.forward * maxGrappleDistance;
            Invoke(nameof(StopGrapple), grappleDelayTime);
        }

        if (lr != null)
        {
            lr.enabled = true;
            lr.SetPosition(0, gunTip.position);
            lr.SetPosition(1, grapplePoint);
        }
    }

    private void ExecuteGrapple()
    {
        pm.freeze = false;

        Vector3 lowestPoint = new Vector3(transform.position.x, transform.position.y - 1f, transform.position.z);

        float grapplePointRelativeYPos = grapplePoint.y - lowestPoint.y;
        float highestPointOnArc = grapplePointRelativeYPos + overshootYAxis;

        if (grapplePointRelativeYPos < 0) highestPointOnArc = overshootYAxis;

        pm.JumpToPosition(grapplePoint, highestPointOnArc);

        if (_stopGrappleCoroutine != null) StopCoroutine(_stopGrappleCoroutine);
        _stopGrappleCoroutine = StartCoroutine(Co_StopWhenArrived());
    }

    private IEnumerator Co_StopWhenArrived()
    {
        float maxWait = 3f;
        float elapsed = 0f;
        while (elapsed < maxWait)
        {
            elapsed += Time.deltaTime;
            if (Vector3.Distance(transform.position, grapplePoint) <= 2f) break;
            yield return null;
        }
        StopGrapple();
        _stopGrappleCoroutine = null;
    }

    public void StopGrapple()
    {
        if (_stopGrappleCoroutine != null) { StopCoroutine(_stopGrappleCoroutine); _stopGrappleCoroutine = null; }
        pm.freeze        = false;
        grappling        = false;
        grapplingCdTimer = grapplingCd;
        if (lr != null) lr.enabled = false;

        // Tell the Animator the physical grapple movement has finished
        if (activeWeapon != null && activeWeapon.gunAnimator != null)
        {
            activeWeapon.gunAnimator.ResetTrigger(AnimGrapple);
            activeWeapon.gunAnimator.SetTrigger(AnimStopGrapple);
        }
    }

    public bool IsGrappling()      => grappling;
    public Vector3 GetGrapplePoint() => grapplePoint;
} 