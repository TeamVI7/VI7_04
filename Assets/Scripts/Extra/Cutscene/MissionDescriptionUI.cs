using UnityEngine;
using TMPro;
using DG.Tweening;
using System.Collections;

public class MissionDescriptionUI : MonoBehaviour
{
    [System.Serializable]
    public class BriefingLine
    {
        public string label;          // e.g., "OBJECTIVE:", "THREAT:"
        [TextArea(2, 5)]
        public string details;        // The actual description text
        public float typeSpeed = 0.03f;
        
        [Header("On Completion Settings")]
        public GameObject revealTarget; // Drag your video panel, map GO, or visual guide here
    }

    [Header("UI Components")]
    public RectTransform containerPanel;
    public TextMeshProUGUI[] textLines; // Assign 4 separate TextMeshPro components

    [Header("Briefing Data")]
    public BriefingLine objective = new BriefingLine { label = "OBJECTIVE:", details = "Locate and extract the high-value target." };
    public BriefingLine conditions = new BriefingLine { label = "EXPECTATION:", details = "Heavy radar jamming and low visibility storm conditions." };
    public BriefingLine enemyForce = new BriefingLine { label = "HOSTILE FORCES:", details = "Automated defense turrets and armored patrols." };
    public BriefingLine directive = new BriefingLine { label = "DIRECTIVE:", details = "Infiltrate cleanly. Leave no trace." };

    [Header("Timing & Style")]
    public float delayBetweenLines = 0.5f;
    public float holdDuration = 4f; // How long everything stays readable before fading
    public float fadeOutDuration = 1f;
    public float glitchIntensity = 2f;

    private BriefingLine[] _briefingSequence;

    void Awake()
    {
        _briefingSequence = new BriefingLine[] { objective, conditions, enemyForce, directive };
    }

    void Start()
    {
        // Hide text layouts at start
        foreach (var tmp in textLines)
        {
            ResetTextField(tmp);
        }

        // Ensure all reveal tracking targets are hidden on frame zero
        foreach (var line in _briefingSequence)
        {
            if (line != null && line.revealTarget != null)
            {
                line.revealTarget.SetActive(false);
            }
        }
    }

    private void ResetTextField(TextMeshProUGUI tmp)
    {
        if (tmp != null)
        {
            tmp.text = "";
            tmp.alpha = 0f;
        }
    }

    // Clean up your MissionDescriptionUI.cs PlayBriefingSequence down to this:
    public IEnumerator PlayBriefingSequence()
    {
        yield return new WaitForSeconds(0.5f);

        for (int i = 0; i < _briefingSequence.Length && i < textLines.Length; i++)
        {
            if (textLines[i] == null) continue;
            yield return StartCoroutine(TypeBriefingLine(textLines[i], _briefingSequence[i]));
            yield return new WaitForSeconds(delayBetweenLines);
        }

        yield return new WaitForSeconds(holdDuration);

        foreach (var tmp in textLines)
        {
            if (tmp != null) tmp.DOFade(0f, fadeOutDuration);
        }

        foreach (var line in _briefingSequence)
        {
            if (line != null && line.revealTarget != null)
            {
                line.revealTarget.SetActive(false); 
            }
        }

        yield return new WaitForSeconds(fadeOutDuration);
    }

    /// Halts playback and releases every tween this sequence owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        StopAllCoroutines();

        foreach (var tmp in textLines)
        {
            if (tmp != null) DOTween.Kill(tmp);
        }
    }

    IEnumerator TypeBriefingLine(TextMeshProUGUI tmp, BriefingLine line)
    {
        tmp.alpha = 1f;
        
        string fullLabel = $"<color=#FFB000><b>{line.label}</b></color> "; 
        string targetDetails = line.details;
        
        tmp.text = fullLabel;
        Vector2 originalPos = tmp.rectTransform.anchoredPosition;

        for (int i = 0; i <= targetDetails.Length; i++)
        {
            string glitchChar = (i < targetDetails.Length && Random.value > 0.3f)
                ? ((char)Random.Range(33, 90)).ToString()
                : "";

            tmp.text = fullLabel + targetDetails.Substring(0, i) + glitchChar;
            tmp.rectTransform.anchoredPosition = originalPos + new Vector2(Random.Range(-glitchIntensity, glitchIntensity), 0f);

            yield return new WaitForSeconds(line.typeSpeed);
            tmp.rectTransform.anchoredPosition = originalPos;
        }

        tmp.text = fullLabel + targetDetails;

        // ───────────────────────────────────────────────────────────
        // TARGET TRIGGER EXECUTION: TEXT IS COMPLETE NOW -> ENGAGE VIDEO/GUIDE
        // ───────────────────────────────────────────────────────────
        if (line.revealTarget != null)
        {
            line.revealTarget.SetActive(true);
            
            // If the target has a Unity VideoPlayer component attached, automatically play it from frame 0
            var videoPlayer = line.revealTarget.GetComponent<UnityEngine.Video.VideoPlayer>();
            if (videoPlayer != null)
            {
                videoPlayer.Play();
            }
        }
    }
}