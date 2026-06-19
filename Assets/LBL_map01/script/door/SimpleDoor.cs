using System.Collections;
using UnityEngine;

public class SimpleDoor : MonoBehaviour
{
    public float slideDistance = 13f;
    public float openDuration  = 3f;
    public Vector3 slideDirection = Vector3.up;

    public AudioClip openSound;
    public AudioSource audioSource;

    [Header("Hint Settings")]
    public GameObject hintCanvas;

    private bool    _isOpen       = false;
    private bool    _moving       = false;
    private Vector3 _openPos;
    private bool    _playerInZone = false;

    void Start()
    {
        _openPos = transform.position + slideDirection.normalized * slideDistance;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        SetHintVisible(false);
    }

    void Update()
    {
        if (_isOpen || _moving)
        {
            SetHintVisible(false);
            return;
        }

        SetHintVisible(_playerInZone);

        if (_playerInZone && Input.GetKeyDown(KeyCode.E))
            StartCoroutine(OpenDoor());
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            _playerInZone = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerInZone = false;
            SetHintVisible(false);
        }
    }

    void SetHintVisible(bool visible)
    {
        if (hintCanvas != null && hintCanvas.activeSelf != visible)
            hintCanvas.SetActive(visible);
    }

    IEnumerator OpenDoor()
    {
        _moving = true;
        SetHintVisible(false);

        if (openSound != null)
            audioSource.PlayOneShot(openSound);

        float elapsed = 0f;
        Vector3 startPos = transform.position;

        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / openDuration);
            transform.position = Vector3.Lerp(startPos, _openPos, t);
            yield return null;
        }

        transform.position = _openPos;
        _isOpen = true;
        _moving = false;
    }
}