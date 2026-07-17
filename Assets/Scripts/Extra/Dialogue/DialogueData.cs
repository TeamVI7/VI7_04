using UnityEngine;

[CreateAssetMenu(menuName = "Dialogue/Dialogue Sequence")]
public class DialogueData : ScriptableObject
{
    public DialogueLine[] lines;

    [Header("Single full-clip mode")]
    [Tooltip("Drag ONE audio file containing the whole conversation here. " +
             "If set, DialogueManager plays it once and advances lines using " +
             "each line's 'duration' below (ignores autoPlay/autoPlayDelay and " +
             "any per-line voiceClip).")]
    public AudioClip fullVoiceClip;

    [Header("Multi-clip / no-clip mode (used only when fullVoiceClip is empty)")]
    public bool autoPlay = false;
    public float autoPlayDelay = 3f; // seconds per line, used only if a line has no voiceClip
}

[System.Serializable]
public struct DialogueLine
{
    public string speaker;
    [TextArea] public string text;
    public Sprite portrait;

    [Header("Multi-clip mode")]
    [Tooltip("Only used when DialogueData has NO fullVoiceClip. If autoPlay is on " +
             "and this is set, the line advances as soon as this clip finishes.")]
    public AudioClip voiceClip;

    [Header("Single full-clip mode")]
    [Tooltip("Only used when DialogueData HAS a fullVoiceClip. How many seconds " +
             "this line stays on screen before the next one shows, e.g. line 1 = 1.5, " +
             "line 2 = 2.5, etc. Just per-line length, not an absolute timestamp.")]
    public float duration;
}