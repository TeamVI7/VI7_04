using System.Collections;
using UnityEngine;
 
/// <summary>
/// Điều khiển cửa trượt lên phía trên.
/// Gắn script này vào GameObject cửa (tạo bằng ProBuilder).
/// 
/// Cách dùng:
///   - Gắn vào GameObject cửa ProBuilder
///   - Gọi OpenDoor() từ MorseMinigameManager khi người chơi nhập đúng
/// </summary>
public class SlidingDoorController : MonoBehaviour
{
    [Header("Door Movement")]
    [Tooltip("Cửa trượt lên cao bao nhiêu đơn vị (Unity units). Thường bằng chiều cao cửa.")]
    public float slideDistance = 3f;
 
    [Tooltip("Thời gian cửa mở hoàn toàn (giây)")]
    public float openDuration = 1.2f;
 
    [Tooltip("Thời gian cửa đóng lại (giây). 0 = không tự đóng.")]
    public float closeDuration = 1.2f;
 
    [Tooltip("Tự động đóng cửa sau bao lâu (giây). 0 = không tự đóng.")]
    public float autoCloseDelay = 0f;
 
    [Header("Animation Curve")]
    [Tooltip("Curve điều chỉnh tốc độ mở cửa. Dùng EaseInOut để trông mượt hơn.")]
    public AnimationCurve openCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
 
    [Header("Sound (tuỳ chọn)")]
    public AudioSource audioSource;
    public AudioClip openSound;
    public AudioClip closeSound;
    public AudioClip lockedSound;
 
    [Header("State")]
    [Tooltip("Cửa có đang khoá không? Khi đúng mật khẩu sẽ tự mở khoá.")]
    public bool isLocked = true;
 
    [Tooltip("Trạng thái hiện tại (chỉ để xem, không chỉnh tay)")]
    [SerializeField] private DoorState currentState = DoorState.Closed;
 
    // ── Internal ────────────────────────────────────────────────
    private Vector3 _closedPosition;
    private Vector3 _openPosition;
    private Coroutine _moveCoroutine;
 
    public enum DoorState { Closed, Opening, Open, Closing }
 
    // ── Unity Lifecycle ─────────────────────────────────────────
    private void Awake()
    {
        // Lưu vị trí ban đầu (vị trí đóng)
        _closedPosition = transform.position;
        // Vị trí mở = dịch lên trên theo trục Y
        _openPosition   = _closedPosition + Vector3.up * slideDistance;
    }
 
    // ── Public API ───────────────────────────────────────────────
 
    /// <summary>
    /// Mở khoá và mở cửa. Gọi từ MorseMinigameManager khi đúng mật khẩu.
    /// </summary>
    public void UnlockAndOpen()
    {
        isLocked = false;
        OpenDoor();
    }
 
    /// <summary>
    /// Mở cửa (nếu chưa khoá).
    /// </summary>
    public void OpenDoor()
    {
        if (isLocked)
        {
            PlaySound(lockedSound);
            Debug.Log("[Door] Cửa đang bị khoá!");
            return;
        }
 
        if (currentState == DoorState.Open || currentState == DoorState.Opening) return;
 
        StopCurrentMove();
        _moveCoroutine = StartCoroutine(MoveDoor(_openPosition, openDuration, DoorState.Opening, DoorState.Open, openSound));
 
        if (autoCloseDelay > 0)
            StartCoroutine(AutoCloseRoutine());
    }
 
    /// <summary>
    /// Đóng cửa lại.
    /// </summary>
    public void CloseDoor()
    {
        if (currentState == DoorState.Closed || currentState == DoorState.Closing) return;
 
        StopCurrentMove();
        _moveCoroutine = StartCoroutine(MoveDoor(_closedPosition, closeDuration, DoorState.Closing, DoorState.Closed, closeSound));
    }
 
    /// <summary>
    /// Toggle mở/đóng.
    /// </summary>
    public void ToggleDoor()
    {
        if (currentState == DoorState.Open || currentState == DoorState.Opening)
            CloseDoor();
        else
            OpenDoor();
    }
 
    /// <summary>
    /// Đặt lại cửa về trạng thái khoá & đóng (dùng khi reset game).
    /// </summary>
    public void ResetDoor()
    {
        StopCurrentMove();
        isLocked = true;
        currentState = DoorState.Closed;
        transform.position = _closedPosition;
    }
 
    // ── Coroutines ────────────────────────────────────────────────
 
    private IEnumerator MoveDoor(Vector3 targetPos, float duration,
                                  DoorState duringState, DoorState endState,
                                  AudioClip sound)
    {
        currentState = duringState;
        PlaySound(sound);
 
        Vector3 startPos = transform.position;
        float elapsed = 0f;
 
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curvedT = openCurve.Evaluate(t);
            transform.position = Vector3.LerpUnclamped(startPos, targetPos, curvedT);
            yield return null;
        }
 
        transform.position = targetPos;
        currentState = endState;
        _moveCoroutine = null;
    }
 
    private IEnumerator AutoCloseRoutine()
    {
        yield return new WaitForSeconds(autoCloseDelay);
        CloseDoor();
    }
 
    // ── Helpers ───────────────────────────────────────────────────
 
    private void StopCurrentMove()
    {
        if (_moveCoroutine != null)
        {
            StopCoroutine(_moveCoroutine);
            _moveCoroutine = null;
        }
    }
 
    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }
 
    // ── Gizmo (hiện đường cửa sẽ trượt trong Scene view) ────────
    private void OnDrawGizmosSelected()
    {
        Vector3 closed = Application.isPlaying ? _closedPosition : transform.position;
        Vector3 open   = closed + Vector3.up * slideDistance;
 
        Gizmos.color = Color.green;
        Gizmos.DrawLine(closed, open);
        Gizmos.DrawWireCube(open, Vector3.one * 0.15f);
 
        Gizmos.color = new Color(0, 1, 0, 0.2f);
        // Vẽ bounds xấp xỉ dựa trên Renderer nếu có
        Renderer r = GetComponent<Renderer>();
        if (r != null)
        {
            Vector3 size = r.bounds.size;
            Gizmos.DrawWireCube(open, size);
        }
    }
}