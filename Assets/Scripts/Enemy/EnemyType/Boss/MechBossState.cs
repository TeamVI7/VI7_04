using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using DG.Tweening;
using FIMSpace.FProceduralAnimation;

public enum MechBossState { Dropping, Idle, Attacking, Staggered, Dead }

[RequireComponent(typeof(EnemyHealth))]
public class MechBossBrain : MonoBehaviour
{
    #region Inspector

    [Header("Drop Intro")]
    public Animator animator;
    public Transform visualRoot;
    [Tooltip("If true, the mech spawns already at ground level and skips the aerial teleport-up-and-fall — it just plays the impact squash/animation in place. Use this when the reveal is already sold some other way (e.g. a ceiling explosion + camera swing cutscene) so you don't get a redundant physical drop on top of it.")]
    public bool skipAerialDrop = false;
    public float dropHeight = 20f;
    public float dropDuration = 0.8f;
    public Ease dropEase = Ease.InQuad;

    [Header("Drop Intro — Stand-In")]
    [Tooltip("Optional, and the recommended way to do the drop. A separate object with its own Animator that performs the whole drop-in/landing in the boss's place. The real mech stays invisible and completely untouched until the landing settles, so none of its gameplay systems (NavMeshAgent, LegsAnimator, colliders) have to survive being flung through the air.\n\nMake it an INACTIVE CHILD of the boss prefab — a prefab field can't reference a scene object, and the flow controller spawns the boss as a clone. Overrides both the aerial drop and Skip Aerial Drop when assigned.")]
    public GameObject dropStandIn;
    [Tooltip("Animator trigger fired on the stand-in when the drop begins. Leave empty if its clip already plays automatically when the object activates.")]
    public string standInTrigger = "DropIn";
    [Tooltip("Tick this if your stand-in clip is only the LANDING (it plays in place, at ground level). The brain then lifts the stand-in to Drop Height and tweens it down over Drop Duration itself, using the Drop Ease above.\n\nLeave it off if the clip already contains the fall — via root motion or an animated root — in which case the clip does all the moving and Stand In Impact Time is what decides when impact fires.")]
    public bool standInFallsFromHeight = false;
    [Tooltip("Seconds from the stand-in activating to the moment of impact — when OnIntroImpact fires, slow-mo ends and the camera shakes. Match this to the landing frame of your clip. Ignored when Stand In Falls From Height is on, since Drop Duration decides it there.")]
    public float standInImpactTime = 1.2f;
    [Tooltip("Seconds after impact before the stand-in is swapped out for the real mech. Give the landing pose time to settle so the swap reads as one continuous shot.")]
    public float standInSettleTime = 0.8f;

    [Header("Impact Squash")]
    public Vector3 squashPunch = new Vector3(0.2f, -0.25f, 0.2f);
    public float squashDuration = 0.35f;
    public int squashVibrato = 6;
    public float squashElasticity = 0.6f;

    [Header("Phases")]
    [Tooltip("HP fraction that triggers each phase-up, in order. One entry = 2 phases, two entries = 3 phases, etc.")]
    [Range(0.01f, 1f)] public float[] phaseHealthThresholds = { 0.5f, 0.25f };
    [Tooltip("Animator trigger fired on each phase-up, index-matched to phaseHealthThresholds (element 0 = the phase 2 transition, element 1 = phase 3, ...). Entries left empty — or missing because the array is shorter than the thresholds — fall back to \"PhaseUp\".")]
    public string[] phaseUpTriggers = { "PhaseUp", "PhaseUp3" };
    [Tooltip("Extra seconds to hold the boss in the phase-up gate after the squash, index-matched like phaseUpTriggers. Use it to keep the mech from chasing/attacking while a longer phase animation plays out.")]
    public float[] phaseTransitionHolds = { 0f, 1.5f };
    [Tooltip("Every attack's cooldown is multiplied by this, indexed by phase (element 0 = phase 1, element 1 = phase 2, ...). Below 1 means the boss re-attacks sooner, i.e. more aggressive. Phases past the end of the array reuse the last entry.")]
    public float[] phaseCooldownMultipliers = { 1f, 0.8f, 0.5f };
    public Vector3 phaseTransitionPunch = new Vector3(0.3f, -0.3f, 0.3f);
    public float phaseTransitionDuration = 0.4f;

