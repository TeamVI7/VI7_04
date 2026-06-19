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

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        // Dừng camera khi UI mở — không đụng script player
        if (ComputerInteraction.UIOpen) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensX;
        float mouseY = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * sensY;

        yRotation += mouseX;
        xRotation -= mouseY;
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