using System.Collections;
using UnityEngine;
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
    public GameObject puzzleUI;           
    public TMPro.TMP_Text progressText; 

    [Header("Âm thanh")]
    [Tooltip("AudioSource để phát âm thanh server trồi lên (nếu để trống sẽ tự thêm 1 cái lúc runtime).")]
    public AudioSource audioSource;
    [Tooltip("Âm thanh phát mỗi lần 1 khối server bắt đầu trồi lên.")]
    public AudioClip riseSound;
    [Range(0f, 1f)] public float riseSfxVolume = 1f;

    [Tooltip("Âm thanh phát khi ĐÃ GẮN XONG toàn bộ SSD (hoàn thành minigame, lúc cửa chuẩn bị mở).")]
    public AudioClip allDoneSound;
    [Range(0f, 1f)] public float allDoneSfxVolume = 1f;

    [Header("VFX khi trồi lên")]
    [Tooltip("Prefab Particle System dùng chung, tự Instantiate tại chân mỗi khối khi nó trồi lên " +
             "(dùng khi bạn KHÔNG muốn đặt sẵn ParticleSystem thủ công cho từng ServerBlock). " +
             "Nếu để trống, sẽ dùng riseVFX đã gán sẵn trên từng ServerBlock (nếu có).")]
    public GameObject riseVFXPrefab;
    [Tooltip("Thời gian tồn tại của VFX prefab trước khi tự huỷ (giây).")]
    public float riseVFXLifetime = 2f;
    private bool    _triggered = false;
    private bool    _solved    = false;
    private int     _filled    = 0;
    private float[] _originalY;

    private void Start()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }
        _originalY = new float[serverBlocks.Length];

        for (int i = 0; i < serverBlocks.Length; i++)
        {
            if (serverBlocks[i] == null) continue;

            _originalY[i] = serverBlocks[i].transform.position.y; 

            Vector3 pos = serverBlocks[i].transform.position;
            pos.y = _originalY[i] - hiddenOffset;        
            serverBlocks[i].transform.position = pos;
        }

        if (puzzleUI) puzzleUI.SetActive(false);
        UpdateProgressUI();
    }
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

            PlayRiseSound();
            PlayRiseVFX(serverBlocks[i]);
            StartCoroutine(RiseBlock(serverBlocks[i].transform, _originalY[i]));
            yield return new WaitForSeconds(riseDelay);
        }
    }

    private void PlayRiseSound()
    {
        if (audioSource != null && riseSound != null)
            audioSource.PlayOneShot(riseSound, riseSfxVolume);
    }
    private void PlayRiseVFX(ServerBlock block)
    {
        if (block.riseVFX != null)
        {
            block.PlayRiseVFX();
            return;
        }

        if (riseVFXPrefab != null)
        {
            GameObject vfx = Instantiate(riseVFXPrefab, block.transform.position, Quaternion.identity);
            Destroy(vfx, riseVFXLifetime);
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

    public float GetRiseSequenceDuration()
    {
        int count = serverBlocks != null ? serverBlocks.Length : 0;
        if (count <= 0) return riseDuration;
        return (count - 1) * riseDelay + riseDuration;
    }

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

        if (audioSource != null && allDoneSound != null)
            audioSource.PlayOneShot(allDoneSound, allDoneSfxVolume);

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