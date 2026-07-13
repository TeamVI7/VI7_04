using System.Collections;
using UnityEngine;
using TMPro;

public class LockedDoor : MonoBehaviour
{
    [Header("Puzzle cần hoàn thành")]
    public WirePuzzleManager wirePuzzleManager;

    [Header("Cài đặt cửa")]
    public float slideDistance = 0f;
    public float openDuration  = 1.0f;

    [Header("Âm thanh")]
    public AudioClip openSound;
    public AudioClip lockedSound;
    public AudioSource audioSource;
    [Header("UI gợi ý")]
    [Tooltip("Kéo TMP Text vào đây — script tự ẩn/hiện.")]
    public TextMeshProUGUI hintLabel;
    [Tooltip("Thời gian hiện hint (giây) rồi tự ẩn.")]
    public float hintDisplayTime = 2.5f;
    private Vector3   _closedPos;
    private Vector3   _openPos;
    private bool      _unlocked = false;
    private bool      _opened   = false;
    private Coroutine _hintCoroutine;
    private void Start()
    {
        if (slideDistance <= 0f)
        {
            Renderer r = GetComponent<Renderer>();
            slideDistance = r != null ? r.bounds.size.y : 3f;
        }
        _closedPos = transform.position;
        _openPos   = _closedPos + Vector3.up * slideDistance;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        if (hintLabel != null)
            hintLabel.enabled = false;
        if (wirePuzzleManager != null)
            wirePuzzleManager.OnPuzzleCompleted += OnWirePuzzleCompleted;
        else
            Debug.LogWarning($"[LockedDoor] '{name}': Chưa gán WirePuzzleManager!", this);
    }
    private void OnDestroy()
    {
        if (wirePuzzleManager != null)
            wirePuzzleManager.OnPuzzleCompleted -= OnWirePuzzleCompleted;
    }

    private void OnWirePuzzleCompleted()
    {
        _unlocked = true;
        Debug.Log($"[LockedDoor] '{name}' đã được mở khoá!");
    }
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (_unlocked)
            TryOpen();
        else
        {
            PlaySfx(lockedSound);
            ShowHint();
        }
    }
    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (_unlocked && !_opened)
            TryOpen();
    }
    private void TryOpen()
    {
        if (_opened) return;
        _opened = true;
        HideHint();

        PlaySfx(openSound);
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
    private void ShowHint()
    {
        if (hintLabel == null) return;

        if (_hintCoroutine != null)
            StopCoroutine(_hintCoroutine);

        _hintCoroutine = StartCoroutine(HintRoutine());
    }

    private void HideHint()
    {
        if (_hintCoroutine != null)
        {
            StopCoroutine(_hintCoroutine);
            _hintCoroutine = null;
        }

        if (hintLabel != null)
            hintLabel.enabled = false;
    }

    private IEnumerator HintRoutine()
    {
        hintLabel.enabled = true;
        yield return new WaitForSeconds(hintDisplayTime);
        hintLabel.enabled = false;
        _hintCoroutine = null;
    }
    private void PlaySfx(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }
    private void OnDrawGizmosSelected()
    {
        float dist = slideDistance > 0f ? slideDistance : 3f;
        Gizmos.color = _unlocked ? Color.green : Color.red;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * dist);
    }
}