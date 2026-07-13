using UnityEngine;

public class TerminalInteract : MonoBehaviour
{
    public FalloutTerminalManager terminal;
    public KeyCode interactKey = KeyCode.F;

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

        // Nếu terminal đang mở: cho phép bấm F để đóng bất kỳ lúc nào,
        // không cần đứng trong vùng trigger nữa (vì player đứng yên nhìn màn hình).
        if (terminal.IsTerminalOpen)
        {
            if (Input.GetKeyDown(interactKey))
                terminal.CloseTerminal();
            return;
        }

        // Terminal đang đóng: chỉ mở được khi player đứng trong vùng trigger.
        if (_playerInRange && Input.GetKeyDown(interactKey))
            terminal.StartTerminal();
    }
}