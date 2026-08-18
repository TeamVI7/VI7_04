using UnityEngine;

// Continuously tracks which enemy (if any) is currently under the player's
// crosshair — independent of firing, so enemies can react before a shot is
// ever taken (see EnemyDodgeBehaviour). Self-bootstrapping like
// PlayerActionLock — nothing needs to remember to drop this in the scene.
public class PlayerAimThreat : MonoBehaviour
{
    [Header("Detection")]
    public float DetectionRange = 25f;
    [Tooltip("SphereCast radius — wider than a pure raycast so this reads as 'roughly pointed at me', not pixel-perfect on the collider.")]
    public float DetectionRadius = 0.25f;
    [Tooltip("Should match WeaponsController's aimColliderLayerMask so this tracks the same things the actual shot would hit.")]
    public LayerMask HitMask;

    public EnemyHealth CurrentTarget { get; private set; }

    public static PlayerAimThreat Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("PlayerAimThreat (auto)");
                _instance = go.AddComponent<PlayerAimThreat>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    private static PlayerAimThreat _instance;

    private Camera _camera;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) { CurrentTarget = null; return; }

        Ray ray = _camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        CurrentTarget = Physics.SphereCast(ray.origin, DetectionRadius, ray.direction,
            out RaycastHit hit, DetectionRange, HitMask, QueryTriggerInteraction.Ignore)
            ? hit.collider.GetComponentInParent<EnemyHealth>()
            : null;
    }
}
