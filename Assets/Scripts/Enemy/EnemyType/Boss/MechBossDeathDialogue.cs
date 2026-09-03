// MechBossDeathDialogue.cs — plays a dialogue sequence once the mech is dead.
//
// Hangs off EnemyHealth.OnDied, but does NOT run the wait itself. The boss is torn
// down shortly after it dies — the corpse despawns, the sliced halves take over, the
// root gets stripped — and any coroutine still sitting on it dies with it. So the
// moment death lands, this hands the job to a detached runner that outlives the boss
// and talks to the DialogueManager singleton directly.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(EnemyHealth))]
[DisallowMultipleComponent]
public class MechBossDeathDialogue : MonoBehaviour
{
    #region Inspector

    [Header("Dialogue")]
    [Tooltip("The sequence to play. Create one via Assets > Create > Dialogue > Dialogue Sequence.")]
    public DialogueData dialogue;

    [Tooltip("Seconds between the mech dying and the dialogue opening. Give the death its own moment first — the explosions, the collapse, the music dropping out — or the line lands on top of them. Counted in real time, so the execute's slow-mo doesn't stretch it.")]
    public float delay = 3f;

    [Header("Conflicts")]
    [Tooltip("If another dialogue is already on screen, wait for it to close instead of being silently dropped. DialogueManager.Play no-ops while one is active, so without this the boss line can just never appear.")]
    public bool waitForActiveDialogue = true;
    [Tooltip("How long to wait for that before giving up, so a dialogue that never closes can't leave this hanging forever.")]
    public float waitTimeout = 15f;

    [Header("Events")]
    [Tooltip("Fires when the boss dialogue closes — the natural place to hook the extraction trigger, the level-complete state, or the next objective.")]
    public UnityEvent onDialogueFinished;

    [Header("Debug")]
    public bool debugLog;

    #endregion

    private EnemyHealth _health;
    private bool _fired;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _health.OnDied += HandleDied;

        if (dialogue == null)
            Debug.LogWarning($"[{nameof(MechBossDeathDialogue)}] {name} has no Dialogue assigned — nothing will play on death.", this);
    }

    private void OnDestroy()
    {
        if (_health != null) _health.OnDied -= HandleDied;
    }

    private void HandleDied(Vector3 impulse)
    {
        // OnDied is raised once, but a boss that somehow took lethal damage twice in a
        // frame would otherwise open two of these.
        if (_fired) return;
        _fired = true;

        if (dialogue == null) return;

        var go = new GameObject($"{name}_DeathDialogue");
        go.AddComponent<BossDialogueRunner>()
          .Run(dialogue, delay, waitForActiveDialogue, waitTimeout, onDialogueFinished, debugLog);

        if (debugLog)
            Debug.Log($"[{nameof(MechBossDeathDialogue)}] {name} died — dialogue queued in {delay:0.##}s.", this);
    }
}

/// <summary>
/// Runs the post-death dialogue from outside the boss's hierarchy, so nothing about
/// the corpse being cleaned up can interrupt it.
/// </summary>
public class BossDialogueRunner : MonoBehaviour
{
    public void Run(DialogueData data, float delay, bool waitForActive, float timeout,
                    UnityEvent onFinished, bool log)
    {
        StartCoroutine(Co_Run(data, delay, waitForActive, timeout, onFinished, log));
    }

    private IEnumerator Co_Run(DialogueData data, float delay, bool waitForActive, float timeout,
                               UnityEvent onFinished, bool log)
    {
        // Realtime, not scaled: the killing blow may well have been an execute, and
        // that runs in slow-mo — a scaled wait would stretch with it.
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        DialogueManager manager = DialogueManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning($"[{nameof(BossDialogueRunner)}] No DialogueManager in the scene — the boss death dialogue can't play.", this);
            Destroy(gameObject);
            yield break;
        }

        if (waitForActive)
        {
            float waited = 0f;
            while (manager.IsActive && waited < timeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // Subscribed before Play, because a zero-line sequence closes immediately.
        bool closed = false;
        Action handler = () => closed = true;
        manager.OnDialogueClosed += handler;

        manager.Play(data);

        // Play() returns without doing anything if the manager is busy or the sequence
        // is empty. Detect that rather than waiting on a close that will never come.
        if (!manager.IsActive)
        {
            manager.OnDialogueClosed -= handler;
            Debug.LogWarning($"[{nameof(BossDialogueRunner)}] DialogueManager didn't accept the boss dialogue — " +
                             "it's either still showing another sequence, or this DialogueData has no lines.", this);
            Destroy(gameObject);
            yield break;
        }

        if (log) Debug.Log($"[{nameof(BossDialogueRunner)}] Boss death dialogue playing ({data.lines.Length} line(s)).", this);

        while (!closed) yield return null;

        manager.OnDialogueClosed -= handler;
        onFinished?.Invoke();
        Destroy(gameObject);
    }
}
