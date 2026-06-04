using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;
using System.Collections;

public class CutsceneManager : MonoBehaviour
{
    [Header("Cutscenes")]
    public BoardingCutscene boarding;
    public TakeoffCutscene takeoff;  
    public InFlightCutscene inFlight;
    
    [Header("Scene")]
    public string rappelSceneName; // name of rappel scene in Build Settings

    [Header("Fade")]
    public Image fadeImage;
    public float fadeDuration = 1f; // seconds

    void Start()
    {
        fadeImage.color = new Color(0f, 0f, 0f, 1f); // start black
        StartCoroutine(PlayAll());
    }

    IEnumerator PlayAll()
    {
        // ── 1. BOARDING ───────────────────────────────────────
        yield return StartCoroutine(FadeIn(0.1f));
        yield return StartCoroutine(boarding.Play());
        yield return StartCoroutine(FadeOut(0.1f));

        // ── 2. TAKEOFF ────────────────────────────────────────
        yield return StartCoroutine(FadeIn(0.2f));
        yield return StartCoroutine(takeoff.Play());
        yield return StartCoroutine(FadeOut(0.2f));

        // ── 3. IN FLIGHT ──────────────────────────────────────
        yield return StartCoroutine(FadeIn(0.2f));
        yield return StartCoroutine(inFlight.Play());
        yield return StartCoroutine(FadeOut(0.5f));

        SceneManager.LoadScene(rappelSceneName);
    }

    public IEnumerator FadeIn(float duration)
    {
        yield return fadeImage.DOFade(0f, duration).WaitForCompletion();
    }

    public IEnumerator FadeOut(float duration)
    {
        yield return fadeImage.DOFade(1f, duration).WaitForCompletion();
    }

}