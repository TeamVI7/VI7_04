using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BIOSCreditsPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI creditsBody;
    [SerializeField] private ScrollRect      scrollRect;

    [Header("Typewriter")]
    [SerializeField] private float charDelay = 0.008f;

    private readonly List<(string key, string value, bool isHeader)> _rows = new()
    {
        ("── CLASSIFIED PERSONNEL FILE ──────────────────", "", true),
        ("",   "",       false),
        ("", "",                 false),
        ("",       "",            false),
        ("",                  "",                              false),
        ("── DEVELOPMENT UNIT ───────────────────────────", "", true),
        ("",          "",                            false),
        ("",           "",                            false),
        ("",      "",                            false),
        ("",      "",                            false),
        ("",                  "",                              false),
        ("── AFFILIATED ENTITIES ────────────────────────", "", true),
        ("Engine",            "Unity 2022.3 LTS",              false),
        ("",                  "",                              false),
        ("── CORPORATE SPONSORS ─────────────────────────", "", true),
        ("",           "",         false),
        ("",          "",    false),
        ("",           "",false),
        ("",              "",          false),
        ("",         "",        false),
        ("",           "",false),
        ("",                  "",                              false),
        ("── FIELD ACKNOWLEDGEMENTS ─────────────────────", "", true),
        ("Playtesters",       "",          false),
        ("",                  "",                              false),
        ("",  "", true),
        ("",          "", true),
    };

    private Coroutine _typeCoroutine;
    private bool      _blockInput  = false;
    private bool      _typing      = true;

    void OnEnable()
    {
        _blockInput = true;
        _typing     = true;

        if (creditsBody != null) creditsBody.text = "";

        // Disable inertia so ScrollRect doesn't drift back
        if (scrollRect != null)
        {
            scrollRect.inertia                    = false;
            scrollRect.verticalNormalizedPosition = 1f;
        }

        if (_typeCoroutine != null) StopCoroutine(_typeCoroutine);
        _typeCoroutine = StartCoroutine(TypeCredits());
    }

    void OnDisable()
    {
        if (_typeCoroutine != null) StopCoroutine(_typeCoroutine);
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        if (_blockInput) { _blockInput = false; return; }

        // Manual scroll only after typing is done
        if (!_typing && scrollRect != null)
        {
            if (Input.GetKey(KeyCode.DownArrow))
                scrollRect.verticalNormalizedPosition =
                    Mathf.Clamp01(scrollRect.verticalNormalizedPosition - Time.deltaTime * 0.8f);

            if (Input.GetKey(KeyCode.UpArrow))
                scrollRect.verticalNormalizedPosition =
                    Mathf.Clamp01(scrollRect.verticalNormalizedPosition + Time.deltaTime * 0.8f);
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F10))
            BIOSMainMenu.Instance?.SwitchTab(0);
    }

    IEnumerator TypeCredits()
    {
        if (creditsBody == null) yield break;

        string full = "";

        foreach (var row in _rows)
        {
            string line;
            if (row.isHeader || string.IsNullOrEmpty(row.value))
                line = row.key + "\n";
            else
            {
                int pad = Mathf.Max(1, 48 - row.key.Length - row.value.Length);
                line = row.key + new string(' ', pad) + row.value + "\n";
            }

            foreach (char c in line)
            {
                full += c;
                creditsBody.text = full;
                yield return new WaitForSeconds(charDelay);
                yield return null; // wait extra frame for layout rebuild
                ScrollToBottom();
            }
        }

        _typing = false;
    }

    void ScrollToBottom()
    {
        if (scrollRect == null) return;

        // Force the RectTransform layout to rebuild fully
        LayoutRebuilder.ForceRebuildLayoutImmediate(
            scrollRect.content
        );

        scrollRect.verticalNormalizedPosition = 0f;
    }
}