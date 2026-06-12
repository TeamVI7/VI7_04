using UnityEngine;

namespace OutOfBullet.Enemy
{
    public class DroneAnimation : MonoBehaviour
    {
        [Header("Hover Settings (Bay nhấp nhô)")]
        public float HoverSpeed = 2f;       // Tốc độ bay lên xuống
        public float HoverAmount = 0.2f;    // Độ cao nhấp nhô (biên độ)

        [Header("Spin Settings (Xoay tròn tự thân)")]
        public Vector3 SpinRotationSpeed = new Vector3(0, 50f, 0); // Tốc độ xoay quanh trục Y mỗi giây

        [Header("Juice Settings (Hiệu ứng phụ)")]
        [Tooltip("Kéo phần khung vỏ quay hoặc lõi năng lượng của Drone vào đây để xoay độc lập nếu có")]
        public Transform CoreVisual;
        public float CoreSpinSpeed = 150f;

        // Lưu vị trí gốc cục bộ để tính toán nhấp nhô không bị lệch hướng di chuyển của AI
        private Vector3 _startLocalPosition;
        private float _hoverTimer;

        // Xử lý lực giật khi bắn (Recoil)
        private Vector3 _recoilOffset;
        private float _recoilVelocity;

        void Start()
        {
            // Lưu lại vị trí ban đầu của phần hiển thị (Visual)
            _startLocalPosition = transform.localPosition;
            _hoverTimer = Random.Range(0f, 100f); // Tạo độ lệch thời gian ngẫu nhiên nếu có nhiều Drone cùng xuất hiện
        }

        void Update()
        {
            // 1. ANIMATION BAY NHẤP NHÔ (Sử dụng hàm Sin toán học)
            _hoverTimer += Time.deltaTime * HoverSpeed;
            float newY = Mathf.Sin(_hoverTimer) * HoverAmount;
            
            // Cập nhật vị trí cục bộ kết hợp với lực giật Recoil nếu có
            transform.localPosition = _startLocalPosition + new Vector3(0, newY, 0) + _recoilOffset;

            // 2. ANIMATION XOAY TRÒN TỰ THÂN (Tạo cảm giác cơ khí đang vận hành)
            transform.Rotate(SpinRotationSpeed * Time.deltaTime);

            // Nếu có lõi năng lượng hoặc bộ phận quay riêng biệt
            if (CoreVisual != null)
            {
                CoreVisual.Rotate(Vector3.up * CoreSpinSpeed * Time.deltaTime);
            }

            // Hồi phục lực giật chấn động về vị trí cân bằng theo thời gian
            _recoilOffset = Vector3.MoveTowards(_recoilOffset, Vector3.zero, Time.deltaTime * 5f);
        }

        /// <summary>
        /// Hàm gọi từ Script bắn đạn của Drone để tạo hiệu ứng giật nảy giật cục cực chất
        /// </summary>
        public void PlayFireRecoil()
        {
            // Đẩy lùi Drone về phía sau một chút dựa vào hướng ngược lại của Drone
            _recoilOffset = -transform.forward * 0.15f; 
        }
    }
}