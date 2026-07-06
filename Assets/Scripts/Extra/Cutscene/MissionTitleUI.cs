using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;

public class MissionTitleUI : MonoBehaviour
{
    [System.Serializable]
    public class TitleEntry
    {
        public string text;
        public float typeSpeed = 0.04f;   // seconds per character
        public float displayDuration = 2f; // seconds before fade out
    }

    [Header("UI References")]
    public RectTransform panel;
    public TextMeshProUGUI[] textLines;  // one TMP per line

    [Header("Entries — one per line")]
    public TitleEntry[] entries;

    [Header("Timing")]
    public float delayBetweenLines = 0.3f;  // seconds between each line appearing
    public float holdDuration = 3f;          // seconds all lines stay before fading

    [Header("Style")]
    public float glitchIntensity = 3f;       // pixels of glitch offset
    public float glitchDuration = 0.05f;     // seconds per glitch hit
    public float fadeOutDuration = 0.8f;

    void Start()
    {
        // Hide all lines at start
        foreach (var t in textLines)
        {
            t.text = "";
            t.alpha = 0f;
        }

        StartCoroutine(PlaySequence()); // Runs immediately when GameObject becomes active!
    }

    IEnumerator PlaySequence()
    {
        yield return new WaitForSeconds(0.5f);

        for (int i = 0; i < entries.Length && i < textLines.Length; i++)
        {
            StartCoroutine(TypeLine(textLines[i], entries[i]));
            yield return new WaitForSeconds(delayBetweenLines);
        }

        // Hold then fade all out
        yield return new WaitForSeconds(holdDuration);

        foreach (var t in textLines)
        {
            t.DOFade(0f, fadeOutDuration);
        }
    }

    IEnumerator TypeLine(TextMeshProUGUI tmp, TitleEntry entry)
    {
        tmp.text = "";
        tmp.alpha = 1f;

        string full = entry.text;

        for (int i = 0; i <= full.Length; i++)
        {
            // Show typed characters + random glitch char at end
            string glitchChar = i < full.Length
                ? ((char)Random.Range(33, 90)).ToString()
                : "";

            tmp.text = full.Substring(0, i) + glitchChar;

            // Glitch position flicker
            tmp.rectTransform.anchoredPosition += new Vector2(
                Random.Range(-glitchIntensity, glitchIntensity), 0f);

            yield return new WaitForSeconds(entry.typeSpeed);

            // Snap back
            tmp.rectTransform.anchoredPosition = new Vector2(0f,
                tmp.rectTransform.anchoredPosition.y);
        }

        tmp.text = full;
    }
}