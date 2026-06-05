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

    // ── Internal ─────────────────────────────────────────────────
    private bool    _isOpen  = false;
    private bool    _moving  = false;
    private Vector3 _closedPos;
    private Vector3 _openPos;
    private Transform _player;
    private Camera    _cam;

    private void Start()
    {
        // Tự tính slideDistance nếu để 0
        if (slideDistance <= 0f)
        {
            Renderer r = GetComponent<Renderer>();
            if (r != null)
            {
                // Lấy kích thước theo hướng trượt
                Vector3 size = r.bounds.size;
                slideDistance = Mathf.Abs(Vector3.Dot(size, slideDirection.normalized));
                slideDistance = Mathf.Max(slideDistance, 1f); // tối thiểu 1
            }
            else
            {
                slideDistance = 3f; // fallback
            }
        }

        _closedPos = transform.position;
        _openPos   = _closedPos + slideDirection.normalized * slideDistance;

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) _player = p.transform;
        _cam = Camera.main;

        if (interactHint) interactHint.SetActive(false);

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

    private IEnumerator OpenDoor()
    {
        _moving = true;
        if (interactHint) interactHint.SetActive(false);

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

    // ── Gizmo: hiện vị trí cửa khi mở trong Scene view ──────────
    private void OnDrawGizmosSelected()
    {
        float dist = slideDistance > 0f ? slideDistance : 3f;
        Vector3 open = transform.position + slideDirection.normalized * dist;

        // Đường trượt
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, open);

        // Vị trí cửa khi mở (outline)
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Renderer r = GetComponent<Renderer>();
        if (r != null)
            Gizmos.DrawWireCube(open, r.bounds.size);
        else
            Gizmos.DrawWireCube(open, Vector3.one);

        // Vùng interact
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}