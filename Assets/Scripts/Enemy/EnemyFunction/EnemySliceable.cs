using UnityEngine;
using DynamicMeshCutter;

/// <summary>
/// Lets the katana bisect this enemy's body mesh — but ONLY on an execute.
///
/// PlayerMelee already runs its MeleeMeshCutter on every landed swing, so without a gate
/// a normal hit would cut a healthy enemy in half. This component is that gate: it hands
/// the cutter a MeshTarget only inside a short window after EnemyHealth.OnExecuted, and
/// returns null the rest of the time. Because MeleeMeshCutter asks an ISliceable *instead*
/// of searching for a MeshTarget itself, having this component on the enemy also blocks
/// the cutter's normal fallback lookup — a MeshTarget sitting on the body can't be hit by
/// accident.
///
/// The cut deliberately does NOT destroy the enemy root. GameobjectRoot is forced to the
/// body mesh's own GameObject so Dynamic Mesh Cutter only removes the renderer it replaced;
/// EnemyHealth's death coroutine keeps running to finish its explosions and spawn loot,
/// and EnemyRagdoll keeps owning despawn timing. What's left of the root is stripped
/// (colliders, leftover renderers, the ragdoll driver) so the now-invisible body can't
/// shove the pieces around, then it's cleaned up once the death sequence has had time
/// to finish.
///
/// Note this bypasses the ragdoll for the kill — the halves are baked from the pose at the
/// moment of the cut and fall as plain rigidbodies. That's intended for an execute; the
/// Ragdoll Animator 2 rig only drives non-execute deaths.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
[DisallowMultipleComponent]
public class EnemySliceable : MonoBehaviour, ISliceable
{
    [Header("Targets")]
    [Tooltip("Every body mesh that should be cut — each needs a MeshTarget on its " +
             "SkinnedMeshRenderer. All of them are cut with the SAME plane, so a body built " +
             "from several meshes (the humanoids are three: Secondary, Primary Top, Primary " +
             "Bottom) comes apart as one. Auto-finds every skinned MeshTarget in children " +
             "when left empty.")]
    public MeshTarget[] BodyMeshTargets;

    [Header("Gate")]
    [Tooltip("Seconds after an execute during which the swing is allowed to cut. PlayerMelee " +
             "cuts on the same frame it executes, so this only needs to be long enough to " +
             "absorb a frame hitch.")]
    public float ExecuteSliceWindow = 0.5f;

    [Header("Pieces")]
    [Tooltip("How fast the halves separate along the cut plane, in PIECE SIZES per second — " +
             "not metres, so the push looks the same whatever size the enemy is.")]
    public float PieceSeparationSpeed = 2f;
    [Tooltip("Random spin added to each half, so they don't slide apart like flat boards. " +
             "Also relative to piece size.")]
    public float PieceTorque = 2f;
    [Tooltip("Spawned at the cut point on a successful slice. Leave empty to skip.")]
    public GameObject SliceVFXPrefab;
    public float SliceVFXLifetime = 3f;

    [Header("After Slice")]
    [Tooltip("Disables the leftover colliders and the ragdoll driver so the invisible body " +
             "stops interacting with the pieces and the world.")]
    public bool StripRigOnSlice = true;
    [Tooltip("Hides any renderer still attached to the rig (weapons, gear) — they'd otherwise " +
             "float on a frozen skeleton with no body around them.")]
    public bool HideRemainingRenderers = true;
    [Tooltip("Destroys the stripped root once EnemyHealth's death sequence has had time to " +
             "finish its explosions and drop loot. Uncheck to leave despawn entirely to EnemyRagdoll.")]
    public bool DespawnRootAfterSlice = true;
    [Tooltip("Safety margin added on top of the death sequence's own duration.")]
    public float RootDespawnMargin = 2f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    /// <summary>True once a cut has been requested — an enemy is only ever sliced once.</summary>
    public bool HasBeenSliced => _sliceRequested;

    private EnemyHealth  _health;
    private EnemyRagdoll _ragdoll;
    private readonly System.Collections.Generic.List<MeshTarget> _resolved =
        new System.Collections.Generic.List<MeshTarget>();
    private float   _executeTime = float.NegativeInfinity;
    private bool    _sliceRequested;
    private Vector3 _cutPoint;
    private Vector3 _cutNormal;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        TryGetComponent(out _ragdoll);

        if (BodyMeshTargets == null || BodyMeshTargets.Length == 0)
            BodyMeshTargets = FindBodyMeshTargets();

        _health.OnExecuted += HandleExecuted;

