using System.Collections;
using UnityEngine;

/// <summary>
/// Quản lý minigame 2: server trồi lên, player gắn SSD, cửa mở.
/// Gắn vào một Empty GameObject "ServerManager".
/// </summary>
public class ServerMinigameManager : MonoBehaviour
{
    [Header("Server Blocks")]
    [Tooltip("Kéo 6 khối server vào đây")]
    public ServerBlock[] serverBlocks;

    [Header("Rise Animation")]
    [Tooltip("Server sẽ bị thụt xuống bao nhiêu đơn vị so với vị trí gốc (nhập số dương, VD: 5)")]
    public float hiddenOffset  = 5f;
    [Tooltip("Thời gian 1 khối trồi lên (giây)")]
    public float riseDuration  = 1.2f;
    [Tooltip("Độ trễ giữa mỗi khối trồi lên")]
    public float riseDelay     = 0.2f;

    [Header("Door")]
    public SlidingDoorController door;

    [Header("UI (tuỳ chọn)")]
    public GameObject puzzleUI;           // Panel "Gắn SSD vào server"
    public TMPro.TMP_Text progressText;   // "3 / 6 SSD đã gắn"

    // ── State ─────────────────────────────────────────────────────
    private bool    _triggered = false;
    private bool    _solved    = false;
    private int     _filled    = 0;
    private float[] _originalY;           // lưu Y gốc của từng khối

    private void Start()
    {
        // Lưu Y gốc rồi ẩn từng khối xuống dưới đất
        _originalY = new float[serverBlocks.Length];

        for (int i = 0; i < serverBlocks.Length; i++)
        {
            if (serverBlocks[i] == null) continue;

            _originalY[i] = serverBlocks[i].transform.position.y; // nhớ vị trí gốc

            Vector3 pos = serverBlocks[i].transform.position;
            pos.y = _originalY[i] - hiddenOffset;                 // thụt xuống
            serverBlocks[i].transform.position = pos;
        }

        if (puzzleUI) puzzleUI.SetActive(false);
        UpdateProgressUI();
    }

    // ── Trigger Zone ──────────────────────────────────────────────
    public void OnPlayerEnterTrigger()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(RiseAllBlocks());
        if (puzzleUI) puzzleUI.SetActive(true);
    }

    private IEnumerator RiseAllBlocks()
    {
        for (int i = 0; i < serverBlocks.Length; i++)
        {
            if (serverBlocks[i] == null) continue;
            StartCoroutine(RiseBlock(serverBlocks[i].transform, _originalY[i]));
            yield return new WaitForSeconds(riseDelay);
        }
    }

    private IEnumerator RiseBlock(Transform t, float targetY)
    {
        Vector3 start  = t.position;
        Vector3 target = new Vector3(t.position.x, targetY, t.position.z);
        float elapsed  = 0f;

        while (elapsed < riseDuration)
        {
            elapsed += Time.deltaTime;
            float curve = Mathf.SmoothStep(0f, 1f, elapsed / riseDuration); // mượt
            t.position  = Vector3.Lerp(start, target, curve);
            yield return null;
        }

        t.position = target;
    }

    // ── Check Complete ────────────────────────────────────────────

    /// <summary>Được gọi bởi ServerBlock mỗi khi 1 khối được gắn SSD.</summary>
    public void CheckAllFilled()
    {
        if (_solved) return;

        _filled = 0;
        foreach (var block in serverBlocks)
            if (block != null && block.IsFilled) _filled++;

        UpdateProgressUI();
        Debug.Log($"[ServerMinigame] {_filled} / {serverBlocks.Length} SSD đã gắn");

        if (_filled >= serverBlocks.Length)
            StartCoroutine(SolveSequence());
    }

    private IEnumerator SolveSequence()
    {
        _solved = true;

        if (progressText)
            progressText.text = "✓ GIẢI MÃ HOÀN TẤT — Cửa đang mở...";

        yield return new WaitForSeconds(1.5f);

        door?.UnlockAndOpen();

        yield return new WaitForSeconds(1f);

        if (puzzleUI) puzzleUI.SetActive(false);
    }

    private void UpdateProgressUI()
    {
        if (progressText == null) return;
        int total = serverBlocks != null ? serverBlocks.Length : 6;
        progressText.text = $"SSD đã gắn: {_filled} / {total}";
    }
}