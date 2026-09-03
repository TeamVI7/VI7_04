using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using DynamicMeshCutter;

/// <summary>
/// A prop that physically blocks the way until the player cuts it apart — a plank across a
/// doorway, a barricade, a hanging cable, a fence panel. Drop this on the prop, make sure the
/// prop's layer is in the katana's cuttableLayers, and a swing bisects it along the slash and
/// opens the path.
///
/// It hooks into the same pipeline the enemies and the riot shield use: PlayerMelee calls
/// MeleeMeshCutter.TryCutHit on every landed swing, the cutter looks for an ISliceable around
/// the collider it hit, and this component nominates what to cut. Nothing in the player code
/// needs to change.
///
/// Two things happen on a successful cut, and they are separate on purpose:
///   1. The MESH is bisected into two halves that fall away as physics debris and despawn.
///   2. The PATH is opened — the blocking colliders are switched off and any NavMeshObstacle
///      with them, so the player (and enemies) can walk straight through the gap.
/// Step 2 is what actually unblocks the way; step 1 is what sells it. The halves are also told
/// to ignore the player so a chunk landing in the gap can never re-block what was just opened.
///
/// Like EnemySliceable, this never lets the cutter touch the prop itself: the mesh is baked
/// into a throwaway proxy object and THAT is what gets cut, so Dynamic Mesh Cutter's
/// "destroy the target root" step can't delete this component out from under its own callback.
/// It also means no MeshTarget setup in the inspector — the proxy gets one at runtime.
///
/// SETUP
///   • Prop needs a MeshRenderer (or several in children) and a collider.
///   • The mesh asset needs Read/Write Enabled in its import settings — that is a Dynamic
///     Mesh Cutter requirement, not a new one. A warning is logged if it isn't.
///   • The prop's layer must be inside MeleeMeshCutter.cuttableLayers on the katana.
/// </summary>
[DisallowMultipleComponent]
public class SliceableObstacle : MonoBehaviour, ISliceable, IMeleeDamageable
{
    [Header("What Gets Cut")]
    [Tooltip("Renderers making up the blocking object. All of them are baked together and cut " +
             "with ONE plane, so a barricade built from several planks comes apart as a single " +
             "object. Auto-finds every renderer in children when left empty.")]
    public Renderer[] MeshesToCut;
    [Tooltip("Material shown on the raw cut surface (the inside of the wood/metal). Leave empty " +
             "to reuse the prop's own material.")]
    public Material CutFaceMaterial;

    [Header("Gate")]
    [Tooltip("Melee hits needed before the prop gives way. 1 = cut on the first swing. Higher " +
             "values make it read as a tough obstacle — the earlier swings still land (sound, " +
             "knockback, OnHit) but nothing is cut.")]
    [Min(1)] public int HitsToCut = 1;
    [Tooltip("Only a melee swing may cut this. Off is the same behaviour — it exists so the " +
             "prop can be disarmed from a cutscene or a puzzle without ripping the component off.")]
    public bool Cuttable = true;

    [Header("Clearing The Path")]
    [Tooltip("Colliders that stop the player getting past. Switched off the moment the cut " +
             "lands — this is what actually opens the way. Auto-finds every non-trigger " +
             "collider in children when left empty.")]
    public Collider[] BlockingColliders;
    [Tooltip("Also switch off any NavMeshObstacle on the prop, so enemies path through the gap " +
             "instead of walking around a hole that is no longer there.")]
    public bool ClearNavMeshObstacle = true;
    [Tooltip("Destroy what's left of the prop (the now-invisible, now-collider-less root) after " +
             "the cut. Turn off if other scripts on this object still need to live — a trigger, " +
             "an objective marker.")]
    public bool DespawnRootAfterCut = true;
    [Tooltip("Grace period before the leftover root goes, so anything listening to OnCut has " +
             "run and the halves have finished being set up.")]
    public float RootDespawnDelay = 0.25f;

