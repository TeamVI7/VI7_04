using System.Collections;
using UnityEngine;

/// <summary>
/// Cửa đơn giản: lại gần + bấm E → trượt lên.
/// Gắn vào GameObject cửa. Tự tính slideDistance từ kích thước mesh.
/// </summary>
public class SimpleDoor : MonoBehaviour
{
    [Header("Mở cửa")]
    [Tooltip("Để 0 → tự tính theo chiều cao mesh. Hoặc tự nhập tay.")]
    public float slideDistance = 13f;

    public float openDuration  = 3f;
    public float interactRange = 14f;

    [Header("Hướng trượt")]
    [Tooltip("Mặc định trượt lên (Vector3.up). Đổi nếu cửa xoay lạ.")]
    public Vector3 slideDirection = Vector3.up;

    [Header("Hint (tuỳ chọn)")]
    public GameObject interactHint;

    [Header("Âm thanh")]
    [Tooltip("AudioClip phát khi cửa bắt đầu mở")]
    public AudioClip openSound;
    [Tooltip("Để trống → tự tạo AudioSource trên GameObject này")]
    public AudioSource audioSource;

    // ── Internal ─────────────────────────────────────────────────
    private bool    _isOpen  = false;
    private bool    _moving  = false;
    private Vector3 _closedPos;
    private Vector3 _openPos;
    private Transform _player;
    private Camera    _cam;

    private void Start()
    {
        if (slideDistance <= 0f)
        {
            Renderer r = GetComponent<Renderer>();
            if (r != null)
            {
                Vector3 size = r.bounds.size;
                slideDistance = Mathf.Abs(Vector3.Dot(size, slideDirection.normalized));
                slideDistance = Mathf.Max(slideDistance, 1f);
            }
            else
            {
                slideDistance = 3f;
            }
        }

        _closedPos = transform.position;
        _openPos   = _closedPos + slideDirection.normalized * slideDistance;

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) _player = p.transform;
        _cam = Camera.main;

        if (interactHint) interactHint.SetActive(false);

        // Tự tạo AudioSource nếu chưa gán
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        Debug.Log($"[SimpleDoor] slideDistance = {slideDistance:F2} | openPos = {_openPos}");
    }

    private void Update()
    {
        if (_isOpen || _moving) return;

        bool inRange = IsPlayerFacing();
        if (interactHint) interactHint.SetActive(inRange);

        if (inRange && Input.GetKeyDown(KeyCode.E))
            StartCoroutine(OpenDoor());
    }

    private bool IsPlayerFacing()
    {
        if (_player == null || _cam == null) return false;

        if (Vector3.Distance(_player.position, transform.position) > interactRange)
            return false;

        Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange))
            if (hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
                return true;

        return false;
    }

    private void PlayOpenSound()
    {
        if (openSound != null && audioSource != null)
            audioSource.PlayOneShot(openSound);
    }

    private IEnumerator OpenDoor()
    {
        _moving = true;
        if (interactHint) interactHint.SetActive(false);

        PlayOpenSound();

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
        _isOpen = true;
        _moving = false;
    }

    // ── Gizmo ────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        float dist = slideDistance > 0f ? slideDistance : 3f;
        Vector3 open = transform.position + slideDirection.normalized * dist;

        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, open);

        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Renderer r = GetComponent<Renderer>();
        if (r != null)
            Gizmos.DrawWireCube(open, r.bounds.size);
        else
            Gizmos.DrawWireCube(open, Vector3.one);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}