using UnityEngine;

public class CameraRecoil : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public WeaponsController weaponController;
    public PlayerCam playerCam;
    [Tooltip("Optional — used only to scale kick down while ADS via adsKickMultiplier.")]
    public ProceduralWeaponAnimator proceduralAnimator;

    [Header("Fallback (used if the current weapon has no RecoilProfile)")]
    public float fallbackKickPitch = 1.2f;
    public float fallbackKickYaw   = 0.4f;
    public float fallbackRecoverySpeed = 6f;

    [Header("ADS Multiplier")]
    [Range(0f, 1f)]
    public float adsKickMultiplier = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private WeaponsController _current;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (weaponController != null) Bind(weaponController);
    }

    private void OnDisable() => Unbind();

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Bind / Unbind
    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (weaponController == null)
            weaponController = GetComponentInParent<WeaponsController>() ?? GetComponentInChildren<WeaponsController>();
    }
    
    private void Bind(WeaponsController w)
    {
        if (w == null) return;
        Unbind();
        _current = w;
        _current.OnWeaponFired += HandleWeaponFired;
        Log($"Bound to {w.name}");
    }

    private void Unbind()
    {
        if (_current == null) return;
        _current.OnWeaponFired -= HandleWeaponFired;
        _current = null;
    }

    /// <summary>Call from WeaponSwitcherProcedural on every switch — mirrors ProceduralRecoil.RebindController.</summary>
    public void RebindController(WeaponsController newController) => Bind(newController);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Kick
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleWeaponFired(Vector3 hitPoint)
    {
        if (playerCam == null) return;

        float pitch = fallbackKickPitch;
        float yaw   = Random.Range(-fallbackKickYaw, fallbackKickYaw);
        float recoverySpeed = fallbackRecoverySpeed;

        var profile = _current != null ? _current.recoilProfile : null;
        if (profile != null)
        {
            pitch = profile.cameraKickPitch;
            yaw   = Random.Range(-profile.cameraKickYaw, profile.cameraKickYaw);
            recoverySpeed = profile.cameraKickRecoverySpeed;
        }

        bool isADS = proceduralAnimator != null && proceduralAnimator.IsADS;
        float mult = isADS ? adsKickMultiplier : 1f;

        playerCam.recoilRecoverySpeed = recoverySpeed;
        playerCam.AddRecoilKick(pitch * mult, yaw * mult);

        Log($"Kick applied — pitch={pitch * mult:F2} yaw={yaw * mult:F2} recovery={recoverySpeed:F1}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[CameraRecoil] {msg}", this);
    }

    #endregion
}