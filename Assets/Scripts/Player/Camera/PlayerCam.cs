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

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (ComputerInteraction.UIOpen) return;
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

    public void DoFov(float endValue)
    {
        GetComponent<Camera>().DOFieldOfView(endValue, 0.25f);
    }

    public void DoTilt(float zTilt)
    {
        wallTiltZ = zTilt;
    }

    public void DoSlideOffset(bool sliding)
    {
        float targetY = sliding ? -0.5f : 0f;
        camHolder.DOLocalMoveY(targetY, 0.15f);
        DoTilt(sliding ? 3f : 0f);
        DoFov(sliding ? 90f : 85f);
    }
}