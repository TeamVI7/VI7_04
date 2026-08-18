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
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
    [Tooltip("What the beams pivot around. Leave empty and one is created as a child of the boss at Auto Pivot Height.")]
    public Transform beamPivot;
    [Tooltip("Local height of the auto-created pivot. Ignored when Beam Pivot is assigned.")]
    public float autoPivotHeight = 3f;

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
    private Transform _pivot;
    private int _chargeTriggerHash;
    private int _firingBoolHash;
    private float _lastSpinSign = 1f;
    private float _nextDamageTime;
    private Collider _playerCollider;
    private CharacterController _playerController;

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
    }

    private void OnDisable()
    {
        // Death mid-sweep kills the coroutine wherever it happens to be. Without
        // this the beams stay on screen, still lethal-looking, over a dead boss.
        TeardownBeams();
        IsSweeping = false;
    }

    #region Execution

    public override void Execute(Action onComplete)
    {
        if (IsExecuting)
        {
            onComplete?.Invoke();
            return;
        }
        StartCoroutine(Co_Execute(onComplete));
    }

    private IEnumerator Co_Execute(Action onComplete)
    {
        IsExecuting = true;
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

        GameObject chargeVfx = chargeVfxPrefab != null
            ? Instantiate(chargeVfxPrefab, _pivot.position, _pivot.rotation, _pivot)
            : null;

        SetBeamsVisible(showChargeBeams);
        float charged = 0f;
        while (charged < chargeTime)
        {
            if (showChargeBeams) DrawBeams(startAngle, heights, chargeBeamWidthScale);
            charged += Time.deltaTime;
            yield return null;
        }

        if (chargeVfx != null) Destroy(chargeVfx);

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

            DrawBeams(angle, heights, 1f);
            TestSweepHit(prevAngle, angle, heights);

            yield return null;
        }

        if (animator != null && _firingBoolHash != 0) animator.SetBool(_firingBoolHash, false);
        IsSweeping = false;
        TeardownBeams();
        onSweepEnd?.Invoke();

        // Short tail so the boss doesn't snap straight into its next move the
        // frame the beams cut out.
        yield return new WaitForSeconds(0.5f);

        IsExecuting = false;
        onComplete?.Invoke();
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
        float baseAngle = _pivot.eulerAngles.y;

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
        if (dist < innerSafeRadius || dist > beamLength) return;

        float playerAngle = Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y;

        // Angular half-width of the beam at the player's distance — a beam is a
        // fixed width in metres, so it covers fewer degrees the further out you are.
        float halfWidth = Mathf.Atan2(beamThickness * 0.5f, dist) * Mathf.Rad2Deg;
        float sweep = curAngle - prevAngle;

        GetPlayerVerticalExtent(out float feetY, out float headY);

        for (int i = 0; i < _beams.Count; i++)
        {
            float beamPrev = prevAngle + BeamAngleOffset(i);
            if (!ArcPassedOver(beamPrev, sweep, playerAngle, halfWidth)) continue;

            // Vertical test — this is the jump/crouch dodge. The beam misses
            // entirely if the player's capsule is wholly above or below it.
            float beamY = heights[i];
            if (beamY + beamThickness * 0.5f < feetY) continue;
            if (beamY - beamThickness * 0.5f > headY) continue;

            // Cover. Sighted from the beam's own height so ducking behind a low
            // wall blocks the low beam without also blocking the high one.
            Vector3 origin = new Vector3(pivotPos.x, beamY, pivotPos.z);
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
        if (beamPivot != null) { _pivot = beamPivot; return; }
        if (_pivot != null) return;

        var go = new GameObject("LaserSweepPivot");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, autoPivotHeight, 0f);
        _pivot = go.transform;
    }

    /// <summary>Angle each beam sits at relative to beam 0 — evenly spread so two
    /// beams are a spinning bar, four are a cross.</summary>
    private float BeamAngleOffset(int index) => 360f / Mathf.Max(1, beamCount) * index;

    private float SampleGroundY()
    {
        Vector3 probe = _pivot.position + Vector3.up * 2f;
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 50f, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return _pivot.position.y;
    }

    /// <summary>Absolute world Y for each beam, from the per-beam heights above the
    /// ground under the emitter. The list repeats if it's shorter than Beam Count.</summary>
    private float[] ResolveBeamHeights(float groundY)
    {
        var result = new float[Mathf.Max(1, beamCount)];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = (beamHeights != null && beamHeights.Length > 0)
                ? groundY + beamHeights[i % beamHeights.Length]
                : _pivot.position.y;
        }
        return result;
    }

    private void BuildBeams()
    {
        TeardownBeams();

        for (int i = 0; i < Mathf.Max(1, beamCount); i++)
        {
            LineRenderer lr = beamPrefab != null
                ? Instantiate(beamPrefab, _pivot)
                : CreateFallbackBeam();

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
        go.transform.SetParent(_pivot, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.material = beamMaterial != null ? beamMaterial : new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = beamColor;
        lr.textureMode = LineTextureMode.Tile;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    private void DrawBeams(float baseAngle, float[] heights, float widthScale)
    {
        Vector3 pivotPos = _pivot.position;
        float width = beamThickness * widthScale;

        for (int i = 0; i < _beams.Count; i++)
        {
            LineRenderer lr = _beams[i];
            if (lr == null) continue;

            Vector3 dir = Quaternion.AngleAxis(baseAngle + BeamAngleOffset(i), Vector3.up) * Vector3.forward;
            Vector3 origin = new Vector3(pivotPos.x, heights[i], pivotPos.z);

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
    }

    private void OnDrawGizmosSelected()
    {
        Transform pivot = beamPivot != null ? beamPivot : _pivot;
        Vector3 origin = pivot != null ? pivot.position : transform.position + Vector3.up * autoPivotHeight;

        // Reach and the dead zone under the emitter.
        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(origin, beamLength);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(origin, innerSafeRadius);

        // One line per beam at its authored height, so the jump/crouch heights are
        // visible in the scene view without entering play mode.
        float groundY = origin.y;
        if (Physics.Raycast(origin + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 50f, groundMask, QueryTriggerInteraction.Ignore))
            groundY = hit.point.y;

        Gizmos.color = beamColor;
        for (int i = 0; i < Mathf.Max(1, beamCount); i++)
        {
            float y = (beamHeights != null && beamHeights.Length > 0)
                ? groundY + beamHeights[i % beamHeights.Length]
                : origin.y;

            Vector3 from = new Vector3(origin.x, y, origin.z);
            Vector3 dir = Quaternion.AngleAxis(transform.eulerAngles.y + BeamAngleOffset(i), Vector3.up) * Vector3.forward;
            Gizmos.DrawLine(from, from + dir * beamLength);
        }
    }
}
