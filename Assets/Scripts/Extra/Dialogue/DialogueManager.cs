using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text speakerLabel;
    [SerializeField] private TMP_Text bodyLabel;
    [SerializeField] private Image portraitImage;
    [SerializeField] private KeyCode advanceKey = KeyCode.E;
    [SerializeField] private KeyCode skipKey = KeyCode.Space;

    [Header("Voice")]
    [Tooltip("AudioSource that plays voice lines. Leave empty to auto-add one on this object.")]
    [SerializeField] private AudioSource voiceSource;

    [Header("Typewriter")]
    [Tooltip("Characters revealed per second.")]
    [SerializeField] private float charsPerSecond = 40f;

    [Tooltip("Extra pause when typing hits . , ! ? ; :")]
    [SerializeField] private float punctuationPause = 0.12f;

    [Tooltip("Optional blip sound per character. Leave empty for silence.")]
    [SerializeField] private AudioClip typeBlipSfx;

    [SerializeField] private AudioSource blipSource;

    public bool IsActive { get; private set; }
    public event Action OnDialogueClosed;

    private DialogueLine[] queue;
    private int index;
    private bool autoPlay;
    private float autoPlayDelay;
    private Coroutine autoPlayRoutine;
    private Coroutine typeRoutine;
    private bool isTyping;
    private string currentFullText;

    // ── Single full-clip mode ─────────────────────────────────────
    private bool singleClipMode;
    private AudioClip fullClip;
    private Coroutine singleClipRoutine;

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);

        if (voiceSource == null)
        {
            voiceSource = GetComponent<AudioSource>();
            if (voiceSource == null)
                voiceSource = gameObject.AddComponent<AudioSource>();
        }
        voiceSource.playOnAwake = false;

        if (blipSource == null)
            blipSource = gameObject.AddComponent<AudioSource>();
        blipSource.playOnAwake = false;
    }

    private void Update()
    {
        if (!IsActive) return;

        if (Input.GetKeyDown(skipKey))
        {
            SkipAll();
            return;
        }

        if (Input.GetKeyDown(advanceKey))
        {
            if (isTyping)
                CompleteTyping();
            else if (!autoPlay && !singleClipMode)
                Advance();
        }
    }

    public void Play(DialogueData data)
    {
        // data comes from a DialogueTrigger's inspector slot, and an unassigned slot arrives
        // here as null — which threw before the length check could run. A trigger nobody
        // filled in should be a silent no-op with a name to go fix, not an exception that
        // aborts whatever UnityEvent chain fired it.
        if (data == null)
        {
            Debug.LogWarning("[DialogueManager] Play called with no DialogueData — a " +
                             "DialogueTrigger in this scene has an empty Data slot.", this);
            return;
        }

        if (IsActive || data.lines == null || data.lines.Length == 0) return;

        IsActive = true;
        queue = data.lines;
        index = 0;
        singleClipMode = data.fullVoiceClip != null;
        fullClip = data.fullVoiceClip;
        autoPlay = data.autoPlay;
        autoPlayDelay = data.autoPlayDelay;

        panel.SetActive(true);
        ShowCurrentLine();

        if (singleClipMode)
        {
            voiceSource.clip = fullClip;
            voiceSource.Play();
            singleClipRoutine = StartCoroutine(SingleClipRoutine());
        }
        else if (autoPlay)
        {
            autoPlayRoutine = StartCoroutine(AutoPlayRoutine());
        }
    }

    // Single full-clip mode: clip plays once continuously, lines advance
    // using each line's own "duration" (e.g. 1.5s, 2.5s, ...).
    private IEnumerator SingleClipRoutine()
    {
        while (index < queue.Length - 1)
        {
            float wait = Mathf.Max(0f, queue[index].duration);
            float t = 0f;
            while (t < wait && voiceSource.isPlaying)
            {
                t += Time.deltaTime;
                yield return null;
            }

            index++;
            ShowCurrentLine();
        }

        // Hold the last line for its own duration, then close.
        yield return new WaitForSeconds(Mathf.Max(0f, queue[index].duration));
        Close();
    }

    private IEnumerator AutoPlayRoutine()
    {
        while (index < queue.Length - 1)
        {
            yield return WaitForLine(queue[index]);
            Advance();
        }
        yield return WaitForLine(queue[index]);
        Advance(); // closes on final line
    }

    // If the line has a voiceClip: wait for it to finish (at least autoPlayDelay if the clip is shorter).
    // If not: just wait autoPlayDelay.
    private IEnumerator WaitForLine(DialogueLine line)
    {
        if (line.voiceClip != null)
        {
            float wait = Mathf.Max(line.voiceClip.length, autoPlayDelay);
            yield return new WaitForSeconds(wait);
        }
        else
        {
            yield return new WaitForSeconds(autoPlayDelay);
        }
    }

    private void Advance()
    {
        index++;
        if (index >= queue.Length)
        {
            Close();
            return;
        }
        ShowCurrentLine();
    }

    public void SkipAll()
    {
        if (autoPlayRoutine != null)
            StopCoroutine(autoPlayRoutine);
        if (singleClipRoutine != null)
            StopCoroutine(singleClipRoutine);
        if (typeRoutine != null)
        {
            StopCoroutine(typeRoutine);
            typeRoutine = null;
        }
        voiceSource.Stop();
        Close();
    }

    private void Close()
    {
        if (typeRoutine != null)
        {
            StopCoroutine(typeRoutine);
            typeRoutine = null;
        }
        isTyping = false;
        voiceSource.Stop();
        panel.SetActive(false);
        IsActive = false;
        OnDialogueClosed?.Invoke();
    }

    private void ShowCurrentLine()
    {
        var line = queue[index];
        speakerLabel.text = line.speaker;
        if (portraitImage != null)
            portraitImage.sprite = line.portrait;

        if (typeRoutine != null)
        {
            StopCoroutine(typeRoutine);
            typeRoutine = null;
        }

        currentFullText = line.text;
        bodyLabel.text = string.Empty;
        isTyping = true;
        typeRoutine = StartCoroutine(TypeTextRoutine(line.text));

        // Single-clip mode: don't touch voiceSource here — the clip is
        // already running continuously, only the text changes.
        if (!singleClipMode)
        {
            voiceSource.Stop();
            if (line.voiceClip != null)
                voiceSource.PlayOneShot(line.voiceClip);
        }
    }

    private IEnumerator TypeTextRoutine(string fullText)
    {
        int visibleCount = 0;
        while (visibleCount < fullText.Length)
        {
            visibleCount++;
            bodyLabel.text = fullText.Substring(0, visibleCount);

            char c = fullText[visibleCount - 1];
            if (typeBlipSfx != null && !char.IsWhiteSpace(c))
                blipSource.PlayOneShot(typeBlipSfx);

            float delay = IsPunctuation(c) ? punctuationPause : 1f / charsPerSecond;
            yield return new WaitForSeconds(delay);
        }

        bodyLabel.text = fullText;
        isTyping = false;
        typeRoutine = null;
    }

    private void CompleteTyping()
    {
        if (typeRoutine != null)
        {
            StopCoroutine(typeRoutine);
            typeRoutine = null;
        }

        if (!string.IsNullOrEmpty(currentFullText))
            bodyLabel.text = currentFullText;

        isTyping = false;
    }

    private static bool IsPunctuation(char c)
    {
        return c == '.' || c == ',' || c == '!' || c == '?' || c == ';' || c == ':';
    }
}