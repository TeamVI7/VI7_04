using UnityEngine;

// Base for every telegraph preset (laser, decal, emission flash, whatever comes
// next). Owns nothing about who's attacking — any attack source just calls
// Play(duration)/Stop(). Presets that need spatial setup (a line, a world
// position) implement ITelegraphAimable / ITelegraphPlaceable and the adapter
// calling Play() sets that up first.
public abstract class TelegraphVisual : MonoBehaviour
{
    #region Inspector

    [SerializeField] protected TelegraphProfile profile;
    [SerializeField] private bool debugLog;

    #endregion

    #region State

    public bool IsActive { get; private set; }
    protected float Elapsed { get; private set; }
    protected float Duration { get; private set; }

    #endregion

    #region Public API

    public void Play(float duration)
    {
        Duration = Mathf.Max(0.01f, duration);
        Elapsed = 0f;
        IsActive = true;
        OnPlay();
        Log("Started.");
    }

    public void Stop()
    {
        if (!IsActive) return;
        IsActive = false;
        OnStop();
        Log("Stopped.");
    }

    #endregion

    #region Update Loop

    protected virtual void Update()
    {
        if (!IsActive) return;

        Elapsed += Time.deltaTime;
        float normalized = Mathf.Clamp01(Elapsed / Duration);
        float curveValue = profile != null ? profile.intensityCurve.Evaluate(normalized) : normalized;

        OnTick(normalized, curveValue);

        if (normalized >= 1f)
            Stop();
    }

    #endregion

    #region Derived Hooks

    protected abstract void OnPlay();
    protected abstract void OnTick(float normalizedTime, float curveValue);
    protected abstract void OnStop();

    #endregion

    #region Debug

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    protected void Log(string msg)
    {
        if (debugLog)
            Debug.Log($"[{GetType().Name}] {name}: {msg}", this);
    }

    #endregion
}
