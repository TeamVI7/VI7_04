using UnityEngine;

[CreateAssetMenu(menuName = "Dialogue/Dialogue Sequence")]
public class DialogueData : ScriptableObject
{
    public DialogueLine[] lines;
    public bool autoPlay = false;       // if true, lines advance on their own
    public float autoPlayDelay = 3f;    // seconds per line when autoPlay is on
}

[System.Serializable]
public struct DialogueLine
{
    public string speaker;
    [TextArea] public string text;
    public Sprite portrait;
}