using System.Collections;
using UnityEngine;
using DynamicMeshCutter;

/// <summary>
/// Lets the katana bisect this enemy's body mesh — but only on an execute, or (opt-in via
/// SliceOnMeleeKill) on a swing that kills outright. Never on a swing the enemy survives.
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

    [Tooltip("Bake the body meshes into ONE mesh at the moment of the cut and slice that once, " +
             "instead of cutting each mesh separately. Gives two whole halves rather than one " +
             "pair of chunks per mesh, and costs one plane solve instead of three. Turn off to " +
             "cut each mesh individually.")]
    public bool CombineBodyMeshes = true;
    [Tooltip("If a combined cut never reports back, the hidden body meshes are shown again " +
             "after this long so the corpse doesn't vanish.")]
    public float ProxyFallbackDelay = 1f;

    [Header("Gate")]
    [Tooltip("Seconds after an execute during which the swing is allowed to cut. PlayerMelee " +
             "cuts on the same frame it executes, so this only needs to be long enough to " +
             "absorb a frame hitch.")]
    public float ExecuteSliceWindow = 0.5f;
    [Tooltip("Also slice when a normal katana swing KILLS the enemy, not just on an execute. " +
             "The enemy still has to be dead — a swing that only damages it never cuts.\n\n" +
             "Note this opens the same window on ANY death, so a swing landing within " +
             "ExecuteSliceWindow of a death you caused some other way (a bullet) will also " +
             "cut the corpse.")]
    public bool SliceOnMeleeKill = true;

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

    [Header("Piece Lifetime")]
    [Tooltip("Override how long the halves lie around before they blink out, instead of using " +
             "MeleeMeshCutter's shared setting. That default is tuned for rank-and-file corpses; " +
             "a boss cut in half is the payoff of the whole fight and wants to stay on screen " +
             "much longer. Off = use the cutter's global timing, which is right for everyone else.")]
    public bool OverridePieceLifetime = false;
    [Tooltip("Seconds the halves lie there before their exit starts.")]
    [Min(0f)] public float PieceLifetime = 20f;
    [Tooltip("How long the blink-out then takes.")]
    [Min(0f)] public float PieceExitDuration = 3f;
    [Tooltip("Blink the halves out at the end. Off makes them vanish in one frame — usually " +
             "worse, but it's the honest option if the blink reads as a glitch on a big piece.")]
    public bool PieceBlink = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    /// <summary>True once a cut has been requested — an enemy is only ever sliced once.</summary>
    public bool HasBeenSliced => _sliceRequested;

    private EnemyHealth  _health;
    private EnemyRagdoll _ragdoll;
    private readonly System.Collections.Generic.List<MeshTarget> _resolved =
        new System.Collections.Generic.List<MeshTarget>();
    private GameObject _proxy;
    private bool       _usingCombinedProxy;
    private Bounds     _preCutBodyBounds;
    private Renderer[] _hiddenRenderers;
    private Coroutine  _proxyFallback;
    private bool       _cutInFlight;

    /// <summary>Floor under ProxyFallbackDelay. The cutter is async, so the fallback has to
    /// outlast the worst-case cut — a merged boss mesh in a heavy scene is far slower than
    /// the same cut in a test scene, which is exactly why this only ever bit the boss in
    /// the real level.</summary>
    private const float MinAsyncFallbackDelay = 6f;
    private float   _executeTime = float.NegativeInfinity;
    private float   _deathTime   = float.NegativeInfinity;
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
        _health.OnDied     += HandleDied;

        if (BodyMeshTargets.Length == 0)
            Debug.LogWarning($"[EnemySliceable] {name}: no MeshTarget found on any body mesh — " +
                             "executes will play the normal ragdoll death instead of slicing.", this);
    }

    private void OnDestroy()
    {
        if (_health != null)
        {
            _health.OnExecuted -= HandleExecuted;
            _health.OnDied     -= HandleDied;
        }
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

    // Die() fires this from inside TakeDamage, so a killing swing has already opened the
    // window by the time PlayerMelee calls TryCutHit a few lines later in the same frame.
    private void HandleDied(Vector3 impulse) => _deathTime = Time.time;

    /// <summary>True while a cut is allowed — an execute, or a lethal swing if enabled.</summary>
    private bool SliceWindowOpen =>
        Time.time - _executeTime <= ExecuteSliceWindow ||
        (SliceOnMeleeKill && Time.time - _deathTime <= ExecuteSliceWindow);

    // ── ISliceable ────────────────────────────────────────────────────────────────

    /// <summary>True only while a combined proxy is in play — BuildCombinedProxy bakes the
    /// body into WORLD space, so the cutter must not apply its parent-chain scale correction
    /// on top. Cutting the real skinned targets individually is the ordinary local-space case.</summary>
    public bool SliceTargetsAreWorldSpace => _usingCombinedProxy;

    public System.Collections.Generic.IReadOnlyList<MeshTarget> ResolveSliceTargets(Vector3 hitPoint)
    {
        if (_sliceRequested) return null;
        if (!SliceWindowOpen) return null;
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

        if (_resolved.Count == 0) return null;

        // Bake the body into one static mesh and cut THAT once, rather than cutting each
        // skinned mesh separately. Two whole halves instead of six loose chunks, one plane
        // solve instead of three, and no bone/bindpose work — the pieces are static geometry
        // either way, because the rig is stripped the moment the cut lands.
        // World bounds of the body as it stands, so the pieces can be measured against it
        // after the cut — that's what turns "the halves look wrong" into a number.
        _preCutBodyBounds = MeasureBodyBounds();

        if (CombineBodyMeshes && _resolved.Count > 1)
        {
            MeshTarget combined = BuildCombinedProxy(_resolved);
            if (combined != null)
            {
                _resolved.Clear();
                _resolved.Add(combined);
                _usingCombinedProxy = true;
            }
            // Null means the combine failed — fall through and cut the meshes individually.
        }

        return _resolved;
    }

    /// <summary>
    /// Bakes every source mesh at its current pose, merges them into one world-space mesh
    /// (one submesh per source material, so nothing loses its look), and returns a MeshTarget
    /// on a throwaway object holding it. The originals are hidden, not destroyed — a failed
    /// cut restores them and the normal ragdoll death plays out.
    /// </summary>
    private MeshTarget BuildCombinedProxy(System.Collections.Generic.List<MeshTarget> sources)
    {
        var combines  = new System.Collections.Generic.List<CombineInstance>();
        var materials = new System.Collections.Generic.List<Material>();
        var hidden    = new System.Collections.Generic.List<Renderer>();

        foreach (var source in sources)
        {
            var skinned = source.GetComponent<SkinnedMeshRenderer>();
            if (skinned == null || skinned.sharedMesh == null) continue;

            var baked = new Mesh();
            skinned.BakeMesh(baked);   // current pose

            // Each mesh bakes into its own space, so it still needs placing in the world —
            // but whether that matrix may carry scale depends on what the bake already did.
            Matrix4x4 place = ResolveBakeMatrix(skinned, baked);

            var sourceMats = skinned.sharedMaterials;
            for (int sub = 0; sub < baked.subMeshCount; sub++)
            {
                combines.Add(new CombineInstance
                {
                    mesh         = baked,
                    subMeshIndex = sub,
                    transform    = place
                });
                materials.Add(sub < sourceMats.Length ? sourceMats[sub] : sourceMats.Length > 0 ? sourceMats[0] : null);
            }

            hidden.Add(skinned);
        }

        if (combines.Count == 0) return null;

        var mesh = new Mesh { name = $"{name}_SliceProxy" };
        // A merged humanoid can pass 65k verts; the 16-bit default would silently corrupt it.
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.CombineMeshes(combines.ToArray(), mergeSubMeshes: false, useMatrices: true);

        ConformToBody(mesh);

        // Identity transform: the combined mesh is already in world space, and it means local
        // and world space agree for the cut — no scale correction, no inherited-scale surprises.
        _proxy = new GameObject($"{name}_SliceProxy");
        _proxy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Parented to the enemy (keeping its world identity) so the cutter's
        // transform.root lookup finds the body's colliders — otherwise the halves are born
        // inside the ragdoll with nothing telling them to ignore it. SetParent preserves the
        // world transform, and the cutter's lossy/local scale correction cancels out whatever
        // local scale that leaves behind.
        _proxy.transform.SetParent(transform, worldPositionStays: true);
        _proxy.layer = sources[0].gameObject.layer;

        _proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
        _proxy.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();

        var proxyTarget = _proxy.AddComponent<MeshTarget>();
        proxyTarget.GameobjectRoot = _proxy;

        // Inherit the cut-face material and piece behaviour from the real body meshes.
        var template = sources[0];
        proxyTarget.OverrideFaceMaterial = template.OverrideFaceMaterial;
        proxyTarget.SeparateMeshes       = template.SeparateMeshes;
        proxyTarget.ApplyTranslation     = template.ApplyTranslation;
        for (int i = 0; i < 2; i++)
        {
            // Fully qualified: UnityEngine.Behaviour (the component base class) is also in
            // scope here, so the bare name is ambiguous.
            proxyTarget.DefaultBehaviour[i]   = DynamicMeshCutter.Behaviour.Stone;
            proxyTarget.CreateRigidbody[i]    = template.CreateRigidbody[i];
            proxyTarget.CreateMeshCollider[i] = template.CreateMeshCollider[i];
        }

        foreach (var renderer in hidden) renderer.enabled = false;
        _hiddenRenderers = hidden.ToArray();

        // If the cut never reports back, put the body on screen again rather than leaving an
        // invisible corpse next to an uncut proxy.
        if (_proxyFallback != null) StopCoroutine(_proxyFallback);
        _proxyFallback = StartCoroutine(Co_ProxyFallback());

        return proxyTarget;
    }

    /// <summary>
    /// Places one baked mesh into world space, applying the renderer's scale exactly once.
    ///
    /// SkinnedMeshRenderer.BakeMesh is documented to leave the transform's scale out (that's
    /// what the useScale overload is for), which would make localToWorldMatrix the right
    /// matrix. On a rig imported at a 0.01 scale factor — this mech's, with nodes at scale
    /// 100 — the bake comes out carrying the scale anyway, because it's already baked into
    /// the bone matrices. Multiplying by localToWorldMatrix then applies it a SECOND time
    /// and the body is born ~100x too big.
    ///
    /// Rather than pick a side, this measures which one actually happened: the baked mesh is
    /// compared against the renderer's own localBounds, which by definition exclude the
    /// transform's scale. A bake that came out roughly localBounds-sized didn't apply scale
    /// and needs the full matrix; one that came out roughly lossyScale times bigger already
    /// did, and must be placed with rotation and position only. The two cases are ~100x
    /// apart here, so the test has enormous margin and collapses to the same answer on a
    /// normally-scaled rig.
    /// </summary>
    private static Matrix4x4 ResolveBakeMatrix(SkinnedMeshRenderer skinned, Mesh baked)
    {
        Transform t = skinned.transform;

        Vector3 localSize = skinned.localBounds.size;
        Vector3 bakedSize = baked.bounds.size;
        if (localSize.sqrMagnitude < 1e-6f || bakedSize.sqrMagnitude < 1e-6f)
            return t.localToWorldMatrix;

        Vector3 lossy = t.lossyScale;
        float averageScale = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
        float grew = bakedSize.magnitude / localSize.magnitude;

        // Nearer the scale factor than to 1 means the scale is already in the vertices.
        bool bakeAlreadyScaled = Mathf.Abs(grew - averageScale) < Mathf.Abs(grew - 1f);

        return bakeAlreadyScaled
            ? Matrix4x4.TRS(t.position, t.rotation, Vector3.one)
            : t.localToWorldMatrix;
    }

    /// <summary>
    /// Shows the body again if a cut never reports back.
    ///
    /// The delay has a floor because the cutter runs async by default: it resolves the cut
    /// several frames later in CutterBehaviour.Update, and a boss-sized combined mesh in a
    /// heavy scene can take well over a second. A fallback that fires first used to tear the
    /// proxy down mid-cut, which is a hard crash — see RestoreOriginalMeshes.
    /// </summary>
    private IEnumerator Co_ProxyFallback()
    {
        yield return new WaitForSeconds(Mathf.Max(ProxyFallbackDelay, MinAsyncFallbackDelay));
        _proxyFallback = null;

        Debug.LogWarning($"[EnemySliceable] {name}: the cut hadn't reported back after " +
                         $"{Mathf.Max(ProxyFallbackDelay, MinAsyncFallbackDelay):0.#}s — showing the body again. " +
                         $"{(_cutInFlight ? "A cut is still in flight, so the proxy is kept until it resolves." : "")}", this);

        RestoreOriginalMeshes();
    }

    /// <summary>
    /// Forces the combined mesh to sit exactly where the body it was baked from sits.
    ///
    /// BakeMesh and localToWorldMatrix have to agree about whose job the renderer's own
    /// scale is — BakeMesh's single-argument form leaves scale out, so the matrix puts it
    /// back. On a rig imported at a 100x scale factor (this mech's is: there are nodes at
    /// scale 100 in its hierarchy) getting that wrong doesn't produce a subtle error, it
    /// produces a body a hundred times too big, positioned a hundred times too far out.
    ///
    /// ResolveBakeMatrix above is what actually gets this right, per renderer and exactly.
    /// This is the net under it: it measures the merged result against the renderers' own
    /// world bounds — ground truth, whatever the rig does — and rescales if something still
    /// came out grossly wrong. Note it can only fit BOUNDS, so the number it lands on is an
    /// approximation (a diagonal ratio, not the rig's real scale factor); if this fires at
    /// all, the matrix test above missed and that's the thing to fix. The threshold is
    /// deliberately loose — a skinned renderer's bounds are conservative, so small
    /// differences are normal and only a gross mismatch is a real fault.
    /// </summary>
    private void ConformToBody(Mesh mesh)
    {
        if (_preCutBodyBounds.size.sqrMagnitude < 0.0001f) return;

        mesh.RecalculateBounds();

        float expected = _preCutBodyBounds.size.magnitude;
        float actual = mesh.bounds.size.magnitude;
        if (expected < 0.0001f || actual < 0.0001f) return;

        float ratio = expected / actual;
        if (ratio < 1.25f && ratio > 0.8f) return; // within bounds slop — leave it alone

        Vector3 pivot = mesh.bounds.center;
        Vector3 target = _preCutBodyBounds.center;

        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
            verts[i] = target + (verts[i] - pivot) * ratio;

        mesh.vertices = verts;
        mesh.RecalculateBounds();

        Debug.LogWarning($"[EnemySliceable] {name}: fallback fit fired — the combined body mesh was ~{1f / ratio:F1}x " +
                         "its true size and has been fitted to the body's bounds. ResolveBakeMatrix should have " +
                         "prevented this, so the per-renderer scale test missed on this rig; the halves will be " +
                         "close but not exact until that's fixed.", this);
    }

    /// <summary>Combined world-space bounds of every body mesh about to be cut.</summary>
    private Bounds MeasureBodyBounds()
    {
        Bounds bounds = default;
        bool started = false;

        foreach (var target in _resolved)
        {
            var renderer = target != null ? target.GetComponent<Renderer>() : null;
            if (renderer == null) continue;

            if (!started) { bounds = renderer.bounds; started = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        return bounds;
    }

    private void SetOriginalRenderers(bool visible)
    {
        if (_hiddenRenderers == null) return;
        foreach (var renderer in _hiddenRenderers)
            if (renderer != null) renderer.enabled = visible;
    }

    /// <summary>
    /// Undoes the proxy swap — used when the cut fails or never resolves.
    ///
    /// Destroying the proxy while a cut is still in flight is a hard crash, not a leak:
    /// DynamicMeshCutter resolves an async cut in a later Update, MeshCreation.CreateObjects
    /// returns null the moment info.MeshTarget is gone, and CutterBehaviour.CreateGameObjects
    /// dereferences that null without a check. The exception is thrown before the created
    /// callback runs, so OnSliceFinished never fires either and the whole slice dies silently
    /// apart from the stack trace. So: show the body again either way, but leave the proxy
    /// standing until the cutter has actually finished with it.
    /// </summary>
    private void RestoreOriginalMeshes()
    {
        _usingCombinedProxy = false;
        SetOriginalRenderers(true);

        if (_cutInFlight)
        {
            // Hidden rather than destroyed — the cutter still holds a reference to it.
            if (_proxy != null && _proxy.TryGetComponent(out MeshRenderer proxyRenderer))
                proxyRenderer.enabled = false;
            return;
        }

        _hiddenRenderers = null;
        if (_proxy != null) { Destroy(_proxy); _proxy = null; }
    }

    public void OnSliceStarted(Vector3 hitPoint, Vector3 planeNormal)
    {
        _sliceRequested = true;
        _cutInFlight = true;
        _cutPoint  = hitPoint;
        _cutNormal = planeNormal.sqrMagnitude > 0.0001f ? planeNormal.normalized : Vector3.up;

        // Detached now that MeleeMeshCutter has captured the source colliders off
        // transform.root (it does that just before calling this). While the proxy stayed
        // parented to the enemy, anything that destroyed the body mid-cut — the corpse
        // despawn, the spawner cleaning up, the boss root being stripped — took the
        // cutter's live MeshTarget down with it and crashed the same way.
        if (_proxy != null) _proxy.transform.SetParent(null, worldPositionStays: true);
    }

    public void OnSliceFinished(bool success, GameObject[] pieces)
    {
        _cutInFlight = false;
        if (_proxyFallback != null) { StopCoroutine(_proxyFallback); _proxyFallback = null; }

        if (!success)
        {
            // Nothing was cut. Put the real body meshes back (the combined proxy hid them) and
            // let the normal ragdoll death play out.
            Log("Cut failed — falling back to the normal ragdoll death.");
            RestoreOriginalMeshes();
            return;
        }

        // A slow cut can resolve after the fallback already put the body back on screen.
        // The halves are what should be visible, so hide it again.
        SetOriginalRenderers(false);

        // The cutter destroys the proxy along with the halves it replaced; drop our reference
        // so nothing tries to restore a body that's already been carved up.
        _proxy = null;
        _hiddenRenderers = null;

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

            ApplyPieceLifetime(pieces);
        }

        if (SliceVFXPrefab != null)
            Destroy(Instantiate(SliceVFXPrefab, _cutPoint, Quaternion.LookRotation(_cutNormal)),
                    SliceVFXLifetime);

        if (StripRigOnSlice) StripRig();
        if (DespawnRootAfterSlice) Destroy(gameObject, DeathSequenceDuration() + RootDespawnMargin);

        ReportPieceScale(pieces);
        Log("Sliced on execute.");
    }

    /// <summary>
    /// Measures the halves against the body they came from and complains if they don't
    /// match. A slice going wrong on scale is otherwise only visible by eye, and the two
    /// causes look identical on screen — this separates them:
    ///
    /// • combined mesh already oversized  → the world-space bake is wrong (BakeMesh /
    ///   localToWorldMatrix disagreeing with this rig's import scale)
    /// • combined mesh right, pieces big  → a scale correction was applied on top of
    ///   world-space vertices (see ISliceable.SliceTargetsAreWorldSpace)
    /// </summary>
    private void ReportPieceScale(GameObject[] pieces)
    {
        if (pieces == null || pieces.Length == 0) return;
        if (_preCutBodyBounds.size.sqrMagnitude < 0.0001f) return;

        Bounds combined = default;
        bool started = false;

        foreach (var piece in pieces)
        {
            if (piece == null) continue;
            foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
            {
                if (!started) { combined = r.bounds; started = true; }
                else combined.Encapsulate(r.bounds);
            }
        }
        if (!started) return;

        float before = _preCutBodyBounds.size.magnitude;
        float after = combined.size.magnitude;
        float ratio = before > 0.0001f ? after / before : 0f;

        // The halves separate slightly before this runs, so the pieces always measure a
        // little larger than the body. Anything past 2x is a scale fault, not separation.
        if (ratio > 2f || ratio < 0.5f)
        {
            Debug.LogWarning($"[EnemySliceable] {name}: the halves came out {ratio:F1}x the size of the body " +
                             $"(body {before:F2}m, pieces {after:F2}m). Combined proxy: {_usingCombinedProxy}. " +
                             "A ratio near the rig's import scale means the cutter's scale correction was applied " +
                             "to world-space vertices; a ratio that's already wrong on the proxy means the bake is.", this);
        }
        else
        {
            Log($"Piece scale OK — {ratio:F2}x the body (body {before:F2}m, pieces {after:F2}m).");
        }
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

    /// <summary>
    /// Replaces the exit timing MeleeMeshCutter attached to each piece with this
    /// enemy's own. Re-Attach reconfigures the existing component in place, and its
    /// coroutine hasn't started yet (Start runs after this frame's callbacks), so
    /// nothing is half-played when the new settings land — same approach RiotShield
    /// uses for its own halves.
    /// </summary>
    private void ApplyPieceLifetime(GameObject[] pieces)
    {
        if (!OverridePieceLifetime) return;

        var exit = new SlicedPieceCleanup.Settings
        {
            Lifetime      = PieceLifetime,
            ExitDuration  = PieceExitDuration,
            Blink         = PieceBlink,
            BlinkInterval = 0.18f,
            ScaleOut      = false,
            ScaleTo       = 1f
        };

        foreach (var piece in pieces)
            if (piece != null) SlicedPieceCleanup.Attach(piece, exit);
    }

    /// <summary>How long EnemyHealth's death sequence needs before the root can go —
    /// its explosions, plus any corpse hold it was given on top.</summary>
    private float DeathSequenceDuration()
    {
        if (_health == null) return 0f;
        return Mathf.Max(0, _health.ExplosionCount) * Mathf.Max(0f, _health.DelayBetweenExplosions)
             + Mathf.Max(0f, _health.CorpseLifetime);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[EnemySliceable] {name}: {msg}", this);
    }
}
