using System.Collections;
using UnityEngine;

public class TabletController : MonoBehaviour
{
    [Header("Core References")]
    [Tooltip("The parent object containing the tablet mesh and animator.")]
    public GameObject tabletVisuals; 
    public Animator tabletAnimator;
    public GameObject playerUI;
    [Tooltip("Assign the WeaponPivot or WeaponHolder here to hide all guns and block shooting.")]
    public GameObject weaponHolder; 

    [Header("Player Control Integration")]
    [Tooltip("Drag the PlayerCam and PlayerMovement scripts here to disable them while the tablet is open.")]
    public Behaviour[] playerScriptsToDisable;

    [Header("Animation Settings")]
    [Tooltip("Time in seconds to wait before re-enabling guns and UI.")]
    public float animationDuration = 0.5f; 

    [Header("Hand Movement Settings")]
    public Transform handBone;
    public float handMoveSpeed = 0.01f;
    public Vector2 xLimits = new Vector2(-0.2f, 0.2f);
    public Vector2 yLimits = new Vector2(-0.2f, 0.2f);

    [Header("Finger Click Settings")]
    public Transform indexFingerBone;
    public Vector3 clickRotationOffset = new Vector3(45f, 0f, 0f);

    private bool isTabletOpen = false;
    private Vector3 initialHandLocalPos;
    private Vector3 initialFingerRotation;
    private Coroutine closeRoutine;

    void Start()
    {
        if (tabletAnimator != null)
            tabletAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;

        if (handBone != null)
            initialHandLocalPos = handBone.localPosition;

        if (indexFingerBone != null)
            initialFingerRotation = indexFingerBone.localEulerAngles;

        if (tabletVisuals != null)
            tabletVisuals.SetActive(false);
    }

    void Update()
    {
        HandleTabletToggle();

        if (isTabletOpen)
        {
            HandleHandMovement();
            HandleFingerClick();
        }
    }

    private void HandleTabletToggle()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            isTabletOpen = !isTabletOpen;

            if (closeRoutine != null)
            {
                StopCoroutine(closeRoutine);
                closeRoutine = null;
            }

            if (isTabletOpen)
            {
                OpenTablet();
            }
            else
            {
                closeRoutine = StartCoroutine(CloseTabletRoutine());
            }
        }
    }

    private void OpenTablet()
    {
        tabletVisuals.SetActive(true);
        Time.timeScale = 0f;
        tabletAnimator.SetFloat("TabletSpeed", 1f);

        if (playerUI != null) 
            playerUI.SetActive(false);

        if (weaponHolder != null) 
            weaponHolder.SetActive(false);

        // Disable camera and movement scripts instead of using UIOpen
        foreach (Behaviour script in playerScriptsToDisable)
        {
            if (script != null) script.enabled = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private IEnumerator CloseTabletRoutine()
    {
        Time.timeScale = 1f; 
        tabletAnimator.SetFloat("TabletSpeed", -1f);

        // Re-enable camera and movement scripts immediately 
        foreach (Behaviour script in playerScriptsToDisable)
        {
            if (script != null) script.enabled = true;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        yield return new WaitForSecondsRealtime(animationDuration);

        tabletVisuals.SetActive(false);

        if (playerUI != null) 
            playerUI.SetActive(true);

        if (weaponHolder != null) 
            weaponHolder.SetActive(true);
    }

    private void HandleHandMovement()
    {
        if (handBone == null) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * handMoveSpeed;
        float mouseY = Input.GetAxisRaw("Mouse Y") * handMoveSpeed;

        Vector3 targetPos = handBone.localPosition + new Vector3(mouseX, mouseY, 0f);

        targetPos.x = Mathf.Clamp(targetPos.x, initialHandLocalPos.x + xLimits.x, initialHandLocalPos.x + xLimits.y);
        targetPos.y = Mathf.Clamp(targetPos.y, initialHandLocalPos.y + yLimits.x, initialHandLocalPos.y + yLimits.y);

        handBone.localPosition = targetPos;
    }

    private void HandleFingerClick()
    {
        if (indexFingerBone == null) return;

        if (Input.GetMouseButton(0))
        {
            indexFingerBone.localEulerAngles = initialFingerRotation + clickRotationOffset;
        }
        else
        {
            indexFingerBone.localEulerAngles = initialFingerRotation;
        }
    }
}