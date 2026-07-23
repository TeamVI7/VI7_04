using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class MechChasePlayer : MonoBehaviour
{
    public Transform player;
    public float chaseRange = 35f;
    public float stopDistance = 5f;
    public float turnSpeed = 240f;

    NavMeshAgent agent;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        // We rotate the whole mech ourselves for a heavier turn.
        agent.updateRotation = false;
        agent.stoppingDistance = stopDistance;
    }

    void Start()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
                player = playerObject.transform;
        }
    }

    void Update()
    {
        if (player == null)
            return;

        Vector3 flatOffset = player.position - transform.position;
        flatOffset.y = 0f;

        bool playerIsNear = flatOffset.magnitude <= chaseRange;
        bool shouldMove = playerIsNear && flatOffset.magnitude > stopDistance;

        agent.isStopped = !shouldMove;

        if (!shouldMove)
            return;

        agent.SetDestination(player.position);

        // Turn toward the direction it is actually trying to travel.
        Vector3 direction = agent.desiredVelocity;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                turnSpeed * Time.deltaTime);
        }
    }
}