    [Header("The Halves")]
    [Tooltip("How fast the halves separate along the cut plane, in PIECE SIZES per second — " +
             "not metres, so a small plank and a whole gate come apart at the same visual rate.")]
    public float PieceSeparationSpeed = 1.5f;
    [Tooltip("Random spin added to each half so they tumble instead of sliding apart flat.")]
    public float PieceTorque = 1f;
    [Tooltip("Extra push along the swing direction, so the halves fly away from the player " +
             "rather than dropping straight into the gap that was just opened.")]
    public float PieceForwardPush = 1f;
    [Tooltip("Stop the halves colliding with the player at all. Without this, a chunk that " +
             "settles in the doorway re-blocks the path the cut just opened.")]
    public bool PiecesIgnorePlayer = true;
    [Tooltip("Player root used to find the colliders to ignore. Falls back to the object tagged " +
             "'Player' when left empty.")]
    public Transform PlayerRoot;

    [Header("Halves Despawn")]
    [Tooltip("Seconds the halves lie around before their exit starts. 0 leaves the cutter's own " +
             "default timing alone.")]
    public float PieceLifetime = 6f;
    [Tooltip("How long the exit (blink and/or shrink) plays before the halves disappear.")]
    public float PieceExitDuration = 1.5f;
    public bool  PieceBlink = true;
    [Tooltip("Shrink the halves as they leave. Scale they reach by the end — below 1 shrinks, " +
             "above 1 grows.")]
    public bool  PieceScaleOut = true;
    public float PieceScaleTo = 0.6f;

    [Header("Feedback")]
    [Tooltip("Spawned at the cut point on a successful cut — splinters, sparks, dust.")]
    public GameObject CutVFXPrefab;
    public float CutVFXLifetime = 3f;
    [Tooltip("Spawned at the impact point on a swing that lands but doesn't yet break the prop " +
             "(only reachable with HitsToCut above 1).")]
    public GameObject HitVFXPrefab;
    public float HitVFXLifetime = 1f;

    [Header("Safety")]
    [Tooltip("If the cut never reports back, the prop is put back on screen and re-armed after " +
             "this long, rather than being left invisible but still blocking.")]
    public float SliceFallbackDelay = 1f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    /// <summary>Fired once, when the prop has been cut and the path is open.</summary>
    public event System.Action OnCut;
    /// <summary>Fired on every landed swing that did NOT break the prop.</summary>
    public event System.Action OnHit;

    /// <summary>True once the path has been opened — the prop no longer blocks anything.</summary>
    public bool IsCleared { get; private set; }

    private readonly List<MeshTarget> _targetBuffer = new List<MeshTarget>();
    private GameObject _proxy;
    private Renderer[] _hiddenRenderers;
    private Coroutine  _fallback;
    private int     _hits;
    private int     _lastHitFrame = -1;
    private bool    _sliceRequested;
    private Vector3 _cutPoint;
    private Vector3 _cutNormal;
    private Vector3 _swingDirection = Vector3.forward;

    private void Awake()
    {
        if (MeshesToCut == null || MeshesToCut.Length == 0)
            MeshesToCut = GetComponentsInChildren<Renderer>(true);

        if (BlockingColliders == null || BlockingColliders.Length == 0)
            BlockingColliders = FindBlockingColliders();

        if (MeshesToCut.Length == 0)
            Debug.LogWarning($"[SliceableObstacle] {name}: no renderer found — there is nothing " +
                             "to cut. Assign MeshesToCut or put this on the prop with the mesh.", this);
    }

    /// <summary>
    /// Every solid collider on the prop. Triggers are skipped — they're doorway/objective
    /// volumes that were never blocking the player and shouldn't be switched off by a cut.
    /// </summary>
    private Collider[] FindBlockingColliders()
    {
        var found = new List<Collider>();
        foreach (var col in GetComponentsInChildren<Collider>(true))
            if (col != null && !col.isTrigger) found.Add(col);

        return found.ToArray();
    }

    // ── IMeleeDamageable ──────────────────────────────────────────────────────────
    // PlayerMelee looks for this on the collider it hit, one step before it runs the cutter.
    // Implementing it is what gives an uncut prop normal melee feedback — the hit sound and
    // the knockback — instead of the swing reading as a miss. It never counts the hit: that
    // happens in ResolveSliceTargets below, which runs on the same swing whether or not this
    // component happens to sit on the collider that was struck.

