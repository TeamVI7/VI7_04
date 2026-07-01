using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// World-space canvas tablet UI. Tab toggles open/closed.
/// Closing plays the open animation backward via manual normalizedTime scrub
/// (Animator.speed can't go negative, so we drive it by hand).
/// Disables PlayerCam and other scripts while open.
/// Click detection: camera-forward raycast against EventSystem raycasters —
/// look at a button, click. No free cursor needed.
/// </summary>
public class TabletController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Core References")]
    public GameObject tabletVisuals;
    public Animator   tabletAnimator;

    [Header("UI / Gun Suppression")]
    public GameObject playerUI;
    public GameObject weaponHolder;

    [Header("Player Control Suppression")]
    [Tooltip("Auto-found via GetComponentInParent if left empty.")]
    public PlayerCam playerCam;
    public Behaviour[] playerScriptsToDisable;

    [Header("Toggle Input")]
    public KeyCode toggleKey = KeyCode.Tab;
    public bool blockToggleDuringTransition = true;

    [Header("Animation")]
    public float animationDuration = 0.5f;
    public float openSpeed = 1f;

    [Header("World-Space Canvas Interaction")]
    [Tooltip("Auto-found via Camera.main if left empty.")]
    public Camera interactionCamera;
    [Tooltip("EventSystem — auto-found if left empty.")]
    public EventSystem eventSystem;
    public float interactionRange = 1.5f;
    public float fingertipOffset  = 0.01f;

    [Header("Minimap")]
    public Camera minimapCamera;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    public event Action             OnTabletOpenStart;
    public event Action             OnTabletOpened;
    public event Action             OnTabletCloseStart;
    public event Action             OnTabletClosed;
    public event Action<string>     OnTabletOpenBlocked;
    public event Action<GameObject> OnUITargetChanged;
    public event Action<GameObject> OnUIClicked;

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

    private Coroutine  _transitionCoroutine;
    private float      _lastNormalizedTime;
    private int        _cachedStateHash;

    private GameObject            _currentHoverTarget;
    private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>(8);
    private PointerEventData      _pointerEventData;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (tabletAnimator != null)
        {
            tabletAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            tabletAnimator.speed      = 0f;
            _cachedStateHash          = tabletAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        }

        if (tabletVisuals != null) tabletVisuals.SetActive(false);
        if (minimapCamera != null) minimapCamera.enabled = false;
        if (playerCam == null)         playerCam         = GetComponentInParent<PlayerCam>();
        if (interactionCamera == null) interactionCamera = Camera.main;
        if (eventSystem == null)       eventSystem       = EventSystem.current;
        if (eventSystem != null)       _pointerEventData = new PointerEventData(eventSystem);
    }

    private void Update()
    {
        HandleToggleInput();
    }

    private void LateUpdate()
    {
        if (CurrentState == TabletState.Open)
            HandleCanvasInteraction();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Toggle / Open / Close
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleToggleInput()
    {
        if (!Input.GetKeyDown(toggleKey)) return;

        bool transitioning = CurrentState == TabletState.Opening || CurrentState == TabletState.Closing;
        if (blockToggleDuringTransition && transitioning) return;

        if      (CurrentState == TabletState.Closed)  StartOpen();
        else if (CurrentState == TabletState.Open)    StartClose();
        else if (!blockToggleDuringTransition)
        {
            StopCoroutineIfRunning();
            if (CurrentState == TabletState.Opening) StartClose();
            else                                     StartOpen();
        }
    }

    public void StartOpen()
    {
        StopCoroutineIfRunning();
        SetState(TabletState.Opening);
        OnTabletOpenStart?.Invoke();

        if (tabletVisuals != null)  tabletVisuals.SetActive(true);
        if (tabletAnimator != null) tabletAnimator.speed = 0f;
        if (playerUI != null)       playerUI.SetActive(false);
        if (weaponHolder != null)   weaponHolder.SetActive(false);

        SetPlayerScriptsEnabled(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        if (minimapCamera != null) minimapCamera.enabled = true;

        _transitionCoroutine = StartCoroutine(Co_ScrubAnimation(forward: true));
        Log("Open started.");
    }

    public void StartClose()
    {
        StopCoroutineIfRunning();
        SetState(TabletState.Closing);
        OnTabletCloseStart?.Invoke();

        if (tabletAnimator != null) tabletAnimator.speed = 0f;

        SetPlayerScriptsEnabled(true);
        SetUIHoverTarget(null);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        if (minimapCamera != null) minimapCamera.enabled = false;

        _transitionCoroutine = StartCoroutine(Co_ScrubAnimation(forward: false));
        Log("Close started.");
    }

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
        if (_transitionCoroutine == null) return;
        StopCoroutine(_transitionCoroutine);
        _transitionCoroutine = null;
    }

    private void SetPlayerScriptsEnabled(bool enabled)
    {
        if (playerCam != null) playerCam.enabled = enabled;
        if (playerScriptsToDisable == null) return;
        foreach (var s in playerScriptsToDisable)
            if (s != null) s.enabled = enabled;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region World-Space Canvas Interaction
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleCanvasInteraction()
    {
        if (interactionCamera == null || eventSystem == null) return;

        _pointerEventData.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        _raycastResults.Clear();
        eventSystem.RaycastAll(_pointerEventData, _raycastResults);

        RaycastResult? hit = null;
        for (int i = 0; i < _raycastResults.Count; i++)
        {
            var r = _raycastResults[i];
            if (r.distance > interactionRange) continue;
            Canvas c = r.gameObject.GetComponentInParent<Canvas>();
            if (c == null || c.renderMode != RenderMode.WorldSpace) continue;
            hit = r;
            break;
        }

        if (hit.HasValue)
        {
            SetUIHoverTarget(hit.Value.gameObject);
            if (Input.GetMouseButtonDown(0))
            {
                _pointerEventData.pointerPressRaycast = hit.Value;
                ExecuteEvents.Execute(hit.Value.gameObject, _pointerEventData, ExecuteEvents.pointerClickHandler);
                OnUIClicked?.Invoke(hit.Value.gameObject);
                Log($"Clicked: {hit.Value.gameObject.name}");
            }
        }
        else
        {
            SetUIHoverTarget(null);
        }
    }

    private void SetUIHoverTarget(GameObject target)
    {
        if (_currentHoverTarget == target) return;
        _currentHoverTarget = target;
        OnUITargetChanged?.Invoke(target);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[TabletController] {msg}", this);
    }

    #endregion
}