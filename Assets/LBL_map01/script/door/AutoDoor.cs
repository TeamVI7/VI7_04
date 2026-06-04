using System.Collections;
using UnityEngine;

/// <summary>
/// Player vào trigger → cửa mở lên, không đóng lại.
/// </summary>
public class AutoDoor : MonoBehaviour
{
    [Header("Cài đặt")]
    [Tooltip("Để 0 → tự tính theo chiều cao mesh")]
    public float slideDistance = 0f;
    public float openDuration  = 1.0f;

    private Vector3 _closedPos;
    private Vector3 _openPos;
    private bool    _opened = false;

    private void Start()
    {
        if (slideDistance <= 0f)
        {
            Renderer r = GetComponent<Renderer>();
            slideDistance = r != null ? r.bounds.size.y : 3f;
        }

        _closedPos = transform.position;
        _openPos   = _closedPos + Vector3.up * slideDistance;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_opened) return;
        if (!other.CompareTag("Player")) return;
        _opened = true;
        StartCoroutine(OpenDoor());
    }

    private IEnumerator OpenDoor()
    {
        Vector3 start   = transform.position;
        float   elapsed = 0f;

        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / openDuration);
            transform.position = Vector3.Lerp(start, _openPos, t);
            yield return null;
        }

        transform.position = _openPos;
    }

    private void OnDrawGizmosSelected()
    {
        float dist = slideDistance > 0f ? slideDistance : 3f;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * dist);
    }
}