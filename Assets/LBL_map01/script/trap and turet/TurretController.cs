using UnityEngine;

public class TurretController : MonoBehaviour
{
    [Header("References")]
    public GameObject FirePoint;
    public GameObject[] LaserPrefabs;

    [Header("Detection")]
    public float detectionRange = 20f;
    public float fieldOfView = 120f;
    public LayerMask obstacleMask;

    [Header("Rotation")]
    public Transform turretHead;
    public float rotationSpeed = 5f;
    public float returnSpeed = 2f;

    // Không cần public defaultDirection nữa
    private Quaternion defaultRotation; // ✅ Lưu rotation lúc Start

    private enum TurretState { Idle, Aiming, Firing }
    private TurretState state = TurretState.Idle;

    private Transform player;
    private GameObject laserInstance;
    private Hovl_Laser laserScript;
    private Hovl_Laser2 laserScript2;
    private int currentPrefab = 0;

    void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;
        else
            Debug.LogWarning("TurretController: Không tìm thấy GameObject với tag 'Player'!");

        // ✅ Lưu lại rotation của nòng súng lúc bắt đầu game
        defaultRotation = turretHead.rotation;
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

    bool IsPlayerDetected()
{
    float dist = Vector3.Distance(turretHead.position, player.position);
    
  //  Debug.Log($"[Turret] TurretPos: {turretHead.position} | PlayerPos: {player.position} | Dist: {dist:F1}");
    
    if (dist > detectionRange) return false;

    Vector3 dirToPlayer = (player.position - turretHead.position).normalized;
    float angle = Vector3.Angle(turretHead.forward, dirToPlayer);
   // Debug.Log($"[Turret] Góc: {angle:F1} / FOV: {fieldOfView * 0.5f}");
    if (angle > fieldOfView * 0.5f) return false;

    bool blocked = Physics.Linecast(FirePoint.transform.position, player.position, obstacleMask);
  //  Debug.Log($"[Turret] Bị chặn: {blocked}");
    if (blocked) return false;

   // Debug.Log("[Turret] ✅ Phát hiện player!");
    return true;
}

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
        // ✅ Quay về đúng rotation lúc đặt trong scene
        turretHead.rotation = Quaternion.Slerp(
            turretHead.rotation, defaultRotation,
            Time.deltaTime * returnSpeed
        );
    }

    bool IsAimingAtPlayer()
    {
        Vector3 dir = (player.position - turretHead.position).normalized;
        return Vector3.Dot(turretHead.forward, dir) > 0.98f;
    }

    void TransitionTo(TurretState newState)
    {
        if (state == TurretState.Firing)
            StopFiring();

        state = newState;

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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        // ✅ Dùng turretHead.forward cho gizmo cũng khớp với detection
        Vector3 forward = turretHead != null ? turretHead.forward : transform.forward;
        Vector3 leftBound  = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * forward * detectionRange;
        Vector3 rightBound = Quaternion.Euler(0,  fieldOfView * 0.5f, 0) * forward * detectionRange;
        Gizmos.DrawLine(transform.position, transform.position + leftBound);
        Gizmos.DrawLine(transform.position, transform.position + rightBound);
    }
}