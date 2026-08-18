using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class PlayerCam : MonoBehaviour
{
    public float sensX;
    public float sensY;

    public Transform orientation;
    public Transform camHolder;
    public float moveTiltAmount = 2f;
    float xRotation;
    float yRotation;
    public bool disableMoveTilt;
    private float wallTiltZ = 0f;

    [Header("Recoil (Camera Kick — see CameraRecoil.cs)")]
    [Tooltip("Degrees/sec the recoil kick settles back at. Overwritten per-shot by " +
             "CameraRecoil if the firing weapon has a RecoilProfile.")]
    public float recoilRecoverySpeed = 6f;
    private float _recoilPitchOffset; // always >= 0, how much pitch kick is still "owed" back
    private float _recoilYawOffset;   // signed, how much yaw kick is still "owed" back

    private Camera _cam;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _cam = GetComponent<Camera>();

        float savedSens = PlayerPrefs.GetFloat("MouseSensitivity", sensX);
        sensX = savedSens;
        sensY = savedSens;
    }

    private void Update()
    {
        // FIX: was checking UIOpen only. PlayerMovement blocks on both flags, so during a
        // UIInputBlocker minigame the body stopped moving but the camera still turned.
        if (ComputerInteraction.UIOpen || UIInputBlocker.IsBlocking) return;
        float mouseX = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensX;
        float mouseY = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * sensY;

        yRotation += mouseX;
        xRotation -= mouseY;

        TickRecoilRecovery();

        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        camHolder.rotation   = Quaternion.Euler(xRotation, yRotation, 0);
        orientation.rotation = Quaternion.Euler(0, yRotation, 0);

        if (!disableMoveTilt)
        {
            float moveTilt = -Input.GetAxisRaw("Horizontal") * moveTiltAmount;
            float combined = wallTiltZ + moveTilt;
            transform.DOLocalRotate(new Vector3(0, 0, combined), 0.15f);
        }
        else
        {
            transform.DOLocalRotate(new Vector3(0, 0, wallTiltZ), 0.15f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    #region Recoil (Camera Kick)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Kicks the actual camera aim — not just a visual weapon-mesh offset.
    /// Called by CameraRecoil on every shot. Positive pitchKick tips the view up;
    /// yawKick is signed (+right/-left) so callers can pass randomised values.
    /// </summary>
    public void AddRecoilKick(float pitchKick, float yawKick)
    {
        xRotation -= pitchKick;
        _recoilPitchOffset += pitchKick;

        yRotation += yawKick;
        _recoilYawOffset += yawKick;
    }

    /// <summary>Instantly clears any in-flight recoil kick — call on weapon switch/death if needed.</summary>
    public void ResetRecoil()
    {
        xRotation += _recoilPitchOffset;
        yRotation -= _recoilYawOffset;
        _recoilPitchOffset = 0f;
        _recoilYawOffset   = 0f;
    }

    private void TickRecoilRecovery()
    {
        float step = recoilRecoverySpeed * Time.deltaTime;

        float prevPitch = _recoilPitchOffset;
        _recoilPitchOffset = Mathf.MoveTowards(_recoilPitchOffset, 0f, step);
        xRotation += (prevPitch - _recoilPitchOffset);

        float prevYaw = _recoilYawOffset;
        _recoilYawOffset = Mathf.MoveTowards(_recoilYawOffset, 0f, step);
        yRotation -= (prevYaw - _recoilYawOffset);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Field of View (layered)
    // ─────────────────────────────────────────────────────────────────────────

    // Dash, wall run and slide all used to call DoFov() with their own private
    // "normal FOV" constant (85 / 80 / 85) on release. Whichever ended LAST won, so
    // ending a slide mid-dash snapped the view back to 85 while the dash was still
    // running, and a wall run ending during a dash pulled it to 80.
    //
    // Now each system claims a LAYER instead. The highest active layer wins, and
    // releasing a layer falls back to whatever is still claimed — or baseFov if
    // nothing is. No system needs to know what "normal" is, so they can't disagree.

    /// <summary>Higher value wins when several layers are active at once.</summary>
    public enum FovLayer
    {
        Slide   = 0,
        WallRun = 1,
        Dash    = 2,
    }

    [Header("Field of View")]
    [Tooltip("FOV when no ability is claiming a layer. Single source of truth for 'normal'.")]
    public float baseFov     = 85f;
    [Tooltip("FOV applied while sliding — claims the Slide layer.")]
    public float slideFov    = 90f;
    public float fovTweenTime = 0.25f;

    private readonly float[] _fovLayers    = new float[3];
    private readonly bool[]  _fovLayerHeld = new bool[3];
    private float            _appliedFov   = float.NaN;

    /// <summary>Claim a FOV layer. Overwrites that layer's previous request.</summary>
    public void SetFov(FovLayer layer, float fov)
    {
        _fovLayers[(int)layer]    = fov;
        _fovLayerHeld[(int)layer] = true;
        ApplyResolvedFov();
    }

    /// <summary>Release a FOV layer. Falls back to the next highest claim, or baseFov.</summary>
    public void ClearFov(FovLayer layer)
    {
        _fovLayerHeld[(int)layer] = false;
        ApplyResolvedFov();
    }

    private void ApplyResolvedFov()
    {
        float target = baseFov;
        for (int i = _fovLayers.Length - 1; i >= 0; i--)
        {
            if (!_fovLayerHeld[i]) continue;
            target = _fovLayers[i];
            break;
        }

        // Re-tweening to a value already in flight restarts the ease and stutters.
        if (Mathf.Approximately(target, _appliedFov)) return;
        _appliedFov = target;

        // Called from ability scripts that may run before this component's Start(),
        // so resolve the camera lazily rather than assuming _cam is set.
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam != null) _cam.DOFieldOfView(target, fovTweenTime);
    }

    #endregion

    public void DoTilt(float zTilt)
    {
        wallTiltZ = zTilt;
    }

    public void DoSlideOffset(bool sliding)
    {
        float targetY = sliding ? -0.5f : 0f;
        camHolder.DOLocalMoveY(targetY, 0.15f);
        DoTilt(sliding ? 3f : 0f);

        if (sliding) SetFov(FovLayer.Slide, slideFov);
        else         ClearFov(FovLayer.Slide);
    }
}