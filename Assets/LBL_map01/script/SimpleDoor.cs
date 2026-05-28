using UnityEngine;
using UnityEngine.UI;

public class DoorController : MonoBehaviour
{
    [Header("Cài đặt cửa")]
    public KeyCode openKey = KeyCode.E;
    public float slideDistance = 8f;
    public float speed = 2f;

    [Header("Hint UI")]
    public GameObject hintUI;        // kéo Canvas/Panel hint vào đây
    public float hintDistance = 3f;  // khoảng cách player thấy hint

    private Vector3 closedPos;
    private Vector3 openPos;
    private bool isOpen = false;
    private bool hasOpened = false;
    private Transform player;

    void Start()
    {
        closedPos = transform.localPosition;
        openPos = closedPos + Vector3.up * slideDistance;

        // Tự tìm player qua tag
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;

        if (hintUI != null) hintUI.SetActive(false);
    }

    void Update()
    {
        if (hasOpened)
        {
            if (hintUI != null) hintUI.SetActive(false);
            MoveDooor();
            return;
        }

        // Hiện hint khi player đến gần
        if (hintUI != null && player != null)
        {
            float dist = Vector3.Distance(player.position, transform.position);
            hintUI.SetActive(dist <= hintDistance);
        }

        if (Input.GetKeyDown(openKey))
        {
            isOpen = true;
            hasOpened = true;
        }

        MoveDooor();
    }

    void MoveDooor()
    {
        if (!isOpen) return;
        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition,
            openPos,
            speed * Time.deltaTime
        );
    }
}