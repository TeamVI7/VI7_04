using System.Collections.Generic;
using UnityEngine;
using TMPro;

// Randomly generates the access code for CodeInputMinigame and scatters
// "letter + digit" clues across a pool of world-space canvases placed in
// another room (e.g. computer screens). The letter tells the player WHICH
// digit position it is (A = 1st digit, B = 2nd digit, C = 3rd digit, ...),
// the number tells the VALUE of that digit.
//
// Example: 3 clue canvases end up showing "A3", "B4", "C2" (in random
// order, on random canvases). The player has to find all of them and sort
// by letter to reconstruct the code: A=3, B=4, C=2 -> "342".
public class CodeClueDistributor : MonoBehaviour
{
    [Header("Clue Canvases (place these around the other room)")]
    [Tooltip("All available canvas/screen text objects the clues can be placed on. Can be MORE than codeLength - leftover canvases will just show decoyText.")]
    public TMP_Text[] clueDisplays;

    [Header("Code Settings")]
    [Tooltip("How many digits the code has. Must match CodeInputMinigame.digitTexts.Length.")]
    [Range(2, 8)]
    public int codeLength = 3;

    [Tooltip("Text shown on canvases that did NOT get assigned a real clue.")]
    public string decoyText = "";

    [Tooltip("If true, the code + clue placement is (re)generated automatically on Start.")]
    public bool generateOnStart = true;

    private string _generatedCode;
    public string GeneratedCode => _generatedCode;

    private void Start()
    {
        if (generateOnStart) GenerateAndDistribute();
    }

    // Generates a new random code and randomly assigns the A/B/C... clues
    // to random canvases from clueDisplays. Returns the generated code
    // (e.g. "342") so callers can feed it into CodeInputMinigame.
    public string GenerateAndDistribute()
    {
        if (clueDisplays == null || clueDisplays.Length < codeLength)
        {
            Debug.LogError($"CodeClueDistributor: need at least {codeLength} clueDisplays, only {(clueDisplays == null ? 0 : clueDisplays.Length)} assigned.");
            return _generatedCode;
        }

        // 1) Random digits -> the actual code
        int[] digits = new int[codeLength];
        for (int i = 0; i < codeLength; i++)
            digits[i] = Random.Range(0, 10);

        System.Text.StringBuilder codeBuilder = new System.Text.StringBuilder();
        foreach (int d in digits) codeBuilder.Append(d);
        _generatedCode = codeBuilder.ToString();

        // 2) Build "letter+digit" clue strings: a=position0, b=position1, ...
        string[] clues = new string[codeLength];
        for (int i = 0; i < codeLength; i++)
        {
            char letter = (char)('A' + i);
            clues[i] = $"{letter}{digits[i]}";
        }

        // 3) Clear every canvas first (so leftovers show the decoy text)
        for (int i = 0; i < clueDisplays.Length; i++)
            if (clueDisplays[i]) clueDisplays[i].text = decoyText;

        // 4) Pick codeLength random, distinct canvases out of the pool and
        //    hand out one clue to each, in random order/location.
        List<int> availableIndices = new List<int>();
        for (int i = 0; i < clueDisplays.Length; i++) availableIndices.Add(i);
        Shuffle(availableIndices);

        for (int i = 0; i < codeLength; i++)
        {
            int canvasIndex = availableIndices[i];
            if (clueDisplays[canvasIndex]) clueDisplays[canvasIndex].text = clues[i];
        }

        return _generatedCode;
    }

    private void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}