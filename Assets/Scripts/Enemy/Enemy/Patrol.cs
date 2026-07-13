using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyBrain))]
public class PatrolBehaviour : MonoBehaviour
{
    // [FIX] Toàn bộ logic tự SetDestination (TickPatrol, FindWaypoint, TickChase cũ) đã bị GỠ BỎ.
    // Lý do: EnemyBrain đã tự SetDestination cho cả 3 state (Idle/Aggro/Investigate) với logic
    // patrol radius + flanking + investigate riêng của nó. Việc PatrolBehaviour ĐỒNG THỜI cũng
    // SetDestination mỗi frame tạo ra 2 script tranh nhau 1 NavMeshAgent, kết quả phụ thuộc
    // Script Execution Order (không ổn định) - đây là nghi phạm chính gây ra bug "mất dấu Player
    // là quái bỏ về tổ luôn" mà cậu và Gemini không tìm ra được, vì nó không nằm trong logic
    // Investigate mà nằm ở việc 2 script giành quyền set đường đi.
    //
    // PatrolBehaviour giờ CHỈ còn 2 trách nhiệm hợp lý cho 1 "movement tuner":
    //   1. Set tốc độ agent theo state (EnemyBrain không quản lý speed nữa, trừ khi đứng một mình).
    //   2. Xoay mặt về phía player khi ở trong tầm ưa thích (không ResetPath, không phá flanking).

    [Header("Movement Speed (CHỈ set tốc độ - EnemyBrain toàn quyền set Destination)")]
    public float PatrolSpeed = 2f;
    public float ChaseSpeed = 10f;
    [Tooltip("Khi player trong tầm này, quái vẫn tiến tới điểm flank (không đứng im), chỉ xoay mặt về player. " +
             "Nếu sau này có script Attack riêng, nó có thể tự ResetPath() trong lúc đang tấn công.")]
    public float PreferredRange = 10f;

    public float PatrolRadius
    {
        get => GetComponent<EnemyBrain>().PatrolRadius;
        set => GetComponent<EnemyBrain>().PatrolRadius = value;
    }

    public float WaypointWaitTime
    {
        get => GetComponent<EnemyBrain>().PatrolWaitTime;
        set => GetComponent<EnemyBrain>().PatrolWaitTime = value;
    }
    private NavMeshAgent _nav;
    private EnemyBrain _brain;

    private void Awake()
    {
        _nav = GetComponent<NavMeshAgent>();
        _brain = GetComponent<EnemyBrain>();
        _brain.OnStateChanged += OnStateChanged;
    }

    private void OnDestroy() => _brain.OnStateChanged -= OnStateChanged;

    private void Update()
    {
        if (!_nav.enabled || !_nav.isOnNavMesh) return;

        switch (_brain.State)
        {
            case EnemyState.Idle:
            case EnemyState.Investigate:
                // [FIX] Trước đây case Investigate có "break;" đặt sai vị trí khiến đoạn xoay người
                // bên dưới không bao giờ chạy được (unreachable code). Đã gộp về set speed đơn giản;
                // việc xoay người khi investigate đã do chính EnemyBrain.TickInvestigate() đảm nhiệm rồi
                // (2 giây đầu xoay 45 độ/giây) nên không cần lặp lại ở đây.
                _nav.speed = PatrolSpeed * _brain.CurrentSpeedMultiplier;
                break;

            case EnemyState.Aggro:
                TickChaseSpeed();
                break;
        }
    }

    private void TickChaseSpeed()
    {
        if (PlayerHealth.Transform == null) return;
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        if (dist > PreferredRange)
        {
            _nav.speed = ChaseSpeed * _brain.CurrentSpeedMultiplier;
        }
        else
        {
            // [FIX] Trước đây gọi _nav.ResetPath() ở đây, đè lên đích flank mà EnemyBrain vừa set
            // trong cùng frame (script conflict y hệt lỗi patrol). Giờ chỉ giảm tốc + xoay mặt,
            // agent vẫn tiếp tục tiến vào điểm flank (attackFlankRadius) do EnemyBrain quyết định.
            _nav.speed = PatrolSpeed * _brain.CurrentSpeedMultiplier;
            FacePlayer();
        }
    }

    public void FacePlayer()
    {
        if (PlayerHealth.Transform == null) return;
        Vector3 dir = PlayerHealth.Transform.position - transform.position;
        dir.y = 0f;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }

    private void OnStateChanged(EnemyState state)
    {
        if (!_nav) return;
        bool canMove = state != EnemyState.Dead && state != EnemyState.Staggered;
        _nav.enabled = canMove;

        // [FIX] Đã bỏ đoạn reset _hasWaypoint/_waitTimer và _nav.ResetPath() khi vào Idle -
        // trước đây đoạn này chạy NGAY LÚC EnemyBrain.ReturnToPatrol() gọi SetState(Idle),
        // tức là xóa path CHỈ VÀI DÒNG CODE trước khi ReturnToPatrol() tự SetDestination(_spawnPos)
        // lại - vô hại về logic nhưng là dấu hiệu rõ của việc 2 script đang cùng đụng vào 1 agent.
        // Giờ PatrolBehaviour không còn tự ý ResetPath trừ lúc chết/choáng.
        if (state == EnemyState.Staggered || state == EnemyState.Dead)
        {
            if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
        }
    }
}