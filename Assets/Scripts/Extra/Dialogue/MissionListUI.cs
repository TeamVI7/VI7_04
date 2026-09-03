using DG.Tweening;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MissionListUI : MonoBehaviour
{
    public static MissionListUI Instance { get; private set; }

    [SerializeField] private Transform listContainer;
    [SerializeField] private GameObject missionEntryPrefab; // simple TMP_Text prefab
    [SerializeField] private float strikeDuration = 0.3f;
    [SerializeField] private float holdBeforeFade = 0.6f;
    [SerializeField] private float fadeOutDuration = 0.5f;

    private readonly Dictionary<string, GameObject> entries = new();

    /// <summary>False when this component is missing the references it needs to build rows.
    /// map1 contains a second, unwired MissionListUI, and because OnMissionsChanged is a
    /// static event that copy received every mission event too — then threw on
    /// Instantiate(null), taking down whatever raised the event. Checked rather than
    /// assumed so a stray duplicate is inert instead of fatal.</summary>
    private bool IsConfigured => missionEntryPrefab != null && listContainer != null;

    private bool _warnedUnconfigured;

    void Awake()
    {
        // An unwired duplicate must not claim the singleton — PlayCompleteThenRemove would
        // then run against a component that has no rows and silently drop the callback that
        // removes the mission.
        if (Instance == null || !Instance.IsConfigured) Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()  => MissionManager.OnMissionsChanged += Rebuild;
    void OnDisable() => MissionManager.OnMissionsChanged -= Rebuild;

    /// <summary>
    /// Syncs the visible rows to <paramref name="missions"/>: adds what is new and removes
    /// what is gone.
    ///
    /// Removal matters for the save system. This used to be add-only, which was fine while
    /// missions only ever arrived one at a time, but a checkpoint restore can take missions
    /// away — and an add-only rebuild left those rows on screen forever.
    /// </summary>
    void Rebuild(List<MissionData> missions)
    {
        if (!IsConfigured)
        {
            if (!_warnedUnconfigured)
            {
                _warnedUnconfigured = true;
                Debug.LogWarning($"[MissionListUI] '{name}' has no {(missionEntryPrefab == null ? "Mission Entry Prefab" : "List Container")} " +
                                 "assigned, so it cannot show objectives. Ignoring mission updates. " +
                                 "If this is a leftover duplicate component, delete it.", this);
            }
            return;
        }

        if (missions == null) missions = new List<MissionData>();

        // Remove rows for missions that are no longer active.
        var live = new HashSet<string>();
        foreach (var m in missions)
            if (m != null) live.Add(m.missionId);

        var stale = new List<string>();
        foreach (var kvp in entries)
            if (!live.Contains(kvp.Key)) stale.Add(kvp.Key);

        foreach (string id in stale)
        {
            if (entries[id] != null) Destroy(entries[id]);
            entries.Remove(id);
        }

        // Add any mission that doesn't have a row yet.
        foreach (var m in missions)
        {
            if (m == null || entries.ContainsKey(m.missionId)) continue;

            var go = Instantiate(missionEntryPrefab, listContainer);

            var label = go.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = m.missionText;

            entries[m.missionId] = go;
        }
    }

    public void PlayCompleteThenRemove(MissionData mission, Action onComplete)
    {
        if (!entries.TryGetValue(mission.missionId, out var go))
        {
            onComplete?.Invoke();
            return;
        }

        var label = go.GetComponentInChildren<TMP_Text>();
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();

        Sequence seq = DOTween.Sequence();
        // Null-guarded: the callback runs a frame or more later, by which point a scene
        // change or a checkpoint restore may already have destroyed the row.
        seq.AppendCallback(() => { if (label != null) label.fontStyle |= FontStyles.Strikethrough; });
        seq.AppendInterval(strikeDuration + holdBeforeFade);
        seq.Append(cg.DOFade(0f, fadeOutDuration));
        seq.OnComplete(() =>
        {
            entries.Remove(mission.missionId);
            Destroy(go);
            onComplete?.Invoke();
        });
    }
}