    [Header("Refs")]
    public NavMeshAgent agent;
    public Collider mainCollider;
    [Tooltip("The procedural foot/hip IK solver (FIMSpace LegsAnimator). Disabled during the drop and re-enabled on landing — otherwise it fights the fast fall trying to keep feet grounded, which is what causes the hip/leg 'staggering' glitch while airborne.")]
    public LegsAnimator legsAnimator;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    #endregion

    #region Events

    public MechBossState State { get; private set; } = MechBossState.Dropping;
    public int Phase { get; private set; } = 1;

    /// <summary>Scale applied to every attack's cooldown for the phase the boss is
    /// currently in — the single lever for "this phase is more aggressive".
    /// Read by <see cref="MechAttackBehaviour.BeginCooldown"/>.</summary>
    public float CooldownMultiplier
    {
        get
        {
            if (phaseCooldownMultipliers == null || phaseCooldownMultipliers.Length == 0) return 1f;
            int i = Mathf.Clamp(Phase - 1, 0, phaseCooldownMultipliers.Length - 1);
            // Floor it — a 0 here would let the selector re-fire the same attack
            // every frame, which reads as a stuck animation rather than aggression.
            return Mathf.Max(0.05f, phaseCooldownMultipliers[i]);
        }
    }

    public event Action<MechBossState, MechBossState> OnStateChanged;
    public event Action<int, int> OnPhaseChanged;
    public event Action OnIntroStart;
    public event Action OnIntroImpact;
    public event Action OnIntroComplete;

    #endregion

    /// <summary>What an intro camera should actually be watching right now — the
    /// falling stand-in while it's up, the real mech once it's been swapped in.</summary>
    public Transform IntroFocus =>
        (dropStandIn != null && dropStandIn.activeSelf) ? dropStandIn.transform : visualRoot;

    private EnemyHealth _health;
    private MechIntroCutscene _cutsceneDirector;
    private MechAttackBehaviour[] _attacks;
    private Renderer[] _bodyRenderers;
    private Animator _standInAnimator;
    private int _standInTriggerHash;
    private bool _introDone;
    private bool _introStarted;
    private bool _animSignal_ImpactLand;
    private int _phaseIndex;
    private static readonly int AnimDropTrigger = Animator.StringToHash("DropIn");
    private static readonly int AnimPhaseUp = Animator.StringToHash("PhaseUp");

    #region Unity Lifecycle

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _cutsceneDirector = GetComponent<MechIntroCutscene>();
        _attacks = GetComponents<MechAttackBehaviour>();
        _health.OnDied += _ => SetState(MechBossState.Dead);
        _health.OnStaggerEntered += () => SetState(MechBossState.Staggered);
        _health.OnStaggerExpired += () => SetState(MechBossState.Idle);
        _health.OnDamaged += (amount, curHP, maxHP, dir, point) => CheckPhaseTransition(curHP, maxHP);

        if (visualRoot == null && animator != null) visualRoot = animator.transform;

        if (agent != null) agent.enabled = false;
        if (mainCollider != null) mainCollider.enabled = false;

        if (dropStandIn != null)
        {
            CacheBodyRenderers();
            _standInAnimator = dropStandIn.GetComponentInChildren<Animator>(true);
            if (!string.IsNullOrEmpty(standInTrigger)) _standInTriggerHash = Animator.StringToHash(standInTrigger);
            dropStandIn.SetActive(false);
        }

