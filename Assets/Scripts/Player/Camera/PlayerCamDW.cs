using UnityEngine;
using DG.Tweening;

/// <summary>
/// DOTween-based camera shaker. Sits on the Camera GameObject.
/// Subscribes to WeaponsController events and applies shake per action.
///
/// SETUP:
///   1. Attach to the Camera GameObject (same one PlayerCam is on).
///   2. Assign weaponSwitcher so it can rebind when weapons change.
///
/// EXTEND:
///   - Call Shake(profile) from anywhere for custom one-off shakes.
///   - Add a ShakeProfile per weapon type for unique feel.
/// </summary>
public class CameraShaker : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public WeaponSwitcherProcedural weaponSwitcher;

    [Header("Reload Shake — Mag Out")]
    public float magOutStrength   = 0.04f;
    public float magOutDuration   = 0.15f;
    public int   magOutVibrato    = 8;

    [Header("Reload Shake — Mag In")]
    public float magInStrength    = 0.05f;
    public float magInDuration    = 0.18f;
    public int   magInVibrato     = 10;

    [Header("Reload Shake — Chamber Round")]
    public float chamberStrength  = 0.06f;
    public float chamberDuration  = 0.20f;
    public int   chamberVibrato   = 12;

    [Header("Fire Shake (light — recoil does the heavy lifting)")]
    public bool  shakeOnFire      = true;
    public float fireStrength     = 0.02f;
    public float fireDuration     = 0.08f;
    public int   fireVibrato      = 6;

    [Header("Landing Shake")]
    public bool  shakeOnLand      = true;
    public float landStrength     = 0.07f;
    public float landDuration     = 0.22f;
    public int   landVibrato      = 8;

    [Header("Global Scale")]
    [Range(0f, 2f)]
    [Tooltip("Master multiplier — tune down if everything feels too much.")]
    public float globalScale      = 1f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private WeaponsController _current;
    private PlayerMovement    _playerMovement;
    private bool              _wasGrounded;
    private Tween             _activeTween;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        _playerMovement = GetComponentInParent<PlayerMovement>();

        if (weaponSwitcher != null)
        {
            weaponSwitcher.OnSwitchStart    += HandleSwitchStart;
            weaponSwitcher.OnSwitchComplete += HandleSwitchComplete;

            // Bind to the first weapon
            Bind(weaponSwitcher.CurrentWeapon);
        }
        else
        {
            FPSDebug.LogWarning("CameraShaker: weaponSwitcher not assigned.", this);
        }
    }

    private void OnDestroy()
    {
        Unbind();
        _activeTween?.Kill();

        if (weaponSwitcher != null)
        {
            weaponSwitcher.OnSwitchStart    -= HandleSwitchStart;
            weaponSwitcher.OnSwitchComplete -= HandleSwitchComplete;
        }
    }

    private void Update()
    {
        if (!shakeOnLand || _playerMovement == null) return;

        bool grounded = _playerMovement.grounded;
        if (!_wasGrounded && grounded)
            Shake(landStrength, landDuration, landVibrato);

        _wasGrounded = grounded;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Bind / Unbind
    // ─────────────────────────────────────────────────────────────────────────

    private void Bind(WeaponsController w)
    {
        if (w == null) return;
        _current = w;
        _current.OnMagOut       += HandleMagOut;
        _current.OnMagIn        += HandleMagIn;
        _current.OnChamberRound += HandleChamberRound;
        _current.OnWeaponFired  += HandleWeaponFired;
        Log($"Bound to {w.name}");
    }

    private void Unbind()
    {
        if (_current == null) return;
        _current.OnMagOut       -= HandleMagOut;
        _current.OnMagIn        -= HandleMagIn;
        _current.OnChamberRound -= HandleChamberRound;
        _current.OnWeaponFired  -= HandleWeaponFired;
        _current = null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleSwitchStart(WeaponsController o, WeaponsController n)
    {
        Unbind();
    }

    private void HandleSwitchComplete(WeaponsController o, WeaponsController n)
    {
        Bind(n);
    }

    private void HandleMagOut()
    {
        Log("Shake: MagOut");
        Shake(magOutStrength, magOutDuration, magOutVibrato);
    }

    private void HandleMagIn()
    {
        Log("Shake: MagIn");
        Shake(magInStrength, magInDuration, magInVibrato);
    }

    private void HandleChamberRound()
    {
        Log("Shake: Chamber");
        Shake(chamberStrength, chamberDuration, chamberVibrato);
    }

    private void HandleWeaponFired(Vector3 hitPoint)
    {
        if (!shakeOnFire) return;
        Shake(fireStrength, fireDuration, fireVibrato);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Shake Core
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fire a shake directly — call from anywhere for custom events.
    /// e.g. explosion nearby, grenade, melee hit.
    /// </summary>
    public void Shake(float strength, float duration, int vibrato = 10)
    {
        // Kill previous shake so they don't stack and drift the camera position
        _activeTween?.Kill(complete: true);

        _activeTween = transform
            .DOShakePosition(
                duration  * 1f,
                strength  * globalScale,
                vibrato,
                randomnessMode: ShakeRandomnessMode.Harmonic)
            .SetUpdate(UpdateType.Normal)
            .SetEase(Ease.OutQuad);
    }

    /// <summary>Shake using a named preset for quick one-liners.</summary>
    public void Shake(ShakePreset preset)
    {
        switch (preset)
        {
            case ShakePreset.Light:    Shake(0.03f, 0.12f, 6);  break;
            case ShakePreset.Medium:   Shake(0.06f, 0.20f, 10); break;
            case ShakePreset.Heavy:    Shake(0.14f, 0.30f, 14); break;
            case ShakePreset.Explosion:Shake(0.25f, 0.45f, 18); break;
        }
    }

    public enum ShakePreset { Light, Medium, Heavy, Explosion }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[CameraShaker] {msg}", this);
    }

    #endregion
}