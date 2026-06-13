using UnityEngine;

public class ElevatorDoor : MonoBehaviour
{
    public enum Side { Left, Right }

    [Header("Cài đặt")]
    public Side side;
    public float openDistance = 1.2f;
    public float speed = 2f;

    [Header("Âm thanh")]
    public AudioClip openSound;
    public AudioClip closeSound;
    public AudioSource audioSource;

    private Vector3 closedPos;
    private Vector3 openPos;
    private bool isOpen = false;

    void Start()
    {
        closedPos = transform.localPosition;
        float dir = (side == Side.Left) ? -1f : 1f;
        openPos = closedPos + new Vector3(0f, 0f, dir * openDistance);

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
    }

    public void Open()
    {
        if (!isOpen) PlaySound(openSound);
        isOpen = true;
    }

    public void Close()
    {
        if (isOpen) PlaySound(closeSound);
        isOpen = false;
    }

    void Update()
    {
        Vector3 target = isOpen ? openPos : closedPos;
        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition, target, speed * Time.deltaTime);
    }

    public bool IsClosed()   => Vector3.Distance(transform.localPosition, closedPos) < 0.01f;
    public bool IsFullyOpen() => Vector3.Distance(transform.localPosition, openPos)   < 0.01f;

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }
}