        if (BodyMeshTargets.Length == 0)
            Debug.LogWarning($"[EnemySliceable] {name}: no MeshTarget found on any body mesh — " +
                             "executes will play the normal ragdoll death instead of slicing.", this);
    }

    private void OnDestroy()
    {
        if (_health != null) _health.OnExecuted -= HandleExecuted;
    }

    /// <summary>
    /// Takes every MeshTarget that sits on a SkinnedMeshRenderer — that's the body. Targets on
    /// plain MeshRenderers are deliberately skipped: an enemy can carry other cuttable props
    /// (a riot shield has its own MeshTarget), and slicing the shield on execute while leaving
    /// the corpse standing is not the intent.
    /// </summary>
    private MeshTarget[] FindBodyMeshTargets()
    {
        var found = new System.Collections.Generic.List<MeshTarget>();
        foreach (var candidate in GetComponentsInChildren<MeshTarget>(true))
            if (candidate != null && candidate.GetComponent<SkinnedMeshRenderer>() != null)
                found.Add(candidate);

        return found.ToArray();
    }

    // EnemyHealth fires this synchronously inside Execute(), which PlayerMelee calls one
    // line before TryCutHit — so the window is always already open by the time we're asked.
    private void HandleExecuted() => _executeTime = Time.time;

    // ── ISliceable ────────────────────────────────────────────────────────────────

    public System.Collections.Generic.IReadOnlyList<MeshTarget> ResolveSliceTargets(Vector3 hitPoint)
    {
        if (_sliceRequested) return null;
        if (Time.time - _executeTime > ExecuteSliceWindow) return null;
        if (BodyMeshTargets == null) return null;

        _resolved.Clear();
        foreach (var target in BodyMeshTargets)
        {
            if (target == null || !target.isActiveAndEnabled) continue;

            // The cutter destroys GameobjectRoot when it swaps in the halves. Pinning each to
            // its own object is what keeps the death coroutine (explosions, loot) and the
            // despawn timer alive after the body is gone — and stops one mesh's cut taking
            // the other two, and the enemy root, down with it.
            target.GameobjectRoot = target.gameObject;
            _resolved.Add(target);
        }

        return _resolved.Count > 0 ? _resolved : null;
    }

    public void OnSliceStarted(Vector3 hitPoint, Vector3 planeNormal)
    {
        _sliceRequested = true;
        _cutPoint  = hitPoint;
        _cutNormal = planeNormal.sqrMagnitude > 0.0001f ? planeNormal.normalized : Vector3.up;
    }

    public void OnSliceFinished(bool success, GameObject[] pieces)
    {
        if (!success)
        {
            // Nothing was cut — the body mesh is untouched and the ragdoll death plays out
            // as normal. Nothing to clean up.
            Log("Cut failed — falling back to the normal ragdoll death.");
            return;
        }

        if (pieces != null)
        {
            foreach (var piece in pieces)
            {
                if (piece == null) continue;
                if (!piece.TryGetComponent(out Rigidbody rb)) continue;

                // Each half sits on its own side of the cut plane — push it out along the
                // normal in whichever direction it already lies.
                float side = Mathf.Sign(Vector3.Dot(piece.transform.position - _cutPoint, _cutNormal));
                if (side == 0f) side = 1f;

                // VelocityChange + size-relative speed: created pieces all get mass 1 no matter
                // how big they are, so a force-based push bears no relation to their size.
                float radius = SlicedPieceCleanup.PieceRadius(piece);
                rb.AddForce(_cutNormal * side * PieceSeparationSpeed * radius, ForceMode.VelocityChange);
                if (PieceTorque > 0f)
                    rb.AddTorque(Random.onUnitSphere * PieceTorque * radius, ForceMode.VelocityChange);
            }
        }

        if (SliceVFXPrefab != null)
            Destroy(Instantiate(SliceVFXPrefab, _cutPoint, Quaternion.LookRotation(_cutNormal)),
                    SliceVFXLifetime);

        if (StripRigOnSlice) StripRig();
        if (DespawnRootAfterSlice) Destroy(gameObject, DeathSequenceDuration() + RootDespawnMargin);

        Log("Sliced on execute.");
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The body is gone, so everything left on the rig is invisible physics and floating
    /// props. Kill both — an unseen ragdoll batting the halves around reads as the pieces
    /// bouncing off thin air.
    /// </summary>
    private void StripRig()
    {
        foreach (var col in GetComponentsInChildren<Collider>(true))
            if (col != null) col.enabled = false;

        if (HideRemainingRenderers)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = false;
        }

        if (_ragdoll != null && _ragdoll.ragdollAnimator != null)
            _ragdoll.ragdollAnimator.enabled = false;

        if (TryGetComponent(out Animator animator)) animator.enabled = false;
        if (TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
    }

    /// <summary>How long EnemyHealth's death coroutine needs before it has spawned loot.</summary>
    private float DeathSequenceDuration()
    {
        if (_health == null) return 0f;
        return Mathf.Max(0, _health.ExplosionCount) * Mathf.Max(0f, _health.DelayBetweenExplosions);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[EnemySliceable] {name}: {msg}", this);
    }
}
