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

    void Awake() => Instance = this;

    void OnEnable()  => MissionManager.OnMissionsChanged += Rebuild;
    void OnDisable() => MissionManager.OnMissionsChanged -= Rebuild;

    void Rebuild(List<MissionData> missions)
    {
        // add any missions that don't have an entry yet
        foreach (var m in missions)
        {
            if (entries.ContainsKey(m.missionId)) continue;

            var go = Instantiate(missionEntryPrefab, listContainer);
            go.GetComponentInChildren<TMP_Text>().text = m.missionText;
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
        seq.AppendCallback(() => label.fontStyle |= FontStyles.Strikethrough);
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