using UnityEngine;

public class EnemyLookAt : MonoBehaviour
{
    [Header("Bones")]
    public Transform HeadBone;
    public Transform TorsoBone;

    [Header("Look Weights")]
    [Range(0f, 1f)] public float HeadWeight = 0.6f;
    [Range(0f, 1f)] public float TorsoWeight = 0.25f;

    [Header("Limits")]
    public float MaxYawAngle = 70f;
    public float MaxPitchAngle = 35f;
    public float TurnSpeed = 8f;

    [Header("Gating")]
    public EnemyBrain Brain;
    public MeleeAttackBehaviour MeleeGate;
    public SniperAttackBehaviour SniperGate;

    private Quaternion _headStartLocalRot;
    private Quaternion _torsoStartLocalRot;

    private void Awake()
    {
        if (Brain      == null) Brain      = GetComponent<EnemyBrain>();
        if (MeleeGate  == null) MeleeGate  = GetComponent<MeleeAttackBehaviour>();
        if (SniperGate == null) SniperGate = GetComponent<SniperAttackBehaviour>();

        if (HeadBone  != null) _headStartLocalRot  = HeadBone.localRotation;
        if (TorsoBone != null) _torsoStartLocalRot = TorsoBone.localRotation;
    }

    private void LateUpdate()
    {
        if (PlayerHealth.Transform == null) return;
        if (Brain != null && Brain.State == EnemyState.Dead) return; // ragdoll owns bones from here

        bool active = Brain == null || Brain.State == EnemyState.Aggro;

        if (!active || IsGated())
        {
            RelaxBone(HeadBone, _headStartLocalRot);
            RelaxBone(TorsoBone, _torsoStartLocalRot);
            return;
        }

        ApplyLook(HeadBone, _headStartLocalRot, HeadWeight);
        ApplyLook(TorsoBone, _torsoStartLocalRot, TorsoWeight);
    }

    private bool IsGated()
    {
        if (MeleeGate  != null && MeleeGate.IsWindingUp)        return true;
        if (SniperGate != null && SniperGate.IsAimingOrLocking) return true;
        return false;
    }

    private void ApplyLook(Transform bone, Quaternion startLocalRot, float weight)
    {
        if (bone == null || weight <= 0f) return;

        Vector3 toPlayer = PlayerHealth.Transform.position - bone.position;
        if (toPlayer.sqrMagnitude < 0.0001f) return;

        Quaternion worldLook = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        Quaternion parentRot = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
        Quaternion localLook = Quaternion.Inverse(parentRot) * worldLook;

        Quaternion clamped = ClampToLimits(startLocalRot, localLook);
        Quaternion target  = Quaternion.Slerp(startLocalRot, clamped, weight);

        bone.localRotation = Quaternion.Slerp(bone.localRotation, target, TurnSpeed * Time.deltaTime);
    }

    private Quaternion ClampToLimits(Quaternion restLocalRot, Quaternion desiredLocalRot)
    {
        Quaternion delta = Quaternion.Inverse(restLocalRot) * desiredLocalRot;
        Vector3 euler = delta.eulerAngles;

        float yaw   = Mathf.DeltaAngle(0f, euler.y);
        float pitch = Mathf.DeltaAngle(0f, euler.x);

        yaw   = Mathf.Clamp(yaw, -MaxYawAngle, MaxYawAngle);
        pitch = Mathf.Clamp(pitch, -MaxPitchAngle, MaxPitchAngle);

        return restLocalRot * Quaternion.Euler(pitch, yaw, 0f);
    }

    private void RelaxBone(Transform bone, Quaternion startLocalRot)
    {
        if (bone == null) return;
        bone.localRotation = Quaternion.Slerp(bone.localRotation, startLocalRot, TurnSpeed * Time.deltaTime);
    }
}