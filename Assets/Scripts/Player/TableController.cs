using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// In-world tablet UI. Tab toggles open/closed; closing plays the open animation
/// backward by manually scrubbing Animator normalizedTime each frame (Animator.speed
/// cannot go negative outside of Recorder mode, so reverse playback is driven by hand
/// via Animator.Play(stateHash, 0, t) rather than relying on speed).
/// Disables Player UI and gun mesh/input while open. Hand bone tracks mouse
/// movement within clamped local-space limits; index finger bone blends toward
/// a "pressed" pose while the player holds the click input.
///
/// HIERARCHY EXPECTATION:
///   tabletVisuals (root mesh + Animator) is a child object, inactive by default.
///   handBone / indexFingerBone are bones inside that hierarchy (Animation Rigging
///   or raw skeleton — either works, this only touches local position/rotation).
///
/// EXTENDING:
///   - Add more finger bones by extending HandleFingerClick() with additional
///     Transform + offset pairs, or refactor into a FingerBone[] array if you need
///     per-finger curl (index/middle/ring/pinky) for multi-button tablet UI.
///   - Add OnAppOpened/OnAppClosed events here if the tablet grows multiple "apps".
///   - Subscribe to OnTabletOpened/OnTabletClosed from WeaponHUD, CameraShaker, or
///     an objective-map system instead of hardcoding cross-references here.
///
/// DEBUG:
///   - Enable debugLog in Inspector for state transition logs.
///   - Gizmos draw the hand's clamped movement bounds in Scene view when selected.
/// </summary>
public class TabletController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Core References")]
    [Tooltip("Parent object containing the tablet mesh + Animator. Inactive by default.")]
    public GameObject tabletVisuals;
    public Animator tabletAnimator;

    [Header("UI / Gun Suppression")]
    [Tooltip("Root Player HUD object to hide while the tablet is open.")]
    public GameObject playerUI;
    [Tooltip("WeaponPivot/WeaponHolder — disabled (mesh hidden) while tablet is open.")]
    public GameObject weaponHolder;

    [Header("Player Control Suppression")]
    [Tooltip("The camera-look script (PlayerCam) — explicitly disabled while open so it stops consuming Mouse X/Y, which would otherwise fight the tablet's hand tracking for the same input. Auto-found via GetComponentInParent if left empty.")]
    public PlayerCam playerCam;
    [Tooltip("PlayerMovement, WeaponsController, etc. — anything else that should freeze while the tablet is open. Disabled while open, re-enabled on close start (not close end) so input feels instant.")]
    public Behaviour[] playerScriptsToDisable;

    [Header("Open Gate — Block Tablet During Weapon Actions")]
    [Tooltip("Currently equipped weapon. Used to block opening during fire/reload/inspect/ADS/bolt-cycle. Auto-rebinds on weapon switch if weaponSwitcher is assigned.")]
    public WeaponsController activeWeapon;
    [Tooltip("Optional — if assigned, blocks opening while a weapon switch is in progress and keeps activeWeapon in sync automatically.")]
    public WeaponSwitcherProcedural weaponSwitcher;
    [Tooltip("Block opening while the player is aiming down sights.")]
    public bool blockWhileADS = true;

    [Header("Toggle Input")]
    public KeyCode toggleKey = KeyCode.Tab;
    [Tooltip("Block toggling again until the current open/close animation finishes.")]
    public bool blockToggleDuringTransition = true;

    [Header("Animation")]
    [Tooltip("How long the open animation takes — used as a timeout fallback and to time the close-deactivate.")]
    public float animationDuration = 0.5f;
    [Tooltip("Scrub-rate multiplier for the manual animation playback. 1 = plays at the clip's authored speed in animationDuration seconds; closing uses the same rate in reverse.")]
    public float openSpeed = 1f;

    [Header("Hand Movement")]
    public Transform handBone;
    public float handMoveSpeed = 0.01f;
    public Vector2 xLimits = new Vector2(-0.2f, 0.2f);
    public Vector2 yLimits = new Vector2(-0.2f, 0.2f);
    [Tooltip("Smoothing applied to hand movement. Higher = snappier.")]
    public float handFollowLerp = 15f;

    [Header("Finger Click")]
    public Transform indexFingerBone;
    public Vector3 clickRotationOffset = new Vector3(45f, 0f, 0f);
    [Tooltip("Degrees/sec blend speed toward pressed/released pose.")]
    public float fingerLerpSpeed = 20f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    [SerializeField] private bool showHandGizmo = true;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fired the instant open begins (animation still playing).</summary>
    public event Action OnTabletOpenStart;
    /// <summary>Fired when the open animation finishes and the tablet is fully interactive.</summary>
    public event Action OnTabletOpened;
    /// <summary>Fired the instant close begins (animation still playing in reverse).</summary>
    public event Action OnTabletCloseStart;
    /// <summary>Fired when the close animation finishes and visuals are deactivated.</summary>
    public event Action OnTabletClosed;
    /// <summary>Fired when the player tries to open the tablet but the action gate blocks it. Arg: reason string.</summary>
    public event Action<string> OnTabletOpenBlocked;
    /// <summary>Fired on click-down / click-up while the tablet is open. Arg: isPressed.</summary>
    public event Action<bool> OnFingerClick;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Machine
    // ─────────────────────────────────────────────────────────────────────────

    public enum TabletState { Closed, Opening, Open, Closing }

    public TabletState CurrentState { get; private set; } = TabletState.Closed;
    public bool IsOpen => CurrentState == TabletState.Open || CurrentState == TabletState.Opening;

    private void SetState(TabletState next)
    {
        if (CurrentState == next) return;
        Log($"State: {CurrentState} → {next}");
        CurrentState = next;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3    _initialHandLocalPos;
    private Vector3    _handVelocityTarget;
    private Quaternion _fingerRestRotation;
    private Quaternion _fingerPressedRotation;
    private bool       _fingerPressed;
    private Coroutine  _transitionCoroutine;
    private float       _lastNormalizedTime; // 0 = fully closed pose, 1 = fully open pose
    private int         _cachedStateHash;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        ValidateSetup();

        if (tabletAnimator != null)
        {
            tabletAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            tabletAnimator.speed      = 0f; // time is driven manually via Co_ScrubAnimation
            _cachedStateHash          = tabletAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        }

        if (handBone != null)
        {
            _initialHandLocalPos = handBone.localPosition;
            _handVelocityTarget  = _initialHandLocalPos;
        }

        if (indexFingerBone != null)
        {
            _fingerRestRotation    = indexFingerBone.localRotation;
            _fingerPressedRotation = _fingerRestRotation * Quaternion.Euler(clickRotationOffset);
        }

        if (tabletVisuals != null)
            tabletVisuals.SetActive(false);

        if (playerCam == null)
            playerCam = GetComponentInParent<PlayerCam>();

        if (weaponSwitcher != null)
        {
            weaponSwitcher.OnSwitchStart    += HandleWeaponSwitchStart;
            weaponSwitcher.OnSwitchComplete += HandleWeaponSwitchComplete;
            if (activeWeapon == null) activeWeapon = weaponSwitcher.CurrentWeapon;
        }
    }

    private void OnDestroy()
    {
        if (weaponSwitcher != null)
        {
            weaponSwitcher.OnSwitchStart    -= HandleWeaponSwitchStart;
            weaponSwitcher.OnSwitchComplete -= HandleWeaponSwitchComplete;
        }
    }

    private void HandleWeaponSwitchStart(WeaponsController outgoing, WeaponsController incoming)
    {
        // Keep activeWeapon null during the switch itself — IsSwitching flag below
        // already covers blocking, this just avoids querying a stale/outgoing reference.
        activeWeapon = null;
    }

    private void HandleWeaponSwitchComplete(WeaponsController outgoing, WeaponsController incoming)
    {
        activeWeapon = incoming;
    }

    private void Update()
    {
        HandleToggleInput();

        if (CurrentState == TabletState.Open)
        {
            HandleHandMovement();
            HandleFingerClick();
        }
        else if (indexFingerBone != null)
        {
            // Always relax the finger back to rest while not actively open
            // (covers mid-close, so it doesn't freeze in a pressed pose).
            indexFingerBone.localRotation = Quaternion.Slerp(
                indexFingerBone.localRotation, _fingerRestRotation, Time.unscaledDeltaTime * fingerLerpSpeed);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Toggle / Open / Close
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleToggleInput()
    {
        if (!Input.GetKeyDown(toggleKey)) return;

        bool transitioning = CurrentState == TabletState.Opening || CurrentState == TabletState.Closing;
        if (blockToggleDuringTransition && transitioning)
        {
            Log("Toggle ignored — transition in progress.");
            return;
        }

        if (CurrentState == TabletState.Closed)
        {
            if (!CanOpen(out string blockReason))
            {
                Log($"Open blocked — {blockReason}");
                OnTabletOpenBlocked?.Invoke(blockReason);
                return;
            }
            StartOpen();
        }
        else if (CurrentState == TabletState.Open)     StartClose();
        else if (!blockToggleDuringTransition)
        {
            // Allow interrupting mid-transition: reverse whatever is currently happening.
            if (transitioning) StopCoroutineIfRunning();
            if (CurrentState == TabletState.Opening)   StartClose();
            else if (CanOpen(out _))                   StartOpen();
        }
    }

    /// <summary>
    /// Returns false if any weapon action is in progress that the tablet shouldn't
    /// interrupt — firing, reloading, inspecting, bolt-cycling, switching, or (optionally)
    /// aiming down sights. Disabling player scripts mid-coroutine does NOT pause those
    /// coroutines, so opening during reload previously left WeaponsController's
    /// Co_Reload() stalled forever waiting on anim signals it could no longer receive.
    /// Blocking the open at the source avoids that entirely.
    /// </summary>
    public bool CanOpen(out string reason)
    {
        if (weaponSwitcher != null && weaponSwitcher.IsSwitching)
        {
            reason = "weapon switching";
            return false;
        }

        if (activeWeapon != null)
        {
            switch (activeWeapon.CurrentState)
            {
                case WeaponsController.WeaponState.Firing:
                    reason = "firing";
                    return false;
                case WeaponsController.WeaponState.Reloading:
                    reason = "reloading";
                    return false;
                case WeaponsController.WeaponState.BoltCycling:
                    reason = "cycling bolt";
                    return false;
                case WeaponsController.WeaponState.Switching:
                    reason = "switching";
                    return false;
            }

            if (activeWeapon.IsInspecting)
            {
                reason = "inspecting weapon";
                return false;
            }

            if (blockWhileADS && activeWeapon.IsADS)
            {
                reason = "aiming";
                return false;
            }
        }

        reason = null;
        return true;
    }

    public void StartOpen()
    {
        StopCoroutineIfRunning();
        SetState(TabletState.Opening);
        OnTabletOpenStart?.Invoke();

        if (tabletVisuals != null) tabletVisuals.SetActive(true);

        if (tabletAnimator != null)
            tabletAnimator.speed = 0f; // we drive time manually — speed must stay 0/non-negative

        if (playerUI != null)      playerUI.SetActive(false);
        if (weaponHolder != null)  weaponHolder.SetActive(false);

        SetPlayerScriptsEnabled(false);

        // Cursor stays locked: hand tracking reads raw Mouse X/Y deltas, which Unity's
        // legacy Input Manager only reports reliably while the cursor is locked+hidden.
        // Unlocking here (as a prior version did) silently zeroed hand movement.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        _transitionCoroutine = StartCoroutine(Co_ScrubAnimation(forward: true));
        Log("Open started.");
    }

    public void StartClose()
    {
        StopCoroutineIfRunning();
        SetState(TabletState.Closing);
        OnTabletCloseStart?.Invoke();

        if (tabletAnimator != null)
            tabletAnimator.speed = 0f;

        // Re-enable player control immediately so closing feels responsive —
        // the tablet visuals/animator keep running unscaled in the background.
        SetPlayerScriptsEnabled(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        _transitionCoroutine = StartCoroutine(Co_ScrubAnimation(forward: false));
        Log("Close started.");
    }

    /// <summary>
    /// Manually scrubs the tablet's Animator state forward (0→1) or backward (1→0)
    /// using normalizedTime, since Animator.speed cannot go negative outside of
    /// Recorder mode. Runs on unscaled time so it's unaffected by Time.timeScale.
    /// </summary>
    private IEnumerator Co_ScrubAnimation(bool forward)
    {
        float duration = Mathf.Max(0.0001f, animationDuration);
        float elapsed  = forward ? _lastNormalizedTime * duration : (1f - _lastNormalizedTime) * duration;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime * openSpeed;
            float t = Mathf.Clamp01(elapsed / duration);
            _lastNormalizedTime = forward ? t : 1f - t;

            if (tabletAnimator != null && _cachedStateHash != 0)
                tabletAnimator.Play(_cachedStateHash, 0, _lastNormalizedTime);

            yield return null;
        }

        _lastNormalizedTime = forward ? 1f : 0f;
        if (tabletAnimator != null && _cachedStateHash != 0)
            tabletAnimator.Play(_cachedStateHash, 0, _lastNormalizedTime);

        if (forward)
        {
            SetState(TabletState.Open);
            OnTabletOpened?.Invoke();
            Log("Open complete.");
        }
        else
        {
            if (tabletVisuals != null) tabletVisuals.SetActive(false);
            if (playerUI != null)      playerUI.SetActive(true);
            if (weaponHolder != null)  weaponHolder.SetActive(true);

            SetState(TabletState.Closed);
            OnTabletClosed?.Invoke();
            Log("Close complete.");
        }

        _transitionCoroutine = null;
    }

    private void StopCoroutineIfRunning()
    {
        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = null;
        }
    }

    private void SetPlayerScriptsEnabled(bool enabled)
    {

        if (playerCam != null) playerCam.enabled = enabled;

        if (playerScriptsToDisable == null) return;
        foreach (var script in playerScriptsToDisable)
            if (script != null) script.enabled = enabled;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Hand Movement
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleHandMovement()
    {
        if (handBone == null) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * handMoveSpeed;
        float mouseY = Input.GetAxisRaw("Mouse Y") * handMoveSpeed;

        _handVelocityTarget += new Vector3(mouseX, mouseY, 0f);
        _handVelocityTarget.x = Mathf.Clamp(_handVelocityTarget.x,
            _initialHandLocalPos.x + xLimits.x, _initialHandLocalPos.x + xLimits.y);
        _handVelocityTarget.y = Mathf.Clamp(_handVelocityTarget.y,
            _initialHandLocalPos.y + yLimits.x, _initialHandLocalPos.y + yLimits.y);

        handBone.localPosition = Vector3.Lerp(
            handBone.localPosition, _handVelocityTarget, Time.unscaledDeltaTime * handFollowLerp);
    }

    /// <summary>Snap the hand back to rest pose — call on tablet close if desired.</summary>
    public void ResetHandPosition()
    {
        _handVelocityTarget = _initialHandLocalPos;
        if (handBone != null) handBone.localPosition = _initialHandLocalPos;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Finger Click
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleFingerClick()
    {
        if (indexFingerBone == null) return;

        bool pressed = Input.GetMouseButton(0);
        if (pressed != _fingerPressed)
        {
            _fingerPressed = pressed;
            OnFingerClick?.Invoke(pressed);
            Log($"Finger {(pressed ? "pressed" : "released")}.");
        }

        Quaternion target = pressed ? _fingerPressedRotation : _fingerRestRotation;
        indexFingerBone.localRotation = Quaternion.Slerp(
            indexFingerBone.localRotation, target, Time.unscaledDeltaTime * fingerLerpSpeed);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Validation / Debug
    // ─────────────────────────────────────────────────────────────────────────

    private void ValidateSetup()
    {
        if (tabletVisuals == null)  LogWarning("tabletVisuals not assigned — tablet will never show.");
        if (tabletAnimator == null) LogWarning("tabletAnimator not assigned — open/close will skip animation entirely.");
        if (handBone == null)       LogWarning("handBone not assigned — hand tracking disabled.");
        if (indexFingerBone == null) LogWarning("indexFingerBone not assigned — click animation disabled.");
        if (activeWeapon == null && weaponSwitcher == null)
            LogWarning("Neither activeWeapon nor weaponSwitcher assigned — open-gate will never block (tablet can open during fire/reload/etc).");
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[TabletController] {msg}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogWarning(string msg) => Debug.LogWarning($"[TabletController] ⚠ {msg}", this);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showHandGizmo || handBone == null) return;

        Vector3 center = handBone.parent != null
            ? handBone.parent.TransformPoint(_initialHandLocalPos == Vector3.zero ? handBone.localPosition : _initialHandLocalPos)
            : handBone.position;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, new Vector3(xLimits.y - xLimits.x, yLimits.y - yLimits.x, 0.01f));
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(handBone.position, 0.01f);
    }
#endif

    #endregion
}