    public bool TakeMeleeDamage(float amount, Vector3 direction, Vector3 hitPoint)
    {
        if (IsCleared || _sliceRequested) return false; // already open — let the swing pass through

        _swingDirection = direction;
        return true;
    }

    // ── ISliceable ────────────────────────────────────────────────────────────────

    /// <summary>This hands over its real MeshTarget, whose vertices live in that object's
    /// own local space — so the cutter's normal scale correction applies.</summary>
    public bool SliceTargetsAreWorldSpace => false;

    public IReadOnlyList<MeshTarget> ResolveSliceTargets(Vector3 hitPoint)
    {
        if (!Cuttable || _sliceRequested || IsCleared) return null;

        // One swing can only ever be one hit. TryCutHit resolves once per swing, but guard the
        // frame anyway so a future double-call (two colliders of the same prop hit by one
        // sphere-cast) can't burn through HitsToCut in a single frame.
        if (Time.frameCount != _lastHitFrame)
        {
            _lastHitFrame = Time.frameCount;
            _hits++;
        }

        if (_hits < HitsToCut)
        {
            if (HitVFXPrefab != null)
                Destroy(Instantiate(HitVFXPrefab, hitPoint, Quaternion.identity), HitVFXLifetime);

            OnHit?.Invoke();
            Log($"Hit {_hits}/{HitsToCut} — not through yet.");
            return null;
        }

        MeshTarget proxy = BuildCutProxy();
        if (proxy == null)
        {
            Log("Could not build a cut proxy — nothing was cut.");
            return null;
        }

        _targetBuffer.Clear();
        _targetBuffer.Add(proxy);

        // If the cut never resolves, put the prop back rather than leaving an invisible wall.
        if (_fallback != null) StopCoroutine(_fallback);
        _fallback = StartCoroutine(Co_SliceFallback());

        return _targetBuffer;
    }

    public void OnSliceStarted(Vector3 hitPoint, Vector3 planeNormal)
    {
        _sliceRequested = true;
        _cutPoint  = hitPoint;
        _cutNormal = planeNormal.sqrMagnitude > 0.0001f ? planeNormal.normalized : Vector3.up;
    }

    public void OnSliceFinished(bool success, GameObject[] pieces)
    {
        if (_fallback != null) { StopCoroutine(_fallback); _fallback = null; }

        if (!success)
        {
            // The plane missed the mesh. Put the prop back exactly as it was — it still blocks,
            // and the player can swing again. Deliberately NOT counted as a hit being spent.
            Log("Cut failed — the prop is restored and can be swung at again.");
            RestoreMeshes();
            _sliceRequested = false;
            _hits = Mathf.Max(0, HitsToCut - 1);
            return;
        }

        // The cutter destroyed the proxy along with the mesh it replaced.
        _proxy = null;
        _hiddenRenderers = null;

        SetUpPieces(pieces);
        ClearPath();

        if (CutVFXPrefab != null)
            Destroy(Instantiate(CutVFXPrefab, _cutPoint, Quaternion.LookRotation(_cutNormal)),
                    CutVFXLifetime);

        Log("Cut through — path open.");
    }

