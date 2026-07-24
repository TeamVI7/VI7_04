// MechAttackSelector.cs
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MechBossBrain))]
public class MechAttackSelector : MonoBehaviour
{
    [SerializeField] private bool showDebugLogs = true;

    private MechBossBrain _brain;
    private MechAttackBehaviour[] _attacks;

    private void Awake()
    {
        _brain = GetComponent<MechBossBrain>();
        _attacks = GetComponents<MechAttackBehaviour>();
    }

    private void Update()
    {
        if (_brain.State != MechBossState.Idle) return;
        if (PlayerHealth.Transform == null) return;

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        var chosen = PickAttack(dist);
        if (chosen == null) return;

        _brain.NotifyAttackStart();
        chosen.BeginCooldown();
        Log($"Executing {chosen.GetType().Name}");
        chosen.Execute(() => _brain.NotifyAttackEnd());
    }

    private MechAttackBehaviour PickAttack(float dist)
    {
        var valid = new List<MechAttackBehaviour>();
        float totalWeight = 0f;
        foreach (var a in _attacks)
        {
            if (!a.IsAvailable(dist)) continue;
            valid.Add(a);
            totalWeight += a.weight;
        }
        if (valid.Count == 0) return null;

        float roll = Random.Range(0f, totalWeight);
        float sum = 0f;
        foreach (var a in valid)
        {
            sum += a.weight;
            if (roll <= sum) return a;
        }
        return valid[valid.Count - 1];
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[MechAttackSelector] {msg}", this);
    }
}