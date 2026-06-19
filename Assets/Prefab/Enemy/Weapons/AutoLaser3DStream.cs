using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class AutoLaser3DStream : MonoBehaviour
{
    private LineRenderer _mainLine;
    private LineRenderer _crossLine;

    [Header("Laser Stream Settings")]
    [Tooltip("Độ dày của luồng laser (0.02 - 0.03 là chuẩn tinh tế)")]
    public float LaserWidth = 0.025f;

    [Tooltip("Tốc độ cuộn của luồng năng lượng (vân sáng)")]
    public float ScrollSpeed = 5f;

    [Header("Target Offset")]
    [Tooltip("Độ cao cộng thêm để tia laser bắn vào ngực thay vì chúi xuống chân Player")]
    public float TargetHeightOffset = 1.2f;

    private void Awake()
    {
        // 1. Cấu hình LineRenderer chính
        _mainLine = GetComponent<LineRenderer>();
        ConfigureLine(_mainLine);

        // 2. Tạo LineRenderer thứ 2 đan chéo vuông góc
        GameObject crossGO = new GameObject("Cross_Line_Stream");
        crossGO.transform.SetParent(transform);
        crossGO.transform.localPosition = Vector3.zero;
        crossGO.transform.localRotation = Quaternion.identity;

        _crossLine = crossGO.AddComponent<LineRenderer>();
        ConfigureLine(_crossLine);

        // Đồng bộ Material phát sáng
        _crossLine.sharedMaterial = _mainLine.sharedMaterial;
    }

    private void ConfigureLine(LineRenderer line)
    {
        // Triệt để ép 2 đầu bằng nhau tuyệt đối, không cho phép to bè ra
        line.startWidth = LaserWidth;
        line.endWidth = LaserWidth;
        
        // Sử dụng TransformZ để ta chủ động điều khiển góc xoay 3D bằng code dưới LateUpdate
        line.alignment = LineAlignment.TransformZ;
        line.positionCount = 2;
    }

    private void LateUpdate()
    {
        if (_mainLine != null && _mainLine.enabled)
        {
            if (_crossLine != null) _crossLine.enabled = true;

            // Đọc vị trí gốc (Họng súng) từ điểm 0 của Line chính do script Enemy cấp
            Vector3 originPos = _mainLine.GetPosition(0);
            // Đọc vị trí đích (Player) từ điểm 1 ban đầu
            Vector3 rawTargetPos = _mainLine.GetPosition(1);

            // BƯỚC ĐỘT PHÁ 1: Tự động nâng tia laser lên ngực Player, tránh bị chúi xuống đất gây dẹt hình học
            Vector3 finalTargetPos = rawTargetPos;
            if (rawTargetPos != originPos)
            {
                finalTargetPos = rawTargetPos + Vector3.up * TargetHeightOffset;
            }

            // Cập nhật lại vị trí chuẩn cho cả 2 đường Line
            _mainLine.SetPosition(0, originPos);
            _mainLine.SetPosition(1, finalTargetPos);

            if (_crossLine != null)
            {
                _crossLine.SetPosition(0, originPos);
                _crossLine.SetPosition(1, finalTargetPos);
            }

            // BƯỚC ĐỘT PHÁ 2: Xoay toán học chính xác GameObject chứa Laser hướng thẳng vào mục tiêu
            // Việc này giúp cơ chế TransformZ hoạt động hoàn hảo, bẻ 2 tấm phẳng đan chính xác góc 90 độ
            if (finalTargetPos != originPos)
            {
                Vector3 direction = finalTargetPos - originPos;
                transform.rotation = Quaternion.LookRotation(direction);
                
                // Ép tấm thứ 2 xoay vuông góc 90 độ so với tấm thứ nhất theo trục dọc (Z) để tạo khối chữ X
                if (_crossLine != null)
                {
                    _crossLine.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                }
            }

            // 3. Hiệu ứng cuộn vân năng lượng chảy dọc luồng laser (Pixel Gun 3D style)
            if (_mainLine.material != null)
            {
                float offset = Time.time * ScrollSpeed;
                _mainLine.material.mainTextureOffset = new Vector2(-offset, 0);
            }
        }
        else
        {
            if (_crossLine != null && _crossLine.enabled)
            {
                _crossLine.enabled = false;
            }
        }
    }
}