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

    void Awake()
    {
        // Hide on frame zero. PlaySequence resets these too, but if the GameObject
        // is left active in the scene the authored placeholder text would otherwise
        // sit on screen through the whole briefing phase.
        foreach (var t in textLines)
        {
            if (t == null) continue;
            t.text = "";
            t.alpha = 0f;
        }
    }

    /// Driven explicitly by CutsceneManager. Previously this ran from Start(),
    /// which meant it fired at scene load or on activation depending on how the
    /// GameObject happened to be left in the scene.
    public IEnumerator PlaySequence()
    {
        foreach (var t in textLines)
        {
            if (t == null) continue;
            t.text = "";
            t.alpha = 0f;
        }

        yield return new WaitForSeconds(0.5f);

        for (int i = 0; i < entries.Length && i < textLines.Length; i++)
        {
            if (textLines[i] == null) continue;

            // Each line is awaited rather than fire-and-forget, so a long title
            // can no longer still be typing when the hold timer starts.
            yield return StartCoroutine(TypeLine(textLines[i], entries[i]));
            yield return new WaitForSeconds(delayBetweenLines);
        }

        // Hold then fade all out
        yield return new WaitForSeconds(holdDuration);

        foreach (var t in textLines)
        {
            if (t != null) t.DOFade(0f, fadeOutDuration);
        }

        yield return new WaitForSeconds(fadeOutDuration);
    }

    /// Halts playback and releases every tween this sequence owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        StopAllCoroutines();

        foreach (var t in textLines)
        {
            if (t != null) DOTween.Kill(t);
        }
    }

    IEnumerator TypeLine(TextMeshProUGUI tmp, TitleEntry entry)
    {
        tmp.text = "";
        tmp.alpha = 1f;

        string full = entry.text;
        // Captured so the glitch snaps back to wherever this line actually sits.
        // Hardcoding x=0 yanked any line that wasn't centre-anchored.
        Vector2 originalPos = tmp.rectTransform.anchoredPosition;

        for (int i = 0; i <= full.Length; i++)
        {
            // Show typed characters + random glitch char at end
            string glitchChar = i < full.Length
                ? ((char)Random.Range(33, 90)).ToString()
                : "";

            tmp.text = full.Substring(0, i) + glitchChar;

            // Glitch position flicker
            tmp.rectTransform.anchoredPosition = originalPos + new Vector2(
                Random.Range(-glitchIntensity, glitchIntensity), 0f);

            yield return new WaitForSeconds(entry.typeSpeed);

            // Snap back
            tmp.rectTransform.anchoredPosition = originalPos;
        }

        tmp.text = full;
    }
}
