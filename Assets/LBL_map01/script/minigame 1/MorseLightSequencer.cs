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

    private void Start()
    {
        if (lights == null || lights.Length == 0)
        {
            Debug.LogWarning("[MorseLightSequencer] Chưa gán đèn nào!");
            return;
        }

        StartCoroutine(SequenceLoop());
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