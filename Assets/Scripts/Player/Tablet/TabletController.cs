using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// In-world tablet UI. Tab toggles open/closed; closing plays the open animation
/// backward by manually scrubbing Animator normalizedTime each frame (Animator.speed
/// cannot go negative outside of Recorder mode, so reverse playback is driven by hand
/// via Animator.Play(stateHash, 0, t) rather than relying on speed).
/// Disables Player UI and gun mesh/input while open.
///
/// TWO HANDS, TWO JOBS:
///   • Left hand (handBone) holds the tablet — tracks small mouse deltas within a
///     clamped local-space box. No IK rig required, position-only.
///   • Right hand (rightHandIK) reaches toward whatever world-space Canvas button
///     the player is looking at — driven by a TwoBoneIKConstraint + ikTarget, same
///     pattern as WallHandIK. Target position comes from a camera-forward raycast
///     against the EventSystem's raycasters, so clicking fires real Button.onClick /
///     IPointerClickHandler events, not just a visual pose.
///
/// WHY CAMERA-FORWARD INSTEAD OF A FREE CURSOR:
///   Cursor stays locked the whole time the tablet is open. Pip-Boy-style interaction
///   is look-to-aim: point your view at a button, the hand reaches for it, click.
///   A free cursor would conflict with the left hand's raw Mouse X/Y deltas
///   (GetAxisRaw only reports reliably while locked) and would need a separate
///   on-screen reticle anyway — camera-forward IS the reticle here.
///
/// MINIMAP:
///   A dedicated top-down Camera renders to a RenderTexture, which is read by a
///   material on the tablet screen mesh (assign that material's main texture slot
///   to minimapRenderTexture in the Editor — this script doesn't touch materials
///   directly, it only manages the camera). The minimap camera is disabled while
///   the tablet is closed so it isn't paying render cost every frame for no reason.
///
/// MINIMAP — MULTI-FLOOR:
///   For buildings with multiple floors, drop a MinimapFloorZone (trigger volume)
///   on each floor and let MinimapFloorRegistry track which one the player is
///   standing in. UpdateMinimap() reads MinimapFloorRegistry.Instance.ActiveZone
///   each frame — if a zone is active, its floorHeight/floorCullingMask/
///   floorZoomOverride take priority over this script's own minimapHeight/zoom
///   fields, which become the fallback for when the player is in no zone at all
///   (e.g. outdoors, or a single-floor area that hasn't been zoned). No zones in
///   the scene at all = identical behaviour to the single-floor version.
///