    // ── The halves ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes the two halves apart and makes sure neither of them can stand in for the wall
    /// that was just removed.
    /// </summary>
    private void SetUpPieces(GameObject[] pieces)
    {
        if (pieces == null) return;

        Collider[] playerColliders = PiecesIgnorePlayer ? FindPlayerColliders()
                                                        : System.Array.Empty<Collider>();

        var exit = new SlicedPieceCleanup.Settings
        {
            Lifetime      = PieceLifetime,
            ExitDuration  = PieceExitDuration,
            Blink         = PieceBlink,
            BlinkInterval = 0.15f,
            ScaleOut      = PieceScaleOut,
            ScaleTo       = PieceScaleTo
        };

        foreach (var piece in pieces)
        {
            if (piece == null) continue;

            IgnorePlayerCollisions(piece, playerColliders);

            // Only override the cutter's default exit when a lifetime is actually configured —
            // 0 means "leave the katana's own timing alone".
            if (PieceLifetime > 0f) SlicedPieceCleanup.Attach(piece, exit);

            if (!piece.TryGetComponent(out Rigidbody rb)) continue;

            // Each half lies on its own side of the cut plane — push it out the way it already
            // leans, then add a shove along the swing so both halves clear the opening.
            float side = Mathf.Sign(Vector3.Dot(piece.transform.position - _cutPoint, _cutNormal));
            if (side == 0f) side = 1f;

            // VelocityChange, not Impulse: created pieces all get mass 1 whatever their size,
            // so a force-based push bears no relation to how big the piece looks.
            float radius = SlicedPieceCleanup.PieceRadius(piece);
            Vector3 push = _cutNormal * side * PieceSeparationSpeed;
            if (PieceForwardPush > 0f && _swingDirection.sqrMagnitude > 0.0001f)
                push += _swingDirection.normalized * PieceForwardPush;

            rb.AddForce(push * radius, ForceMode.VelocityChange);
            if (PieceTorque > 0f)
                rb.AddTorque(Random.onUnitSphere * PieceTorque * radius, ForceMode.VelocityChange);
        }
    }

