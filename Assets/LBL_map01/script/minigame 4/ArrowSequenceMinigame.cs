using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ArrowSequenceMinigame : MonoBehaviour
{
    private enum Direction { Up, Down, Left, Right }

    [Header("UI References")]
    public TMP_Text progressText;
    public RectTransform arrowContainer;
    public GameObject arrowIconPrefab;

    [Header("Settings")]
    public int sequenceLength = 9;
    public Color pendingColor = Color.white;
    public Color currentColor = Color.cyan;
    public Color doneColor = Color.gray;

    private List<Direction> _sequence;
    private List<GameObject> _icons;
    private int _currentIndex;
    private Action _onComplete;
    private bool _active;

    public void StartMinigame(Action onComplete)
    {
        _onComplete = onComplete;
        GenerateSequence();
        BuildIcons();
        _currentIndex = 0;
        _active = true;
        UpdateProgress();
        UpdateHighlight();
    }

    private void Update()
    {
        if (!_active) return;

        if (Input.GetKeyDown(KeyCode.W)) HandleInput(Direction.Up);
        else if (Input.GetKeyDown(KeyCode.S)) HandleInput(Direction.Down);
        else if (Input.GetKeyDown(KeyCode.A)) HandleInput(Direction.Left);
        else if (Input.GetKeyDown(KeyCode.D)) HandleInput(Direction.Right);
    }

    private void GenerateSequence()
    {
        _sequence = new List<Direction>();
        Direction[] all = (Direction[])Enum.GetValues(typeof(Direction));
        for (int i = 0; i < sequenceLength; i++)
            _sequence.Add(all[UnityEngine.Random.Range(0, all.Length)]);
    }

    private void BuildIcons()
    {
        foreach (Transform child in arrowContainer)
            Destroy(child.gameObject);

        _icons = new List<GameObject>();
        for (int i = 0; i < _sequence.Count; i++)
        {
            GameObject go = Instantiate(arrowIconPrefab, arrowContainer);
            TMP_Text t = go.GetComponentInChildren<TMP_Text>();
            if (t) t.text = GetArrowSymbol(_sequence[i]);
            _icons.Add(go);
        }
    }

    private string GetArrowSymbol(Direction d)
    {
        switch (d)
        {
            case Direction.Up: return "↑";
            case Direction.Down: return "↓";
            case Direction.Left: return "←";
            default: return "→";
        }
    }

    private void HandleInput(Direction d)
    {
        if (d == _sequence[_currentIndex])
        {
            SetIconColor(_icons[_currentIndex], doneColor);
            _currentIndex++;
            UpdateProgress();

            if (_currentIndex >= _sequence.Count)
            {
                Complete();
                return;
            }

            UpdateHighlight();
        }
        else
        {
            ResetSequence();
        }
    }

    private void ResetSequence()
    {
        _currentIndex = 0;
        for (int i = 0; i < _icons.Count; i++)
            SetIconColor(_icons[i], pendingColor);

        UpdateProgress();
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        for (int i = 0; i < _icons.Count; i++)
        {
            Color c = i < _currentIndex ? doneColor : (i == _currentIndex ? currentColor : pendingColor);
            SetIconColor(_icons[i], c);
        }
    }

    private void SetIconColor(GameObject go, Color c)
    {
        TMP_Text t = go.GetComponentInChildren<TMP_Text>();
        if (t) t.color = c;
    }

    private void UpdateProgress()
    {
        if (progressText) progressText.text = _currentIndex + " / " + _sequence.Count;
    }

    private void Complete()
    {
        _active = false;
        _onComplete?.Invoke();
    }
}