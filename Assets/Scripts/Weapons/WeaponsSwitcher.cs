using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponSwitcher : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Weapons — slot order matches key 1 / 2 / …")]
    [Tooltip("Drag each weapon's WeaponsController here in slot order.")]
    public List<WeaponsController> weapons = new();

    [Header("Timing")]
    [Tooltip("Maximum seconds to wait for AnimEvent_HolsterEnd before forcing the swap.")]
    public float holsterTimeout = 1.5f;
    [Tooltip("Maximum seconds to wait for AnimEvent_DrawEnd before marking the switch done.")]
    public float drawTimeout    = 1.5f;

    [Header("Input")]
    public bool useScrollWheel    = true;
    public bool useNumberKeys     = true;
    [Tooltip("Prevents switching while the current weapon is mid-reload.")]
    public bool blockDuringReload = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires the moment a switch is requested.
    /// Either argument may be null (null outgoing on first equip).
    /// </summary>
    public event Action<WeaponsController, WeaponsController> OnSwitchStart;

    /// <summary>Fires after the incoming weapon finishes its draw animation.</summary>
    public event Action<WeaponsController, WeaponsController> OnSwitchComplete;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The weapon currently in hand. Null while a switch is in progress.</summary>
    public WeaponsController CurrentWeapon =>
        IsSwitching ? null : GetWeapon(_currentIndex);

    public int  CurrentIndex => _currentIndex;
    public bool IsSwitching  => _switchCoroutine != null;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private int       _currentIndex   = 0;
    private Coroutine _switchCoroutine;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (weapons.Count == 0)
        {
            Debug.LogWarning("[WeaponSwitcher] Weapons list is empty — nothing to switch.", this);
            return;
        }

        // Deactivate all slots then silently equip slot 0 (no draw animation at spawn).
        for (int i = 0; i < weapons.Count; i++)
            GetWeapon(i)?.gameObject.SetActive(i == 0);

        // Tell slot 0 it is equipped so it can initialise correctly.
        GetWeapon(0)?.NotifyEquipped();

        Log($"Ready — active: [{0}] {GetWeapon(0)?.name}");
    }

    private void Update()
    {
        if (IsSwitching) return;
        HandleSwitchInput();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Input
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleSwitchInput()
    {
        // Optional: block while the current weapon is mid-reload.
        if (blockDuringReload)
        {
            var cw = GetWeapon(_currentIndex);
            if (cw != null && cw.IsReloading) return;
        }

        // ── Scroll wheel ─────────────────────────────────────────────────────
        if (useScrollWheel && weapons.Count > 1)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0f)
            {
                TrySwitchTo((_currentIndex - 1 + weapons.Count) % weapons.Count);
                return;
            }
            if (scroll < 0f)
            {
                TrySwitchTo((_currentIndex + 1) % weapons.Count);
                return;
            }
        }

        // ── Alpha 1 – 9 ──────────────────────────────────────────────────────
        if (useNumberKeys)
        {
            int slotCount = Mathf.Min(weapons.Count, 9);
            for (int i = 0; i < slotCount; i++)
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

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Programmatically switch to weapon at <paramref name="index"/>.</summary>
    public void SwitchTo(int index) => TrySwitchTo(index);

    /// <summary>Cycle to the next weapon slot (wraps).</summary>
    public void SwitchToNext() =>
        TrySwitchTo((_currentIndex + 1) % weapons.Count);

    /// <summary>Cycle to the previous weapon slot (wraps).</summary>
    public void SwitchToPrevious() =>
        TrySwitchTo((_currentIndex - 1 + weapons.Count) % weapons.Count);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Switch Coroutine
    // ─────────────────────────────────────────────────────────────────────────

    private void TrySwitchTo(int index)
    {
        if (index == _currentIndex)              return; // already equipped
        if (IsSwitching)                         return; // mid-switch — ignore
        if (index < 0 || index >= weapons.Count) return;
        if (GetWeapon(index) == null)            return;

        _switchCoroutine = StartCoroutine(Co_Switch(index));
    }

    private IEnumerator Co_Switch(int nextIndex)
    {
        WeaponsController outgoing = GetWeapon(_currentIndex);
        WeaponsController incoming = GetWeapon(nextIndex);

        Log($"[{_currentIndex}] {outgoing?.name}  →  [{nextIndex}] {incoming?.name}");
        OnSwitchStart?.Invoke(outgoing, incoming);

        // ── STEP 1: Cancel everything on the outgoing weapon ─────────────────
        if (outgoing != null)
        {
            // ForceIdle stops all coroutines (fire / reload / inspect) cleanly.
            outgoing.ForceIdle();

            // Trigger Holster animation and wait for completion signal.
            outgoing.StartHolster();

            float t = 0f;
            while (!outgoing.HolsterComplete && t < holsterTimeout)
            {
                t += Time.deltaTime;
                yield return null;
            }

            if (t >= holsterTimeout)
                LogWarning($"Holster timed out on '{outgoing.name}' — forcing switch.");

            outgoing.gameObject.SetActive(false);
            outgoing.NotifyUnequipped();
        }

        // ── STEP 2: Activate and draw the incoming weapon ─────────────────────
        _currentIndex = nextIndex;
        incoming.gameObject.SetActive(true);
        incoming.StartDraw();

        float dt = 0f;
        while (!incoming.DrawComplete && dt < drawTimeout)
        {
            dt += Time.deltaTime;
            yield return null;
        }

        if (dt >= drawTimeout)
            LogWarning($"Draw timed out on '{incoming.name}' — switch complete anyway.");

        incoming.NotifyEquipped();

        Log($"Switch complete → [{nextIndex}] {incoming.name}");
        OnSwitchComplete?.Invoke(outgoing, incoming);

        _switchCoroutine = null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private WeaponsController GetWeapon(int i) =>
        (i >= 0 && i < weapons.Count) ? weapons[i] : null;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[WeaponSwitcher] {msg}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogWarning(string msg) =>
        Debug.LogWarning($"[WeaponSwitcher] ⚠ {msg}", this);

    #endregion
}