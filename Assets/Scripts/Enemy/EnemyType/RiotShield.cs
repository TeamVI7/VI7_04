using System.Collections;
using UnityEngine;
using DynamicMeshCutter;

/// <summary>
/// A physical riot-shield prop. Place this on a separate collider positioned in front
/// of the body (e.g. a child of the shield-holding hand bone) — weapon raycasts and
/// melee sphere-casts that visually hit the shield collider route damage here first,
/// before ever reaching the body's own collider, exactly like hitting a wall.
///
/// Ranged hits are blocked forever — no armor pool, no wear-down. Melee hits are
/// different: routed through IMeleeDamageable (PlayerMelee checks this before falling
/// back to IDamageable, same as MechWeakpoint), a bash physically destroys the shield
/// outright, permanently exposing the enemy to everything from then on. This gives the
/// player two valid counters — flank it, or melee-rush and smash it open.
///
/// With SliceOnMeleeBreak on, the bash routes through ISliceable so PlayerMelee's
/// MeleeMeshCutter carves the shield mesh in half along the swing plane (Dynamic Mesh
/// Cutter) rather than just despawning it. The halves get an impulse, then blink out
/// via SlicedPieceCleanup.
///
/// Contrast with ShieldBehaviour (Shield.cs) — that's an omnidirectional energy-armor
/// pool that depletes and dissolves. This is a one-sided physical block.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RiotShieldBehaviour : MonoBehaviour, IDamageable, IMeleeDamageable, ISliceable
{
    [Tooltip("Root whose forward direction defines the shield's facing. Defaults to the parent EnemyHealth's transform.")]
    public Transform BodyRoot;

    [Tooltip("Half-angle (degrees) from BodyRoot.forward, measured toward the attacker, that still counts as a blocked hit. " +
             "Only matters as a fallback for damage delivered without a real raycast (e.g. AOE) — normally the shield collider's " +
             "physical placement is what blocks/doesn't-block a hit.")]
    public float BlockAngle = 100f;

    [Header("Block Feedback (ranged)")]
    public GameObject BlockVFXPrefab;
    public float BlockVFXLifetime = 1f;

    [Header("Destroy Feedback (melee)")]
    [Tooltip("Debris/spark burst spawned at the shield's position when a melee hit destroys it.")]
    public GameObject BreakVFXPrefab;
    public float BreakVFXLifetime = 2f;

    [Header("Melee Slice (Dynamic Mesh Cutter)")]
    [Tooltip("Slice the shield mesh apart along the swing instead of just despawning it. " +
             "Needs a MeshTarget on the shield mesh — assign it below or leave empty to auto-find in children.")]
    public bool SliceOnMeleeBreak = true;
    [Tooltip("Mesh that gets cut. Auto-found on this object or its children when left empty.")]
    public MeshTarget ShieldMeshTarget;
    [Tooltip("How fast the halves separate, in PIECE SIZES per second — not metres. A shield " +
             "half is small, so a plain m/s impulse launches it across the room. 1 = the half " +
             "drifts apart by its own width every second.")]
    public float SlicePieceSeparationSpeed = 1.5f;
    [Tooltip("Delay from the cut to the exit starting. Keep it short so the scale-up reads as " +
             "part of the slice rather than a despawn that happens later.")]
    public float SlicePieceLifetime = 0.35f;
    [Tooltip("How long the exit takes.")]
    public float SlicePieceExitDuration = 0.8f;
    [Tooltip("Scale the halves reach by the end of the exit, as a multiple of their size at " +
             "the cut. Above 1 grows them, below 1 shrinks them.")]
    public float SlicePieceScaleTo = 1.5f;
    [Tooltip("Blink while scaling, so the halves have a tell before they're removed.")]
    public bool SlicePieceBlink = true;
    [Tooltip("Safety net — if the cut never reports back (cutter missing on the swing, async " +
             "hiccup) the shield despawns normally after this many seconds.")]
    public float SliceFallbackDelay = 0.5f;

    public bool IsDestroyed { get; private set; }
    /// <summary>True between the melee bash and the async cut resolving.</summary>
    public bool IsAwaitingSlice { get; private set; }

    /// <summary>Fired every time a ranged hit is absorbed by the shield. EnemyAudio subscribes to this for a clang/impact sound.</summary>
    public event System.Action OnBlocked;
    /// <summary>Fired once, when a melee hit destroys the shield outright.</summary>
    public event System.Action OnShieldDestroyed;

    private EnemyHealth _health;
    private Collider _collider;
    private Coroutine _sliceFallback;
    private bool _sliceRequested;
    private readonly MeshTarget[] _targetBuffer = new MeshTarget[1];
    private Vector3 _breakPoint;
    private Vector3 _breakDirection;

    private void Awake()
    {
        _health   = GetComponentInParent<EnemyHealth>();
        _collider = GetComponent<Collider>();
        if (BodyRoot == null) BodyRoot = _health != null ? _health.transform : transform.parent;
        if (ShieldMeshTarget == null) ShieldMeshTarget = GetComponentInChildren<MeshTarget>(true);
    }

    public void TakeDamage(float amount, Vector3 direction, Vector3 hitPoint)
    {
        if (IsDestroyed) { _health?.TakeDamage(amount, direction, hitPoint); return; }

        if (IsFrontal(direction))
        {
            Block(hitPoint, direction);
            return;
        }

        // Shouldn't normally happen — the shield collider only physically occupies the
        // front, so a raycast/spherecast landing here at all is already a frontal hit.
        // Kept as a fallback in case something (AOE, scripted damage) calls this directly.
        _health?.TakeDamage(amount, direction, hitPoint);
    }

    // PlayerMelee checks IMeleeDamageable before IDamageable, so every melee swing that
    // visually connects with the shield collider lands here instead of TakeDamage above —
    // a bash always destroys the shield, it never just "blocks" a melee hit.
    public bool TakeMeleeDamage(float amount, Vector3 direction, Vector3 hitPoint)
    {
        if (IsDestroyed) return false; // nothing left to hit here — let the swing miss/pass through

        DestroyShield(hitPoint, direction);
        return true; // landed — player gets normal melee hit feedback (sound, knockback)
    }

    private bool IsFrontal(Vector3 hitDirection)
    {
        if (BodyRoot == null) return true;
        if (hitDirection.sqrMagnitude < 0.0001f) return true;

        // hitDirection is the shot's travel direction (shooter -> target), so the
        // direction back toward the attacker is the negation of it.
        Vector3 towardAttacker = -hitDirection.normalized;
        return Vector3.Angle(BodyRoot.forward, towardAttacker) <= BlockAngle * 0.5f;
    }

    private void Block(Vector3 hitPoint, Vector3 hitDirection)
    {
        OnBlocked?.Invoke();

        if (BlockVFXPrefab == null) return;
        Quaternion rot = hitDirection.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(-hitDirection) : Quaternion.identity;
        Destroy(Instantiate(BlockVFXPrefab, hitPoint, rot), BlockVFXLifetime);
    }

    private void DestroyShield(Vector3 hitPoint, Vector3 hitDirection)
    {
        IsDestroyed = true;
        if (_collider != null) _collider.enabled = false; // stop absorbing anything, ranged or melee, from this point on

        _breakPoint     = hitPoint;
        _breakDirection = hitDirection;

        OnShieldDestroyed?.Invoke();

        if (BreakVFXPrefab != null)
        {
            Quaternion rot = hitDirection.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(-hitDirection) : Quaternion.identity;
            Destroy(Instantiate(BreakVFXPrefab, hitPoint, rot), BreakVFXLifetime);
        }

        // PlayerMelee resolves damage first and runs its mesh cutter immediately after, so
        // when slicing is on we hold off despawning and let the cutter carve this mesh up —
        // OnSliceFinished (or the fallback below) finishes the teardown.
        if (SliceOnMeleeBreak && ResolveSliceTarget(hitPoint) != null)
        {
            IsAwaitingSlice = true;
            _sliceFallback  = StartCoroutine(Co_SliceFallback());
            return;
        }

        // Removes the prop's own renderer/collider from the scene; EnemyHealth, EnemyAudio
        // etc. all live on the parent root and are unaffected by this GameObject going away.
        Destroy(gameObject);
    }

    // ── ISliceable ────────────────────────────────────────────────────────────────

    /// <summary>The shield hands over its real MeshTarget, whose vertices are in that
    /// object's own local space — so the cutter's normal scale correction applies.</summary>
    public bool SliceTargetsAreWorldSpace => false;
    // The swing hits this object's block collider, which is usually a plain box that
    // isn't the visible mesh — so the cutter asks us which mesh to actually cut.

    public System.Collections.Generic.IReadOnlyList<MeshTarget> ResolveSliceTargets(Vector3 hitPoint)
    {
        var target = ResolveSliceTarget(hitPoint);
        if (target == null) return null;

        _targetBuffer[0] = target;
        return _targetBuffer;
    }

    /// <summary>A shield is a single mesh — the list form above just wraps this.</summary>
    private MeshTarget ResolveSliceTarget(Vector3 hitPoint)
    {
        if (!SliceOnMeleeBreak || _sliceRequested) return null;
        // DestroyShield flips IsDestroyed before asking, so this only ever cuts a shield
        // that the bash actually broke — never one still doing its job.
        if (!IsDestroyed) return null;
        if (ShieldMeshTarget == null || !ShieldMeshTarget.isActiveAndEnabled) return null;

        // The cutter destroys GameobjectRoot when it replaces the mesh with the halves.
        // Keep that inside the shield's own hierarchy — a root left pointing at the enemy
        // (or unset) would otherwise delete the whole enemy on a shield bash.
        var root = ShieldMeshTarget.GameobjectRoot;
        if (root == null || !root.transform.IsChildOf(transform))
            ShieldMeshTarget.GameobjectRoot = ShieldMeshTarget.gameObject;

        return ShieldMeshTarget;
    }

    public void OnSliceStarted(Vector3 hitPoint, Vector3 planeNormal)
    {
        _sliceRequested = true;
    }

    public void OnSliceFinished(bool success, GameObject[] pieces)
    {
        IsAwaitingSlice = false;
        if (_sliceFallback != null) { StopCoroutine(_sliceFallback); _sliceFallback = null; }

        if (success && pieces != null)
        {
            foreach (var piece in pieces)
            {
                if (piece == null) continue;
                if (!piece.TryGetComponent(out Rigidbody rb)) continue;

                // Push the halves apart from the impact point, carried along by the swing.
                Vector3 away = piece.transform.position - _breakPoint;
                if (away.sqrMagnitude < 0.0001f) away = Random.onUnitSphere;
                Vector3 dir = (away.normalized + _breakDirection.normalized * 0.5f).normalized;

                // VelocityChange, not Impulse: the created pieces all get mass 1 regardless of
                // how big they are, so a force-based push has no relationship to their size.
                float speed = SlicePieceSeparationSpeed * SlicedPieceCleanup.PieceRadius(piece);
                rb.AddForce(dir * speed, ForceMode.VelocityChange);
            }

            // Override whatever default exit MeleeMeshCutter attached — the shield's halves
            // scale as they go. Re-Attach reconfigures the existing component, and its
            // coroutine hasn't started yet (Start runs after this frame's callbacks), so
            // nothing is half-played when the new settings land.
            var exit = new SlicedPieceCleanup.Settings
            {
                Lifetime      = SlicePieceLifetime,
                ExitDuration  = SlicePieceExitDuration,
                Blink         = SlicePieceBlink,
                BlinkInterval = 0.12f,
                ScaleOut      = true,
                ScaleTo       = SlicePieceScaleTo
            };

            foreach (var piece in pieces)
                SlicedPieceCleanup.Attach(piece, exit);
        }

        // Either way this prop is done. If the cut succeeded the cutter already destroyed
        // the mesh target; this clears the collider/holder object it lived under.
        Destroy(gameObject);
    }

    private IEnumerator Co_SliceFallback()
    {
        yield return new WaitForSeconds(SliceFallbackDelay);

        if (!IsAwaitingSlice) yield break;
        IsAwaitingSlice = false;
        _sliceFallback  = null;
        Destroy(gameObject);
    }
}
