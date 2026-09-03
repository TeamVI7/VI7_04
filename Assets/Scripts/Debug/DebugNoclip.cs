using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Free-fly debug camera. Toggle with noclipKey (F10).
///
/// While active:
///   - PlayerMovement is disabled, the Rigidbody goes kinematic and colliders are
///     switched off, so nothing pushes back and nothing gets hit.
///   - PlayerCam stays enabled, so mouse look feels identical to normal play.
///   - The player root is flown directly; camHolder is a child of it, so the camera
///     comes along for the ride.
///   - Renderers under the player (body + viewmodel) and every root Canvas are hidden.
///
/// Attach to the PLAYER ROOT (the object with PlayerMovement / the Rigidbody).
///
/// Controls: WASD move, Space up, Ctrl down, Shift fast, Alt slow, scroll = speed.
/// </summary>
public class DebugNoclip : MonoBehaviour
{
    [Header("Toggle")]
    public KeyCode noclipKey = KeyCode.F10;

    [Header("Speed")]
    [Tooltip("Units/sec at normal speed. Scroll wheel scales this while flying.")]
    public float moveSpeed      = 12f;
    public float fastMultiplier = 4f;
    public float slowMultiplier = 0.25f;
    public float minSpeed       = 1f;
    public float maxSpeed       = 200f;
    [Tooltip("How quickly the fly velocity reaches the input direction. 0 = instant.")]
    public float acceleration   = 12f;

    [Header("Keys")]
    public KeyCode upKey   = KeyCode.Space;
    public KeyCode downKey = KeyCode.LeftControl;
    public KeyCode fastKey = KeyCode.LeftShift;
    public KeyCode slowKey = KeyCode.LeftAlt;

    [Header("Hiding")]
    [Tooltip("Hide every Renderer under this transform (body mesh + weapon viewmodel).")]
    public bool hidePlayer = true;
    [Tooltip("Disable every root Canvas in the scene (HUD, crosshair, minimap).")]
    public bool hideUI = true;
    [Tooltip("Canvases listed here stay visible while noclipping.")]
    public List<Canvas> uiExceptions = new List<Canvas>();

    [Header("Refs (auto-filled)")]
    public Transform     flyCamera;   // used for direction only
    public PlayerMovement movement;
    public PlayerCam      playerCam;
    public Rigidbody      body;

    public bool IsActive { get; private set; }

    // ── cached state so exiting restores exactly what was there ──────────────
    private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
    private readonly List<Canvas>   _hiddenCanvases  = new List<Canvas>();
    private readonly List<Collider> _disabledColliders = new List<Collider>();
    private bool    _prevKinematic;
    private bool    _prevUseGravity;
    private bool    _prevMovementEnabled;
    private Vector3 _velocity;

    void Reset()  => AutoFill();
    void Awake()  => AutoFill();

    void AutoFill()
    {
        if (movement  == null) movement  = GetComponent<PlayerMovement>();
        if (body      == null) body      = GetComponent<Rigidbody>();
        if (playerCam == null) playerCam = GetComponentInChildren<PlayerCam>(true);
        if (flyCamera == null)
            flyCamera = playerCam != null ? playerCam.transform
                      : (Camera.main != null ? Camera.main.transform : transform);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void Update()
    {
        if (Input.GetKeyDown(noclipKey))
        {
            if (IsActive) Disable();
            else          Enable();
        }

        if (IsActive) Fly();
    }

    void OnDisable()
    {
        // Never leave the player invisible/kinematic if this component dies mid-flight.
        if (IsActive) Disable();
    }
#endif

    void Fly()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
            moveSpeed = Mathf.Clamp(moveSpeed * (1f + scroll * 0.15f), minSpeed, maxSpeed);

        Vector3 dir = flyCamera.forward * Input.GetAxisRaw("Vertical")
                    + flyCamera.right   * Input.GetAxisRaw("Horizontal");
        if (Input.GetKey(upKey))   dir += Vector3.up;
        if (Input.GetKey(downKey)) dir -= Vector3.up;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        float speed = moveSpeed;
        if (Input.GetKey(fastKey)) speed *= fastMultiplier;
        if (Input.GetKey(slowKey)) speed *= slowMultiplier;

        Vector3 target = dir * speed;
        _velocity = acceleration <= 0f
            ? target
            : Vector3.Lerp(_velocity, target, 1f - Mathf.Exp(-acceleration * Time.unscaledDeltaTime));

        transform.position += _velocity * Time.unscaledDeltaTime;
    }

    public void Enable()
    {
        if (IsActive) return;
        IsActive  = true;
        _velocity = Vector3.zero;

        if (movement != null)
        {
            _prevMovementEnabled = movement.enabled;
            movement.enabled = false;
        }

        if (body != null)
        {
            _prevKinematic  = body.isKinematic;
            _prevUseGravity = body.useGravity;
            body.linearVelocity  = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity  = false;
            body.isKinematic = true;
        }

        // Colliders off so we pass through geometry and stop triggering world volumes.
        _disabledColliders.Clear();
        foreach (var col in GetComponentsInChildren<Collider>(true))
        {
            if (!col.enabled) continue;
            col.enabled = false;
            _disabledColliders.Add(col);
        }

        if (hidePlayer)
        {
            _hiddenRenderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                _hiddenRenderers.Add(r);
            }
        }

        if (hideUI)
        {
            _hiddenCanvases.Clear();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                // Only touch root canvases — nested ones follow their parent.
                if (!c.isRootCanvas || !c.enabled || uiExceptions.Contains(c)) continue;
                c.enabled = false;
                _hiddenCanvases.Add(c);
            }
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        Debug.Log("[Noclip] ON");
    }

    public void Disable()
    {
        if (!IsActive) return;
        IsActive  = false;
        _velocity = Vector3.zero;

        foreach (var c in _hiddenCanvases)  if (c != null) c.enabled = true;
        foreach (var r in _hiddenRenderers) if (r != null) r.enabled = true;
        foreach (var col in _disabledColliders) if (col != null) col.enabled = true;
        _hiddenCanvases.Clear();
        _hiddenRenderers.Clear();
        _disabledColliders.Clear();

        if (body != null)
        {
            body.isKinematic = _prevKinematic;
            body.useGravity  = _prevUseGravity;
            body.linearVelocity  = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = transform.position;
        }

        if (movement != null) movement.enabled = _prevMovementEnabled;

        Debug.Log("[Noclip] OFF");
    }
}