        // Deliberately NOT disabling legsAnimator here. A MonoBehaviour disabled
        // in Awake never gets its Start called, and LegsAnimator assigns its own
        // baseTransform there — leaving it null makes every later call into the
        // solver throw UnassignedReferenceException. Co_Intro suppresses it after
        // a frame instead, once it has initialized itself.
    }

    // If a MechIntroCutscene sits on this boss, it takes charge of *when* the drop
    // starts (so it can whip-pan the camera into place and lock the player first)
    // by calling BeginIntro() itself. Without one, the boss behaves exactly as before.
    private void Start()
    {
        if (_cutsceneDirector == null) BeginIntro();
    }

    #endregion

    public void AnimEvent_ImpactLand() => _animSignal_ImpactLand = true;

    #region Intro

    /// <summary>Starts the drop-in sequence. Safe to call once; a MechIntroCutscene
    /// calls this after it has locked the player and cut the camera into place.</summary>
    public void BeginIntro()
    {
        if (_introStarted) return;
        _introStarted = true;
        StartCoroutine(Co_Intro());
    }

    private IEnumerator Co_Intro()
    {
        OnIntroStart?.Invoke();

        // One frame so every Start() on the prefab has run — specifically
        // LegsAnimator's, which is what assigns its baseTransform.
        yield return null;

        bool usingStandIn = dropStandIn != null;

        // Only worth suppressing the procedural legs if the mech is actually
        // going to be yanked through the air. With skipAerialDrop, or with a
        // stand-in doing the falling, the real mech never moves — so there is
        // nothing for the solver to fight and no reason to touch it at all.
        bool suppressLegs = legsAnimator != null && !skipAerialDrop && !usingStandIn;
        if (suppressLegs) legsAnimator.enabled = false;

        if (usingStandIn)
        {
            SetBodyVisible(false);

            Vector3 landingPos = transform.position;
            dropStandIn.transform.SetPositionAndRotation(landingPos, transform.rotation);
            dropStandIn.SetActive(true);

            if (_standInAnimator != null && _standInTriggerHash != 0)
                _standInAnimator.SetTrigger(_standInTriggerHash);

            if (standInFallsFromHeight)
            {
                // The clip is a landing-only pose, so drive the fall here. Safe to
                // throw this object around in a way the real mech can't be — it
                // has no agent, no solver and no colliders to knock out of sync.
                dropStandIn.transform.position = landingPos + Vector3.up * dropHeight;
                yield return dropStandIn.transform
                    .DOMove(landingPos, dropDuration)
                    .SetEase(dropEase)
                    .WaitForCompletion();
            }
            else
            {
                yield return new WaitForSeconds(standInImpactTime);
            }
        }
        else if (!skipAerialDrop)
        {
            Vector3 groundPos = transform.position;
            transform.position = groundPos + Vector3.up * dropHeight;

            if (animator != null) animator.SetTrigger(AnimDropTrigger);

            yield return transform
                .DOMove(groundPos, dropDuration)
                .SetEase(dropEase)
                .WaitForCompletion();
        }
        else if (animator != null)
        {
            animator.SetTrigger(AnimDropTrigger);
        }

        Log("Impact.");
        OnIntroImpact?.Invoke();

        if (usingStandIn)
        {
            // Let the landing pose settle, then hand off. Both halves of the swap
            // happen in the same frame so there's never a gap with nothing on
            // screen — and because the stand-in landed on the mech's own
            // transform, the two line up exactly.
            yield return new WaitForSeconds(standInSettleTime);

            dropStandIn.SetActive(false);
            SetBodyVisible(true);
            Log("Stand-in swapped out for the real mech.");
        }
        else
        {
            if (visualRoot != null)
            {
                yield return visualRoot
                    .DOPunchScale(squashPunch, squashDuration, squashVibrato, squashElasticity)
                    .WaitForCompletion();
            }

            // Only meaningful for the real mech's own drop clip — a stand-in has
            // no AnimEvent_ImpactLand hookup, so waiting here would just burn
            // the full 1s timeout every time.
            float waited = 0f;
            while (!_animSignal_ImpactLand && waited < 1f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        if (mainCollider != null) mainCollider.enabled = true;
        if (agent != null)
        {
            agent.enabled = true;

            // If the landing spot isn't on a baked NavMesh, the agent silently
            // never comes online — MechChaseBehaviour/MechAttackSelector both
            // no-op forever (isOnNavMesh guard), so the boss just stands there
            // with no error unless you're watching for it. Snap it onto the
            // nearest valid NavMesh point instead of failing silently.
            if (!agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
                {
                    agent.Warp(navHit.position);
                    Log($"Agent wasn't on the NavMesh at landing position — warped to nearest point ({Vector3.Distance(transform.position, navHit.position):F2}m away).");
                }
                else
                {
                    Debug.LogWarning("[MechBossBrain] Landing position has no NavMesh within 10m — the boss will not be able to chase/attack. Check NavMesh baking around the boss spawn point.", this);
                }
            }
        }
        RestoreLegsAnimator(suppressLegs);

        _introDone = true;
        OnIntroComplete?.Invoke();
        SetState(MechBossState.Idle);
        Log("Intro complete.");
    }

    /// <summary>Caches the mech's own renderers so the body can be hidden while the
    /// stand-in does the drop. Renderers are toggled rather than the GameObject
    /// deactivated — visualRoot can resolve to this very object, and SetActive on
    /// it would kill the coroutine running this intro.</summary>
    private void CacheBodyRenderers()
    {
        var found = GetComponentsInChildren<Renderer>(true);
        var body = new List<Renderer>(found.Length);

        foreach (var r in found)
        {
            // The stand-in is expected to be a child of the boss prefab, so keep
            // it out of the body set — it manages its own visibility.
            if (r.transform.IsChildOf(dropStandIn.transform)) continue;
            // Only ones already on, so restoring can't switch on something that
            // was intentionally left disabled.
            if (!r.enabled) continue;
            body.Add(r);
        }

        _bodyRenderers = body.ToArray();
    }

    private void SetBodyVisible(bool visible)
    {
        if (_bodyRenderers == null) return;
        foreach (var r in _bodyRenderers)
            if (r != null) r.enabled = visible;
    }

    private void RestoreLegsAnimator(bool wasSuppressed)
    {
        if (legsAnimator == null || !wasSuppressed) return;

        try
        {
            // Not just enabled = true — that leaves the procedural legs/hips to
            // interpolate out of their stale pose over several frames, which is
            // what reads as staggering with wrong hips. User_Teleport re-enables
            // AND forces an instant recalibration to the mech's resting position.
            // It only works once the solver has initialized; before that it
            // dereferences a null baseTransform.
            if (legsAnimator.LegsInitialized) legsAnimator.User_Teleport(transform.position);
            else legsAnimator.enabled = true;
        }
        catch (Exception e)
        {
            // Never let a third-party solver fault strand the intro. If this
            // throws uncaught, _introDone stays false — the boss never leaves
            // Dropping, and any cutscene waiting on OnIntroComplete leaves the
            // player stuck watching a camera they don't control.
            Debug.LogError($"[MechBossBrain] LegsAnimator restore failed, continuing without it: {e.Message}", this);
            legsAnimator.enabled = true;
        }
    }

    #endregion

    #region Phase

    private void CheckPhaseTransition(float currentHP, float maxHP)
    {
        if (!_introDone || State == MechBossState.Dead) return;
        if (phaseHealthThresholds == null || _phaseIndex >= phaseHealthThresholds.Length) return;

        float fraction = maxHP > 0f ? currentHP / maxHP : 0f;
        if (fraction > phaseHealthThresholds[_phaseIndex]) return;

        // One big hit — an execute, a full barrage — can cross several thresholds
        // at once. Advancing a single step per damage event left the boss sitting
        // in phase 2 at 15% HP until it happened to be hit again.
        int prev = Phase;
        while (_phaseIndex < phaseHealthThresholds.Length && fraction <= phaseHealthThresholds[_phaseIndex])
            _phaseIndex++;

        Phase = _phaseIndex + 1;

        // Index of the LAST transition crossed, so the animation and hold that play
        // are the ones authored for the phase the boss actually ends up in.
        int transitionIndex = _phaseIndex - 1;

        Log($"Phase {prev} -> {Phase}" + (Phase - prev > 1 ? " (skipped a phase — big hit)" : ""));
        OnPhaseChanged?.Invoke(prev, Phase);
        StartCoroutine(Co_PhaseTransition(transitionIndex));
    }

    private IEnumerator Co_PhaseTransition(int transitionIndex)
    {
        // Only borrow the Attacking gate if the boss is actually Idle right now.
        // If it's mid-attack or Staggered, forcing it to Attacking-then-Idle here
        // would end that attack's/stagger's gate early (letting the selector fire
        // a second attack on top of the first, or cutting the stagger window short).
        bool borrowedGate = State == MechBossState.Idle;
        if (borrowedGate) NotifyAttackStart();

        if (animator != null) animator.SetTrigger(ResolvePhaseTrigger(transitionIndex));

        if (visualRoot != null)
        {
            yield return visualRoot
                .DOPunchScale(phaseTransitionPunch, phaseTransitionDuration, squashVibrato, squashElasticity)
                .WaitForCompletion();
        }

        // The squash is only ~0.4s; a bespoke phase animation (the phase 3 roar,
        // armour shedding, etc.) usually runs longer than that. Holding the gate
        // keeps the selector and the chase from cutting it off mid-play.
        float hold = phaseTransitionHolds != null && transitionIndex < phaseTransitionHolds.Length
            ? phaseTransitionHolds[transitionIndex]
            : 0f;
        if (hold > 0f) yield return new WaitForSeconds(hold);

        if (borrowedGate) NotifyAttackEnd();
    }

    /// <summary>Animator trigger for the given phase transition, falling back to
    /// the shared "PhaseUp" when no per-phase override is set.</summary>
    private int ResolvePhaseTrigger(int transitionIndex)
    {
        if (phaseUpTriggers != null
            && transitionIndex < phaseUpTriggers.Length
            && !string.IsNullOrEmpty(phaseUpTriggers[transitionIndex]))
        {
            return Animator.StringToHash(phaseUpTriggers[transitionIndex]);
        }
        return AnimPhaseUp;
    }

    #endregion

    #region Attack State Bridge

    public void NotifyAttackStart() => SetState(MechBossState.Attacking);

    public void NotifyAttackEnd()
    {
        if (State == MechBossState.Attacking) SetState(MechBossState.Idle);
    }

    #endregion

    private void SetState(MechBossState next)
    {
        if (!_introDone && next != MechBossState.Dead) return;
        // Dead is terminal. Nothing should be able to walk the boss back out of it.
        if (State == MechBossState.Dead && next != MechBossState.Dead) return;
        if (State == next) return;

        var prev = State;
        State = next;

        // Set the state *before* aborting, so each attack's completion callback
        // sees Staggered/Dead and NotifyAttackEnd correctly declines to flip the
        // boss back to Idle mid-stagger.
        if (next == MechBossState.Staggered || next == MechBossState.Dead) AbortAttacks();

        OnStateChanged?.Invoke(prev, next);
        Log($"State: {prev} -> {next}");
    }

    /// <summary>Cuts short whatever the boss was in the middle of. Without this a
    /// stagger only changed the state flag: the running attack coroutine carried on
    /// to completion, so a staggered boss kept firing, and its late NotifyAttackEnd
    /// dropped the boss back to Idle where the selector could start a *second*
    /// attack on top of the first.</summary>
    private void AbortAttacks()
    {
        if (_attacks == null) return;

        foreach (var attack in _attacks)
        {
            if (attack == null || !attack.IsExecuting) continue;
            Log($"Aborting {attack.GetType().Name}.");
            attack.Abort();
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[MechBossBrain] {msg}", this);
    }
}