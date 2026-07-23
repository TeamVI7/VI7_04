using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Platform hangs from a crane point and swings like a pendulum in the wind.
/// Drives itself via pendulum math (not physics joints) for predictable parkour timing.
/// Carries anything standing on it by applying its per-frame delta to registered riders.
/// </summary>
public class CraneSwingPlatform : MonoBehaviour
{
    [Header("Cable")]
    [Tooltip("World-space point the platform hangs from. If null, uses this object's start position + cableLength up.")]
    [SerializeField] private Transform pivot;
    [SerializeField] private float cableLength = 6f;

    [Header("Pendulum")]
    [Tooltip("Starting swing angle in degrees, off vertical.")]
    [SerializeField] private float startAngleDeg = 15f;
    [SerializeField] private float gravity = 9.81f;
    [Tooltip("0 = never stops swinging, higher = settles faster.")]
    [SerializeField] private float damping = 0.08f;

    [Header("Wind")]
    [SerializeField] private bool windEnabled = true;
    [SerializeField] private float windStrength = 1.2f;
    [SerializeField] private float windFrequency = 0.35f;
    [Tooltip("Swing axis wind pushes along, in local space of the pivot.")]
    [SerializeField] private Vector3 windAxis = Vector3.forward;
    [SerializeField] private float noiseSeedOffset = 0f;

    [Header("Tilt (visual sell)")]
    [SerializeField] private bool tiltWithSwing = true;
    [SerializeField] private float maxTiltDeg = 8f;

    [Header("Rider carrying")]
    [Tooltip("Layers allowed to ride the platform via trigger detection.")]
    [SerializeField] private LayerMask riderMask = ~0;
    [SerializeField] private Transform riderDetector; // optional separate trigger collider above the deck

    private Vector3 _pivotWorld;
    private Vector3 _swingAxisWorld;
    private float _angle;      // current angle, radians
    private float _angularVel; // radians/sec
    private float _noiseTime;

    private Vector3 _prevPos;
    private Quaternion _prevRot;

    private readonly HashSet<Transform> _riders = new HashSet<Transform>();
    private readonly Dictionary<Transform, CharacterController> _riderCC = new Dictionary<Transform, CharacterController>();

    private void Awake()
    {
        _pivotWorld = pivot != null ? pivot.position : transform.position + Vector3.up * cableLength;
        _swingAxisWorld = transform.TransformDirection(windAxis.normalized);
        _angle = startAngleDeg * Mathf.Deg2Rad;
        _angularVel = 0f;
        _noiseTime = noiseSeedOffset;

        SnapToAngle(_angle);
        _prevPos = transform.position;
        _prevRot = transform.rotation;
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Pendulum equation: angular accel = -(g / L) * sin(angle) - damping term
        float angularAccel = -(gravity / Mathf.Max(0.01f, cableLength)) * Mathf.Sin(_angle);
        angularAccel -= damping * _angularVel;

        if (windEnabled)
        {
            _noiseTime += dt * windFrequency;
            // Perlin in [0,1] -> [-1,1], gives a wandering push instead of pure sine repetition
            float wind = (Mathf.PerlinNoise(_noiseTime, 0.5f) * 2f - 1f) * windStrength;
            angularAccel += wind;
        }

        _angularVel += angularAccel * dt;
        _angle += _angularVel * dt;

        SnapToAngle(_angle);
        CarryRiders();

        _prevPos = transform.position;
        _prevRot = transform.rotation;
    }

    private void SnapToAngle(float angleRad)
    {
        // Position: swing around pivot on the plane defined by swingAxis and down-vector
        Vector3 down = Vector3.down;
        Vector3 offset = (Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, _swingAxisWorld) * (down * cableLength));
        transform.position = _pivotWorld + offset;

        if (tiltWithSwing)
        {
            float tilt = Mathf.Clamp(angleRad * Mathf.Rad2Deg, -maxTiltDeg, maxTiltDeg);
            transform.rotation = Quaternion.AngleAxis(tilt, _swingAxisWorld);
        }
    }

    private void CarryRiders()
    {
        if (_riders.Count == 0) return;

        Vector3 deltaPos = transform.position - _prevPos;
        Quaternion deltaRot = transform.rotation * Quaternion.Inverse(_prevRot);

        foreach (var rider in _riders)
        {
            if (rider == null) continue;

            if (_riderCC.TryGetValue(rider, out var cc) && cc != null)
            {
                // CharacterController: move it via .Move, don't touch transform directly.
                // Net displacement = platform's translation + what rotation does to the
                // rider's offset from the platform's previous position.
                Vector3 toRider = rider.position - _prevPos;
                Vector3 rotationDelta = (deltaRot * toRider) - toRider;
                cc.Move(deltaPos + rotationDelta);
            }
            else
            {
                // Plain transform rider
                Vector3 toRider = rider.position - _prevPos;
                Vector3 rotationDelta = (deltaRot * toRider) - toRider;
                rider.position += deltaPos + rotationDelta;
                rider.rotation = deltaRot * rider.rotation;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & riderMask) == 0) return;
        Transform root = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
        _riders.Add(root);
        _riderCC[root] = root.GetComponent<CharacterController>();
    }

    private void OnTriggerExit(Collider other)
    {
        Transform root = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
        _riders.Remove(root);
        _riderCC.Remove(root);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 p = pivot != null ? pivot.position : transform.position + Vector3.up * cableLength;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(p, transform.position);
        Gizmos.DrawWireSphere(p, 0.15f);
    }
}