using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(EnemyHealth))]
public class EnemyHitReaction : MonoBehaviour
{
    [Header("Directional Kick")]
    public Transform VisualRoot;
    public float MaxKickDistance = 0.06f;
    public float KickDuration    = 0.15f;
    public float DamageFractionForMaxKick = 0.35f;
    [Range(0f, 0.2f)]
    public float MinDamageFraction = 0.01f;

    [Header("Torso Reaction")]
    [Tooltip("Spine/chest bone. Optional — leave empty to skip. Twists independently of VisualRoot so a hit reads as the body absorbing the impact, not the whole enemy sliding as one rigid block.")]
    public Transform TorsoBone;
    public float MaxTorsoTwistAngle = 12f;
    public float TorsoTwistDuration = 0.18f;

    [Header("Body Flash")]
    public Renderer[] BodyRenderers;
    public Material FlashMaterial;
    public float FlashDuration = 0.08f;

    [Header("Headshot")]
    public Transform HeadTransform;
    public float HeadshotRadius = 0.35f;
    public Material HeadshotFlashMaterial;
    public float HeadshotFlashDurationMultiplier = 1.75f;

    [Header("Stagger Flash")]
    public Material StaggerFlashMaterial;
    [Tooltip("Seconds between on/off toggles while staggered — this is the 'execute me' blink, active for the whole stagger window, not just one flash. Driven by EnemySSO.HitReactionStaggerFlashDuration.")]
    public float StaggerFlashDuration = 0.25f;

    [System.Serializable]
    public class LimbBone
    {
        public Transform Bone;
        public float HitRadius = 0.25f;
    }

    [Header("Limb Dismember")]
    public LimbBone[] Limbs;
    [Tooltip("Damage fraction (of MaxHP) a single hit needs to reach, on top of landing within a limb's HitRadius, to blow that limb off.")]
    public float LimbDismemberFraction = 0.5f;
    public float LimbScaleDownDuration = 0.15f;

    [Header("Gating")]
    public MeleeAttackBehaviour MeleeGate;
    public SniperAttackBehaviour SniperGate;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    private EnemyHealth _health;
    private Vector3      _visualRootStartLocalPos;
    private Tween         _kickTween;

    private Quaternion _torsoStartLocalRot;
    private Tween        _torsoTween;

    private Material[][] _originalMats;
    private Material[][] _flashMatSets;
    private Material[][] _headshotMatSets;
    private Material[][] _staggerMatSets;

    private Coroutine _flashRoutine;
    private float      _flashEndTime;
    private bool        _staggerFlashActive;
    private Coroutine _staggerBlinkRoutine;

    private HashSet<Transform> _dismemberedBones = new HashSet<Transform>();

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();

        if (VisualRoot == null) VisualRoot = transform;
        _visualRootStartLocalPos = VisualRoot.localPosition;

        if (TorsoBone != null) _torsoStartLocalRot = TorsoBone.localRotation;

        if (MeleeGate  == null) MeleeGate  = GetComponent<MeleeAttackBehaviour>();
        if (SniperGate == null) SniperGate = GetComponent<SniperAttackBehaviour>();

        if (BodyRenderers == null || BodyRenderers.Length == 0)
            BodyRenderers = GetComponentsInChildren<Renderer>();

