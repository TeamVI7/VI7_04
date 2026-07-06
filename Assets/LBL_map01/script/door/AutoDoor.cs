using System.Collections;
using UnityEngine;

/// <summary>
/// Player vào trigger → cửa mở lên. Sau một khoảng thời gian sẽ tự đóng lại.
/// Nếu player còn đứng trong trigger, cửa sẽ đợi đến khi player rời đi rồi mới đóng.
/// </summary>
public class AutoDoor : MonoBehaviour
{
    [Header("Cài đặt")]
    [Tooltip("Để 0 → tự tính theo chiều cao mesh")]
    public float slideDistance = 0f;
    public float openDuration  = 1.0f;
    public float closeDuration = 1.0f;

    [Header("Tự đóng")]
    [Tooltip("Bật/tắt tính năng tự đóng cửa")]
    public bool autoClose = true;
    [Tooltip("Thời gian (giây) cửa mở trước khi tự đóng, tính từ lúc mở xong")]
    public float closeDelay = 3f;

    [Header("Âm thanh")]
    [Tooltip("AudioClip phát khi cửa bắt đầu mở")]
    public AudioClip openSound;
    [Tooltip("AudioClip phát khi cửa bắt đầu đóng")]
    public AudioClip closeSound;
    [Tooltip("Để trống → tự tạo AudioSource trên GameObject này")]
    public AudioSource audioSource;

    private Vector3 _closedPos;
    private Vector3 _openPos;
    private bool    _isOpen        = false;
    private bool    _playerInside  = false;
    private Coroutine _doorRoutine;

    private void Start()
    {
        if (slideDistance <= 0f)
        {
            Renderer r = GetComponent<Renderer>();
            slideDistance = r != null ? r.bounds.size.y : 3f;
        }

        _closedPos = transform.position;
        _openPos   = _closedPos + Vector3.up * slideDistance;

        // Tự tạo AudioSource nếu chưa gán
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInside = true;

        if (_isOpen) return; // đã mở rồi thì thôi

        if (_doorRoutine != null) StopCoroutine(_doorRoutine);
        _doorRoutine = StartCoroutine(OpenThenClose());
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInside = false;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    private IEnumerator OpenThenClose()
    {
        // --- MỞ CỬA ---
        yield return MoveDoor(_closedPos, _openPos, openDuration, openSound);
        _isOpen = true;

        if (!autoClose) yield break;

        // --- CHỜ ---
        float waited = 0f;
        while (waited < closeDelay)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        // Nếu player vẫn còn đứng trong trigger, đợi đến khi họ rời đi
        while (_playerInside)
        {
            yield return null;
        }

        // --- ĐÓNG CỬA ---
        yield return MoveDoor(transform.position, _closedPos, closeDuration, closeSound);
        _isOpen = false;
        _doorRoutine = null;
    }

    private IEnumerator MoveDoor(Vector3 from, Vector3 to, float duration, AudioClip sound)
    {
        PlaySound(sound);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            transform.position = Vector3.Lerp(from, to, t);
            yield return null;
        }
        transform.position = to;
    }

    private void OnDrawGizmosSelected()
    {
        float dist = slideDistance > 0f ? slideDistance : 3f;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * dist);
    }
}