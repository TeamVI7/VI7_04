using UnityEngine;

public class TerminalInteract : MonoBehaviour
{
    [Header("Liên kết với Nhạc Trưởng")]
    public MinigameFlowController flowController;

    [Header("Cài đặt phím")]
    public KeyCode interactKey = KeyCode.F;

    [Header("Outline khi player đứng trong vùng")]
    public Outline outline;

    private bool _isPlayerInZone = false;

    private void Start()
    {
        if (outline == null) outline = GetComponent<Outline>();
        if (outline != null) outline.enabled = false;
    }

    private void Update()
    {
        if (_isPlayerInZone && Input.GetKeyDown(interactKey))
        {
            if (flowController != null)
            {
                flowController.StartTerminal();
            }
            else
            {
                Debug.LogError("[TerminalInteract] Missing MinigameFlowController!");
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _isPlayerInZone = true;
            if (outline != null) outline.enabled = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _isPlayerInZone = false;
            if (outline != null) outline.enabled = false;

            if (flowController != null)
                flowController.ForceExitZone();
        }
    }
}