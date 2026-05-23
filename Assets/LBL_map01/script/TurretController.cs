using UnityEngine;

public class TurretController : MonoBehaviour
{
    [Header("References")]
    public GameObject FirePoint;
    public GameObject[] LaserPrefabs;

    [Header("Detection")]
    public float detectionRange = 20f;
    public float fieldOfView = 120f;          // góc nhìn (độ)
    public LayerMask obstacleMask;             // layer tường/vật cản

    [Header("Rotation")]
    public Transform turretHead;              // phần xoay của turret
    public float rotationSpeed = 5f;
    public float returnSpeed = 2f;            // tốc độ quay về góc mặc định
    public Vector3 defaultDirection = Vector3.forward;

    // State machine
    private enum TurretState { Idle, Aiming, Firing }
    private TurretState state = TurretState.Idle;

    private Transform player;
    private GameObject laserInstance;
    private Hovl_Laser laserScript;
    private Hovl_Laser2 laserScript2;
    private int currentPrefab = 0;

    void Start()
    {
        // Tìm player theo tag
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;
        else
            Debug.LogWarning("TurretController: Không tìm thấy GameObject với tag 'Player'!");
    }

    void Update()
    {
        if (player == null) return;

        bool playerDetected = IsPlayerDetected();

        switch (state)
        {
            case TurretState.Idle:
                ReturnToDefault();
                if (playerDetected) TransitionTo(TurretState.Aiming);
                break;

            case TurretState.Aiming:
                AimAtPlayer();
                if (!playerDetected)
                    TransitionTo(TurretState.Idle);
                else if (IsAimingAtPlayer())
                    TransitionTo(TurretState.Firing);
                break;

            case TurretState.Firing:
                AimAtPlayer();
                if (!playerDetected)
                    TransitionTo(TurretState.Idle);
                break;
        }
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    bool IsPlayerDetected()
    {
        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > detectionRange) return false;

        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, dirToPlayer);
        if (angle > fieldOfView * 0.5f) return false;

        // Kiểm tra line-of-sight (có vật cản không)
        if (Physics.Linecast(FirePoint.transform.position, player.position, obstacleMask))
            return false;

        return true;
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    void AimAtPlayer()
    {
        Vector3 dir = player.position - turretHead.position;
        Quaternion targetRot = Quaternion.LookRotation(dir);
        turretHead.rotation = Quaternion.Slerp(
            turretHead.rotation, targetRot,
            Time.deltaTime * rotationSpeed
        );
    }

    void ReturnToDefault()
    {
        Quaternion targetRot = Quaternion.LookRotation(defaultDirection);
        turretHead.rotation = Quaternion.Slerp(
            turretHead.rotation, targetRot,
            Time.deltaTime * returnSpeed
        );
    }

    bool IsAimingAtPlayer()
    {
        Vector3 dir = (player.position - turretHead.position).normalized;
        return Vector3.Dot(turretHead.forward, dir) > 0.98f; // ~cos(11°)
    }

    // ── State Transitions ─────────────────────────────────────────────────────

    void TransitionTo(TurretState newState)
    {
        // Exit current state
        if (state == TurretState.Firing)
            StopFiring();

        state = newState;

        // Enter new state
        if (state == TurretState.Firing)
            StartFiring();
    }

    void StartFiring()
    {
        if (LaserPrefabs.Length == 0) return;

        laserInstance = Instantiate(
            LaserPrefabs[currentPrefab],
            FirePoint.transform.position,
            FirePoint.transform.rotation
        );
        laserInstance.transform.parent = FirePoint.transform;

        laserScript  = laserInstance.GetComponent<Hovl_Laser>();
        laserScript2 = laserInstance.GetComponent<Hovl_Laser2>();
    }

    void StopFiring()
    {
        if (laserScript)  laserScript.DisablePrepare();
        if (laserScript2) laserScript2.DisablePrepare();
        if (laserInstance) Destroy(laserInstance, 1f);

        laserInstance = null;
        laserScript   = null;
        laserScript2  = null;
    }

    // ── Debug Gizmos ──────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        // Vòng tròn detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Hình nón field-of-view
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Vector3 leftBound  = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward * detectionRange;
        Vector3 rightBound = Quaternion.Euler(0,  fieldOfView * 0.5f, 0) * transform.forward * detectionRange;
        Gizmos.DrawLine(transform.position, transform.position + leftBound);
        Gizmos.DrawLine(transform.position, transform.position + rightBound);
    }
}