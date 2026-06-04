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
        // get mouse input
        float mouseX = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensX;
        float mouseY = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * sensY;
        float tilt = -Input.GetAxisRaw("Horizontal") * moveTiltAmount;

        yRotation += mouseX;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        // rotate cam and orientation
        camHolder.rotation = Quaternion.Euler(xRotation, yRotation, 0);
        orientation.rotation = Quaternion.Euler(0, yRotation, 0);
        transform.DOLocalRotate(new Vector3(0, 0, tilt), 0.15f);
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
        float targetY = sliding ? -1f : 0f; // was -0.5f, lower = more crouch feel
        camHolder.DOLocalMoveY(targetY, 0.15f);
        DoTilt(sliding ? 3f : 0f);
        DoFov(sliding ? 90f : 85f);
    }
}