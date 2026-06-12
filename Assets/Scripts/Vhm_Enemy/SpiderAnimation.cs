using UnityEngine;
using System.Collections;

namespace OutOfBullet.Enemy
{
    public class SpiderLegController : MonoBehaviour
    {
        [Header("Chân nhện - kéo Cube.002 đến Cube.007 vào đây")]
        public Transform[] legs;

        [Header("Vị trí nghỉ - kéo Rest_1 đến Rest_6 vào đây")]
        public Transform[] restPositions;

        [Header("Cài đặt bước chân")]
        public float stepDistance = 0.5f;
        public float stepHeight = 0.15f;
        public float stepSpeed = 5f;
        public LayerMask groundMask;

        [Header("Body")]
        public Transform body;
        public float bodyHeightOffset = 1.1f;
        public float bodySmoothing = 5f;

        // Internal 
        private Vector3[] currentFootPos;
        private bool[] isStepping;
        private Vector3 bodyVelocity;
        private Vector3[] _localOffsets;

        // Biến lưu vị trí khung hình trước của thân để tự tính vận tốc thực tế
        private Vector3 lastBodyPosition;

        // Biến cờ hiệu kiểm tra xem cấu hình Inspector đã chuẩn chưa
        private bool isInitialized = false;

        void Start()
        {
            // KHẮC PHỤC LỖI KHÔNG GÁN CHÂN: Kiểm tra an toàn dữ liệu đầu vào
            if (legs == null || legs.Length == 0 || restPositions == null || restPositions.Length == 0)
            {
                Debug.LogWarning($"[SpiderLegController] Trên <color=yellow>{gameObject.name}</color> chưa được kéo gán đầy đủ chân hoặc vị trí nghỉ ở bảng Inspector kìa cậu ơi!", gameObject);
                isInitialized = false;
                return;
            }

            if (legs.Length != restPositions.Length)
            {
                Debug.LogError($"[SpiderLegController] Số lượng Chân ({legs.Length}) và Vị trí nghỉ ({restPositions.Length}) không khớp nhau trên {gameObject.name}!", gameObject);
                isInitialized = false;
                return;
            }

            // Khởi tạo mảng khi mọi điều kiện đã an toàn
            currentFootPos = new Vector3[legs.Length];
            isStepping = new bool[legs.Length];
            _localOffsets = new Vector3[legs.Length];

            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null || restPositions[i] == null)
                {
                    Debug.LogError($"[SpiderLegController] Phần tử thứ {i} trong danh sách chân hoặc vị trí nghỉ bị Null trên {gameObject.name}!", gameObject);
                    isInitialized = false;
                    return;
                }

                _localOffsets[i] = legs[i].position - restPositions[i].position;
                currentFootPos[i] = GetGroundPoint(restPositions[i].position);
                legs[i].position = currentFootPos[i] + _localOffsets[i];
            }

            if (body != null) lastBodyPosition = body.position;
            isInitialized = true; // Kích hoạt cờ hiệu cho phép chạy logic tính toán di chuyển
        }

        void LateUpdate()
        {
            // Nếu chưa khởi tạo thành công hoặc thiếu Body, lập tức thoát ra để chặn lỗi NaN toán học
            if (!isInitialized || body == null) return;

            // Tính toán Vector vận tốc thực tế của con nhện trong khung hình này
            Vector3 currentVelocity = (body.position - lastBodyPosition) / Time.deltaTime;
            lastBodyPosition = body.position;

            for (int i = 0; i < legs.Length; i++)
            {
                // Di chuyển chân bám sát vị trí dậm đất hiện tại + Offset
                Vector3 targetLegPosition = currentFootPos[i] + _localOffsets[i];

                // Phản hồi vị trí chân mượt mà theo tốc độ bước
                legs[i].position = Vector3.Lerp(legs[i].position, targetLegPosition, Time.deltaTime * stepSpeed * 3f);

                if (isStepping[i]) continue;

                // VỊ TRÍ LÝ TƯỞNG CÓ ĐÓN ĐẦU VẬN TỐC (Velocity Prediction)
                Vector3 predictedRestPos = restPositions[i].position + currentVelocity * 0.05f;
                Vector3 idealPos = GetGroundPoint(predictedRestPos);

                float dist = Vector3.Distance(idealPos, currentFootPos[i]);

                int opposite = (i % 2 == 0) ? i + 1 : i - 1;
                bool oppStepping = opposite < legs.Length && isStepping[opposite];

                if (dist > stepDistance && !oppStepping)
                {
                    StartCoroutine(DoStep(legIndex: i, target: idealPos));
                }
            }

            AdjustBody();
        }

        IEnumerator DoStep(int legIndex, Vector3 target)
        {
            isStepping[legIndex] = true;
            Vector3 start = currentFootPos[legIndex];
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime * stepSpeed;
                float arc = Mathf.Sin(t * Mathf.PI) * stepHeight;
                currentFootPos[legIndex] = Vector3.Lerp(start, target, t) + Vector3.up * arc;
                yield return null;
            }

            currentFootPos[legIndex] = target;
            isStepping[legIndex] = false;
        }

        void AdjustBody()
        {
            // SỬA LỖI GÂY NAN (0 chia 0): Kiểm tra mảng chân có phần tử nào không trước khi chia
            if (currentFootPos == null || currentFootPos.Length == 0) return;

            float avgY = 0f;
            foreach (var pos in currentFootPos)
            {
                avgY += pos.y;
            }
            avgY /= currentFootPos.Length; // Bây giờ phép tính này tuyệt đối an toàn 100%

            // Vị trí mục tiêu của thân
            Vector3 targetPos = new Vector3(
                transform.position.x,
                avgY + bodyHeightOffset,
                transform.position.z
            );

            // Cập nhật vị trí cho thân nhện
            body.position = Vector3.Lerp(body.position, targetPos, Time.deltaTime * bodySmoothing);
        }

        Vector3 GetGroundPoint(Vector3 from)
        {
            if (Physics.Raycast(from + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, groundMask))
                return hit.point;
            return from;
        }

        void OnDrawGizmos()
        {
            if (!isInitialized || currentFootPos == null) return;
            for (int i = 0; i < currentFootPos.Length; i++)
            {
                Gizmos.color = isStepping[i] ? Color.red : Color.green;
                Gizmos.DrawSphere(currentFootPos[i], 0.05f);
            }
        }
    }
}