using System;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// World-space canvas mission-list panel. Tab toggles open/closed.
/// Open/close is a DOTween scale punch on panelVisuals — no Animator dependency.
/// Disables PlayerCam and other scripts while open. Also drives the minimap
/// camera used by MissionListUI / MinimapRoomHighlight while the panel is open.
/// Read-only panel — no world-space canvas click handling needed.
/// </summary>
public class MissionPanelController : MonoBehaviour
{
    [Header("Core References")]
    public GameObject panelVisuals;

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
    public float animationDuration = 0.3f;
    public Ease openEase  = Ease.OutBack;
    public Ease closeEase = Ease.InBack;

    [Header("Minimap")]
    public Camera minimapCamera;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    public event Action OnPanelOpenStart;
    public event Action OnPanelOpened;
    public event Action OnPanelCloseStart;
    public event Action OnPanelClosed;

    public enum PanelState { Closed, Opening, Open, Closing }

    public PanelState CurrentState { get; private set; } = PanelState.Closed;
    public bool IsOpen => CurrentState == PanelState.Open || CurrentState == PanelState.Opening;

    /// <summary>True while any MissionPanelController instance is open/opening — checked by PauseMenuController.</summary>
    public static bool AnyOpen { get; private set; }

    private Tween _scaleTween;

    /// <summary>
    /// AnyOpen gates WeaponsController.IsInputBlocked and the pause toggle. It's static, so
    /// a scene load (or destroying this panel) while it was open used to strand it at true —
    /// permanently blocking firing and Escape in the next scene with no way to clear it.
    /// </summary>
    private void OnDestroy()
    {
        _scaleTween?.Kill();
        if (AnyOpen && IsOpen) AnyOpen = false;
    }

    private void Start()
    {
        if (panelVisuals != null)
        {
            panelVisuals.transform.localScale = Vector3.zero;
            panelVisuals.SetActive(false);
        }
        if (minimapCamera != null) minimapCamera.enabled = false;
        if (playerCam == null) playerCam = GetComponentInParent<PlayerCam>();
    }

    private void Update()
    {
        HandleToggleInput();

        if (IsOpen && PauseMenuController.IsPaused)
            StartClose();
    }

    private void HandleToggleInput()
    {
        if (!Input.GetKeyDown(toggleKey)) return;
        if (PauseMenuController.IsPaused) return;

        bool transitioning = CurrentState == PanelState.Opening || CurrentState == PanelState.Closing;
        if (blockToggleDuringTransition && transitioning) return;

        if      (CurrentState == PanelState.Closed) StartOpen();
        else if (CurrentState == PanelState.Open)   StartClose();
        else if (!blockToggleDuringTransition)
        {
            if (CurrentState == PanelState.Opening) StartClose();
            else                                    StartOpen();
        }
    }

    public void StartOpen()
    {
        _scaleTween?.Kill();
        SetState(PanelState.Opening);
        OnPanelOpenStart?.Invoke();

        if (panelVisuals != null)
        {
            panelVisuals.SetActive(true);
            panelVisuals.transform.localScale = Vector3.zero;
        }
        if (playerUI != null)     playerUI.SetActive(false);
        if (weaponHolder != null) weaponHolder.SetActive(false);

        SetPlayerScriptsEnabled(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        if (minimapCamera != null) minimapCamera.enabled = true;

        if (panelVisuals != null)
        {
            _scaleTween = panelVisuals.transform
                .DOScale(Vector3.one, animationDuration)
                .SetEase(openEase)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    SetState(PanelState.Open);
                    OnPanelOpened?.Invoke();
                    Log("Open complete.");
                });
        }
        else
        {
            SetState(PanelState.Open);
            OnPanelOpened?.Invoke();
        }

        Log("Open started.");
    }

    public void StartClose()
    {
        _scaleTween?.Kill();
        SetState(PanelState.Closing);
        OnPanelCloseStart?.Invoke();

        SetPlayerScriptsEnabled(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        if (minimapCamera != null) minimapCamera.enabled = false;

        if (panelVisuals != null)
        {
            _scaleTween = panelVisuals.transform
                .DOScale(Vector3.zero, animationDuration)
                .SetEase(closeEase)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    panelVisuals.SetActive(false);
                    FinishClose();
                });
        }
        else
        {
            FinishClose();
        }

        Log("Close started.");
    }

    private void FinishClose()
    {
        if (playerUI != null)     playerUI.SetActive(true);
        if (weaponHolder != null) weaponHolder.SetActive(true);

        SetState(PanelState.Closed);
        OnPanelClosed?.Invoke();
        Log("Close complete.");
    }

    private void SetPlayerScriptsEnabled(bool enabled)
    {
        if (playerCam != null) playerCam.enabled = enabled;
        if (playerScriptsToDisable == null) return;
        foreach (var s in playerScriptsToDisable)
            if (s != null) s.enabled = enabled;
    }

    private void SetState(PanelState next)
    {
        if (CurrentState == next) return;
        Log($"State: {CurrentState} -> {next}");
        CurrentState = next;
        AnyOpen = CurrentState == PanelState.Open || CurrentState == PanelState.Opening;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[MissionPanelController] {msg}", this);
    }
}
