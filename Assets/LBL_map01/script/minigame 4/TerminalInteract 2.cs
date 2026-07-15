using UnityEngine;

public class TerminalInteract : MonoBehaviour
{
    [Header("Liên kết với Nhạc Trưởng")]
    public MinigameFlowController flowController;

    [Header("Cài đặt phím")]
    public KeyCode interactKey = KeyCode.F;

    private bool _isPlayerInZone = false;

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
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _isPlayerInZone = false;
        }
    }
}