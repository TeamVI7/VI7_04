// ExtractionTrigger.cs — hands off from gameplay to the outro cutscene scene
// (put this on the extraction point's TriggerZone, e.g. the LZ collider)
using UnityEngine;
using System.Collections;

public class ExtractionTrigger : MonoBehaviour
{
    [SerializeField] private TriggerZone zone;

    [Header("Destination")]
    [Tooltip("Transition into the scene hosting ExtractionCutsceneManager.")]
    [SerializeField] private SceneTransitionConfig cutsceneTransition;
    [Tooltip("Used only when no LoadingScreenController is live.")]
    [SerializeField] private string cutsceneSceneName;

    [Header("Hold")]
    [Tooltip("Seconds left in gameplay after the player reaches the LZ — room " +
             "for a pickup animation or a last line of dialogue before the cut.")]
    [SerializeField] private float delayBeforeCut = 0f;

    private bool fired;

    void Awake()
    {
        if (zone == null) zone = GetComponent<TriggerZone>();
        if (zone != null) zone.OnPlayerEnter.AddListener(BeginExtraction);
    }

    public void BeginExtraction()
    {
        // TriggerZone is one-shot by default, but the method is public so it can
        // also be wired to a button or a mission event — guard either path.
        if (fired) return;
        fired = true;

        StartCoroutine(Handoff());
    }

    IEnumerator Handoff()
    {
        if (delayBeforeCut > 0f)
            yield return new WaitForSeconds(delayBeforeCut);

        if (LoadingScreenController.Instance != null && cutsceneTransition != null)
            LoadingScreenController.Instance.BeginLoad(cutsceneTransition.BuildSteps());
        else if (!string.IsNullOrEmpty(cutsceneSceneName))
            UnityEngine.SceneManagement.SceneManager.LoadScene(cutsceneSceneName);
        else
            Debug.LogWarning("ExtractionTrigger: no transition config or scene name set.", this);
    }
}
