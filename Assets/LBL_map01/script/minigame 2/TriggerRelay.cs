using UnityEngine;

public class TriggerRelay : MonoBehaviour
{
    public ServerMinigameManager manager;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (manager == null)
        {
            Debug.LogError("[TriggerRelay] Chưa gán 'manager' — vùng trigger này không làm gì cả.", this);
            return;
        }

        // ServerMinigameManager chỉ khởi động được ĐÚNG MỘT LẦN. Nếu vùng trigger này
        // "cướp" lượt kích hoạt trước khi FalloutTerminalManager / MinigameFlowController
        // kịp đăng ký callback thì các script đó sẽ chờ callback vĩnh viễn (kẹt cứng,
        // mất luôn quyền điều khiển). Chỉ dùng relay này trong scene KHÔNG có script nào
        // khác sở hữu cùng manager.
        if (!manager.OnPlayerEnterTrigger())
            Debug.LogWarning("[TriggerRelay] ServerMinigameManager đã được kích hoạt trước đó — bỏ qua.", this);
    }
}