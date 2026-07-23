using System;
using UnityEngine;

public class PlayerActionLock : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Types
    // ─────────────────────────────────────────────────────────────────────────

    [Flags]
    public enum LockReason
    {
        None        = 0,
        Reloading   = 1 << 0,
        Meleeing    = 1 << 1,
        Switching   = 1 << 2,
        Inspecting  = 1 << 3,
        Dead        = 1 << 4,
        Sliding     = 1 << 5,
        WallRunning = 1 << 6,
        // Room for future abilities — grappling hook, vaulting override, cutscene lock, etc.
        Custom1     = 1 << 7,
        Custom2     = 1 << 8,
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fired any time the combined lock state changes. Arg: the new combined flags.</summary>
    public event Action<LockReason> OnLockChanged;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public State
    // ─────────────────────────────────────────────────────────────────────────

    public static PlayerActionLock Instance
    {
        get
        {
            if (_instance == null)
            {
                // Self-bootstrapping — this lock must never depend on someone
                // remembering to drop it in the scene. First thing that touches
                // it (SetLock/CanX from any script, in any load order) creates it.
                var go = new GameObject("PlayerActionLock (auto)");
                _instance = go.AddComponent<PlayerActionLock>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    private static PlayerActionLock _instance;

    public LockReason CurrentLocks { get; private set; } = LockReason.None;

    public bool IsDead => IsBlockedBy(LockReason.Dead);

    /// <summary>Can the weapon fire right now?</summary>
    public bool CanFire => !IsBlockedBy(LockReason.Dead | LockReason.Switching | LockReason.Meleeing);

    /// <summary>Can a reload be *started* right now? (Doesn't include Reloading itself —
    /// WeaponsController already guards re-entrancy on its own state machine.)</summary>
    public bool CanReload => !IsBlockedBy(LockReason.Dead | LockReason.Switching | LockReason.Meleeing
                                        | LockReason.Sliding | LockReason.WallRunning);

    /// <summary>Can the player switch weapons right now?</summary>
    public bool CanSwitch => !IsBlockedBy(LockReason.Dead | LockReason.Meleeing | LockReason.Reloading);

    /// <summary>Can a melee swing be *started* right now?</summary>
    public bool CanMelee => !IsBlockedBy(LockReason.Dead | LockReason.Switching | LockReason.Reloading);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Raise or clear a lock reason. Safe to call every frame — no-ops if unchanged.</summary>
    public void SetLock(LockReason reason, bool active)
    {
        LockReason prev = CurrentLocks;
        CurrentLocks = active ? (CurrentLocks | reason) : (CurrentLocks & ~reason);

        if (CurrentLocks == prev) return;

        Log($"{reason} {(active ? "LOCKED" : "released")} → {CurrentLocks}");
        OnLockChanged?.Invoke(CurrentLocks);
    }

    /// <summary>Clears every lock. Useful on respawn/scene reset so a stuck flag can't
    /// permanently soft-lock the player.</summary>
    public void ClearAll()
    {
        if (CurrentLocks == LockReason.None) return;
        CurrentLocks = LockReason.None;
        Log("All locks cleared.");
        OnLockChanged?.Invoke(CurrentLocks);
    }

    /// <summary>True if any of the given reasons are currently active.</summary>
    public bool IsBlockedBy(LockReason mask) => (CurrentLocks & mask) != 0;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[PlayerActionLock] {msg}", this);
    }

    #endregion
}