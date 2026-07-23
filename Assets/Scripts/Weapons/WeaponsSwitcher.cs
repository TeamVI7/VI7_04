using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop-in replacement for WeaponSwitcher that uses procedural Transform lerp
/// to switch weapons — no animation events, no holster/draw clips required.
///
/// WHAT IT DOES:
///   Outgoing weapon lerps down-and-tilted out of frame, then the incoming
///   weapon rises up from below into the rest pose.  Inspired by the smooth
///   procedural switching in Battlefield/CoD.
///
/// SETUP:
///   1. Replace WeaponSwitcher with this component on the WeaponHolder object.
///   2. Assign the same weapons list (WeaponsControllers).
///   3. Assign weaponPivot (the WeaponPivot Transform that ProceduralWeaponAnimator sits on).
///   4. The outgoing weapon's GameObject is hidden at the MIDPOINT of the switch,
///      not immediately — this is what gives the "dip out, rise in" feel.
///
/// CROSS-SYSTEM LOCKING:
///   Switching raises/releases PlayerActionLock.LockReason.Switching for the whole
///   coroutine, and TrySwitchTo() itself checks PlayerActionLock.CanSwitch before starting
///   — this is what stops the player from switching mid-melee-swing or after dying, without
///   this script needing a direct reference to PlayerMelee or PlayerHealth.
/// </summary>
public class WeaponSwitcherProcedural : MonoBehaviour
{
    #region Inspector

    [Header("Weapons")]
    public List<WeaponsController> weapons = new();

    [Header("Pivot Reference")]
    [Tooltip("The WeaponPivot Transform that ProceduralWeaponAnimator + ProceduralRecoil live on.")]
    public Transform weaponPivot;

    [Header("Switch Animation")]
    [Tooltip("Local Y position the weapon drops to when leaving.")]
    public float dropY          = -0.35f;
    [Tooltip("Local Z rotation the weapon tilts to when leaving.")]
    public float dropTiltZ      = -15f;
    [Tooltip("Time to animate the outgoing weapon out of frame.")]
    public float dropDuration   = 0.14f;
    [Tooltip("Local Y position the incoming weapon rises from.")]
    public float riseFromY      = -0.45f;
    [Tooltip("Time to animate the incoming weapon into the rest pose.")]
    public float riseDuration   = 0.18f;

    [Header("Input")]
    public bool useScrollWheel  = true;
    public bool useNumberKeys   = true;
    [Tooltip("Block switching while the current weapon is mid-reload.")]
    public bool blockDuringReload = true;

    [Header("References")]
    public ProceduralWeaponAnimator proceduralAnimator;
    public ProceduralRecoil         recoilModule;
    public WallHandIK               wallHandIK;

    [Header("ADS Profiles")]
    [Tooltip("One entry per weapon slot, matching the weapons list order. " +
             "Leave an element null to use the fallback pose in ProceduralWeaponAnimator.")]
    public WeaponADSProfile[] adsProfiles = Array.Empty<WeaponADSProfile>();

    [Header("Weapon Unlocking")]
    [Tooltip("If true, weapon slots start locked (except index 0) and must be unlocked via " +
             "PickupWeapon() — used for WeaponPickup world items. If false, all weapons in " +
             "the list are available immediately (legacy/no-pickups behaviour).")]
    public bool useWeaponPickups = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    #endregion

    #region Events

    public event Action<WeaponsController, WeaponsController> OnSwitchStart;
    public event Action<WeaponsController, WeaponsController> OnSwitchComplete;

    #endregion

    #region Public Read-only State

    public WeaponsController CurrentWeapon =>
        IsSwitching ? null : GetWeapon(_currentIndex);

    public int  CurrentIndex => _currentIndex;
    public bool IsSwitching  => _switchCoroutine != null;

    // Static reference — pickups read this instead of GetComponentInParent,
    // which fails if WeaponSwitcherProcedural sits on a child of the player
    // (e.g. under the camera / weapon holder) rather than the root.
    public static WeaponSwitcherProcedural Instance { get; private set; }

    #endregion

    #region Private State