        BuildMatSets();
    }

    private void BuildMatSets()
    {
        _originalMats = new Material[BodyRenderers.Length][];
        for (int i = 0; i < BodyRenderers.Length; i++)
            _originalMats[i] = BodyRenderers[i] != null ? BodyRenderers[i].sharedMaterials : null;

        _flashMatSets    = BuildSet(FlashMaterial);
        _headshotMatSets = BuildSet(HeadshotFlashMaterial != null ? HeadshotFlashMaterial : FlashMaterial);
        _staggerMatSets  = BuildSet(StaggerFlashMaterial  != null ? StaggerFlashMaterial  : FlashMaterial);
    }

    private Material[][] BuildSet(Material mat)
    {
        var set = new Material[BodyRenderers.Length][];
        if (mat == null) return set;

        for (int i = 0; i < BodyRenderers.Length; i++)
        {
            if (BodyRenderers[i] == null || _originalMats[i] == null) continue;
            var arr = new Material[_originalMats[i].Length];
            for (int s = 0; s < arr.Length; s++) arr[s] = mat;
            set[i] = arr;
        }
        return set;
    }

    private void OnEnable()
    {
        _health.OnDamaged        += HandleDamaged;
        _health.OnDied           += HandleDied;
        _health.OnStaggerEntered += HandleStaggerEntered;
        _health.OnStaggerExpired += HandleStaggerExpired;
    }

    private void OnDisable()
    {
        _health.OnDamaged        -= HandleDamaged;
        _health.OnDied           -= HandleDied;
        _health.OnStaggerEntered -= HandleStaggerEntered;
        _health.OnStaggerExpired -= HandleStaggerExpired;

        _kickTween?.Kill();
        _torsoTween?.Kill();
        if (_flashRoutine != null) { StopCoroutine(_flashRoutine); _flashRoutine = null; }
        if (_staggerBlinkRoutine != null) { StopCoroutine(_staggerBlinkRoutine); _staggerBlinkRoutine = null; }
        _staggerFlashActive = false;
    }

    private void HandleDamaged(float amount, float currentHP, float maxHP, Vector3 hitDirection, Vector3 hitPoint)
    {
        if (!_health.IsAlive) { Log("Damage received but enemy already dead — ignoring."); return; }
        if (IsGated()) { Log("Damage received but gated (melee windup or sniper lock) — reaction skipped."); return; }

        float damageFraction = maxHP > 0f ? amount / maxHP : 0f;
        Log($"Hit received — amount={amount:F1}, maxHP={maxHP:F1}, fraction={damageFraction:F2}, hitPoint={hitPoint}");

        if (damageFraction < MinDamageFraction)
        {
            Log($"Fraction {damageFraction:F2} below MinDamageFraction {MinDamageFraction:F2} — no reaction played.");
            return;
        }

        bool headshot = IsHeadshot(hitPoint);

        PlayKick(hitDirection, headshot ? 1f : damageFraction);
        PlayTorsoReaction(hitDirection, headshot ? 1f : damageFraction);

        if (headshot)
            TriggerFlash(_headshotMatSets, FlashDuration * HeadshotFlashDurationMultiplier, false);
        else
            TriggerFlash(_flashMatSets, FlashDuration, false);

        if (damageFraction >= LimbDismemberFraction)
        {
            LimbBone limb = FindLimbHit(hitPoint);
            if (limb != null) DismemberLimb(limb);
            else Log("No limb matched this hitPoint — check HitRadius, or that Bone references point at real skeleton bones, not hitbox objects.");
        }
        else
        {
            Log($"Fraction {damageFraction:F2} below LimbDismemberFraction {LimbDismemberFraction:F2} — no dismember check performed.");
        }
    }

    private void HandleStaggerEntered()
    {
        if (_staggerBlinkRoutine != null) StopCoroutine(_staggerBlinkRoutine);
        _staggerBlinkRoutine = StartCoroutine(Co_StaggerBlink());
    }

    private void HandleStaggerExpired()
    {
        if (_staggerBlinkRoutine != null) { StopCoroutine(_staggerBlinkRoutine); _staggerBlinkRoutine = null; }
        _staggerFlashActive = false;
        RestoreOriginalMats();
    }

    private IEnumerator Co_StaggerBlink()
    {
        _staggerFlashActive = true;
        bool on = false;

        while (true)
        {
            on = !on;
            if (on) ApplyFlashSet(_staggerMatSets);
            else    RestoreOriginalMats();

            yield return new WaitForSeconds(StaggerFlashDuration);
        }
    }

    private void HandleDied(Vector3 impulse)
    {
        _kickTween?.Kill();
        _torsoTween?.Kill();
        if (_flashRoutine != null) { StopCoroutine(_flashRoutine); _flashRoutine = null; }
        if (_staggerBlinkRoutine != null) { StopCoroutine(_staggerBlinkRoutine); _staggerBlinkRoutine = null; }
        _staggerFlashActive = false;

        // Death can land mid-blink, on the "stagger mat visible" half of the cycle —
        // without this the corpse (and its ragdoll) is stuck wearing the stagger
        // flash material forever, since nothing else ever restores it after this.
        RestoreOriginalMats();
    }

    private bool IsGated()
    {
        if (MeleeGate  != null && MeleeGate.IsWindingUp)        return true;
        if (SniperGate != null && SniperGate.IsAimingOrLocking) return true;
        return false;
    }

    private bool IsHeadshot(Vector3 hitPoint)
    {
        if (HeadTransform == null) return false;
        return (hitPoint - HeadTransform.position).sqrMagnitude <= HeadshotRadius * HeadshotRadius;
    }

    private LimbBone FindLimbHit(Vector3 hitPoint)
    {
        LimbBone closest = null;
        float closestSqr = float.MaxValue;

        if (Limbs == null) return null;

        foreach (var limb in Limbs)
        {
            if (limb == null || limb.Bone == null) continue;
            if (_dismemberedBones.Contains(limb.Bone))
            {
                Log($"Skipping '{limb.Bone.name}' — already dismembered.");
                continue;
            }

            float sqr = (hitPoint - limb.Bone.position).sqrMagnitude;
            if (sqr <= limb.HitRadius * limb.HitRadius && sqr < closestSqr)
            {
                closestSqr = sqr;
                closest = limb;
            }
        }

        if (closest != null)
            Log($"Matched limb '{closest.Bone.name}' — distance={Mathf.Sqrt(closestSqr):F2}m, HitRadius={closest.HitRadius:F2}m.");

        return closest;
    }

    private void DismemberLimb(LimbBone limb)
    {
        if (limb.Bone == null || _dismemberedBones.Contains(limb.Bone)) return;

        _dismemberedBones.Add(limb.Bone);
        limb.Bone.DOKill();
        limb.Bone.DOScale(0f, LimbScaleDownDuration).SetEase(Ease.InBack);

        Log($"Dismembered '{limb.Bone.name}'.");
    }

    private void PlayTorsoReaction(Vector3 hitDirection, float damageFraction)
    {
        if (TorsoBone == null) return;

        Vector3 dir = hitDirection;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Vector3 bendAxis = Vector3.Cross(Vector3.up, dir);
        Vector3 localAxis = TorsoBone.parent != null
            ? TorsoBone.parent.InverseTransformDirection(bendAxis)
            : bendAxis;

        float t = Mathf.Clamp01(damageFraction / Mathf.Max(0.0001f, DamageFractionForMaxKick));
        float angle = Mathf.Lerp(MaxTorsoTwistAngle * 0.2f, MaxTorsoTwistAngle, t);

        _torsoTween?.Kill();
        TorsoBone.localRotation = _torsoStartLocalRot;

        _torsoTween = TorsoBone.DOPunchRotation(localAxis.normalized * angle, TorsoTwistDuration, vibrato: 4, elasticity: 0.3f);
    }

    private void PlayKick(Vector3 hitDirection, float damageFraction)
    {
        if (VisualRoot == null) return;

        Vector3 dir = hitDirection;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Vector3 localDir = VisualRoot.parent != null
            ? VisualRoot.parent.InverseTransformDirection(dir)
            : dir;

        float t = Mathf.Clamp01(damageFraction / Mathf.Max(0.0001f, DamageFractionForMaxKick));
        float kickDistance = Mathf.Lerp(MaxKickDistance * 0.2f, MaxKickDistance, t);

        _kickTween?.Kill();
        VisualRoot.localPosition = _visualRootStartLocalPos;

        _kickTween = VisualRoot.DOPunchPosition(localDir * kickDistance, KickDuration, vibrato: 4, elasticity: 0.3f);
    }

    // Stagger's flash is the loudest signal in the set (it's the "go grapple this
    // one" read) — a regular or headshot flash landing mid-stagger extends nothing
    // and doesn't swap the material set out from under it.
    private void TriggerFlash(Material[][] matSet, float duration, bool isStagger)
    {
        if (matSet == null || BodyRenderers.Length == 0) return;
        if (_staggerFlashActive && !isStagger) return;

        _staggerFlashActive = isStagger;
        _flashEndTime        = Time.time + duration;

        ApplyFlashSet(matSet);

        if (_flashRoutine == null)
            _flashRoutine = StartCoroutine(Co_HoldFlash());
    }

    private IEnumerator Co_HoldFlash()
    {
        while (Time.time < _flashEndTime)
            yield return null;

        RestoreOriginalMats();
        _staggerFlashActive = false;
        _flashRoutine = null;
    }

    private void ApplyFlashSet(Material[][] matSet)
    {
        for (int i = 0; i < BodyRenderers.Length; i++)
        {
            if (BodyRenderers[i] == null || matSet[i] == null) continue;
            BodyRenderers[i].sharedMaterials = matSet[i];
        }
    }

    private void RestoreOriginalMats()
    {
        for (int i = 0; i < BodyRenderers.Length; i++)
        {
            if (BodyRenderers[i] != null && _originalMats[i] != null)
                BodyRenderers[i].sharedMaterials = _originalMats[i];
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[EnemyHitReaction] {name}: {msg}", this);
    }
}