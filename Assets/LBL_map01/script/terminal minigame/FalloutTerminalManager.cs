using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using TMPro;

public class FalloutTerminalManager : MonoBehaviour
{
    [Header("Word Pool")]
    public string[] wordPool = new string[]
    {
        "SERVER","ROUTER","MEMORY","BUFFER","KERNEL","SOCKET",
        "BINARY","MODULE","SYSTEM","ENGINE","CIPHER","ACCESS",
        "REBOOT","STORED","BRIDGE","DRIVER"
    };

    [Header("Round Settings")]
    public int maxAttempts = 5;
    public bool enableDudRemovalBrackets = true;
    public int maxDudBrackets = 2;
    public string junkCharacters = ".,;:!?@#$%^&*_-+=~/\\|<>{}[]()01";

    [Header("Memory Address Prefix (like 0xF7A2)")]
    [Tooltip("Tự thêm 1 địa chỉ bộ nhớ dạng hex (0xF7A2...) vào đầu mỗi dòng, giống terminal Fallout thật. " +
             "Địa chỉ gắn cố định theo VỊ TRÍ của slot trong mảng Word Slots, không đổi giữa các ván " +
             "(giống bản gốc: vị trí dòng trên màn hình luôn có cùng 1 địa chỉ).")]
    public bool showMemoryAddress = true;

    [Tooltip("Địa chỉ hex bắt đầu (dạng thập phân, VD 63400 = 0xF7A8)")]
    public int addressStart = 0xF7A0;

    [Tooltip("Mỗi slot cách nhau bao nhiêu (hex) so với slot trước, để địa chỉ tăng dần xuống dưới")]
    public int addressStep = 4;

    [Tooltip("Màu địa chỉ hex (nên để mờ hơn màu chữ chính cho giống terminal thật)")]
    public Color addressColor = new Color(0.25f, 0.55f, 0.3f);

    [Header("Canvas Slots")]
    public TerminalWordSlot[] wordSlots;

    [Header("Secondary UI")]
    public TMP_Text terminalMessageText;
    public TMP_Text attemptsText;
    public TMP_Text historyText;
    public GameObject terminalPanel;

    [Header("Display Text")]
    [TextArea] public string introMessage  = "ENTER PASSWORD\n>REMAINING ATTEMPTS";
    [TextArea] public string solvedMessage = "ACCESS GRANTED.";
    [TextArea] public string lockedMessage = "!!! TERMINAL LOCKED !!!\n>TRY AGAIN LATER";

    [Header("Big Screen Popup (on solve)")]
    public GameObject bigScreenPanel;
    public TMP_Text bigScreenText;
    [TextArea] public string bigScreenMessage = "WARNING\nYOU MUST DISCONNECT THE SERVERS TO SHUT DOWN THE AI!";
    public bool autoHideBigScreen = true;
    public float bigScreenAutoHideDelay = 5f;
    public AudioClip bigScreenSound;

    [Header("Server Minigame Link")]
    public ServerMinigameManager serverMinigameManager;
    public float delayBeforeServerRise = 1.5f;
    public bool closeTerminalAfterSolve = true;
    public float closeDelayAfterSolve = 2.5f;

    [Header("UI / Raycast Fixes (optional)")]
    public UIInputBlocker uiInputBlocker;
    public ForceRaycastTarget forceRaycastTarget;
    public AutoDisableBlockingColliders autoDisableBlockingColliders;

    [Header("Terminal Camera (optional)")]
    [Tooltip("Camera riêng đặt sẵn nhìn thẳng vào TerminalCanvas, ban đầu để tắt (disabled) trong scene. " +
             "Khi mở terminal, script sẽ tắt camera player và bật camera này, giống hiệu ứng " +
             "'zoom vào màn hình' của Fallout thật. Để trống nếu không cần, camera player sẽ giữ nguyên.")]
    public Camera terminalCamera;