    private int       _currentIndex;
    private Coroutine _switchCoroutine;
    private bool[]    _unlocked;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (weapons.Count == 0)
        {
            FPSDebug.LogWarning("Weapons list is empty.", this);
            return;
        }

        for (int i = 0; i < weapons.Count; i++)
            GetWeapon(i)?.gameObject.SetActive(i == 0);

        _unlocked = new bool[weapons.Count];
        for (int i = 0; i < _unlocked.Length; i++)
            _unlocked[i] = !useWeaponPickups || i == 0;

        var first = GetWeapon(0);
        if (first != null)
        {
            first.NotifyEquipped();
            wallHandIK?.SetIK(first.leftArmIK);
        }

        recoilModule?.RebindController(first);
        proceduralAnimator?.RebindWeaponData(first != null ? first.weaponData : null);   // ← ADD: needed for scope to activate
        proceduralAnimator?.LoadProfile(GetProfile(0));

        Log($"Ready — active: [{0}] {first?.name}");
    }

    private void Update()
    {
        if (!IsSwitching) HandleSwitchInput();
    }

    /// <summary>
    /// Safety net — if this is disabled while Co_Switch is mid-yield, the coroutine dies
    /// silently and the Switching lock would otherwise stay stuck ON forever.
    /// </summary>
    private void OnDisable()
    {
        _switchCoroutine = null;
        PlayerActionLock.Instance.SetLock(PlayerActionLock.LockReason.Switching, false);
    }

    #endregion

    #region Input

    private void HandleSwitchInput()
    {
        if (blockDuringReload)
        {
            var cw = GetWeapon(_currentIndex);
            if (cw != null && cw.IsReloading) return;
        }

        if (useScrollWheel && weapons.Count > 1)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0f) { TrySwitchTo((_currentIndex - 1 + weapons.Count) % weapons.Count); return; }
            if (scroll < 0f) { TrySwitchTo((_currentIndex + 1) % weapons.Count); return; }
        }

        if (useNumberKeys)
        {
            int max = Mathf.Min(weapons.Count, 9);
            for (int i = 0; i < max; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    TrySwitchTo(i);
                    return;
                }
            }
        }
    }

    #endregion

    #region Public API

    public void SwitchTo(int index)  => TrySwitchTo(index);
    public void SwitchToNext()       => TrySwitchTo((_currentIndex + 1) % weapons.Count);
    public void SwitchToPrevious()   => TrySwitchTo((_currentIndex - 1 + weapons.Count) % weapons.Count);

    public bool IsUnlocked(int index) =>
        !useWeaponPickups || (index >= 0 && index < _unlocked.Length && _unlocked[index]);

    /// <summary>
    /// Called by WeaponPickup when the player walks over a weapon crate.
    /// Unlocks the slot and (optionally) switches to it immediately.
    /// Returns false if the slot was already unlocked (nothing to do — caller
    /// can fall back to granting bonus ammo instead so the pickup isn't wasted).
    /// </summary>
    public bool PickupWeapon(int index, bool switchToItImmediately = true)
    {
        if (index < 0 || index >= weapons.Count) return false;
        if (GetWeapon(index) == null)             return false;
        if (_unlocked == null)                    return false;
        if (IsUnlocked(index))                    return false;

        _unlocked[index] = true;
        Log($"Weapon unlocked: [{index}] {GetWeapon(index).name}");

        if (switchToItImmediately) TrySwitchTo(index);
        return true;
    }

    #endregion

    #region Switch Coroutine

    private void TrySwitchTo(int index)
    {
        if (index == _currentIndex)              return;
        if (IsSwitching)                         return;
        if (index < 0 || index >= weapons.Count) return;
        if (GetWeapon(index) == null)            return;
        if (!IsUnlocked(index)) { Log($"Weapon [{index}] is locked — pick it up first."); return; }

        if (PlayerActionLock.Instance != null && !PlayerActionLock.Instance.CanSwitch)
        {
            Log("Blocked — action lock active (dead/meleeing/reloading).");
            return;
        }

        _switchCoroutine = StartCoroutine(Co_Switch(index));
    }

    private IEnumerator Co_Switch(int nextIndex)
    {
        WeaponsController outgoing = GetWeapon(_currentIndex);
        WeaponsController incoming = GetWeapon(nextIndex);

        OnSwitchStart?.Invoke(outgoing, incoming);
        PlayerActionLock.Instance?.SetLock(PlayerActionLock.LockReason.Switching, true);

        if (outgoing != null) outgoing.ForceIdle();
        outgoing?.NotifyUnequipped();

        if (proceduralAnimator != null) proceduralAnimator.IsSwitching = true;

        // ── Drop out ──────────────────────────────────────────────────────
        if (weaponPivot != null && outgoing != null)
        {
            Vector3    startPos  = weaponPivot.localPosition;
            Quaternion startRot  = weaponPivot.localRotation;
            Vector3    targetPos = startPos + new Vector3(0f, dropY, 0f);
            Quaternion targetRot = startRot * Quaternion.Euler(0f, 0f, dropTiltZ);
            yield return AnimatePivot(startPos, targetPos, startRot, targetRot,
                                      dropDuration, Easing.EaseInQuad);
        }
        else yield return new WaitForSeconds(dropDuration);

        // ── Swap ──────────────────────────────────────────────────────────
        if (outgoing != null) outgoing.gameObject.SetActive(false);
        _currentIndex = nextIndex;
        incoming.gameObject.SetActive(true);
        
        recoilModule?.RebindController(incoming);
        proceduralAnimator?.RebindWeaponData(incoming.weaponData);   // ← ADD: needed for scope to activate on swap
        wallHandIK?.SetIK(incoming.leftArmIK);
        proceduralAnimator?.SnapToHip();
        
        var profile = GetProfile(nextIndex);
        proceduralAnimator?.LoadProfile(profile);
        if (profile?.recoilProfile != null) recoilModule?.ApplyProfile(profile.recoilProfile);

        if (weaponPivot != null)
        {
            Vector3 riseStart = weaponPivot.localPosition + new Vector3(0f, riseFromY - dropY, 0f);
            weaponPivot.localPosition = riseStart;
            weaponPivot.localRotation = Quaternion.identity;
        }

        // ── Rise in ───────────────────────────────────────────────────────
        if (weaponPivot != null)
        {
            Vector3    riseStartPos  = weaponPivot.localPosition;
            Quaternion riseStartRot  = weaponPivot.localRotation;
            Vector3    riseTargetPos = proceduralAnimator != null
                                     ? proceduralAnimator.CurrentHipPos   // ← FIX: was ActiveHipPos, doesn't exist on your animator
                                     : riseStartPos + new Vector3(0f, -riseFromY, 0f);
            yield return AnimatePivot(riseStartPos, riseTargetPos, riseStartRot,
                                      Quaternion.identity, riseDuration, Easing.EaseOutBack);
        }
        else yield return new WaitForSeconds(riseDuration);

        if (proceduralAnimator != null) proceduralAnimator.IsSwitching = false;

        incoming.NotifyEquipped();
        OnSwitchComplete?.Invoke(outgoing, incoming);
        PlayerActionLock.Instance?.SetLock(PlayerActionLock.LockReason.Switching, false);
        _switchCoroutine = null;
    }

    private IEnumerator AnimatePivot(
        Vector3 startPos, Vector3 endPos,
        Quaternion startRot, Quaternion endRot,
        float duration, Func<float, float> ease)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = ease(elapsed / duration);
            weaponPivot.localPosition = Vector3.Lerp(startPos, endPos, t);
            weaponPivot.localRotation = Quaternion.Slerp(startRot, endRot, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        weaponPivot.localPosition = endPos;
        weaponPivot.localRotation = endRot;
    }

    #endregion

    #region Helpers

    private WeaponsController GetWeapon(int i) =>
        (i >= 0 && i < weapons.Count) ? weapons[i] : null;

    private WeaponADSProfile GetProfile(int i) =>
        (i >= 0 && i < adsProfiles.Length) ? adsProfiles[i] : null;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[WeaponSwitcherProcedural] {msg}", this);
    }

    #endregion

    #region Easing Library

    private static class Easing
    {
        public static float Linear(float t)      => t;
        public static float EaseInQuad(float t)  => t * t;
        public static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float EaseInOutQuad(float t) =>
            t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;

        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
        public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    }

    #endregion
}