///   If tabletAnimator drives the left arm holding the tablet, give that layer an
///   Avatar Mask that excludes the right arm/hand chain. Otherwise the body
///   animation fights the right-hand IK every frame the same way
///   ProceduralWeaponAnimator fights TwoBoneIKConstraint during wall-contact (see
///   WallHandIK's blend-suppression pattern) — IK weight alone won't save you if
///   the underlying Animator keyframes also write to the same bones above the rig.
///
/// HIERARCHY EXPECTATION:
///   tabletVisuals (root mesh + Animator) is a child object, inactive by default.
///   handBone is a left-hand bone (position-only).
///   rightHandIK is a TwoBoneIKConstraint on the right arm; rightHandIKTarget is a
///   free child Transform it drives toward — not parented under the canvas, so it
///   can be repositioned every frame without inheriting canvas scale/rotation.
///   minimapCamera is a separate Camera in the scene (not the player's main camera),
///   parented loosely so it can follow the player from directly above.
///
/// EXTENDING:
///   - Add more finger bones by extending HandleFingerClick() with additional
///     Transform + offset pairs, or refactor into a FingerBone[] array if you need
///     per-finger curl for multi-button tablet UI.
///   - Add OnAppOpened/OnAppClosed events here if the tablet grows multiple "apps"
///     (minimap could become one app among several rather than always-on).
///   - Minimap rotation-with-player vs north-locked is a one-line toggle, see
///     UpdateMinimap() — minimapRotatesWithPlayer.
///
/// DEBUG:
///   - Enable debugLog in Inspector for state transition logs.
///   - Gizmos draw the hand bounds, right-hand raycast, and minimap camera frustum
///     in Scene view when selected.
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

    [Header("Left Hand — Holds Tablet")]
    public Transform handBone;
    public float handMoveSpeed = 0.01f;
    public Vector2 xLimits = new Vector2(-0.2f, 0.2f);
    public Vector2 yLimits = new Vector2(-0.2f, 0.2f);
    [Tooltip("Smoothing applied to left-hand movement. Higher = snappier.")]
    public float handFollowLerp = 15f;

    [Header("Left Hand — Finger Click (tablet hold hand)")]
    public Transform indexFingerBone;
    public Vector3 clickRotationOffset = new Vector3(45f, 0f, 0f);
    [Tooltip("Degrees/sec blend speed toward pressed/released pose.")]
    public float fingerLerpSpeed = 20f;

    [Header("Right Hand — World-Space Canvas Interaction")]
    [Tooltip("TwoBoneIKConstraint on the right arm. Weight drives to 1 while the tablet is open and a button is reachable, 0 otherwise.")]
    public TwoBoneIKConstraint rightHandIK;
    [Tooltip("Free Transform the IK constraint drives toward. NOT parented under the canvas — repositioned every frame in world space.")]
    public Transform rightHandIKTarget;
    [Tooltip("Speed the IK weight blends in/out and the target Transform follows the raycast hit.")]
    public float rightHandFollowLerp = 12f;
    [Tooltip("Camera used for the look-to-aim raycast. Auto-found via Camera.main if left empty.")]
    public Camera interactionCamera;
    [Tooltip("Max distance the raycast will reach to find world-space Canvas UI.")]
    public float interactionRange = 1.5f;
    [Tooltip("EventSystem used to raycast against world-space Canvases. Auto-found via EventSystem.current if left empty.")]
    public EventSystem eventSystem;
    [Tooltip("How far in front of the hit point the IK target sits — keeps the fingertip from clipping through the canvas plane.")]
    public float fingertipOffset = 0.01f;

    [Header("Minimap")]
    [Tooltip("Dedicated top-down Camera (NOT the player's main camera) that renders the minimap. Its targetTexture should already be set to minimapRenderTexture in the Editor.")]
    public Camera minimapCamera;
    [Tooltip("The RenderTexture the minimap camera writes to. Assign this same texture as the main texture on the tablet screen's material — that material swap happens in the Editor, not in code.")]
    public RenderTexture minimapRenderTexture;
    [Tooltip("Transform the minimap follows — usually the player root, NOT the camera (so camera pitch/lean doesn't tilt the map).")]
    public Transform minimapFollowTarget;
    [Tooltip("Height above minimapFollowTarget the camera sits.")]
    public float minimapHeight = 30f;
    [Tooltip("Orthographic size — smaller = more zoomed in.")]
    public float minimapZoom = 20f;
    [Tooltip("If true, the map rotates so 'up' always matches the player's facing direction. If false, the map stays north-locked.")]
    public bool minimapRotatesWithPlayer = false;
    [Tooltip("Only render the minimap camera while the tablet is open — saves render cost the rest of the time.")]
    public bool minimapOnlyRendersWhenOpen = true;
    [Tooltip("Culling mask used when no MinimapFloorZone is active (e.g. outdoors, or before any zone has been entered). Ignored if a zone is active — the zone's own floorCullingMask wins.")]
    public LayerMask minimapDefaultCullingMask = ~0;
    [Tooltip("Smoothing applied to camera height/zoom when crossing between floor zones, so switching floors doesn't visually snap. Set to a large value (e.g. 9999) for an instant cut instead.")]
    public float minimapFloorBlendSpeed = 6f;

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
    /// <summary>Fired on click-down / click-up of the tablet-hold (left) hand. Arg: isPressed.</summary>
    public event Action<bool> OnFingerClick;
    /// <summary>Fired when the right-hand reticle starts/stops hovering a clickable UI element. Arg: the hovered GameObject (null when nothing hovered).</summary>
    public event Action<GameObject> OnUITargetChanged;
    /// <summary>Fired when the right hand actually clicks a world-space UI element. Arg: the clicked GameObject.</summary>
    public event Action<GameObject> OnUIClicked;
    /// <summary>Fired when the active minimap floor zone changes. Arg: the new zone (null if player left all zones — minimap falls back to defaults).</summary>
    public event Action<MinimapFloorZone> OnMinimapFloorChanged;

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
    private float      _lastNormalizedTime; // 0 = fully closed pose, 1 = fully open pose
    private int        _cachedStateHash;

    // Right-hand world UI interaction
    private GameObject       _currentHoverTarget;
    private Vector3          _rightHandRestLocalPos;
    private Quaternion       _rightHandRestLocalRot;
    private bool             _rightHandHasTarget;
    private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>(8);
    private PointerEventData _pointerEventData;

    // Minimap floor blending
    private float _currentMinimapHeight;
    private float _currentMinimapZoom;

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

        if (rightHandIKTarget != null)
        {
            _rightHandRestLocalPos = rightHandIKTarget.localPosition;
            _rightHandRestLocalRot = rightHandIKTarget.localRotation;
        }

        if (rightHandIK != null)
            rightHandIK.weight = 0f;

        if (tabletVisuals != null)
            tabletVisuals.SetActive(false);

        if (playerCam == null)
            playerCam = GetComponentInParent<PlayerCam>();

        if (interactionCamera == null)
            interactionCamera = Camera.main;

        if (eventSystem == null)
            eventSystem = EventSystem.current;

        if (eventSystem != null)
            _pointerEventData = new PointerEventData(eventSystem);

        if (minimapFollowTarget == null && interactionCamera != null)
            minimapFollowTarget = interactionCamera.transform.root;

        _currentMinimapHeight = minimapHeight;
        _currentMinimapZoom   = minimapZoom;

        SetMinimapCameraEnabled(!minimapOnlyRendersWhenOpen);

        if (MinimapFloorRegistry.Instance != null)
            MinimapFloorRegistry.Instance.OnActiveZoneChanged += HandleMinimapFloorChanged;

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

        if (MinimapFloorRegistry.Instance != null)
            MinimapFloorRegistry.Instance.OnActiveZoneChanged -= HandleMinimapFloorChanged;
    }

    private void HandleMinimapFloorChanged(MinimapFloorZone zone)
    {
        Log($"Minimap floor changed → {zone?.floorName ?? "(default/no zone)"}");
        OnMinimapFloorChanged?.Invoke(zone);
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
    }

    /// <summary>
    /// All bone-writing happens here, not in Update(). Unity evaluates the Animator
    /// between Update and LateUpdate — if hand/finger/IK writes happened in Update,
    /// the tablet's Animator (even just holding a single static pose) would
    /// overwrite them every single frame, which is exactly the "hand frozen in
    /// place" symptom: the write was happening, it just got clobbered immediately
    /// after. This is the same class of conflict documented for
    /// ProceduralWeaponAnimator vs TwoBoneIKConstraint — see the Avatar Mask note
    /// in this class's header for the complementary Editor-side fix.
    /// </summary>
    private void LateUpdate()
    {
        if (CurrentState == TabletState.Open)
        {
            HandleHandMovement();
            HandleFingerClick();
            HandleRightHandUIInteraction();
            UpdateMinimap();
        }
        else
        {
            // Always relax both hands back to rest while not actively open
            // (covers mid-close, so nothing freezes in a pressed/reaching pose).
            if (indexFingerBone != null)
                indexFingerBone.localRotation = Quaternion.Slerp(
                    indexFingerBone.localRotation, _fingerRestRotation, Time.unscaledDeltaTime * fingerLerpSpeed);

            RelaxRightHand();
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
        SetMinimapCameraEnabled(true);

        // Cursor stays locked: left-hand tracking reads raw Mouse X/Y deltas, which
        // Unity's legacy Input Manager only reports reliably while the cursor is
        // locked+hidden. Right-hand UI interaction is look-to-aim via camera-forward
        // raycast (see HandleRightHandUIInteraction), so a free cursor isn't needed
        // for clicking either — you aim your view at a button, not a cursor at it.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

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

        SetUIHoverTarget(null); // clear hover/highlight state immediately on close

        if (minimapOnlyRendersWhenOpen)
            SetMinimapCameraEnabled(false);

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
    #region Left Hand — Holds Tablet
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

    /// <summary>Snap the left hand back to rest pose — call on tablet close if desired.</summary>
    public void ResetHandPosition()
    {
        _handVelocityTarget = _initialHandLocalPos;
        if (handBone != null) handBone.localPosition = _initialHandLocalPos;
    }

    private void HandleFingerClick()
    {
        if (indexFingerBone == null) return;

        bool pressed = Input.GetMouseButton(0);
        if (pressed != _fingerPressed)
        {
            _fingerPressed = pressed;
            OnFingerClick?.Invoke(pressed);
            Log($"Left finger {(pressed ? "pressed" : "released")}.");
        }

        Quaternion target = pressed ? _fingerPressedRotation : _fingerRestRotation;
        indexFingerBone.localRotation = Quaternion.Slerp(
            indexFingerBone.localRotation, target, Time.unscaledDeltaTime * fingerLerpSpeed);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Right Hand — World-Space Canvas Interaction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raycasts camera-forward against the EventSystem's registered raycasters
    /// (this picks up GraphicRaycaster on world-space Canvases automatically — no
    /// manual Canvas reference needed). If something UI-clickable is hit within
    /// range, the right-hand IK target moves to that point and weight blends in.
    /// Clicking fires a real IPointerClickHandler event at that point, so actual
    /// Button.onClick listeners run — this is not just a visual finger-point.
    /// </summary>
    private void HandleRightHandUIInteraction()
    {
        if (rightHandIK == null || rightHandIKTarget == null || interactionCamera == null || eventSystem == null)
            return;

        _pointerEventData.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        _raycastResults.Clear();
        eventSystem.RaycastAll(_pointerEventData, _raycastResults);

        RaycastResult? hit = null;
        for (int i = 0; i < _raycastResults.Count; i++)
        {
            if (_raycastResults[i].distance <= interactionRange)
            {
                hit = _raycastResults[i];
                break; // RaycastAll returns sorted nearest-first
            }
        }

        if (hit.HasValue)
        {
            GameObject target = hit.Value.gameObject;
            SetUIHoverTarget(target);

            Vector3 worldPoint = hit.Value.worldPosition;
            Vector3 approachDir = (interactionCamera.transform.position - worldPoint).normalized;
            Vector3 targetPos = worldPoint + approachDir * fingertipOffset;

            rightHandIKTarget.position = Vector3.Lerp(
                rightHandIKTarget.position, targetPos, Time.unscaledDeltaTime * rightHandFollowLerp);
            rightHandIKTarget.rotation = Quaternion.LookRotation(-approachDir, Vector3.up);

            rightHandIK.weight = Mathf.MoveTowards(rightHandIK.weight, 1f, Time.unscaledDeltaTime * rightHandFollowLerp);
            _rightHandHasTarget = true;

            if (Input.GetMouseButtonDown(0))
                ClickUITarget(target, hit.Value);
        }
        else
        {
            SetUIHoverTarget(null);
            RelaxRightHand();
        }
    }

    private void ClickUITarget(GameObject target, RaycastResult hit)
    {
        _pointerEventData.pointerPressRaycast = hit;
        ExecuteEvents.Execute(target, _pointerEventData, ExecuteEvents.pointerClickHandler);
        OnUIClicked?.Invoke(target);
        Log($"Right hand clicked: {target.name}");
    }

    private void SetUIHoverTarget(GameObject target)
    {
        if (_currentHoverTarget == target) return;
        _currentHoverTarget = target;
        OnUITargetChanged?.Invoke(target);
    }

    private void RelaxRightHand()
    {
        _rightHandHasTarget = false;

        if (rightHandIK != null)
            rightHandIK.weight = Mathf.MoveTowards(rightHandIK.weight, 0f, Time.unscaledDeltaTime * rightHandFollowLerp);

        if (rightHandIKTarget != null)
        {
            rightHandIKTarget.localPosition = Vector3.Lerp(
                rightHandIKTarget.localPosition, _rightHandRestLocalPos, Time.unscaledDeltaTime * rightHandFollowLerp);
            rightHandIKTarget.localRotation = Quaternion.Slerp(
                rightHandIKTarget.localRotation, _rightHandRestLocalRot, Time.unscaledDeltaTime * rightHandFollowLerp);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Minimap
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Positions the minimap camera directly above minimapFollowTarget every frame.
    /// If a MinimapFloorZone is currently active (player standing in one), that
    /// zone's height/culling mask/zoom override this script's own defaults — that's
    /// how multi-floor buildings avoid floors above/below bleeding into each other's
    /// top-down view. Height and zoom blend smoothly across the change; culling
    /// mask swaps instantly (masks can't be lerped, and a one-frame layer swap
    /// isn't visually noticeable the way a camera snap would be).
    /// The camera renders into minimapRenderTexture (assigned to it directly in the
    /// Inspector, not here) — that texture is what the tablet screen's material
    /// reads from. This script never touches the material/Renderer.
    /// </summary>
    private void UpdateMinimap()
    {
        if (minimapCamera == null || minimapFollowTarget == null) return;

        MinimapFloorZone zone = MinimapFloorRegistry.Instance != null
            ? MinimapFloorRegistry.Instance.ActiveZone
            : null;

        float targetHeight = zone != null ? zone.floorHeight : minimapHeight;
        float targetZoom   = zone != null && zone.floorZoomOverride > 0f ? zone.floorZoomOverride : minimapZoom;
        LayerMask targetMask = zone != null ? zone.floorCullingMask : minimapDefaultCullingMask;

        float blend = minimapFloorBlendSpeed * Time.unscaledDeltaTime;
        _currentMinimapHeight = Mathf.Lerp(_currentMinimapHeight, targetHeight, blend);
        _currentMinimapZoom   = Mathf.Lerp(_currentMinimapZoom, targetZoom, blend);

        Vector3 pos = minimapFollowTarget.position + Vector3.up * _currentMinimapHeight;
        minimapCamera.transform.position = pos;

        float yaw = minimapRotatesWithPlayer ? minimapFollowTarget.eulerAngles.y : 0f;
        minimapCamera.transform.rotation = Quaternion.Euler(90f, yaw, 0f);

        if (minimapCamera.orthographic)
            minimapCamera.orthographicSize = _currentMinimapZoom;

        minimapCamera.cullingMask = targetMask;
    }

    private void SetMinimapCameraEnabled(bool enabled)
    {
        if (minimapCamera != null) minimapCamera.enabled = enabled;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Validation / Debug
    // ─────────────────────────────────────────────────────────────────────────

    private void ValidateSetup()
    {
        if (tabletVisuals == null)  LogWarning("tabletVisuals not assigned — tablet will never show.");
        if (tabletAnimator == null) LogWarning("tabletAnimator not assigned — open/close will skip animation entirely.");
        if (handBone == null)       LogWarning("handBone not assigned — left-hand tracking disabled.");
        if (indexFingerBone == null) LogWarning("indexFingerBone not assigned — left-hand click animation disabled.");
        if (rightHandIK == null || rightHandIKTarget == null)
            LogWarning("rightHandIK and/or rightHandIKTarget not assigned — right-hand world-UI interaction disabled.");
        if (activeWeapon == null && weaponSwitcher == null)
            LogWarning("Neither activeWeapon nor weaponSwitcher assigned — open-gate will never block (tablet can open during fire/reload/etc).");
        if (minimapCamera != null && minimapRenderTexture == null)
            LogWarning("minimapCamera assigned but minimapRenderTexture is not — assign the same RenderTexture to both this field and the minimap camera's Target Texture, and to the tablet screen material.");
        if (minimapCamera != null && minimapCamera.targetTexture != minimapRenderTexture)
            LogWarning("minimapCamera.targetTexture does not match minimapRenderTexture — set the camera's Target Texture in the Inspector to the same asset.");
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
        if (showHandGizmo && handBone != null)
        {
            Vector3 center = handBone.parent != null
                ? handBone.parent.TransformPoint(_initialHandLocalPos == Vector3.zero ? handBone.localPosition : _initialHandLocalPos)
                : handBone.position;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(center, new Vector3(xLimits.y - xLimits.x, yLimits.y - yLimits.x, 0.01f));
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(handBone.position, 0.01f);
        }

        if (interactionCamera != null)
        {
            Gizmos.color = _rightHandHasTarget ? Color.green : new Color(1f, 0.4f, 0.4f, 0.6f);
            Gizmos.DrawRay(interactionCamera.transform.position, interactionCamera.transform.forward * interactionRange);
        }

        if (rightHandIKTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(rightHandIKTarget.position, 0.02f);
        }

        if (minimapFollowTarget != null)
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            Vector3 top = minimapFollowTarget.position + Vector3.up * minimapHeight;
            Gizmos.DrawLine(minimapFollowTarget.position, top);
            Gizmos.DrawWireSphere(top, 0.3f);
            // Approximate ground footprint of the orthographic view
            Gizmos.DrawWireCube(minimapFollowTarget.position + Vector3.up * 0.05f,
                new Vector3(minimapZoom * 2f, 0.05f, minimapZoom * 2f));
        }
    }
#endif

    #endregion
}