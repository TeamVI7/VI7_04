// MechWeakpoint.cs
using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MechWeakpoint : MonoBehaviour, IDamageable, IMeleeDamageable
{
    [Header("References")]
    public EnemyHealth bossHealth;
    public Renderer[] BodyRenderers;

    [Header("Exposure")]
    public float ExposeThreshold   = 60f;
    public float AccumulatorWindow = 4f;
    public float ExposedDuration   = 5f;
    public bool  CloseOnMeleeHit   = true;

    [Header("Melee Bonus")]
    public float MeleeDamageAmount = 50f;

    [Header("Flash")]
    public Material FlashMaterial;
    public float FlashInterval = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    public event Action OnWeakpointExposed;
    public event Action OnWeakpointClosed;
    public bool IsExposed { get; private set; }

    private float _accumulator;
    private float _accumulatorTimer;

    private Material[][] _originalMats;
    private Material[][] _flashMatSet;
    private Coroutine _blinkRoutine;
    private Coroutine _exposedTimeoutRoutine;

    private void Awake()
    {
        if (bossHealth == null) bossHealth = GetComponentInParent<EnemyHealth>();
        if (BodyRenderers == null || BodyRenderers.Length == 0)
            BodyRenderers = GetComponentsInChildren<Renderer>();

        BuildMatSets();
    }

    private void BuildMatSets()
    {
        _originalMats = new Material[BodyRenderers.Length][];
        for (int i = 0; i < BodyRenderers.Length; i++)
            _originalMats[i] = BodyRenderers[i] != null ? BodyRenderers[i].sharedMaterials : null;

        _flashMatSet = new Material[BodyRenderers.Length][];
        if (FlashMaterial == null) return;

        for (int i = 0; i < BodyRenderers.Length; i++)
        {
            if (_originalMats[i] == null) continue;
            var arr = new Material[_originalMats[i].Length];
            for (int s = 0; s < arr.Length; s++) arr[s] = FlashMaterial;
            _flashMatSet[i] = arr;
        }
    }

    private void Update()
    {
        if (IsExposed || _accumulatorTimer <= 0f) return;

        _accumulatorTimer -= Time.deltaTime;
        if (_accumulatorTimer <= 0f) _accumulator = 0f;
    }

    public void TakeDamage(float amount, Vector3 direction, Vector3 hitPoint)
    {
        bossHealth?.TakeDamage(amount, direction, hitPoint);
        if (IsExposed) return;

        _accumulator += amount;
        _accumulatorTimer = AccumulatorWindow;

        Log($"Accumulated {_accumulator:F1}/{ExposeThreshold:F1}.");

        if (_accumulator >= ExposeThreshold)
        {
            _accumulator = 0f;
            ExposeWeakpoint();
        }
    }

    public bool TakeMeleeDamage(float amount, Vector3 direction, Vector3 hitPoint)
    {
        if (!IsExposed)
        {
            Log("Melee hit but weakpoint not exposed — no bonus damage.");
            return false;
        }

        bossHealth?.TakeDamage(MeleeDamageAmount, direction, hitPoint);
        Log($"Melee bonus hit for {MeleeDamageAmount}.");

        if (CloseOnMeleeHit) CloseWeakpoint();
        return true;
    }

    private void ExposeWeakpoint()
    {
        IsExposed = true;
        Log("Weakpoint EXPOSED.");
        OnWeakpointExposed?.Invoke();

        if (_blinkRoutine != null) StopCoroutine(_blinkRoutine);
        _blinkRoutine = StartCoroutine(Co_Blink());

        if (_exposedTimeoutRoutine != null) StopCoroutine(_exposedTimeoutRoutine);
        _exposedTimeoutRoutine = StartCoroutine(Co_ExposedTimeout());
    }

    private IEnumerator Co_ExposedTimeout()
    {
        yield return new WaitForSeconds(ExposedDuration);
        CloseWeakpoint();
    }

    private void CloseWeakpoint()
    {
        if (!IsExposed) return;
        IsExposed = false;

        if (_blinkRoutine != null) { StopCoroutine(_blinkRoutine); _blinkRoutine = null; }
        if (_exposedTimeoutRoutine != null) { StopCoroutine(_exposedTimeoutRoutine); _exposedTimeoutRoutine = null; }

        RestoreOriginalMats();
        Log("Weakpoint closed.");
        OnWeakpointClosed?.Invoke();
    }

    private IEnumerator Co_Blink()
    {
        bool on = false;
        while (true)
        {
            on = !on;
            if (on) ApplyFlashSet();
            else    RestoreOriginalMats();
            yield return new WaitForSeconds(FlashInterval);
        }
    }

    private void ApplyFlashSet()
    {
        for (int i = 0; i < BodyRenderers.Length; i++)
        {
            if (BodyRenderers[i] == null || _flashMatSet[i] == null) continue;
            BodyRenderers[i].sharedMaterials = _flashMatSet[i];
        }
    }

    private void RestoreOriginalMats()
    {
        for (int i = 0; i < BodyRenderers.Length; i++)
        {
            if (BodyRenderers[i] != null && _originalMats[i] != null)
                BodyRenderers[i].sharedMaterials = _originalMats[i];
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[MechWeakpoint] {msg}", this);
    }

    private void OnDrawGizmosSelected()
    {
        Color color = IsExposed ? Color.yellow : Color.red;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position, 0.3f);

        // The accumulator is the whole mechanic and it's invisible otherwise —
        // without this you can't tell whether you're two hits or ten from opening it.
        string label = !Application.isPlaying
            ? $"weakpoint\n{ExposeThreshold:0} dmg in {AccumulatorWindow:0.#}s"
            : IsExposed
                ? "EXPOSED"
                : $"{_accumulator:0}/{ExposeThreshold:0}";

        MechGizmos.Label(transform.position + Vector3.up * 0.6f, label, color);
    }
}