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

    public bool IsActive { get; private set; }
    public event Action OnDialogueClosed;

    private DialogueLine[] queue;
    private int index;
    private bool autoPlay;
    private float autoPlayDelay;
    private Coroutine autoPlayRoutine;

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);
    }

    private void Update()
    {
        if (!IsActive) return;

        if (Input.GetKeyDown(skipKey))
        {
            SkipAll();
            return;
        }

        if (!autoPlay && Input.GetKeyDown(advanceKey))
            Advance();
    }

    public void Play(DialogueData data)
    {
        if (IsActive || data.lines.Length == 0) return;

        IsActive = true;
        queue = data.lines;
        index = 0;
        autoPlay = data.autoPlay;
        autoPlayDelay = data.autoPlayDelay;

        panel.SetActive(true);
        ShowCurrentLine();

        if (autoPlay)
            autoPlayRoutine = StartCoroutine(AutoPlayRoutine());
    }

    private IEnumerator AutoPlayRoutine()
    {
        while (index < queue.Length - 1)
        {
            yield return new WaitForSeconds(autoPlayDelay);
            Advance();
        }
        yield return new WaitForSeconds(autoPlayDelay);
        Advance(); // closes on final line
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
        Close();
    }

    private void Close()
    {
        panel.SetActive(false);
        IsActive = false;
        OnDialogueClosed?.Invoke();
    }

    private void ShowCurrentLine()
    {
        var line = queue[index];
        speakerLabel.text = line.speaker;
        bodyLabel.text = line.text;
        if (portraitImage != null)
            portraitImage.sprite = line.portrait;
    }
}