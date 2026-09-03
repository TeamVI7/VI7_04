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
    [Tooltip("Chỉ bật nếu terminal NÀY là script sở hữu ServerMinigameManager. " +
             "ServerMinigameManager chỉ khởi động được ĐÚNG MỘT LẦN — nếu scene còn có " +
             "MinigameFlowController cũng trỏ vào cùng manager đó, hãy TẮT cái này, " +
             "nếu không script nào chạy trước sẽ nuốt luôn lượt kích hoạt của script kia.")]
    public bool triggerServerPuzzleOnSolve = true;
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
    [Tooltip("Tiếng 'blip' khi rê chuột qua 1 dòng bất kỳ (kể cả dòng garbage), giống bản gốc.")]
    public AudioClip hoverSound;

    [Header("Bracket Effects (giống bản gốc: 2 kết quả ngẫu nhiên)")]
    [Tooltip("Âm thanh khi bracket cho hiệu ứng hồi đầy lượt thử. Để trống sẽ dùng chung dudRemovedSound.")]
    public AudioClip allowanceReplenishedSound;
    [Range(0f, 1f)]
    [Tooltip("Xác suất bracket trả về hiệu ứng 'Replenish Allowance' (hồi lượt) thay vì 'Remove Dud'.")]
    public float replenishAllowanceChance = 0.5f;

    [Header("Typewriter Effect (gõ chữ giống bản gốc)")]
    public bool useTypewriterEffect = true;
    [Tooltip("Thời gian giữa mỗi ký tự khi 'gõ' chữ lên màn hình.")]
    public float typewriterCharDelay = 0.018f;
    [Tooltip("Tiếng bíp phát mỗi ký tự khi gõ chữ. Để trống nếu không muốn có tiếng.")]
    public AudioClip typeCharSound;
    public Vector2 typeCharPitchRange = new Vector2(0.92f, 1.08f);

    private Coroutine _messageTypeCoroutine;
    private Coroutine _historyTypeCoroutine;

    [Header("Cursor (giống cách tương tác bản gốc: rê chuột tự do để chọn)")]
    [Tooltip("Khi mở terminal, con trỏ chuột sẽ tự hiện ra và mở khoá (CursorLockMode.None) để bạn rê chuột chọn từ, giống hệt cách Fallout gốc hoạt động. Khi đóng terminal, trạng thái chuột cũ (khoá giữa màn hình để chơi FPS) sẽ được khôi phục lại.")]
    public bool manageCursorOnOpen = true;
    private CursorLockMode _previousCursorLockState;
    private bool _previousCursorVisible;

    [Header("Keyboard Navigation (điều hướng bằng phím mũi tên)")]
    [Tooltip("Bật để chọn dòng bằng phím mũi tên Lên/Xuống + Enter, thay vì phải rê chuột.")]
    public bool enableKeyboardNavigation = true;
    public KeyCode navUpKey = KeyCode.UpArrow;
    public KeyCode navDownKey = KeyCode.DownArrow;
    public KeyCode navConfirmKeyPrimary = KeyCode.Return;
    public KeyCode navConfirmKeySecondary = KeyCode.KeypadEnter;
    [Tooltip("Tiếng phát ra khi bấm Enter vào 1 dòng rác/không chọn được (không mất lượt, chỉ báo hiệu).")]
    public AudioClip invalidSelectSound;

    private readonly List<TerminalWordSlot> _navigableSlots = new List<TerminalWordSlot>();
    private TerminalWordSlot _keyboardHighlightedSlot;

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

    private void Update()
    {
        if (!enableKeyboardNavigation) return;
        if (!IsTerminalOpen || _solved || _locked) return;
        HandleKeyboardNavigation();
    }

    private void HandleKeyboardNavigation()
    {
        if (Input.GetKeyDown(navUpKey))
            MoveKeyboardSelection(-1);
        else if (Input.GetKeyDown(navDownKey))
            MoveKeyboardSelection(1);

        if (Input.GetKeyDown(navConfirmKeyPrimary) || Input.GetKeyDown(navConfirmKeySecondary))
            ConfirmKeyboardSelection();
    }

    /// <summary>Di chuyển highlight tới dòng kế tiếp/trước đó có thể chọn được (word slot hoặc bracket).</summary>
    private void MoveKeyboardSelection(int direction)
    {
        if (_navigableSlots.Count == 0) return;

        int currentIndex = _keyboardHighlightedSlot != null ? _navigableSlots.IndexOf(_keyboardHighlightedSlot) : -1;
        _keyboardHighlightedSlot?.SetRowHighlighted(false);

        int nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + direction + _navigableSlots.Count) % _navigableSlots.Count;

        _keyboardHighlightedSlot = _navigableSlots[nextIndex];
        _keyboardHighlightedSlot.SetRowHighlighted(true);
        PlaySound(hoverSound);
    }

    /// <summary>Chọn dòng đang được highlight bằng bàn phím. Nếu là dòng rác/không hợp lệ thì chỉ phát tiếng
    /// báo hiệu, KHÔNG mất lượt — người chơi vẫn phải tự đọc màn hình để biết dòng nào là ứng viên thật,
    /// con trỏ không tự động bỏ qua dòng rác (nếu bỏ qua thì lộ luôn đáp án, mất ý nghĩa minigame).</summary>
    private void ConfirmKeyboardSelection()
    {
        if (_keyboardHighlightedSlot == null) return;

        if (!_keyboardHighlightedSlot.isSelectable || _keyboardHighlightedSlot.isUsed)
        {
            PlaySound(invalidSelectSound);
            return;
        }

        _keyboardHighlightedSlot.OnSelected?.Invoke(_keyboardHighlightedSlot);
    }

    /// <summary>Dựng danh sách TẤT CẢ các dòng trên màn hình (kể cả dòng rác) để bàn phím có thể di chuyển qua
    /// từng dòng một, y hệt con trỏ tự do của bản gốc — không lọc riêng ra "dòng chọn được" vì như vậy sẽ vô
    /// tình lộ đáp án cho người chơi chỉ bằng cách bấm mũi tên mà không cần đọc màn hình.</summary>
    private void RefreshNavigableSlots(bool resetToFirst)
    {
        _navigableSlots.Clear();
        foreach (var s in wordSlots)
            if (s != null) _navigableSlots.Add(s);

        if (resetToFirst || _keyboardHighlightedSlot == null || !_navigableSlots.Contains(_keyboardHighlightedSlot))
        {
            _keyboardHighlightedSlot?.SetRowHighlighted(false);
            _keyboardHighlightedSlot = _navigableSlots.Count > 0 ? _navigableSlots[0] : null;
            _keyboardHighlightedSlot?.SetRowHighlighted(true);
        }
    }

    public bool IsTerminalOpen => terminalPanel != null && terminalPanel.activeSelf;

    public void StartTerminal()
    {
        if (terminalPanel != null) terminalPanel.SetActive(true);

        uiInputBlocker?.BlockInput();
        autoDisableBlockingColliders?.DisableBlockers();
        forceRaycastTarget?.FixAll();
        SwitchToTerminalCamera();
        ShowCursorForTerminal();

        SetupNewGame();
    }

    public void CloseTerminal()
    {
        if (terminalPanel != null) terminalPanel.SetActive(false);

        uiInputBlocker?.UnblockInput();
        autoDisableBlockingColliders?.RestoreBlockers();
        RestoreCursorAfterTerminal();
        RestorePlayerCamera();
    }

    private void SwitchToTerminalCamera()
    {
        if (terminalCamera == null) return;

        // Camera.main chỉ trả về camera ĐANG BẬT. Nếu một minigame khác đã tắt camera
        // player trước đó, Camera.main = null; nếu cứ để _playerCamera = null thì lúc
        // đóng terminal sẽ không có camera nào được bật lại -> màn hình đen.
        _playerCamera = Camera.main != null ? Camera.main : FindPlayerCameraFallback();

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
        else
        {
            _playerListener = null;
        }

        terminalCamera.enabled = true;
        var termListener = terminalCamera.GetComponent<AudioListener>();
        if (termListener != null) termListener.enabled = true;
    }

    /// <summary>Tìm camera player kể cả khi nó đang bị TẮT (Camera.main bỏ qua camera tắt).
    /// Ưu tiên object tag "MainCamera", nếu không có thì lấy camera đầu tiên tìm được.</summary>
    private Camera FindPlayerCameraFallback()
    {
        var all = FindObjectsOfType<Camera>(true);
        foreach (var c in all)
        {
            if (c == terminalCamera) continue;
            if (c.CompareTag("MainCamera")) return c;
        }

        foreach (var c in all)
            if (c != terminalCamera) return c;

        return null;
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

        // Lưới an toàn: nếu camera player lúc mở terminal vốn đã bị script khác tắt,
        // khôi phục "đúng trạng thái cũ" sẽ để lại scene KHÔNG có camera nào -> màn hình đen.
        // Trường hợp đó thì cứ bật camera player lên, thà lệch quyền sở hữu còn hơn đen màn hình.
        if (!AnyCameraEnabled() && _playerCamera != null)
        {
            Debug.LogWarning("[FalloutTerminal] Không còn camera nào được bật sau khi đóng terminal — " +
                             "bật lại camera player để tránh màn hình đen.", this);
            _playerCamera.enabled = true;
            if (_playerListener != null) _playerListener.enabled = true;
        }
    }

    private bool AnyCameraEnabled()
    {
        foreach (var c in FindObjectsOfType<Camera>())
            if (c.enabled && c.gameObject.activeInHierarchy) return true;
        return false;
    }

    /// <summary>Mở khoá + hiện con trỏ chuột, giống bản gốc (bạn rê chuột tự do trên màn hình terminal để chọn dòng).</summary>
    private void ShowCursorForTerminal()
    {
        if (!manageCursorOnOpen) return;

        _previousCursorLockState = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>Trả con trỏ chuột về đúng trạng thái trước khi mở terminal (thường là khoá giữa màn hình để chơi FPS).</summary>
    private void RestoreCursorAfterTerminal()
    {
        if (!manageCursorOnOpen) return;

        Cursor.lockState = _previousCursorLockState;
        Cursor.visible = _previousCursorVisible;
    }

    private void SetupNewGame()
    {
        _solved = false;
        _locked = false;
        _attemptsLeft = Mathf.Max(1, maxAttempts);
        _historyBuilder.Clear();

        if (terminalMessageText != null) PlayMessageTypewriter(introMessage);
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

        RefreshNavigableSlots(true);
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
        slot.isSelectable = true;
        slot.SetHighlight(TerminalVisualState.Normal);
        slot.OnSelected = OnWordSlotClicked;
        slot.OnInvalidClick = HandleInvalidClick;
        slot.OnHover = HandleSlotHover;
    }

    private void SetupGarbageOnlySlot(TerminalWordSlot slot, string address)
    {
        slot.assignedWord = null;
        slot.isDudRemovalBracket = false;
        slot.SetText(address + BuildGarbageLine(10 + Random.Range(0, 6)));
        slot.SetInteractable(false);
        slot.isSelectable = false;
        slot.OnSelected = null;
        slot.SetHighlight(TerminalVisualState.Normal);
        slot.OnInvalidClick = HandleInvalidClick;
        slot.OnHover = HandleSlotHover;
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
        slot.isSelectable = true;
        slot.SetHighlight(TerminalVisualState.Normal);
        slot.OnSelected = OnBracketSlotClicked;
        slot.OnInvalidClick = HandleInvalidClick;
        slot.OnHover = HandleSlotHover;
    }

    /// <summary>Bấm vào dòng rác (không phải từ, không phải bracket): không tốn lượt, không có tác dụng gì,
    /// chỉ phát tiếng báo hiệu — đúng như bấm vào khoảng trống trong bản gốc.</summary>
    private void HandleInvalidClick(TerminalWordSlot slot)
    {
        if (_solved || _locked) return;
        PlaySound(invalidSelectSound);
    }

    /// <summary>Phát tiếng "blip" khi rê chuột vào bất kỳ dòng nào, kể cả dòng garbage không click được — giống bản gốc.</summary>
    private void HandleSlotHover(TerminalWordSlot slot, bool entered)
    {
        if (entered) PlaySound(hoverSound);
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
        slot.isSelectable = false;
        slot.SetInteractable(false);
        slot.SetHighlight(TerminalVisualState.Disabled);

        // Giống bản gốc: bracket cho 1 trong 2 hiệu ứng ngẫu nhiên.
        bool canReplenish = _attemptsLeft < Mathf.Max(1, maxAttempts);
        bool doReplenish = canReplenish && Random.value < replenishAllowanceChance;

        if (doReplenish)
        {
            PlaySound(allowanceReplenishedSound != null ? allowanceReplenishedSound : dudRemovedSound);
            _attemptsLeft = Mathf.Max(1, maxAttempts);
            UpdateAttemptsText();
            AppendHistory("Allowance replenished.");
            RefreshNavigableSlots(false);
            return;
        }

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
            target.isSelectable = false;
            target.SetInteractable(false);
            target.SetHighlight(TerminalVisualState.Disabled);
            target.SetText(targetAddress + BuildGarbageLine(10 + Random.Range(0, 6)));
            AppendHistory("1 wrong password removed. No attempt lost.");
        }
        else
        {
            AppendHistory("No wrong password left to remove.");
        }

        RefreshNavigableSlots(false);
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
        slot.isSelectable = false;
        slot.SetInteractable(false);
        slot.SetHighlight(TerminalVisualState.Wrong);
        PlaySound(wrongSound);

        int likeness = ComputeLikeness(guess, _correctWord);
        _attemptsLeft--;
        UpdateAttemptsText();
        AppendHistory($"> {guess}\nACCESS DENIED — Likeness: {likeness}/{_correctWord.Length}");

        RefreshNavigableSlots(false);

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
        if (historyText == null)
        {
            _historyBuilder.AppendLine(line);
            return;
        }

        string alreadyShown = _historyBuilder.ToString();
        _historyBuilder.AppendLine(line);
        string fullText = _historyBuilder.ToString();

        if (_historyTypeCoroutine != null) StopCoroutine(_historyTypeCoroutine);
        _historyTypeCoroutine = StartCoroutine(TypeAppend(historyText, alreadyShown, fullText));
    }

    private void PlayMessageTypewriter(string fullText)
    {
        if (terminalMessageText == null) return;
        if (_messageTypeCoroutine != null) StopCoroutine(_messageTypeCoroutine);
        _messageTypeCoroutine = StartCoroutine(TypeText(terminalMessageText, fullText));
    }

    /// <summary>Gõ toàn bộ text từ đầu (dùng cho message chính: intro / solved / locked).</summary>
    private IEnumerator TypeText(TMP_Text target, string fullText)
    {
        if (!useTypewriterEffect || target == null)
        {
            if (target != null) target.text = fullText;
            yield break;
        }

        target.text = "";
        var sb = new StringBuilder();
        bool inTag = false;

        foreach (char c in fullText)
        {
            bool isTagChar = false;
            if (c == '<') inTag = true;
            if (inTag) isTagChar = true;
            sb.Append(c);
            if (c == '>') inTag = false;

            target.text = sb.ToString();

            if (!isTagChar)
            {
                PlayTypeCharSound();
                yield return new WaitForSecondsRealtime(typewriterCharDelay);
            }
        }
    }

    /// <summary>Chỉ gõ phần text MỚI được thêm vào, giữ nguyên phần đã hiện trước đó (dùng cho history log).</summary>
    private IEnumerator TypeAppend(TMP_Text target, string alreadyShown, string fullText)
    {
        if (target == null) yield break;

        if (!useTypewriterEffect)
        {
            target.text = fullText;
            yield break;
        }

        target.text = alreadyShown;
        var sb = new StringBuilder(alreadyShown);
        string toType = fullText.Length >= alreadyShown.Length ? fullText.Substring(alreadyShown.Length) : fullText;
        bool inTag = false;

        foreach (char c in toType)
        {
            bool isTagChar = false;
            if (c == '<') inTag = true;
            if (inTag) isTagChar = true;
            sb.Append(c);
            if (c == '>') inTag = false;

            target.text = sb.ToString();

            if (!isTagChar)
            {
                PlayTypeCharSound();
                yield return new WaitForSecondsRealtime(typewriterCharDelay);
            }
        }
    }

    private void PlayTypeCharSound()
    {
        if (typeCharSound == null || audioSource == null) return;
        float originalPitch = audioSource.pitch;
        audioSource.pitch = Random.Range(typeCharPitchRange.x, typeCharPitchRange.y);
        audioSource.PlayOneShot(typeCharSound);
        audioSource.pitch = originalPitch;
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

        _keyboardHighlightedSlot?.SetRowHighlighted(false);
        _keyboardHighlightedSlot = null;

        foreach (var slot in wordSlots)
            if (slot != null) { slot.SetInteractable(false); slot.isSelectable = false; }

        if (terminalMessageText != null)
            PlayMessageTypewriter(solvedMessage);

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

        if (!triggerServerPuzzleOnSolve)
        {
            Debug.Log("[FalloutTerminal] triggerServerPuzzleOnSolve = false — để script khác kích hoạt puzzle tắt server.");
        }
        else if (serverMinigameManager == null)
        {
            Debug.LogWarning("[FalloutTerminal] ServerMinigameManager not assigned in Inspector!");
        }
        else if (!serverMinigameManager.OnPlayerEnterTrigger())
        {
            Debug.LogWarning("[FalloutTerminal] ServerMinigameManager đã được script khác kích hoạt trước đó. " +
                             "Nếu MinigameFlowController mới là chủ sở hữu, hãy tắt 'triggerServerPuzzleOnSolve' " +
                             "trên terminal này.", this);
        }

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

        _keyboardHighlightedSlot?.SetRowHighlighted(false);
        _keyboardHighlightedSlot = null;

        foreach (var slot in wordSlots)
            if (slot != null) { slot.SetInteractable(false); slot.isSelectable = false; }

        if (terminalMessageText != null)
            PlayMessageTypewriter(lockedMessage);
    }
}