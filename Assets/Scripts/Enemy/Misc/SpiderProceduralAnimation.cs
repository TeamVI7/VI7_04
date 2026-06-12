using UnityEngine;

public class ProceduralSpiderDrone : MonoBehaviour
{
    [System.Serializable]
    public class SpiderLeg
    {
        public Transform IKTarget;      // The target transform assigned to the Two-Bone IK
        public Transform RaycastOrigin; // Empty GameObject on the chassis casting downward
        
        [HideInInspector] public Vector3 CurrentRestPosition;
        [HideInInspector] public Vector3 OldPosition;
        [HideInInspector] public float   StepLerp;
        [HideInInspector] public bool    IsStepping;
    }

    [Header("Leg Setup")]
    [Tooltip("Order: 0=FrontLeft, 1=FrontRight, 2=BackLeft, 3=BackRight")]
    public SpiderLeg[] Legs; 

    [Header("Movement Parameters")]
    public float StepDistance = 0.6f;
    public float StepHeight   = 0.3f;
    public float StepSpeed    = 6f;
    public float RaycastRange = 3f;
    public LayerMask GroundLayer;

    // 0: FrontLeft(0) & BackRight(3)
    // 1: FrontRight(1) & BackLeft(2)
    private int _currentlyMovingGroup = 0; 

    private void Start()
    {
        // Initialize all feet to the ground beneath them
        foreach (var leg in Legs)
        {
            if (PerformRaycast(leg.RaycastOrigin, out Vector3 hitPoint))
            {
                leg.CurrentRestPosition = hitPoint;
                leg.IKTarget.position   = hitPoint;
            }
        }
    }

    private void Update()
    {
        Debug.Log("Update is ticking. Legs array size: " + Legs.Length);
        CheckAndTriggerSteps();
        ProcessStepping();
    }

    private void CheckAndTriggerSteps()
    {
        // Prevent triggering new steps if the current group is still in motion
        bool isGroupMoving = false;
        for (int i = 0; i < Legs.Length; i++)
        {
            if (Legs[i].IsStepping && GetLegGroup(i) == _currentlyMovingGroup)
            {
                isGroupMoving = true;
                break;
            }
        }

        if (!isGroupMoving)
        {
            int nextGroup = 1 - _currentlyMovingGroup;
            bool needsStep = false;

            // Check if any leg in the resting group has stretched too far
            for (int i = 0; i < Legs.Length; i++)
            {
                if (GetLegGroup(i) == nextGroup)
                {
                    if (PerformRaycast(Legs[i].RaycastOrigin, out Vector3 idealPoint))
                    {
                        if (Vector3.Distance(Legs[i].CurrentRestPosition, idealPoint) > StepDistance)
                        {
                            needsStep = true;
                            break;
                        }
                    }
                }
            }

            // Trigger the step for the entire group
            if (needsStep)
            {
                _currentlyMovingGroup = nextGroup;
                for (int i = 0; i < Legs.Length; i++)
                {
                    if (GetLegGroup(i) == _currentlyMovingGroup)
                    {
                        if (PerformRaycast(Legs[i].RaycastOrigin, out Vector3 newRestPoint))
                        {
                            // Optional: Calculate velocity vector to predict where the foot SHOULD land
                            // Vector3 overstep = (newRestPoint - Legs[i].CurrentRestPosition).normalized * (StepDistance * 0.25f);
                            
                            Legs[i].OldPosition         = Legs[i].CurrentRestPosition;
                            Legs[i].CurrentRestPosition = newRestPoint; // + overstep
                            Legs[i].StepLerp            = 0f;
                            Legs[i].IsStepping          = true;
                        }
                    }
                }
            }
        }
    }

    private void ProcessStepping()
    {
        for (int i = 0; i < Legs.Length; i++)
        {
            if (Legs[i].IsStepping)
            {
                Legs[i].StepLerp += Time.deltaTime * StepSpeed;
                
                if (Legs[i].StepLerp >= 1f)
                {
                    Legs[i].StepLerp   = 1f;
                    Legs[i].IsStepping = false;
                }

                // Interpolate horizontally
                Vector3 horizontalPos = Vector3.Lerp(Legs[i].OldPosition, Legs[i].CurrentRestPosition, Legs[i].StepLerp);
                
                // Add vertical arc using a Sine wave (0 to PI)
                float arcY = Mathf.Sin(Legs[i].StepLerp * Mathf.PI) * StepHeight;
                
                Legs[i].IKTarget.position = new Vector3(horizontalPos.x, horizontalPos.y + arcY, horizontalPos.z);
            }
        }
    }

    private int GetLegGroup(int index)
    {
        return (index == 0 || index == 3) ? 0 : 1;
    }

    private bool PerformRaycast(Transform origin, out Vector3 hitPoint)
    {
        Vector3 rayStart = origin.position + Vector3.up * 1f;
        
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, RaycastRange + 1f, GroundLayer))
        {
            // Draws a green line if it successfully hits your defined GroundLayer
            Debug.DrawLine(rayStart, hit.point, Color.green, 0.1f);
            hitPoint = hit.point;
            return true;
        }
        
        // Draws a red line if the raycast hits nothing or hits the wrong layer
        Debug.DrawRay(rayStart, Vector3.down * (RaycastRange + 1f), Color.red, 0.1f);
        hitPoint = origin.position - Vector3.up * RaycastRange;
        return false;
    }
}