/*using UnityEngine;

public class TerminalInteract : MonoBehaviour
{
    public FalloutTerminalManager terminal;
    public KeyCode interactKey = KeyCode.F;
    [Tooltip("Phím phụ để thoát terminal, giống bản gốc cho phép Esc để back ra.")]
    public KeyCode closeKey = KeyCode.Escape;

    private bool _playerInRange = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) _playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) _playerInRange = false;
    }

    private void Update()
    {
        if (terminal == null) return;
        if (terminal.IsTerminalOpen)
        {
            if (Input.GetKeyDown(interactKey) || Input.GetKeyDown(closeKey))
                terminal.CloseTerminal();
            return;
        }
        if (_playerInRange && Input.GetKeyDown(interactKey))
            terminal.StartTerminal();
    }
}*/