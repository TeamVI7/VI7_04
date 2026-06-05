using UnityEngine;

public class ElevatorDoor : MonoBehaviour
{
    public enum Side { Left, Right }

    [Header("Cài đặt")]
    public Side side;
    public float openDistance = 1.2f;
    public float speed = 2f;

    private Vector3 closedPos;
    private Vector3 openPos;
    private bool isOpen = false;

    void Start()
    {
        closedPos = transform.localPosition;
        float dir = (side == Side.Left) ? -1f : 1f;
        openPos = closedPos + new Vector3(0f, 0f, dir * openDistance);
    }

    public void Open()  { isOpen = true; }
    public void Close() { isOpen = false; }

    void Update()
    {
        Vector3 target = isOpen ? openPos : closedPos;
        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition, target, speed * Time.deltaTime);
    }

    public bool IsClosed()
    {
        return Vector3.Distance(transform.localPosition, closedPos) < 0.01f;
    }

    public bool IsFullyOpen()
    {
        return Vector3.Distance(transform.localPosition, openPos) < 0.01f;
    }
}