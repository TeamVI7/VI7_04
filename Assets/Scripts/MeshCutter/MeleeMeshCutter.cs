using UnityEngine;
using DynamicMeshCutter;

// Attach to the melee weapon/hand, alongside a MeleeCutterBehaviour on the same
// object. Call RegisterSwingSample() each frame the swing is active (e.g. from
// PlayerMelee.Update), then call TryCutHit() from your existing hit-detection
// callback alongside normal damage application.
[RequireComponent(typeof(MeleeCutterBehaviour))]
public class MeleeMeshCutter : MonoBehaviour
{
    [Header("References")]
    public Transform swingOrigin;
    public Camera    viewCamera;

    [Header("Settings")]
    public LayerMask cuttableLayers = ~0;
    [Tooltip("Fallback impulse if swing motion can't be measured this frame.")]
    public float minSwingSpeed = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    private Vector3 _lastSwingPos;
    private bool    _hasSample;
    private MeleeCutterBehaviour _cutter;

    private void OnEnable()
    {
        _hasSample = false;
        if (viewCamera == null) viewCamera = Camera.main;
        _cutter = GetComponent<MeleeCutterBehaviour>();
    }

    // Call every frame the melee swing is active (before hit checks).
    public void RegisterSwingSample()
    {
        Vector3 currentPos = swingOrigin != null ? swingOrigin.position : transform.position;
        _lastSwingPos = currentPos;
        _hasSample = true;
    }

    // Call from your existing hit-detection path when a melee hit registers.
    // Returns true once a valid cut is QUEUED, not once it's confirmed successful —
    // Dynamic Mesh Cutter cuts async by default, so the actual result arrives later
    // in OnCutResult below.
    public bool TryCutHit(Collider hitCollider, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (((1 << hitCollider.gameObject.layer) & cuttableLayers) == 0) return false;

        var target = hitCollider.GetComponentInParent<MeshTarget>();
        if (target == null) return false;

        Vector3 currentPos = swingOrigin != null ? swingOrigin.position : transform.position;
        Vector3 swingDir = _hasSample ? (currentPos - _lastSwingPos) : Vector3.zero;

        if (swingDir.magnitude < minSwingSpeed * Time.deltaTime)
            swingDir = swingOrigin != null ? swingOrigin.forward : transform.forward;
        swingDir.Normalize();

        Vector3 viewDir = viewCamera != null ? viewCamera.transform.forward : transform.forward;
        Vector3 planeNormal = Vector3.Cross(swingDir, viewDir);
        if (planeNormal.sqrMagnitude < 0.0001f) planeNormal = hitNormal;
        planeNormal.Normalize();

        int sourceLayer = target.gameObject.layer;
        _cutter.Cut(target, hitPoint, planeNormal, OnCutResult,
            (info, cData) => OnCreated(info, cData, sourceLayer));

        _lastSwingPos = currentPos;
        return true;
    }

    private void OnCutResult(bool success, Info info)
    {
        Log($"Cut on '{info.MeshTarget.name}' -> {success}");
    }

    private void OnCreated(Info info, MeshCreationData cData, int sourceLayer)
    {
        foreach (var obj in cData.CreatedObjects)
        {
            if (obj != null) obj.layer = sourceLayer;
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[MeleeMeshCutter] {msg}", this);
    }
}