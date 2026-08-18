using System.Collections;
using UnityEngine;

/// <summary>
/// Added at runtime to every piece a melee cut creates. The piece lies around for
/// <see cref="Lifetime"/> seconds, then plays out its exit — blinking, shrinking, or
/// both — over <see cref="ExitDuration"/> and destroys itself.
/// </summary>
public class SlicedPieceCleanup : MonoBehaviour
{
    /// <summary>Inspector-free config, so callers can hand pieces their own timing.</summary>
    [System.Serializable]
    public struct Settings
    {
        public float Lifetime;
        public float ExitDuration;
        public bool  Blink;
        public float BlinkInterval;
        public bool  ScaleOut;
        [Tooltip("Scale multiplier the piece reaches before it despawns. Above 1 grows, below 1 shrinks.")]
        public float ScaleTo;

        public static Settings Default => new Settings
        {
            Lifetime      = 5f,
            ExitDuration  = 1.5f,
            Blink         = true,
            BlinkInterval = 0.18f,
            ScaleOut      = false,
            ScaleTo       = 1f
        };
    }

    public float Lifetime      = 5f;
    [Tooltip("How long the exit (blink and/or shrink) plays before the piece is destroyed.")]
    public float ExitDuration  = 1.5f;

    [Header("Blink")]
    public bool  Blink = true;
    [Tooltip("Seconds between blink toggles at the START of the exit. It speeds up toward the end.")]
    public float BlinkInterval = 0.18f;
    [Tooltip("Fastest toggle interval, reached just before the piece disappears.")]
    public float BlinkIntervalMin = 0.04f;

    [Header("Scale")]
    public bool  ScaleOut = false;
    [Tooltip("Scale multiplier the piece reaches over its exit. Above 1 grows, below 1 shrinks.")]
    public float ScaleTo = 1.2f;
    [Tooltip("When SHRINKING, colliders switch off once the piece is under this fraction — tiny " +
             "convex mesh colliders are both pointless and a source of PhysX warnings. Ignored " +
             "when the piece scales up.")]
    [Range(0f, 1f)] public float ColliderDisableScale = 0.4f;

    [Header("Speed Limit")]
    [Tooltip("Hard cap on how fast the piece may travel, in m/s, for SpeedClampDuration after " +
             "the cut. 0 = no limit. Catches every source of launch at once — the separation " +
             "impulse, PhysX pushing the piece out of the body it was cut from, a bone " +
             "collider flicking it. Set by MeleeMeshCutter, scaled to the piece's own size.")]
    public float MaxSpeed = 0f;
    public float SpeedClampDuration = 0.75f;

    private Renderer[] _renderers;
    private Collider[] _colliders;
    private Vector3    _baseScale;
    private Rigidbody  _rb;
    private float      _clampTimer;

    /// <summary>
    /// Rough world-space radius of a piece. Everything about how a piece should behave —
    /// how fast it may separate, how hard physics may shove it — is relative to how big it
    /// is on screen, not to metres.
    /// </summary>
    public static float PieceRadius(GameObject piece)
    {
        if (piece == null) return 0f;

        var renderers = piece.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return 0f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        return bounds.extents.magnitude;
    }

    /// <summary>Applied by the cutter after Attach, so a re-Attach of the exit settings
    /// (the riot shield does one) can't wipe the limit.</summary>
    public void SetSpeedLimit(float maxSpeed, float duration)
    {
        MaxSpeed           = maxSpeed;
        SpeedClampDuration = duration;
        _clampTimer        = duration;
    }

    /// <summary>Attaches (or reconfigures) the cleanup on a piece. Safe to call again on a
    /// piece that already has one — the last caller's settings win.</summary>
    public static SlicedPieceCleanup Attach(GameObject piece, Settings settings)
    {
        if (piece == null) return null;

        var cleanup = piece.GetComponent<SlicedPieceCleanup>();
        if (cleanup == null) cleanup = piece.AddComponent<SlicedPieceCleanup>();

        cleanup.Lifetime      = settings.Lifetime;
        cleanup.ExitDuration  = settings.ExitDuration;
        cleanup.Blink         = settings.Blink;
        cleanup.BlinkInterval = settings.BlinkInterval;
        cleanup.ScaleOut      = settings.ScaleOut;
        cleanup.ScaleTo       = settings.ScaleTo;
        return cleanup;
    }

    private void Start()
    {
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        _colliders = GetComponentsInChildren<Collider>(includeInactive: true);
        TryGetComponent(out _rb);
        StartCoroutine(Co_Expire());
    }

    private void FixedUpdate()
    {
        if (_rb == null || MaxSpeed <= 0f || _clampTimer <= 0f) return;

        _clampTimer -= Time.fixedDeltaTime;

        Vector3 v = _rb.linearVelocity;
        if (v.sqrMagnitude > MaxSpeed * MaxSpeed)
            _rb.linearVelocity = v.normalized * MaxSpeed;

        // Spin reads as "flying" too when a piece is small enough to whirl on the spot.
        float maxSpin = MaxSpeed * 4f;
        Vector3 w = _rb.angularVelocity;
        if (w.sqrMagnitude > maxSpin * maxSpin)
            _rb.angularVelocity = w.normalized * maxSpin;
    }

    private IEnumerator Co_Expire()
    {
        if (Lifetime > 0f) yield return new WaitForSeconds(Lifetime);

        // Captured HERE, not in Start — anything that adjusts the piece's scale after
        // creation (the cutter's source-scale correction, an ISliceable, a future effect)
        // must not get undone the moment the exit begins. Whatever size the piece is when
        // it starts to leave is the size it scales FROM.
        _baseScale = transform.localScale;

        float t = 0f;
        bool  visible = true;
        bool  collidersLive = true;

        while (t < ExitDuration)
        {
            float p = ExitDuration > 0f ? Mathf.Clamp01(t / ExitDuration) : 1f;

            if (ScaleOut)
            {
                float scale = Mathf.Lerp(1f, ScaleTo, p);
                transform.localScale = _baseScale * scale;

                // Only relevant on the way down — a piece that grows keeps a usable collider.
                if (collidersLive && ScaleTo < 1f && scale <= ColliderDisableScale)
                {
                    SetCollidersEnabled(false);
                    collidersLive = false;
                }
            }

            if (Blink)
            {
                visible = !visible;
                SetVisible(visible);

                // Ramp the toggle rate up as the piece runs out of time, so the blink reads
                // as "this is going away" instead of a steady flicker.
                float interval = Mathf.Max(0.01f, Mathf.Lerp(BlinkInterval, BlinkIntervalMin, p));
                yield return new WaitForSeconds(interval);
                t += interval;
            }
            else
            {
                yield return null;
                t += Time.deltaTime;
            }
        }

        Destroy(gameObject);
    }

    private void SetVisible(bool visible)
    {
        if (_renderers == null) return;
        foreach (var r in _renderers)
        {
            if (r != null) r.enabled = visible;
        }
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (_colliders == null) return;
        foreach (var c in _colliders)
        {
            if (c != null) c.enabled = enabled;
        }
    }
}