    private Collider[] FindPlayerColliders()
    {
        Transform root = PlayerRoot;
        if (root == null)
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) root = tagged.transform;
        }

        return root != null ? root.root.GetComponentsInChildren<Collider>(true)
                            : System.Array.Empty<Collider>();
    }

    /// <summary>
    /// The whole point of the prop is gone once it's cut, so the debris must not take over its
    /// job. Pairing the halves off against the player's own colliders is more reliable than a
    /// debris layer here, because it can't be undone by whatever layer the prop was authored on.
    /// </summary>
    private static void IgnorePlayerCollisions(GameObject piece, Collider[] playerColliders)
    {
        if (playerColliders == null || playerColliders.Length == 0) return;

        foreach (var pieceCol in piece.GetComponentsInChildren<Collider>(true))
        {
            if (pieceCol == null || !pieceCol.gameObject.activeInHierarchy) continue;
            foreach (var playerCol in playerColliders)
            {
                // Unity refuses the pair (and logs an error) unless both sides are active.
                if (playerCol == null || playerCol == pieceCol) continue;
                if (!playerCol.gameObject.activeInHierarchy) continue;

                Physics.IgnoreCollision(pieceCol, playerCol, true);
            }
        }
    }

    // ── Opening the path ──────────────────────────────────────────────────────────

    /// <summary>
    /// Switches off everything that was standing in the player's way. This — not the mesh cut —
    /// is what makes the prop passable, so it also runs on the fallback path where the visuals
    /// went wrong but the player has already earned the opening.
    /// </summary>
    private void ClearPath()
    {
        if (IsCleared) return;
        IsCleared = true;

        if (BlockingColliders != null)
        {
            foreach (var col in BlockingColliders)
                if (col != null) col.enabled = false;
        }

        if (ClearNavMeshObstacle)
        {
            foreach (var obstacle in GetComponentsInChildren<NavMeshObstacle>(true))
                if (obstacle != null) obstacle.enabled = false;
        }

        // Anything still rendering here is a leftover the cut didn't replace (a bolt, a
        // decal plane). With the body gone it would hang in mid-air.
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r != null) r.enabled = false;

        OnCut?.Invoke();

        if (DespawnRootAfterCut) Destroy(gameObject, RootDespawnDelay);
    }

    // ── Cut proxy ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bakes every source mesh into ONE world-space mesh on a throwaway object and puts a
    /// MeshTarget on that. Two reasons, both load-bearing:
    ///
    ///   • Dynamic Mesh Cutter destroys the target's GameobjectRoot when it swaps in the halves.
    ///     Cutting a proxy means it destroys the proxy, so this component (and any trigger,
    ///     objective hook or audio source on the prop) survives to run OnSliceFinished and open
    ///     the path. It also means the prop needs no MeshTarget set up by hand.
    ///   • A prop built from several meshes comes apart as one object rather than as one pair
    ///     of chunks per mesh.
    ///
    /// The originals are hidden, not destroyed, so a failed cut can put them straight back.
    /// </summary>
    private MeshTarget BuildCutProxy()
    {
        var combines  = new List<CombineInstance>();
        var materials = new List<Material>();
        var hidden    = new List<Renderer>();

        foreach (var renderer in MeshesToCut)
        {
            if (renderer == null || !renderer.enabled) continue;

            Mesh mesh = GetBakedMesh(renderer);
            if (mesh == null) continue;

            if (!mesh.isReadable)
            {
                Debug.LogWarning($"[SliceableObstacle] {name}: mesh '{mesh.name}' is not readable — " +
                                 "tick Read/Write Enabled in its model import settings or Dynamic " +
                                 "Mesh Cutter cannot cut it.", this);
                continue;
            }

            var sourceMats = renderer.sharedMaterials;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                combines.Add(new CombineInstance
                {
                    mesh         = mesh,
                    subMeshIndex = sub,
                    // World matrix, because each mesh is in its OWN local space — without this
                    // they all stack up at the origin of the first one.
                    transform    = renderer.localToWorldMatrix
                });
                materials.Add(sub < sourceMats.Length ? sourceMats[sub]
                            : sourceMats.Length > 0   ? sourceMats[0] : null);
            }

            hidden.Add(renderer);
        }

        if (combines.Count == 0) return null;

        var combined = new Mesh { name = $"{name}_CutProxy" };
        // A detailed prop can pass 65k verts; the 16-bit default would silently corrupt it.
        combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        combined.CombineMeshes(combines.ToArray(), mergeSubMeshes: false, useMatrices: true);

        // Identity transform: the combined mesh is already in world space, so local and world
        // space agree for the cut — no scale correction, no inherited-scale surprises.
        _proxy = new GameObject($"{name}_CutProxy");
        _proxy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Parented to the prop (keeping its world identity) so the cutter's transform.root
        // lookup finds the prop's colliders — that's what tells the fresh halves to ignore the
        // object they were cut out of instead of being launched out of it.
        _proxy.transform.SetParent(transform, worldPositionStays: true);
        _proxy.layer = gameObject.layer;

        _proxy.AddComponent<MeshFilter>().sharedMesh = combined;
        _proxy.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();

        var proxyTarget = _proxy.AddComponent<MeshTarget>();
        proxyTarget.GameobjectRoot      = _proxy;
        proxyTarget.OverrideFaceMaterial = CutFaceMaterial;

        foreach (var renderer in hidden) renderer.enabled = false;
        _hiddenRenderers = hidden.ToArray();

        return proxyTarget;
    }

    /// <summary>
    /// The mesh to cut, in the renderer's own local space. Skinned meshes are baked at their
    /// current pose — a hanging cable or a cloth banner can be rigged and still cut correctly.
    /// </summary>
    private static Mesh GetBakedMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
        {
            if (skinned.sharedMesh == null) return null;

            var baked = new Mesh();
            skinned.BakeMesh(baked);
            return baked;
        }

        return renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
    }

    /// <summary>Undoes the proxy swap — used when the cut fails or never resolves.</summary>
    private void RestoreMeshes()
    {
        if (_hiddenRenderers != null)
        {
            foreach (var renderer in _hiddenRenderers)
                if (renderer != null) renderer.enabled = true;
            _hiddenRenderers = null;
        }

        if (_proxy != null) { Destroy(_proxy); _proxy = null; }
    }

    private IEnumerator Co_SliceFallback()
    {
        yield return new WaitForSeconds(SliceFallbackDelay);
        _fallback = null;

        if (IsCleared) yield break;

        // The cut was queued but never reported. The player has landed the hits that should
        // have broken this, so open the path anyway — a prop that eats a successful swing and
        // stays solid is a dead end. The visuals just skip the halves.
        Log("Cut never reported back — opening the path without the halves.");
        RestoreMeshes();
        ClearPath();
    }

    // ── Manual control ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the path from script, without a cut — for a cutscene, a puzzle solution, or a
    /// door that should already be clear on a replay. The prop stops blocking immediately.
    /// </summary>
    public void ForceClear()
    {
        if (_fallback != null) { StopCoroutine(_fallback); _fallback = null; }
        RestoreMeshes();
        ClearPath();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[SliceableObstacle] {name}: {msg}", this);
    }
}
