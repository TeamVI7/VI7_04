using UnityEngine;

// Gắn vào lăng trụ tam giác (3 mặt gương).
// Người chơi lại gần, giữ phím F để xoay, canh góc sao cho phản xạ đúng vào cảm biến.
// Lưu ý: GameObject này (và các mặt gương con) phải nằm ở Layer "Mirror"
// để LaserSource nhận diện được là gương.
public class RotatableMirrorPrism : MonoBehaviour
{
    [Header("Xoay")]
    public float rotateSpeed = 60f;
    public Vector3 rotateAxis = Vector3.up;
    public KeyCode interactKey = KeyCode.F;

    [Header("Tương tác")]
    public float interactRange = 3f;
    public Transform player; // có thể để trống, script sẽ tự tìm theo tag "Player"

    void Start()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    void Update()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= interactRange && Input.GetKey(interactKey))
        {
            transform.Rotate(rotateAxis, rotateSpeed * Time.deltaTime, Space.World);
        }
    }
}