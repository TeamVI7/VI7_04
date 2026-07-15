using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VoltageCalibrationMinigame : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text percentText;
    public TMP_Text statusText;

    [Header("Dial (Rotation, A/D)")]
    public RectTransform dialPointer;
    public TMP_Text dialValueText;
    public Image dialFill;
    public RectTransform dialTargetMinMarker;
    public RectTransform dialTargetMaxMarker;

    [Header("Vertical Bar (W/S)")]
    public RectTransform barIndicator;
    public RectTransform barTargetZone;
    public TMP_Text barValueText;
    public Image barFill;
    public float barTrackHeight = 300f;

    [Header("Shared Settings")]
    public float rotateSpeed = 90f;
    public float moveSpeed = 90f;
    public int targetRangeWidth = 60;
    [Tooltip("How far outside the target zone (same units as the value) the percent starts ramping up from 0. Larger = smoother/more gradual approach.")]
    public float proximityFalloff = 90f;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color matchedColor = Color.green;

    [Header("Keys")]
    public KeyCode rotateLeftKey = KeyCode.A;
    public KeyCode rotateRightKey = KeyCode.D;
    public KeyCode barUpKey = KeyCode.W;
    public KeyCode barDownKey = KeyCode.S;
    public KeyCode confirmKey = KeyCode.Space;

    private float _dialValue;
    private float _barValue;
    private float _dialTargetMin;
    private float _dialTargetMax;
    private float _barTargetMin;
    private float _barTargetMax;
    private Action _onComplete;
    private bool _active;

    public void StartMinigame(Action onComplete)
    {
        _onComplete = onComplete;

        _dialValue = UnityEngine.Random.Range(1f, 361f);
        _barValue = UnityEngine.Random.Range(1f, 361f);

        _dialTargetMin = UnityEngine.Random.Range(1f, 361f - targetRangeWidth);
        _dialTargetMax = _dialTargetMin + targetRangeWidth;

        _barTargetMin = UnityEngine.Random.Range(1f, 361f - targetRangeWidth);
        _barTargetMax = _barTargetMin + targetRangeWidth;

        _active = true;

        if (statusText) statusText.text = "";

        PositionDialTargetMarkers();
        PositionBarTargetZone();
        RefreshVisuals();
        UpdatePercent();
    }

    private void Update()
    {
        if (!_active) return;

        if (Input.GetKey(rotateLeftKey)) _dialValue -= rotateSpeed * Time.deltaTime;
        if (Input.GetKey(rotateRightKey)) _dialValue += rotateSpeed * Time.deltaTime;
        _dialValue = Mathf.Repeat(_dialValue, 360f);
        if (_dialValue < 1f) _dialValue = 360f;

        if (Input.GetKey(barUpKey)) _barValue += moveSpeed * Time.deltaTime;
        if (Input.GetKey(barDownKey)) _barValue -= moveSpeed * Time.deltaTime;
        _barValue = Mathf.Clamp(_barValue, 1f, 360f);

        RefreshVisuals();
        UpdatePercent();

        if (Input.GetKeyDown(confirmKey)) TryComplete();
    }

    private bool IsDialMatched()
    {
        return _dialValue >= _dialTargetMin && _dialValue <= _dialTargetMax;
    }

    private bool IsBarMatched()
    {
        return _barValue >= _barTargetMin && _barValue <= _barTargetMax;
    }

    private void UpdatePercent()
    {
        float dialPercent = GetAxisPercent(_dialValue, _dialTargetMin, _dialTargetMax, IsDialMatched(), circular: true);
        float barPercent = GetAxisPercent(_barValue, _barTargetMin, _barTargetMax, IsBarMatched(), circular: false);

        int percent = Mathf.RoundToInt(dialPercent + barPercent);
        if (percentText) percentText.text = percent + "%";
    }

    // Returns a smooth 0-50 contribution for one axis: 50 when inside the target
    // zone, ramping down to 0 as the value gets further than proximityFalloff
    // away from the nearest edge of the zone.
    private float GetAxisPercent(float value, float targetMin, float targetMax, bool isMatched, bool circular)
    {
        if (isMatched) return 50f;

        float distance;
        if (circular)
        {
            distance = Mathf.Min(
                Mathf.Abs(Mathf.DeltaAngle(value, targetMin)),
                Mathf.Abs(Mathf.DeltaAngle(value, targetMax)));
        }
        else
        {
            distance = Mathf.Min(Mathf.Abs(value - targetMin), Mathf.Abs(value - targetMax));
        }

        float falloff = Mathf.Max(proximityFalloff, 0.0001f);
        float closeness = 1f - Mathf.Clamp01(distance / falloff);
        return 50f * closeness;
    }

    private void RefreshVisuals()
    {
        if (dialPointer) dialPointer.localRotation = Quaternion.Euler(0f, 0f, -_dialValue);
        if (dialValueText) dialValueText.text = Mathf.RoundToInt(_dialValue).ToString();
        if (dialFill) dialFill.color = IsDialMatched() ? matchedColor : normalColor;

        if (barIndicator)
        {
            Vector2 pos = barIndicator.anchoredPosition;
            pos.y = (_barValue / 360f) * barTrackHeight;
            barIndicator.anchoredPosition = pos;
        }
        if (barValueText) barValueText.text = Mathf.RoundToInt(_barValue).ToString();
        if (barFill) barFill.color = IsBarMatched() ? matchedColor : normalColor;
    }

    private void PositionDialTargetMarkers()
    {
        if (dialTargetMinMarker) dialTargetMinMarker.localRotation = Quaternion.Euler(0f, 0f, -_dialTargetMin);
        if (dialTargetMaxMarker) dialTargetMaxMarker.localRotation = Quaternion.Euler(0f, 0f, -_dialTargetMax);
    }

    private void PositionBarTargetZone()
    {
        if (!barTargetZone) return;
        Vector2 pos = barTargetZone.anchoredPosition;
        pos.y = (_barTargetMin / 360f) * barTrackHeight;
        barTargetZone.anchoredPosition = pos;

        Vector2 size = barTargetZone.sizeDelta;
        size.y = (targetRangeWidth / 360f) * barTrackHeight;
        barTargetZone.sizeDelta = size;
    }

    private void TryComplete()
    {
        if (IsDialMatched() && IsBarMatched())
        {
            _active = false;
            if (statusText) statusText.text = "CALIBRATION COMPLETE";
            _onComplete?.Invoke();
        }
        else
        {
            if (statusText) statusText.text = "NOT BALANCED YET";
        }
    }
}