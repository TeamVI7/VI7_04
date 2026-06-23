using System.Collections;
using UnityEngine;

/// <summary>
/// Điều phối nhiều đèn Morse nháy lần lượt nhau.
/// Gắn vào 1 Empty GameObject, kéo tất cả đèn vào danh sách.
/// Đèn 1 nháy xong → đèn 2 bắt đầu → ... → lặp lại từ đầu.
/// </summary>
public class MorseLightSequencer : MonoBehaviour
{
    [Header("Danh sách đèn (theo thứ tự nháy)")]
    public MorseLightController[] lights;

    [Tooltip("Khoảng nghỉ giữa 2 đèn (giây)")]
    public float delayBetweenLights = 0.3f;

    [Tooltip("Khoảng nghỉ sau khi tất cả đèn đã nháy xong 1 vòng")]
    public float delayBetweenRounds = 1f;

    [Tooltip("Lặp lại vô tận")]
    public bool looping = true;

    [Tooltip("Nếu false: KHÔNG tự chạy lúc Start(). Phải gọi BeginSequence() từ script khác " +
             "(ví dụ WireBoxInteraction khi puzzle nối dây hoàn thành).")]
    public bool activateOnStart = false;

    private Coroutine _loopCoroutine;
    private bool      _isRunning = false;

    private void Start()
    {
        // Luôn đảm bảo tắt hẳn lúc khởi đầu scene, tránh đèn sáng/nháy dở
        // do trạng thái mặc định của từng đèn — dù activateOnStart = false hay true.
        if (lights != null)
            foreach (var light in lights)
                light?.StopMorse();

        if (activateOnStart)
            BeginSequence();
    }

    /// <summary>
    /// Bắt đầu chạy chuỗi đèn Morse. Gọi thủ công nếu activateOnStart = false
    /// (ví dụ sau khi minigame nối dây hoàn thành).
    /// </summary>
    public void BeginSequence()
    {
        if (_isRunning) return;

        if (lights == null || lights.Length == 0)
        {
            Debug.LogWarning("[MorseLightSequencer] Chưa gán đèn nào!");
            return;
        }

        _isRunning = true;
        _loopCoroutine = StartCoroutine(SequenceLoop());
    }

    /// <summary>Dừng chuỗi đèn (về trạng thái idle ở lần Update tiếp theo của từng đèn).</summary>
    public void StopSequence()
    {
        if (_loopCoroutine != null)
        {
            StopCoroutine(_loopCoroutine);
            _loopCoroutine = null;
        }
        _isRunning = false;

        if (lights != null)
            foreach (var light in lights)
                light?.StopMorse();
    }

    private IEnumerator SequenceLoop()
    {
        do
        {
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;

                bool done = false;

                // Phát đèn i, khi xong set done = true
                lights[i].PlayOnce(lights[i].messageToEncode, () => done = true);

                // Chờ đèn i phát xong
                yield return new WaitUntil(() => done);

                // Nghỉ trước khi đèn tiếp theo
                yield return new WaitForSeconds(delayBetweenLights);
            }

            // Nghỉ giữa các vòng
            yield return new WaitForSeconds(delayBetweenRounds);

        } while (looping);
    }
}