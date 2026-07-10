using UnityEngine;
using System.Collections;

public class SecretDoorController : MonoBehaviour
{
    [Header("Cài đặt cửa")]
    public Transform door;                 // Object cửa cần di chuyển/xoay
    public bool useRotation = true;        // true = xoay bản lề, false = trượt

    [Header("Xoay (nếu useRotation = true)")]
    public Vector3 openRotationOffset = new Vector3(0, 90, 0);

    [Header("Trượt (nếu useRotation = false)")]
    public Vector3 openPositionOffset = new Vector3(0, 0, -2f);

    public float openDuration = 1.5f;
    public AnimationCurve openCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Âm thanh & VFX")]
    public AudioSource audioSource;
    public AudioClip doorOpenSound;
    public ParticleSystem dustVFX;         // VFX bụi/mảnh vỡ khi cửa mở

    private bool isOpen = false;
    private Vector3 closedPos, openPos;
    private Quaternion closedRot, openRot;

    void Start()
    {
        if (door == null) door = transform;

        closedPos = door.localPosition;
        openPos = closedPos + openPositionOffset;

        closedRot = door.localRotation;
        openRot = Quaternion.Euler(door.localEulerAngles + openRotationOffset);
    }

    public void OpenDoor()
    {
        if (isOpen) return;
        isOpen = true;

        if (audioSource != null && doorOpenSound != null)
            audioSource.PlayOneShot(doorOpenSound);

        if (dustVFX != null)
            dustVFX.Play();

        StopAllCoroutines();
        StartCoroutine(AnimateDoor(true));
    }

    IEnumerator AnimateDoor(bool opening)
    {
        float t = 0f;
        Vector3 startPos = door.localPosition;
        Vector3 targetPos = opening ? openPos : closedPos;
        Quaternion startRot = door.localRotation;
        Quaternion targetRot = opening ? openRot : closedRot;

        while (t < openDuration)
        {
            t += Time.deltaTime;
            float progress = openCurve.Evaluate(t / openDuration);

            if (useRotation)
                door.localRotation = Quaternion.Slerp(startRot, targetRot, progress);
            else
                door.localPosition = Vector3.Lerp(startPos, targetPos, progress);

            yield return null;
        }

        door.localRotation = targetRot;
        door.localPosition = targetPos;
    }
}