    private Camera _playerCamera;
    private bool   _playerCameraWasEnabled;
    private AudioListener _playerListener;
    private bool   _playerListenerWasEnabled;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip clickSound;
    public AudioClip correctSound;
    public AudioClip wrongSound;
    public AudioClip dudRemovedSound;
    public AudioClip lockedSound;

    private string _correctWord;
    private int    _attemptsLeft;
    private bool   _solved;
    private bool   _locked;
    private readonly List<string> _activeWords = new List<string>();
    private readonly StringBuilder _historyBuilder = new StringBuilder();

    private void Awake()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        if (terminalPanel != null) terminalPanel.SetActive(false);
    }

    public bool IsTerminalOpen => terminalPanel != null && terminalPanel.activeSelf;

    public void StartTerminal()
    {
        if (terminalPanel != null) terminalPanel.SetActive(true);

        uiInputBlocker?.BlockInput();
        autoDisableBlockingColliders?.DisableBlockers();
        forceRaycastTarget?.FixAll();
        SwitchToTerminalCamera();

        SetupNewGame();
    }

    public void CloseTerminal()
    {
        if (terminalPanel != null) terminalPanel.SetActive(false);

        uiInputBlocker?.UnblockInput();
        autoDisableBlockingColliders?.RestoreBlockers();
        RestorePlayerCamera();
    }

    private void SwitchToTerminalCamera()
    {
        if (terminalCamera == null) return;

        _playerCamera = Camera.main;
        if (_playerCamera != null)
        {
            _playerCameraWasEnabled = _playerCamera.enabled;
            _playerCamera.enabled = false;

            _playerListener = _playerCamera.GetComponent<AudioListener>();
            if (_playerListener != null)
            {
                _playerListenerWasEnabled = _playerListener.enabled;
                _playerListener.enabled = false;
            }
        }

        terminalCamera.enabled = true;
        var termListener = terminalCamera.GetComponent<AudioListener>();
        if (termListener != null) termListener.enabled = true;
    }

    private void RestorePlayerCamera()
    {
        if (terminalCamera == null) return;

        terminalCamera.enabled = false;

        if (_playerCamera != null)
        {
            _playerCamera.enabled = _playerCameraWasEnabled;
            if (_playerListener != null) _playerListener.enabled = _playerListenerWasEnabled;
        }
    }

    private void SetupNewGame()
    {
        _solved = false;
        _locked = false;
        _attemptsLeft = Mathf.Max(1, maxAttempts);
        _historyBuilder.Clear();

        if (terminalMessageText != null) terminalMessageText.text = introMessage;
        UpdateAttemptsText();
        if (historyText != null) historyText.text = "";

        foreach (var slot in wordSlots)
            if (slot != null) slot.ResetSlot();

        PickWordsForRound();
        LayoutSlots();
    }

    private void PickWordsForRound()
    {
        int slotsAvailableForWords = wordSlots.Length;
        if (enableDudRemovalBrackets)
            slotsAvailableForWords -= Mathf.Min(maxDudBrackets, wordSlots.Length / 2);

        slotsAvailableForWords = Mathf.Max(2, slotsAvailableForWords);

        var groupsByLength = wordPool
            .Where(w => !string.IsNullOrEmpty(w))
            .Select(w => w.ToUpperInvariant())
            .GroupBy(w => w.Length)
            .Where(g => g.Count() >= 2)
            .ToList();

        if (groupsByLength.Count == 0)
        {
            Debug.LogError("[FalloutTerminal] wordPool has no words of equal length!");
            return;
        }

        var chosenGroup = groupsByLength[Random.Range(0, groupsByLength.Count)].ToList();
        Shuffle(chosenGroup);

        int wordCount = Mathf.Min(slotsAvailableForWords, chosenGroup.Count);
        wordCount = Mathf.Max(2, wordCount);

        _activeWords.Clear();
        _activeWords.AddRange(chosenGroup.Take(wordCount));

        _correctWord = _activeWords[Random.Range(0, _activeWords.Count)];
    }

    private void LayoutSlots()
    {
        var indices = Enumerable.Range(0, wordSlots.Length).ToList();
        Shuffle(indices);

        int wordIdx = 0;
        int dudsPlaced = 0;
        int dudsToPlace = DudBracketCount();

        foreach (int i in indices)
        {
            var slot = wordSlots[i];
            if (slot == null) continue;

            slot.Button.onClick.RemoveAllListeners();
            string address = BuildAddressPrefix(i);

            if (enableDudRemovalBrackets && dudsPlaced < dudsToPlace && wordIdx >= _activeWords.Count)
            {
                SetupBracketSlot(slot, address);
                dudsPlaced++;
                continue;
            }

            if (enableDudRemovalBrackets && dudsPlaced < dudsToPlace && Random.value < 0.35f && wordIdx < _activeWords.Count)
            {
                SetupBracketSlot(slot, address);
                dudsPlaced++;
                continue;
            }

            if (wordIdx < _activeWords.Count)
            {
                string word = _activeWords[wordIdx];
                wordIdx++;
                SetupWordSlot(slot, word, address);
            }
            else
            {
                SetupGarbageOnlySlot(slot, address);
            }
        }
    }

    /// <summary>Tạo chuỗi "0xF7A0 " (rich text, màu mờ) dựa trên vị trí cố định của slot trong mảng.</summary>
    private string BuildAddressPrefix(int slotIndex)
    {
        if (!showMemoryAddress) return "";
        int address = addressStart + slotIndex * addressStep;
        string hex = System.Convert.ToString(address, 16).ToUpperInvariant();
        string colorHex = ColorUtility.ToHtmlStringRGB(addressColor);
        return $"<color=#{colorHex}>0x{hex}</color> ";
    }

    private int DudBracketCount()
    {
        if (!enableDudRemovalBrackets) return 0;
        return Mathf.Clamp(maxDudBrackets, 0, wordSlots.Length / 3);
    }

    private void SetupWordSlot(TerminalWordSlot slot, string word, string address)
    {
        slot.assignedWord = word;
        slot.isDudRemovalBracket = false;
        slot.SetText(address + BuildLineWithWord(word));
        slot.SetInteractable(true);
        slot.SetHighlight(TerminalVisualState.Normal);
        slot.Button.onClick.AddListener(() => OnWordSlotClicked(slot));
    }

    private void SetupGarbageOnlySlot(TerminalWordSlot slot, string address)
    {
        slot.assignedWord = null;
        slot.isDudRemovalBracket = false;
        slot.SetText(address + BuildGarbageLine(10 + Random.Range(0, 6)));
        slot.SetInteractable(false);
        slot.SetHighlight(TerminalVisualState.Normal);
    }

    private void SetupBracketSlot(TerminalWordSlot slot, string address)
    {
        string[] pairs = { "()", "[]", "{}", "<>" };
        string pair = pairs[Random.Range(0, pairs.Length)];
        string line = BuildGarbageLine(4 + Random.Range(0, 4)) + pair[0] + BuildGarbageLine(1 + Random.Range(0, 3)) + pair[1] + BuildGarbageLine(2 + Random.Range(0, 4));

        slot.assignedWord = null;
        slot.isDudRemovalBracket = true;
        slot.SetText(address + line);
        slot.SetInteractable(true);
        slot.SetHighlight(TerminalVisualState.Normal);
        slot.Button.onClick.AddListener(() => OnBracketSlotClicked(slot));
    }

    private string BuildLineWithWord(string word)
    {
        int prefixLen = Random.Range(2, 8);
        int suffixLen = Random.Range(2, 8);
        return BuildGarbageLine(prefixLen) + word + BuildGarbageLine(suffixLen);
    }

    private string BuildGarbageLine(int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(junkCharacters[Random.Range(0, junkCharacters.Length)]);
        return sb.ToString();
    }

    private void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void OnBracketSlotClicked(TerminalWordSlot slot)
    {
        if (_solved || _locked || slot.isUsed) return;
        slot.isUsed = true;
        slot.SetInteractable(false);
        slot.SetHighlight(TerminalVisualState.Disabled);
        PlaySound(dudRemovedSound);

        var removableSlots = wordSlots
            .Where(s => s != null && !s.isUsed && !s.isDudRemovalBracket &&
                        !string.IsNullOrEmpty(s.assignedWord) && s.assignedWord != _correctWord)
            .ToList();

        if (removableSlots.Count > 0)
        {
            var target = removableSlots[Random.Range(0, removableSlots.Count)];
            int targetIndex = System.Array.IndexOf(wordSlots, target);
            string targetAddress = targetIndex >= 0 ? BuildAddressPrefix(targetIndex) : "";
            target.isUsed = true;
            target.SetInteractable(false);
            target.SetHighlight(TerminalVisualState.Disabled);
            target.SetText(targetAddress + BuildGarbageLine(10 + Random.Range(0, 6)));
            AppendHistory("1 wrong password removed. No attempt lost.");
        }
        else
        {
            AppendHistory("No wrong password left to remove.");
        }
    }

    private void OnWordSlotClicked(TerminalWordSlot slot)
    {
        if (_solved || _locked || slot.isUsed || string.IsNullOrEmpty(slot.assignedWord)) return;

        PlaySound(clickSound);
        string guess = slot.assignedWord;

        if (guess == _correctWord)
        {
            slot.SetHighlight(TerminalVisualState.Correct);
            AppendHistory($"> {guess}\nACCESS GRANTED");
            Solve();
            return;
        }

        slot.isUsed = true;
        slot.SetInteractable(false);
        slot.SetHighlight(TerminalVisualState.Wrong);
        PlaySound(wrongSound);

        int likeness = ComputeLikeness(guess, _correctWord);
        _attemptsLeft--;
        UpdateAttemptsText();
        AppendHistory($"> {guess}\nACCESS DENIED — Likeness: {likeness}/{_correctWord.Length}");

        if (_attemptsLeft <= 0)
            Lock();
    }

    private int ComputeLikeness(string guess, string answer)
    {
        int count = 0;
        int len = Mathf.Min(guess.Length, answer.Length);
        for (int i = 0; i < len; i++)
            if (guess[i] == answer[i]) count++;
        return count;
    }

    private void AppendHistory(string line)
    {
        if (historyText == null) return;
        _historyBuilder.AppendLine(line);
        historyText.text = _historyBuilder.ToString();
    }

    private void UpdateAttemptsText()
    {
        if (attemptsText == null) return;
        attemptsText.text = $"ATTEMPTS REMAINING: {_attemptsLeft}";
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private void Solve()
    {
        _solved = true;
        PlaySound(correctSound);

        foreach (var slot in wordSlots)
            if (slot != null) slot.SetInteractable(false);

        if (terminalMessageText != null)
            terminalMessageText.text = solvedMessage;

        StartCoroutine(SolveSequence());
    }

    private IEnumerator SolveSequence()
    {
        if (closeTerminalAfterSolve)
        {
            yield return new WaitForSeconds(closeDelayAfterSolve);
            CloseTerminal();
        }

        ShowBigScreen();

        yield return new WaitForSeconds(delayBeforeServerRise);

        if (serverMinigameManager != null)
            serverMinigameManager.OnPlayerEnterTrigger();
        else
            Debug.LogWarning("[FalloutTerminal] ServerMinigameManager not assigned in Inspector!");

        if (autoHideBigScreen)
        {
            yield return new WaitForSeconds(bigScreenAutoHideDelay);
            HideBigScreen();
        }
    }

    public void ShowBigScreen()
    {
        if (bigScreenText != null) bigScreenText.text = bigScreenMessage;
        if (bigScreenPanel != null) bigScreenPanel.SetActive(true);
        PlaySound(bigScreenSound);
    }

    public void HideBigScreen()
    {
        if (bigScreenPanel != null) bigScreenPanel.SetActive(false);
    }

    private void Lock()
    {
        _locked = true;
        PlaySound(lockedSound);

        foreach (var slot in wordSlots)
            if (slot != null) slot.SetInteractable(false);

        if (terminalMessageText != null)
            terminalMessageText.text = lockedMessage;
    }
}