// MechLaserSweepUltimate.cs — "Big Ass Laser"
//
// The boss plants itself, charges up, then extends one or more enormous beams and
// spins them a full 360 (or several) around itself. The player survives by reading
// the beam's height: a low beam is jumped, a high beam is ducked/crouched under,
// and a beam at torso height has to be broken line-of-sight on with cover.
//
// Hit detection is deliberately NOT a per-frame raycast. At sweep speed the beam
// can cross the player entirely between two frames, so instead this tests whether
// the swept ARC between last frame's angle and this frame's angle passed over the
// player — no tunnelling regardless of frame rate or spin speed. The vertical test
// is separate, which is what makes jumping/crouching an actual dodge rather than
// a cosmetic one.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public class MechLaserSweepUltimate : MechAttackBehaviour
{
    public enum SpinDirection
    {
        [Tooltip("Random each cast.")]
        Random,
        Clockwise,
        CounterClockwise,
        [Tooltip("Flips every time the attack is used, so the player can't learn one dodge and coast.")]
        Alternate,
    }

    public enum EmitterMode
    {
        [Tooltip("The mech emits the beams itself, from the pivot below.")]
        Self,
        [Tooltip("The mech launches drones that orbit it, one per beam, each dragging a beam outward. " +
                 "Same sweep, but the ring under the drones is safe and shooting a drone down kills its beam.")]
        Drones,
    }

    public enum PivotMode
    {
        [Tooltip("Measured from the mech's renderers every cast — horizontally centred on the body, vertically at the anchor below.")]
        AutoCentre,
        [Tooltip("Use the Beam Pivot transform exactly as it is.")]
        ExplicitTransform,
    }

    /// <summary>Where up the mech's measured height the emitter sits. Named points
    /// rather than a raw metre value so it stays right across differently sized
    /// mechs and survives someone rescaling the model.</summary>
    public enum PivotAnchor
    {
        Ground,
        Knees,
        Hips,
        Chest,
        Shoulders,
        Head,
        [Tooltip("Uses Pivot Height Fraction.")]
        Custom,
    }

    #region Inspector

    [Header("Animation")]
    public Animator animator;
    [Tooltip("Animator trigger fired when the charge begins. Leave empty to skip.")]
    public string chargeTrigger = "LaserCharge";
    [Tooltip("Animator bool held true for as long as the beams are actually sweeping. Leave empty to skip.")]
    public string firingBool = "LaserFiring";

    [Header("Charge")]
    [Tooltip("Seconds of wind-up before the beams start turning. This is the whole tell — long enough for the player to find cover or get clear of the emitter.")]
    public float chargeTime = 2f;
    [Tooltip("Optional VFX spawned at the emitter during the charge and destroyed when the sweep starts.")]
    public GameObject chargeVfxPrefab;
    [Tooltip("Show the beams during the charge at a fraction of their real width, held still. Free, honest telegraph — the player sees exactly what height each beam is at and which way they'll have to move.")]
    public bool showChargeBeams = true;
    [Range(0.01f, 1f)] public float chargeBeamWidthScale = 0.15f;

    [Header("Emitter")]
    [Tooltip("Who actually holds the beams — the mech itself, or a flight of drones it launches.")]
    public EmitterMode emitterMode = EmitterMode.Self;
    [Tooltip("Auto Centre measures the mech's own renderers and puts the pivot on its centre line at the anchor below — that's what a 360 sweep wants, and it can't drift the way a rig bone does. " +
             "Explicit Transform hands control to Beam Pivot instead; only use it for a purpose-built empty, never for a muzzle or gun bone.")]
    public PivotMode pivotMode = PivotMode.AutoCentre;
    [Tooltip("Which point up the mech's body the beams come from. Ground/Head are the extremes of its measured height; Custom uses Pivot Height Fraction.")]
    public PivotAnchor pivotAnchor = PivotAnchor.Chest;
    [Tooltip("Only used by the Custom anchor. 0 = the mech's feet, 1 = the top of its head.")]
    [Range(0f, 1.5f)] public float pivotHeightFraction = 0.7f;
    [Tooltip("Fine-tune in metres, added on top of the anchor. Positive raises the emitter.")]
    public float pivotHeightOffset = 0f;
    [Tooltip("Only used when Pivot Mode is Explicit Transform.")]
    public Transform beamPivot;

    [Header("Drones (Emitter Mode = Drones)")]
    [Tooltip("The drone that carries a beam. One is spawned per beam. Without a prefab the beams still work — they just come from invisible points on the orbit.")]
    public GameObject dronePrefab;
    [Tooltip("Drones in the flight, and therefore beams. Overrides Beam Count while in drone mode. 2-3 reads best; more and the safe angles get too tight to find.")]
    [Range(1, 8)] public int droneCount = 3;
    [Tooltip("How far out from the mech's centre line the drones orbit. Everything inside this ring is safe, so this is the size of the bubble at the boss's feet.")]
    public float droneOrbitRadius = 6f;
    [Tooltip("Seconds the drones take to fly from the mech out to their orbit. Clamped to the charge time — they're in position by the moment the beams go live.")]
    public float droneDeployTime = 1.2f;
    [Tooltip("Where on the mech the drones launch from, relative to the emitter pivot.")]
    public Vector3 droneLaunchOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("Where the beam leaves the drone, in the drone's own local space. Leave at zero to fire from its origin; use it to move the beam out to a nose or an underslung emitter.")]
    public Vector3 droneBeamOriginOffset = Vector3.zero;
    [Tooltip("How far a drone may be from its station and still have the beam anchored to it. Beyond this the beam stays on the station instead — that's what stops a drone that's still flying out (or one whose own prefab is driving it around) from dragging its beam back through the mech.")]
    public float droneOnStationTolerance = 1.5f;
    [Tooltip("Switch off the drone's own NavMeshAgent and physics for the sweep so this script's formation flying is the only thing moving it. Turn this off only if the prefab is a dumb visual with no movement of its own.")]
    public bool droneMotionOverride = true;
    [Tooltip("Burst spawned on each drone as the flight is recalled at the end of the sweep.")]
    public GameObject droneDespawnVfxPrefab;
    [Tooltip("Leave the drones behind when the sweep ends instead of despawning them. Only do this if the prefab can look after itself — it has its own health and despawn.")]
    public bool droneOutlivesSweep;

    [Header("Beams")]
    [Min(1)] public int beamCount = 2;
    [Tooltip("How far each beam reaches. Anything past this is a safe zone, so keep it comfortably larger than the arena if you want the whole floor covered.")]
    public float beamLength = 40f;
    [Tooltip("Beam width in metres. Also the hit thickness — a fat beam is harder to jump.")]
    public float beamThickness = 0.6f;
    [Tooltip("Players inside this radius of the emitter are never hit — the dead zone right under the boss. Keeps the sweep from being an unavoidable point-blank kill when the player is hugging its legs.")]
    public float innerSafeRadius = 1.5f;
    [Tooltip("Height of each beam ABOVE THE GROUND under the emitter, index-matched to the beams (beam 0 gets element 0, and so on). This is the dodge language of the whole attack: ~0.5 = jump it, ~2.2 = crouch under it. Fewer entries than Beam Count means the list repeats. Empty means every beam sits at the pivot's own height.")]
    public float[] beamHeights = { 0.6f, 2.3f };
    [Tooltip("Where the ground under the emitter is sampled from, for the heights above. Falls back to the pivot's own Y if nothing is hit.")]
    public LayerMask groundMask = ~0;

    [Header("Sweep")]
    [Tooltip("Full turns the beams make before shutting off.")]
    public float revolutions = 1.5f;
    [Tooltip("Extra turns per phase past this attack's Min Phase — a phase 4 boss doing a longer version of the same ultimate.")]
    public float extraRevolutionsPerPhase = 0.5f;
    [Tooltip("Degrees/sec at the start of the sweep.")]
    public float startSpinSpeed = 45f;
    [Tooltip("Degrees/sec by the end. Ramping up means the first lap teaches the pattern and the last one tests it.")]
    public float endSpinSpeed = 110f;
    public SpinDirection spinDirection = SpinDirection.Alternate;
    [Tooltip("Start the sweep pointed at the player. Off means it starts from wherever the emitter is facing.")]
    public bool startAimedAtPlayer = true;
    [Tooltip("Degrees of head start given AWAY from the player, so the sweep never opens by instantly clipping them before they can react.")]
    public float startAngleOffset = 30f;

    [Header("Damage")]
    public float damagePerTick = 18f;
    [Tooltip("Minimum seconds between two hits on the player, across all beams. Stops a two-beam sweep double-dipping in the same instant.")]
    public float damageTickInterval = 0.45f;
    [Tooltip("Impulse applied away from the emitter when the beam connects. Needs a Rigidbody on the player.")]
    public float knockback = 6f;
    [Tooltip("Layers that block the beam — walls, pillars, cover. The player's own layer must NOT be in here. Blocks both the visual (the beam stops at the wall) and the damage, so cover genuinely works.")]
    public LayerMask blockMask;

    [Header("Visuals")]
    [Tooltip("Optional prefab with a LineRenderer on it, used for each beam. Without one a plain LineRenderer is built at runtime from Beam Material.")]
    public LineRenderer beamPrefab;
    [Tooltip("Used only when Beam Prefab is empty. Leave unset for an unlit magenta fallback so you can at least see it working.")]
    public Material beamMaterial;
    public Color beamColor = new Color(1f, 0.15f, 0.1f);
    [Tooltip("Optional looping VFX kept at the point where each beam terminates (wall hit or max range).")]
    public GameObject beamImpactVfxPrefab;
    [Tooltip("Optional scorch/ground VFX spawned where a beam crosses the floor. Purely cosmetic.")]
    public GameObject fireStartVfxPrefab;

    [Header("Debug")]
    [Tooltip("Log one line per cast with the beams' resolved world width, ground Y and heights. Turn this on if the beams don't show up — it says exactly which number is wrong.")]
    public bool debugLog;

    [Header("Events")]
    [Tooltip("Fires when the charge begins — hook the audio cue and any camera push-in here.")]
    public UnityEvent onChargeStart;
    [Tooltip("Fires the instant the beams switch on and start turning — hook the boom and the camera shake here.")]
    public UnityEvent onSweepStart;
    public UnityEvent onSweepEnd;

    #endregion

    /// <summary>True while the beams are live and turning (not during the charge).</summary>
    public bool IsSweeping { get; private set; }

    // Rooted for the whole cast on purpose. The attack is read off the emitter's
    // position — a boss that wanders mid-sweep makes the safe angle unlearnable.
    public override bool AllowsMovementDuringExecution => false;

    private readonly List<LineRenderer> _beams = new List<LineRenderer>();
    private readonly List<GameObject> _impactVfx = new List<GameObject>();
    private readonly List<GameObject> _drones = new List<GameObject>();
    private Vector3 _droneLaunchWorld;
    private readonly RaycastHit[] _groundHits = new RaycastHit[8];
    private Transform _pivot;
    // Beams live under here rather than under the pivot. A LineRenderer's WIDTH is
    // multiplied by its transform's lossy scale even when useWorldSpace is on, and
    // the pivot is usually a rig bone with a wildly non-uniform scale — parenting to
    // it silently shrank every beam to a hairline. This holder is kept at world
    // scale 1 so beamThickness means metres.
    private Transform _beamRoot;
    private Material _runtimeBeamMaterial;
    private int _chargeTriggerHash;
    private int _firingBoolHash;
    private float _lastSpinSign = 1f;
    private float _nextDamageTime;
    private Collider _playerCollider;
    private CharacterController _playerController;
    private GameObject _chargeVfx;

    private void ClearChargeVfx()
    {
        if (_chargeVfx == null) return;
        Destroy(_chargeVfx);
        _chargeVfx = null;
    }

    private void Reset()
    {
        // An ultimate: late-fight only, expensive, and it needs room to be fair.
        minPhase = 3;
        minRange = 4f;
        maxRange = 35f;
        cooldown = 25f;
        weight = 3f;
        tokenCost = 25;
    }

    protected override void Awake()
    {
        base.Awake();
        if (!string.IsNullOrEmpty(chargeTrigger)) _chargeTriggerHash = Animator.StringToHash(chargeTrigger);
        if (!string.IsNullOrEmpty(firingBool)) _firingBoolHash = Animator.StringToHash(firingBool);
        EnsurePivot();

        // The single easiest thing to get wrong here: wire up a drone prefab, leave
        // Emitter Mode on Self, and wonder why the beams still come out of the mech.
        if (dronePrefab != null && emitterMode != EmitterMode.Drones)
            Debug.LogWarning($"[{nameof(MechLaserSweepUltimate)}] {name} has a Drone Prefab assigned but Emitter Mode is " +
                             $"{emitterMode} — the beams will fire from the mech and no drones will launch. " +
                             "Set Emitter Mode to Drones.", this);
    }

    private void OnDisable()
    {
        // Backstop for the component being switched off outright, which no abort
        // path runs through. Normal interruption goes via OnAborted/ShutDown.
        ClearChargeVfx();
        TeardownBeams();
        RecallDrones(false);
        IsSweeping = false;
    }

    private void OnDestroy()
    {
        // Built with `new Material(...)`, so nothing else will collect it.
        if (_runtimeBeamMaterial != null) Destroy(_runtimeBeamMaterial);
    }

    #region Execution

    protected override IEnumerator Run()
    {
        EnsurePivot();

        float groundY = SampleGroundY();
        float[] heights = ResolveBeamHeights(groundY);
        float spinSign = ResolveSpinSign();
        float startAngle = ResolveStartAngle(spinSign);
        float totalDegrees = ResolveTotalDegrees();

        BuildBeams();

        // Charge. Beams are drawn at a sliver of their real width and held at the
        // start angle, so the tell shows both where the sweep opens and how high
        // each beam sits — everything the player needs to plan the dodge.
        if (animator != null && _chargeTriggerHash != 0) animator.SetTrigger(_chargeTriggerHash);
        RaiseTelegraphStart(chargeTime);
        onChargeStart?.Invoke();

        // Held in a field, not a local: an abort during the charge kills this
        // coroutine outright, and a parented VFX would otherwise sit on the pivot
        // spinning up forever.
        _chargeVfx = chargeVfxPrefab != null
            ? Instantiate(chargeVfxPrefab, _pivot.position, _pivot.rotation, _pivot)
            : null;

        // The flight launches with the charge and is on station by the time the
        // beams go live — the drones flying out IS the tell in that mode.
        DeployDrones(heights);
        float deployTime = Mathf.Clamp(droneDeployTime, 0.01f, Mathf.Max(0.01f, chargeTime));
        LogSetup(groundY, heights, startAngle);

        SetBeamsVisible(showChargeBeams);
        float charged = 0f;
        while (charged < chargeTime)
        {
            UpdateDrones(startAngle, heights, Mathf.Clamp01(charged / deployTime));
            if (showChargeBeams) DrawBeams(startAngle, heights, chargeBeamWidthScale);
            charged += Time.deltaTime;
            yield return null;
        }

        UpdateDrones(startAngle, heights, 1f);
        ClearChargeVfx();

        // Fire.
        RaiseTelegraphResolved();
        IsSweeping = true;
        SetBeamsVisible(true);
        if (animator != null && _firingBoolHash != 0) animator.SetBool(_firingBoolHash, true);
        if (fireStartVfxPrefab != null)
            Destroy(Instantiate(fireStartVfxPrefab, _pivot.position, Quaternion.identity), 4f);
        onSweepStart?.Invoke();

        float swept = 0f;
        float angle = startAngle;
        while (swept < totalDegrees)
        {
            float t = totalDegrees > 0f ? swept / totalDegrees : 1f;
            float speed = Mathf.Lerp(startSpinSpeed, endSpinSpeed, t);
            float step = speed * Time.deltaTime;
            if (swept + step > totalDegrees) step = totalDegrees - swept;

            float prevAngle = angle;
            angle += step * spinSign;
            swept += step;

            UpdateDrones(angle, heights, 1f);
            DrawBeams(angle, heights, 1f);
            TestSweepHit(prevAngle, angle, heights);

            yield return null;
        }

        ShutDown();

        // Short tail so the boss doesn't snap straight into its next move the
        // frame the beams cut out.
        yield return new WaitForSeconds(0.5f);
    }

    // A stagger or death mid-sweep leaves the coroutine dead wherever it stood —
    // without this the beams stay lit and turning over a boss that isn't there.
    protected override void OnAborted() => ShutDown();

    private void ShutDown()
    {
        ClearChargeVfx();
        if (animator != null && _firingBoolHash != 0) animator.SetBool(_firingBoolHash, false);

        bool wasLive = IsSweeping || _beams.Count > 0;
        IsSweeping = false;
        TeardownBeams();
        RecallDrones(wasLive);

        if (wasLive) onSweepEnd?.Invoke();
    }

    private float ResolveTotalDegrees()
    {
        int phasesPast = Mathf.Max(0, brain != null ? brain.Phase - minPhase : 0);
        float turns = Mathf.Max(0.1f, revolutions + extraRevolutionsPerPhase * phasesPast);
        return turns * 360f;
    }

    private float ResolveSpinSign()
    {
        switch (spinDirection)
        {
            case SpinDirection.Clockwise: return 1f;
            case SpinDirection.CounterClockwise: return -1f;
            case SpinDirection.Alternate: _lastSpinSign = -_lastSpinSign; return _lastSpinSign;
            default: return UnityEngine.Random.value < 0.5f ? -1f : 1f;
        }
    }

    /// <summary>Opening angle of beam 0, in world Y degrees. Offset backwards along
    /// the spin so the player is swept *towards*, never spawned on top of.</summary>
    private float ResolveStartAngle(float spinSign)
    {
        // The mech's yaw, not the pivot's. The pivot is often a rig bone (a muzzle,
        // a gun gimbal) whose own rotation is baked at some arbitrary angle, which
        // made the opening direction unrelated to where the boss is facing.
        float baseAngle = transform.eulerAngles.y;

        if (startAimedAtPlayer && PlayerHealth.Transform != null)
        {
            Vector3 toPlayer = PlayerHealth.Transform.position - _pivot.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f)
                baseAngle = Quaternion.LookRotation(toPlayer, Vector3.up).eulerAngles.y;
        }

        return baseAngle - startAngleOffset * spinSign;
    }

    #endregion

    #region Hit Detection

    /// <summary>Did any beam's arc pass over the player between the previous frame's
    /// angle and this one's? Arc-based rather than a raycast, so a fast sweep can't
    /// step straight over the player between frames.</summary>
    private void TestSweepHit(float prevAngle, float curAngle, float[] heights)
    {
        if (Time.time < _nextDamageTime) return;
        if (PlayerHealth.Instance == null || PlayerHealth.Transform == null) return;

        Vector3 pivotPos = _pivot.position;
        Vector3 flat = PlayerHealth.Transform.position - pivotPos;
        flat.y = 0f;

        float dist = flat.magnitude;
        if (dist < EffectiveInnerRadius || dist > EffectiveOuterRadius) return;

        float playerAngle = Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y;

        // Angular half-width of the beam at the player's distance — a beam is a
        // fixed width in metres, so it covers fewer degrees the further out you are.
        float halfWidth = Mathf.Atan2(beamThickness * 0.5f, dist) * Mathf.Rad2Deg;
        float sweep = curAngle - prevAngle;

        GetPlayerVerticalExtent(out float feetY, out float headY);

        for (int i = 0; i < _beams.Count; i++)
        {
            if (BeamEmitterLost(i)) continue; // that drone is down — no beam to be hit by

            float beamPrev = prevAngle + BeamAngleOffset(i);
            if (!ArcPassedOver(beamPrev, sweep, playerAngle, halfWidth)) continue;

            // Vertical test — this is the jump/crouch dodge. The beam misses
            // entirely if the player's capsule is wholly above or below it.
            float beamY = heights[i];
            if (beamY + beamThickness * 0.5f < feetY) continue;
            if (beamY - beamThickness * 0.5f > headY) continue;

            // Cover. Sighted from the beam's own origin — the drone's position in
            // drone mode — so ducking behind a low wall blocks the low beam without
            // also blocking the high one.
            Vector3 origin = BeamOrigin(i, curAngle, heights);
            Vector3 target = new Vector3(PlayerHealth.Transform.position.x,
                                         Mathf.Clamp(beamY, feetY, headY),
                                         PlayerHealth.Transform.position.z);
            if (IsBlocked(origin, target)) continue;

            ApplyHit(pivotPos);
            return;
        }
    }

    /// <summary>True if the player's angle lies inside the arc travelled from
    /// <paramref name="fromAngle"/> through <paramref name="sweep"/> degrees,
    /// widened by the beam's own half-width at that distance.</summary>
    private static bool ArcPassedOver(float fromAngle, float sweep, float playerAngle, float halfWidth)
    {
        // A single frame covering the whole circle can only mean a pathological
        // spin speed or a hitch — treat it as a hit rather than silently missing.
        if (Mathf.Abs(sweep) + halfWidth * 2f >= 360f) return true;

        // Player's position relative to where the arc started, in [-180, 180].
        float rel = Mathf.DeltaAngle(fromAngle, playerAngle);

        return sweep >= 0f
            ? rel >= -halfWidth && rel <= sweep + halfWidth
            : rel <= halfWidth && rel >= sweep - halfWidth;
    }

    private void ApplyHit(Vector3 pivotPos)
    {
        _nextDamageTime = Time.time + damageTickInterval;
        PlayerHealth.Instance.TakeDamage(damagePerTick);

        if (knockback <= 0f) return;

        var rb = PlayerHealth.Transform.GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        Vector3 outward = PlayerHealth.Transform.position - pivotPos;
        outward.y = 0f;
        outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
        rb.AddForce(outward * knockback + Vector3.up * (knockback * 0.25f), ForceMode.Impulse);
    }

    private bool IsBlocked(Vector3 from, Vector3 to)
    {
        if (blockMask == 0) return false;
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.01f) return false;
        return Physics.Raycast(from, dir / dist, dist, blockMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>World-space Y range the player's body occupies, so a jump actually
    /// lifts them over a low beam. Falls back to a 1.8m stand-in if the player has
    /// no collider we can measure.</summary>
    private void GetPlayerVerticalExtent(out float feetY, out float headY)
    {
        Transform t = PlayerHealth.Transform;

        if (_playerController == null && _playerCollider == null && t != null)
        {
            _playerController = t.GetComponentInParent<CharacterController>();
            if (_playerController == null) _playerCollider = t.GetComponentInParent<Collider>();
        }

        // CharacterController first: crouching resizes its height, which is exactly
        // the signal we want for "ducked under the high beam".
        if (_playerController != null)
        {
            Vector3 centre = _playerController.transform.TransformPoint(_playerController.center);
            float half = Mathf.Max(_playerController.height, _playerController.radius * 2f) * 0.5f;
            feetY = centre.y - half;
            headY = centre.y + half;
            return;
        }

        if (_playerCollider != null)
        {
            Bounds b = _playerCollider.bounds;
            feetY = b.min.y;
            headY = b.max.y;
            return;
        }

        feetY = t != null ? t.position.y : 0f;
        headY = feetY + 1.8f;
    }

    #endregion

    #region Beams

    private void EnsurePivot()
    {
        if (pivotMode == PivotMode.ExplicitTransform && beamPivot != null)
        {
            _pivot = beamPivot;
            return;
        }

        if (_pivot == null || _pivot == beamPivot)
        {
            var go = new GameObject("LaserSweepPivot");
            go.transform.SetParent(transform, false);
            _pivot = go.transform;
        }

        // Re-solved on every cast rather than once at Awake: the mech's measured
        // bounds move with its pose and its phase, and the pivot has to stay on the
        // body's centre line for the sweep to read as coming from the boss itself.
        _pivot.position = ResolveAutoPivotWorld();
        _pivot.rotation = transform.rotation;
        _pivot.localScale = Vector3.one;
    }

    /// <summary>The mech's own centre line at the chosen anchor height. Measured from
    /// the renderers, so it lands correctly whatever the model's scale or rig layout.</summary>
    private Vector3 ResolveAutoPivotWorld()
    {
        if (!TryGetBodyBounds(out Bounds body))
            return transform.position + Vector3.up * pivotHeightOffset;

        float y = Mathf.Lerp(body.min.y, body.max.y, AnchorFraction()) + pivotHeightOffset;
        return new Vector3(body.center.x, y, body.center.z);
    }

    private float AnchorFraction()
    {
        switch (pivotAnchor)
        {
            case PivotAnchor.Ground: return 0f;
            case PivotAnchor.Knees: return 0.25f;
            case PivotAnchor.Hips: return 0.5f;
            case PivotAnchor.Chest: return 0.7f;
            case PivotAnchor.Shoulders: return 0.85f;
            case PivotAnchor.Head: return 1f;
            default: return pivotHeightFraction;
        }
    }

    /// <summary>World bounds of the mech's body. Only mesh/skinned renderers count —
    /// line, trail and particle renderers would drag the bounds out to wherever the
    /// beams and VFX currently reach, which would feed the pivot back into itself.</summary>
    private bool TryGetBodyBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;

        var renderers = GetComponentsInChildren<Renderer>(false);
        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled) continue;
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (_beamRoot != null && r.transform.IsChildOf(_beamRoot)) continue;

            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (any) return true;

        // Nothing renderable (renderers disabled during a phase transition, say) —
        // the collider still describes roughly the right volume.
        var col = GetComponentInChildren<Collider>();
        if (col == null) return false;

        bounds = col.bounds;
        return true;
    }

    /// <summary>Beams in this cast. In drone mode the flight size decides it, so the
    /// two numbers can't disagree.</summary>
    private int ActiveBeamCount =>
        Mathf.Max(1, emitterMode == EmitterMode.Drones ? droneCount : beamCount);

    /// <summary>Angle each beam sits at relative to beam 0 — evenly spread so two
    /// beams are a spinning bar, four are a cross.</summary>
    private float BeamAngleOffset(int index) => 360f / ActiveBeamCount * index;

    /// <summary>Nothing inside this ring can be hit. With drones the beams start out
    /// at the orbit rather than at the mech, so the bubble under the boss grows to
    /// match — which is the whole trade of the drone version.</summary>
    private float EffectiveInnerRadius =>
        emitterMode == EmitterMode.Drones ? Mathf.Max(innerSafeRadius, droneOrbitRadius) : innerSafeRadius;

    /// <summary>Furthest a beam reaches from the mech's centre line.</summary>
    private float EffectiveOuterRadius =>
        emitterMode == EmitterMode.Drones ? droneOrbitRadius + beamLength : beamLength;

    private Vector3 BeamDirection(int index, float baseAngle) =>
        Quaternion.AngleAxis(baseAngle + BeamAngleOffset(index), Vector3.up) * Vector3.forward;

    /// <summary>Where drone <paramref name="index"/> should be sitting for this angle
    /// — on the orbit, out along its own beam, at its beam's height.</summary>
    private Vector3 DroneStation(int index, float baseAngle, float[] heights)
    {
        Vector3 pivotPos = _pivot.position;
        Vector3 dir = BeamDirection(index, baseAngle);
        Vector3 centre = new Vector3(pivotPos.x, heights[index], pivotPos.z);
        return centre + dir * droneOrbitRadius;
    }

    /// <summary>Point the beam is drawn and traced from.
    ///
    /// In drone mode this is the drone's STATION, not the drone's current position.
    /// Following the live position meant that for the whole fly-out the beams poured
    /// out of the middle of the mech — the drones hadn't got there yet — which reads
    /// as the boss firing them itself and makes the telegraph a lie about where the
    /// beams end up. The station is fixed from the first frame of the charge, and the
    /// drones arrive onto beams already drawn where they'll be.</summary>
    private Vector3 BeamOrigin(int index, float baseAngle, float[] heights)
    {
        if (emitterMode != EmitterMode.Drones)
        {
            Vector3 pivotPos = _pivot.position;
            return new Vector3(pivotPos.x, heights[index], pivotPos.z);
        }

        Vector3 station = DroneStation(index, baseAngle, heights);

        // Once it's actually on station, hand the origin to the drone so the beam
        // stays welded to its muzzle even if the model bobs or the prefab animates.
        GameObject drone = index < _drones.Count ? _drones[index] : null;
        if (drone == null) return station;

        Vector3 muzzle = drone.transform.TransformPoint(droneBeamOriginOffset);
        return Vector3.SqrMagnitude(muzzle - station) < droneOnStationTolerance * droneOnStationTolerance
            ? muzzle
            : station;
    }

    /// <summary>True once a drone that was supposed to be carrying this beam is gone —
    /// shot down, or despawned by its own prefab. Its beam goes dark with it.</summary>
    private bool BeamEmitterLost(int index) =>
        emitterMode == EmitterMode.Drones && dronePrefab != null
        && (index >= _drones.Count || _drones[index] == null);

    private void DeployDrones(float[] heights)
    {
        RecallDrones(false);
        if (emitterMode != EmitterMode.Drones || dronePrefab == null) return;

        _droneLaunchWorld = _pivot.position + _pivot.rotation * droneLaunchOffset;

        for (int i = 0; i < ActiveBeamCount; i++)
        {
            GameObject drone = Instantiate(dronePrefab, _droneLaunchWorld, _pivot.rotation);
            SetDroneMotionSuspended(drone, droneMotionOverride);
            _drones.Add(drone);
        }
    }

    /// <summary>A drone prefab reused from a normal enemy brings its own NavMeshAgent
    /// and rigidbody, and both will happily overwrite the position this script writes
    /// each frame — the drone wanders off formation and drags its beam with it. For
    /// the duration of the sweep, this script is the only thing flying them.</summary>
    private static void SetDroneMotionSuspended(GameObject drone, bool suspend)
    {
        if (drone == null) return;

        if (drone.TryGetComponent(out NavMeshAgent agent))
        {
            if (suspend && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            agent.enabled = !suspend;
        }

        if (drone.TryGetComponent(out Rigidbody rb))
        {
            if (suspend)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = suspend;
        }
    }

    /// <summary><paramref name="deployT"/> runs 0..1 across the fly-out, then stays
    /// at 1 for the sweep itself.</summary>
    private void UpdateDrones(float baseAngle, float[] heights, float deployT)
    {
        if (emitterMode != EmitterMode.Drones) return;

        for (int i = 0; i < _drones.Count; i++)
        {
            GameObject drone = _drones[i];
            if (drone == null) continue;

            Vector3 station = DroneStation(i, baseAngle, heights);
            drone.transform.position = deployT >= 1f
                ? station
                : Vector3.Lerp(_droneLaunchWorld, station, Mathf.SmoothStep(0f, 1f, deployT));

            // Nose pointed down its own beam, so the drone reads as the thing firing
            // it rather than a prop that happens to be in the way.
            drone.transform.rotation = Quaternion.LookRotation(BeamDirection(i, baseAngle), Vector3.up);
        }
    }

    private void RecallDrones(bool withVfx)
    {
        foreach (GameObject drone in _drones)
        {
            if (drone == null) continue;

            if (withVfx && droneDespawnVfxPrefab != null)
                Destroy(Instantiate(droneDespawnVfxPrefab, drone.transform.position, drone.transform.rotation), 3f);

            if (droneOutlivesSweep) SetDroneMotionSuspended(drone, false); // hand it back to its own AI
            else Destroy(drone);
        }

        _drones.Clear();
    }

    /// <summary>Floor level under the emitter. The probe starts above the pivot and
    /// looks down, so with a permissive Ground Mask the first thing it hits is the
    /// mech's OWN collider — that put every beam at shoulder height instead of on the
    /// floor. Hits belonging to this boss are skipped.</summary>
    private float SampleGroundY() => GroundYUnder(_pivot.position);

    private float GroundYUnder(Vector3 point)
    {
        Vector3 probe = point + Vector3.up * 2f;
        int count = Physics.RaycastNonAlloc(probe, Vector3.down, _groundHits, 50f, groundMask, QueryTriggerInteraction.Ignore);

        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider c = _groundHits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (_groundHits[i].point.y > best) best = _groundHits[i].point.y;
        }

        // Nothing but ourselves down there — fall back to the boss's own feet, which
        // are on the navmesh, rather than the pivot's height up on the chassis.
        return best > float.NegativeInfinity ? best : transform.position.y;
    }

    /// <summary>Absolute world Y for each beam, from the per-beam heights above the
    /// ground under the emitter. The list repeats if it's shorter than Beam Count.</summary>
    private float[] ResolveBeamHeights(float groundY)
    {
        var result = new float[ActiveBeamCount];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = (beamHeights != null && beamHeights.Length > 0)
                ? groundY + beamHeights[i % beamHeights.Length]
                : _pivot.position.y;
        }
        return result;
    }

    /// <summary>Holder for the beams, pinned to world scale 1. See _beamRoot.</summary>
    private void EnsureBeamRoot()
    {
        if (_beamRoot != null) return;

        var go = new GameObject("LaserSweepBeams");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        Vector3 parentScale = transform.lossyScale;
        go.transform.localScale = new Vector3(
            Mathf.Abs(parentScale.x) > 0.0001f ? 1f / parentScale.x : 1f,
            Mathf.Abs(parentScale.y) > 0.0001f ? 1f / parentScale.y : 1f,
            Mathf.Abs(parentScale.z) > 0.0001f ? 1f / parentScale.z : 1f);

        _beamRoot = go.transform;
    }

    private void BuildBeams()
    {
        TeardownBeams();
        EnsureBeamRoot();

        for (int i = 0; i < ActiveBeamCount; i++)
        {
            LineRenderer lr = beamPrefab != null
                ? Instantiate(beamPrefab, _beamRoot)
                : CreateFallbackBeam();

            // An assigned prefab can carry any parent scale of its own; the width
            // has to come out in metres either way.
            lr.transform.localScale = Vector3.one;

            lr.useWorldSpace = true;
            lr.positionCount = 2;
            _beams.Add(lr);

            _impactVfx.Add(beamImpactVfxPrefab != null
                ? Instantiate(beamImpactVfxPrefab, _pivot.position, Quaternion.identity)
                : null);
        }

        SetBeamsVisible(false);
    }

    private LineRenderer CreateFallbackBeam()
    {
        var go = new GameObject("LaserBeam");
        go.transform.SetParent(_beamRoot, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.material = beamMaterial != null ? beamMaterial : ResolveFallbackMaterial();
        lr.startColor = lr.endColor = beamColor;
        lr.textureMode = LineTextureMode.Tile;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    /// <summary>Built once and shared by every beam. Shader.Find is tried against a
    /// URP name first and a null result is reported rather than fed to the Material
    /// constructor — that used to throw inside Run(), which killed the coroutine and
    /// left the cast with no beams at all and no obvious reason why.</summary>
    private Material ResolveFallbackMaterial()
    {
        if (_runtimeBeamMaterial != null) return _runtimeBeamMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color");

        if (shader == null)
        {
            Debug.LogError($"[{nameof(MechLaserSweepUltimate)}] No fallback shader available on {name} — " +
                           "assign Beam Material (or Beam Prefab) or the sweep has no visuals.", this);
            return null;
        }

        _runtimeBeamMaterial = new Material(shader) { name = "LaserSweepBeam (runtime)" };

        // Vertex colour alone leaves the beam grey under some of these shaders, so
        // set whichever tint property the chosen one actually exposes.
        if (_runtimeBeamMaterial.HasProperty("_BaseColor")) _runtimeBeamMaterial.SetColor("_BaseColor", beamColor);
        if (_runtimeBeamMaterial.HasProperty("_Color")) _runtimeBeamMaterial.SetColor("_Color", beamColor);

        return _runtimeBeamMaterial;
    }

    private void DrawBeams(float baseAngle, float[] heights, float widthScale)
    {
        float width = beamThickness * widthScale;

        for (int i = 0; i < _beams.Count; i++)
        {
            LineRenderer lr = _beams[i];
            if (lr == null) continue;

            // Drone shot down — its beam dies with it, and so does the impact VFX
            // sitting at the far end of it.
            if (BeamEmitterLost(i))
            {
                lr.enabled = false;
                if (_impactVfx[i] != null) _impactVfx[i].SetActive(false);
                continue;
            }

            Vector3 dir = BeamDirection(i, baseAngle);
            Vector3 origin = BeamOrigin(i, baseAngle, heights);

            // The visual stops where the damage does, so a beam clipped by a wall
            // isn't drawn shining through it.
            Vector3 end = origin + dir * beamLength;
            if (blockMask != 0
                && Physics.Raycast(origin, dir, out RaycastHit hit, beamLength, blockMask, QueryTriggerInteraction.Ignore))
                end = hit.point;

            lr.startWidth = lr.endWidth = width;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, end);

            GameObject vfx = _impactVfx[i];
            if (vfx != null) vfx.transform.position = end;
        }
    }

    private void SetBeamsVisible(bool visible)
    {
        foreach (var lr in _beams)
            if (lr != null) lr.enabled = visible;

        foreach (var vfx in _impactVfx)
            if (vfx != null) vfx.SetActive(visible);
    }

    /// <summary>Everything that decides whether a beam is visible, in one line. The
    /// world width is the number that matters most — anything under a centimetre is
    /// a beam that technically exists and can't be seen.</summary>
    private void LogSetup(float groundY, float[] heights, float startAngle)
    {
        if (!debugLog) return;

        Vector3 rootScale = _beamRoot != null ? _beamRoot.lossyScale : Vector3.one;
        string mat = beamMaterial != null ? beamMaterial.name
                   : beamPrefab != null ? "(from Beam Prefab)"
                   : _runtimeBeamMaterial != null ? _runtimeBeamMaterial.shader.name
                   : "NONE — no shader found";

        string emitter = emitterMode == EmitterMode.Drones
            ? $"{_drones.Count} drone(s) @ r{droneOrbitRadius:0.#}" + (dronePrefab == null ? " [NO PREFAB]" : "")
            : "self";

        Debug.Log($"[{nameof(MechLaserSweepUltimate)}] {name}: {emitter}, {_beams.Count} beam(s), material {mat}, " +
                  $"width {beamThickness:0.###}m x rootScale {rootScale.y:0.###} = {beamThickness * rootScale.y:0.###}m world, " +
                  $"pivot {_pivot.position} ({(pivotMode == PivotMode.ExplicitTransform && beamPivot != null ? beamPivot.name : pivotAnchor.ToString())}), " +
                  $"beam0 fires from {BeamOrigin(0, startAngle, heights)}, " +
                  $"groundY {groundY:0.##}, beamY [{string.Join(", ", System.Array.ConvertAll(heights, h => h.ToString("0.##")))}].", this);
    }

    private void TeardownBeams()
    {
        foreach (var lr in _beams)
            if (lr != null) Destroy(lr.gameObject);
        _beams.Clear();

        foreach (var vfx in _impactVfx)
            if (vfx != null) Destroy(vfx);
        _impactVfx.Clear();
    }

    #endregion

    private void OnValidate()
    {
        beamCount = Mathf.Max(1, beamCount);
        beamThickness = Mathf.Max(0.05f, beamThickness);
        innerSafeRadius = Mathf.Max(0f, innerSafeRadius);
        damageTickInterval = Mathf.Max(0.05f, damageTickInterval);
        droneOrbitRadius = Mathf.Max(0.5f, droneOrbitRadius);
        droneDeployTime = Mathf.Max(0.01f, droneDeployTime);
        droneOnStationTolerance = Mathf.Max(0.05f, droneOnStationTolerance);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // Resolved the same way the cast will, so what you're looking at while you
        // pick an anchor is where the beams will actually come from.
        Vector3 origin = pivotMode == PivotMode.ExplicitTransform && beamPivot != null
            ? beamPivot.position
            : ResolveAutoPivotWorld();

        // Same self-hit filter as the runtime path, so the gizmo shows the heights
        // the attack will actually use rather than ones measured off the chassis.
        float groundY = GroundYUnder(origin);

        Vector3 groundOrigin = new Vector3(origin.x, groundY + 0.02f, origin.z);

        // The emitter itself — a plumb line from the floor up to the anchor point,
        // so you can see the pivot is on the mech's centre line and read off how
        // high the chosen anchor put it.
        Gizmos.color = MechGizmos.Laser;
        Gizmos.DrawLine(groundOrigin, origin);
        Gizmos.DrawWireSphere(origin, 0.25f);
        MechGizmos.Label(origin + Vector3.up * 0.4f,
                         pivotMode == PivotMode.ExplicitTransform && beamPivot != null
                             ? $"emitter: {beamPivot.name} (explicit)"
                             : $"emitter: {pivotAnchor} @ {origin.y - groundY:0.##}m",
                         MechGizmos.Laser);

        // Reach, the dead zone under the emitter, and the band the selector will
        // actually pick this from — all flat on the floor the player stands on.
        MechGizmos.GroundRing(groundOrigin, EffectiveOuterRadius, MechGizmos.Laser, "beam reach", 225f);
        MechGizmos.GroundRing(groundOrigin, EffectiveInnerRadius, MechGizmos.Safe, "safe", 240f);
        MechGizmos.GroundBand(groundOrigin, minRange, maxRange, MechGizmos.Laser * 0.55f, "Laser range", 255f);

        bool drones = emitterMode == EmitterMode.Drones;
        if (drones)
            MechGizmos.GroundRing(groundOrigin, droneOrbitRadius, MechGizmos.Gatling,
                                  $"{ActiveBeamCount} drones orbit here", 195f, true);

        // The dodge language of the whole attack: one bar per beam at its real
        // height, labelled with what the player has to do about it. This is the
        // number that's impossible to tune blind, so it gets spelled out.
        for (int i = 0; i < ActiveBeamCount; i++)
        {
            float above = (beamHeights != null && beamHeights.Length > 0)
                ? beamHeights[i % beamHeights.Length]
                : origin.y - groundY;

            Vector3 dir = Quaternion.AngleAxis(transform.eulerAngles.y + BeamAngleOffset(i), Vector3.up) * Vector3.forward;

            // In drone mode the beam doesn't start at the mech — it starts out at
            // the drone on the orbit, so the marker starts there too.
            Vector3 barOrigin = drones ? groundOrigin + dir * droneOrbitRadius : groundOrigin;

            MechGizmos.HeightMarker(barOrigin, groundY + above, dir, beamLength, beamColor,
                                    $"beam {i}: {above:0.##}m — {DodgeHint(above)}");

            if (!drones) continue;

            // The drone itself, and its tether back to the boss, so the formation is
            // legible without pressing play.
            Vector3 station = new Vector3(barOrigin.x, groundY + above, barOrigin.z);
            Gizmos.color = MechGizmos.Gatling;
            Gizmos.DrawWireCube(station, Vector3.one * 0.5f);
            Gizmos.DrawLine(origin, station);
        }
    }

    /// <summary>How a beam at this height is meant to be survived. Naming it in the
    /// gizmo is what turns an arbitrary number into a design decision you can see.</summary>
    private static string DodgeHint(float heightAboveGround)
    {
        if (heightAboveGround <= 1.0f) return "jump it";
        if (heightAboveGround >= 1.9f) return "crouch under";
        return "cover only";
    }
}
