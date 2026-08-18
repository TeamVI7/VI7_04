using System.Collections.Generic;
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
    [Tooltip("Slash angle used when the swing can't be measured, in degrees around the view " +
             "axis. 0 = horizontal cut, 90 = vertical, 30 = the usual katana diagonal.")]
    [Range(-180f, 180f)] public float fallbackSlashAngle = 30f;

    [Header("Cut Pieces")]
    [Tooltip("Layer for the created halves. Leave at -1 to keep the layer of the mesh that was " +
             "cut. Set this to a debris layer for enemy/shield chunks so their mesh colliders " +
             "don't soak up bullets or block hit-scans.")]
    public int pieceLayerOverride = -1;

    [Header("Cut Piece Cleanup")]
    [Tooltip("Cut pieces play an exit and despawn instead of piling up forever. 0 disables cleanup.")]
    public float pieceLifetime = 5f;
    [Tooltip("How long the exit (blink and/or shrink) plays before the piece disappears.")]
    public float pieceExitDuration = 1.5f;
    public bool  pieceBlink = true;
    [Tooltip("Starting blink interval — it accelerates toward the despawn.")]
    public float pieceBlinkInterval = 0.18f;
    [Tooltip("Scale the piece over its exit. An ISliceable can override this for its own " +
             "pieces — the riot shield does.")]
    public bool  pieceScaleOut = false;
    [Tooltip("Scale multiplier the piece reaches by the end of its exit. Above 1 grows, " +
             "below 1 shrinks.")]
    public float pieceScaleTo = 1.2f;

    [Header("Piece Scale Fix")]
    [Tooltip("Rescale each piece so it comes out the size the mesh it replaced was rendered at.\n\n" +
             "Dynamic Mesh Cutter copies the source's LOCAL scale onto the piece and parents it " +
             "to a fresh root at scale 1, so any scale inherited from parents is dropped. " +
             "Enemy_Shielded's armature is at 100, so its shield halves come out at a hundredth " +
             "of the right size without this. Turn off only if your cut meshes all sit on " +
             "unscaled parents — it's a no-op in that case anyway.")]
    public bool matchSourceScale = true;
    [Tooltip("Caps how fast a piece can be pushed out of an overlap, measured in PIECE SIZES " +
             "per second. Pieces are born inside the object they were cut from — with Unity's " +
             "default (effectively unlimited) PhysX resolves that overlap by launching them. " +
             "0 leaves the Unity default.")]
    public float pieceMaxDepenetrationPerSize = 1f;
    [Tooltip("Hard speed cap on a fresh piece, in PIECE SIZES per second. A half the size of a " +
             "coin travelling at a few m/s reads as launched across the room, so this is " +
             "deliberately measured relative to the piece rather than in metres. 0 = no cap.")]
    public float pieceSpeedLimitPerSize = 3f;
    [Tooltip("How long the speed cap stays on after the cut. Long enough to cover the launch, " +
             "short enough that the piece still falls and tumbles naturally afterwards.")]
    public float pieceSpeedLimitDuration = 0.75f;
    [Tooltip("Stop the pieces colliding with the hierarchy they were cut out of — the body, " +
             "the ragdoll bones, the hand still holding them.")]
    public bool ignoreCollisionWithSource = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    /// <summary>Default exit config handed to every piece. An ISliceable can re-Attach its
    /// own settings in OnSliceFinished — the cleanup component reconfigures in place.</summary>
    public SlicedPieceCleanup.Settings PieceCleanupSettings => new SlicedPieceCleanup.Settings
    {
        Lifetime      = pieceLifetime,
        ExitDuration  = pieceExitDuration,
        Blink         = pieceBlink,
        BlinkInterval = pieceBlinkInterval,
        ScaleOut      = pieceScaleOut,
        ScaleTo       = pieceScaleTo
    };

    private Vector3 _lastSwingPos;
    private Vector3 _prevSwingPos;
    private int     _sampleCount;
    private MeleeCutterBehaviour _cutter;
    private readonly List<MeshTarget> _targetBuffer = new List<MeshTarget>();

    private void OnEnable()
    {
        _sampleCount = 0;
        if (viewCamera == null) viewCamera = Camera.main;
        _cutter = GetComponent<MeleeCutterBehaviour>();
    }

    // Call every frame the melee swing is active (before hit checks).
    // Two samples are kept, because PlayerMelee samples in Update and resolves the hit from
    // a coroutine in the SAME frame — comparing against a position written moments earlier
    // in the same frame always measures a zero-length swing.
    public void RegisterSwingSample()
    {
        Vector3 currentPos = swingOrigin != null ? swingOrigin.position : transform.position;
        _prevSwingPos = _lastSwingPos;
        _lastSwingPos = currentPos;
        _sampleCount++;
    }

    // Call from your existing hit-detection path when a melee hit registers.
    // Returns true once a valid cut is QUEUED, not once it's confirmed successful —
    // Dynamic Mesh Cutter cuts async by default, so the actual result arrives later
    // in OnCutResult below.
    public bool TryCutHit(Collider hitCollider, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (((1 << hitCollider.gameObject.layer) & cuttableLayers) == 0) return false;

        // An ISliceable (riot shield, prop with a simplified collider) gets to nominate
        // its own mesh, because the collider that was hit is often not the cuttable mesh.
        var sliceable = hitCollider.GetComponentInParent<ISliceable>()
                     ?? hitCollider.GetComponentInChildren<ISliceable>();

        _targetBuffer.Clear();
        if (sliceable != null)
        {
            var nominated = sliceable.ResolveSliceTargets(hitPoint);
            if (nominated != null)
            {
                for (int i = 0; i < nominated.Count; i++) AddTarget(nominated[i]);
            }
        }
        else AddTarget(hitCollider.GetComponentInParent<MeshTarget>());

        if (_targetBuffer.Count == 0) return false;

        Vector3 currentPos = swingOrigin != null ? swingOrigin.position : transform.position;
        Vector3 viewDir = viewCamera != null ? viewCamera.transform.forward : transform.forward;

        Vector3 swingDir = _sampleCount > 1 ? (currentPos - _prevSwingPos) : Vector3.zero;

        // Flatten the swing against the view plane — only the on-screen direction of the
        // slash decides where the cut runs, and the toward-the-target component of the
        // blade's motion is what used to collapse the cross product below.
        swingDir = Vector3.ProjectOnPlane(swingDir, viewDir);

        // Too small to be a real slash — jitter would give a randomly angled cut.
        if (swingDir.magnitude < minSwingSpeed * Time.deltaTime) swingDir = Vector3.zero;

        Vector3 planeNormal = Vector3.Cross(swingDir.normalized, viewDir);

        // Unmeasurable or view-aligned swing. The old fallback used the blade's forward,
        // which on a thrust-style pose points almost straight down the view axis — the cross
        // product then collapses and the plane ends up parallel to the target's face, so it
        // misses a flat object like a shield entirely and the cut silently fails.
        if (planeNormal.sqrMagnitude < 0.0001f)
            planeNormal = Vector3.Cross(FallbackSlashDirection(viewDir), viewDir);

        // Last resort, if even the camera basis was degenerate.
        if (planeNormal.sqrMagnitude < 0.0001f) planeNormal = hitNormal;

        planeNormal.Normalize();

        // One set for the whole batch — every target of a multi-mesh body shares a root.
        Collider[] sourceColliders = ignoreCollisionWithSource
            ? _targetBuffer[0].transform.root.GetComponentsInChildren<Collider>(true)
            : System.Array.Empty<Collider>();

        // Before Cut(), not after — with UseAsync off the callbacks fire synchronously
        // inside Cut(), which would otherwise report the slice finished before it started.
        sliceable?.OnSliceStarted(hitPoint, planeNormal);

        // The batch holds the callback open until every target has reported, so an ISliceable
        // hears "finished" once, with all the pieces, rather than once per mesh.
        var batch = new SliceBatch(sliceable, _targetBuffer.Count);

        foreach (var target in _targetBuffer)
        {
            // Captured per target, now, because the cutter destroys it before the OnCreated
            // callback that needs them.
            int layer = target.gameObject.layer;
            Vector3 localScale = target.transform.localScale;
            Vector3 lossyScale = target.transform.lossyScale;

            _cutter.Cut(target, hitPoint, planeNormal,
                (success, info) => OnCutResult(success, info, batch),
                (info, cData) => OnCreated(info, cData, layer, batch,
                                           localScale, lossyScale, sourceColliders));
        }

        _lastSwingPos = currentPos;
        return true;
    }

    /// <summary>Validates a nominated target and queues it for this swing.</summary>
    private void AddTarget(MeshTarget target)
    {
        // CutterBehaviour.Cut() silently no-ops on an inactive target without ever firing a
        // callback, so drop it here rather than leaving the batch waiting on a cut that can
        // never resolve.
        if (target == null || !target.isActiveAndEnabled) return;
        if (_targetBuffer.Contains(target)) return;

        // MeshCreation dereferences GameobjectRoot when building the pieces, so make sure
        // it points at something even when the target was set up without one.
        if (target.GameobjectRoot == null) target.GameobjectRoot = target.gameObject;

        _targetBuffer.Add(target);
    }

    /// <summary>
    /// Collects the results of the cuts queued by one swing. Dynamic Mesh Cutter reports each
    /// target separately and asynchronously — and a failed cut never reaches OnCreated at all —
    /// so each target reports exactly once here, through whichever path it took.
    /// </summary>
    private class SliceBatch
    {
        private readonly ISliceable _sliceable;
        private readonly List<GameObject> _pieces = new List<GameObject>();
        private int  _pending;
        private bool _anySuccess;

        public SliceBatch(ISliceable sliceable, int targetCount)
        {
            _sliceable = sliceable;
            _pending   = targetCount;
        }

        public void ReportFailure() { _pending--; TryFinish(); }

        public void ReportSuccess(GameObject[] pieces)
        {
            _anySuccess = true;
            if (pieces != null)
            {
                foreach (var piece in pieces)
                    if (piece != null) _pieces.Add(piece);
            }
            _pending--;
            TryFinish();
        }

        private void TryFinish()
        {
            if (_pending > 0) return;
            _sliceable?.OnSliceFinished(_anySuccess, _anySuccess ? _pieces.ToArray() : null);
        }
    }

    /// <summary>
    /// On-screen slash direction to cut along when the blade's real motion tells us nothing.
    /// Built from the camera's own right/up so the cut always runs ACROSS the view — which is
    /// the one thing guaranteed to actually pass through whatever the player is looking at.
    /// </summary>
    private Vector3 FallbackSlashDirection(Vector3 viewDir)
    {
        Transform cam = viewCamera != null ? viewCamera.transform : transform;
        Vector3 right = Vector3.ProjectOnPlane(cam.right, viewDir).normalized;
        if (right.sqrMagnitude < 0.0001f) right = cam.right;

        return Quaternion.AngleAxis(fallbackSlashAngle, viewDir) * right;
    }

    private void OnCutResult(bool success, Info info, SliceBatch batch)
    {
        string targetName = info.MeshTarget != null ? info.MeshTarget.name : "<destroyed>";
        if (success) Log($"Cut '{targetName}' -> success.");
        else         Log($"Cut '{targetName}' -> FAILED (plane missed the mesh — check the " +
                         "swing direction and that the cut point is inside the mesh, not just " +
                         "on an oversized collider).");

        // On success OnCreated fires right after this and reports the pieces; a failed cut
        // never reaches OnCreated, so this is the only chance to close this target's slot.
        if (!success) batch?.ReportFailure();
    }

    private void OnCreated(Info info, MeshCreationData cData, int sourceLayer, SliceBatch batch,
                           Vector3 sourceLocalScale, Vector3 sourceLossyScale,
                           Collider[] sourceColliders)
    {
        if (cData == null) { batch?.ReportFailure(); return; }

        int pieceLayer = pieceLayerOverride >= 0 ? pieceLayerOverride : sourceLayer;
        Vector3 scaleFix = matchSourceScale
            ? ScaleCorrection(sourceLocalScale, sourceLossyScale)
            : Vector3.one;

        foreach (var obj in cData.CreatedObjects)
        {
            if (obj == null) continue;
            SetLayerRecursive(obj, pieceLayer);

            // Scale correction first, so the collider is already its final size before PhysX
            // ever sees it — growing a collider that's mid-simulation is what turns a small
            // overlap into a launch.
            //
            // Applied to the MESH CHILD, not to obj itself: obj's scale belongs to
            // SlicedPieceCleanup's exit animation, and two systems writing the same transform
            // is how the piece ended up snapping back to its uncorrected size mid-flight.
            ApplyScaleCorrection(obj, scaleFix);

            // Everything below is expressed relative to the piece's own size — see PieceRadius.
            float radius = Mathf.Max(0.0001f, SlicedPieceCleanup.PieceRadius(obj));

            if (pieceMaxDepenetrationPerSize > 0f && obj.TryGetComponent(out Rigidbody pieceRb))
                pieceRb.maxDepenetrationVelocity = pieceMaxDepenetrationPerSize * radius;

            IgnoreSourceCollisions(obj, sourceColliders);

            if (pieceLifetime > 0f)
            {
                var cleanup = SlicedPieceCleanup.Attach(obj, PieceCleanupSettings);

                // Set after Attach, so an ISliceable re-Attaching its own exit settings
                // (the riot shield does) can't wipe the limit.
                if (cleanup != null && pieceSpeedLimitPerSize > 0f)
                    cleanup.SetSpeedLimit(pieceSpeedLimitPerSize * radius, pieceSpeedLimitDuration);
            }
        }

        batch?.ReportSuccess(cData.CreatedObjects);
    }

    /// <summary>
    /// Scales the piece's mesh children rather than the piece root. MeshCreation builds each
    /// piece as an empty rigidbody parent with the mesh + collider on a child, so correcting
    /// the child gives an identical result while leaving the root's scale free for the exit
    /// animation to own.
    /// </summary>
    private static void ApplyScaleCorrection(GameObject piece, Vector3 scaleFix)
    {
        if (scaleFix == Vector3.one) return;

        foreach (Transform child in piece.transform)
            child.localScale = Vector3.Scale(child.localScale, scaleFix);
    }

    /// <summary>
    /// A piece is born occupying the same space as the body it was cut from, so without this
    /// its first physics step is a shove out of that overlap — straight up, usually, since
    /// that's the shortest way out of a hand or a torso.
    /// </summary>
    private static void IgnoreSourceCollisions(GameObject piece, Collider[] sourceColliders)
    {
        if (sourceColliders == null || sourceColliders.Length == 0) return;

        var pieceColliders = piece.GetComponentsInChildren<Collider>(true);
        foreach (var pieceCol in pieceColliders)
        {
            if (pieceCol == null || !pieceCol.gameObject.activeInHierarchy) continue;
            foreach (var sourceCol in sourceColliders)
            {
                // Unity refuses the pair (and logs an error) unless both sides are active.
                if (sourceCol == null || sourceCol == pieceCol) continue;
                if (!sourceCol.gameObject.activeInHierarchy) continue;

                Physics.IgnoreCollision(pieceCol, sourceCol, true);
            }
        }
    }

    /// <summary>
    /// MeshCreation gives the piece's mesh object the source's LOCAL scale and leaves the
    /// rigidbody parent at 1, so any scale inherited from parents is dropped. Scaling the
    /// parent by lossy/local puts the piece back at the size the original was rendered at —
    /// on a rig imported at the FBX default of 0.01 that's the difference between a correct
    /// piece and one a hundredth of the size.
    /// </summary>
    private static Vector3 ScaleCorrection(Vector3 localScale, Vector3 lossyScale)
    {
        return new Vector3(
            Mathf.Approximately(localScale.x, 0f) ? 1f : lossyScale.x / localScale.x,
            Mathf.Approximately(localScale.y, 0f) ? 1f : lossyScale.y / localScale.y,
            Mathf.Approximately(localScale.z, 0f) ? 1f : lossyScale.z / localScale.z);
    }

    // MeshCreation builds each piece as a rigidbody parent with the mesh + MeshCollider on a
    // child, so setting the layer on the returned object alone would leave the collider —
    // the part physics queries actually see — on whatever layer it was created with.
    private static void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[MeleeMeshCutter] {msg}", this);
    }
}