using UnityEngine;
using System.Collections;

public class EnemyStealth : EnemyFodder
{
    public enum StealthState { Visible, Cloaked }

    [Header("Stealth Loop Settings")]
    public float VisibleDuration = 3f;
    public float CloakedDuration = 20f;

    [Header("Render & Material Settings")]
    public Renderer[] EnemyRenderers;
    public Material   StealthMaterial;
    public float      FadeSpeed = 2f;

    [Header("Melee Damage")]
    public float DamageRange      = 2f;
    public float DamagePerSecond  = 15f;

    [Header("Bleeding Settings")]
    public float BleedDuration       = 5f;
    public float BleedDamagePerTick  = 10f;
    public float BleedInterval       = 1f;

    private Material[]   _originalMaterials;
    private Material[]   _runtimeStealthMats;
    private float        _targetAlpha        = 1f;
    private float        _currentAlpha       = 1f;
    private StealthState _currentState       = StealthState.Visible;
    private float        _stateTimer         = 0f;
    private bool         _playerWasInZone    = false;
    private Coroutine    _activeBleedCoroutine;

    protected override void Awake()
    {
        base.Awake();
        _currentState = StealthState.Visible;
        // FIX: Random offset so multiple stealth enemies don't sync-cloak
        _stateTimer = VisibleDuration + Random.Range(0f, CloakedDuration);
    }

    void Start()
    {
        if (EnemyRenderers != null && EnemyRenderers.Length > 0)
        {
            _originalMaterials  = new Material[EnemyRenderers.Length];
            _runtimeStealthMats = new Material[EnemyRenderers.Length];

            for (int i = 0; i < EnemyRenderers.Length; i++)
            {
                if (EnemyRenderers[i] != null)
                {
                    _originalMaterials[i] = EnemyRenderers[i].sharedMaterial;
                    if (StealthMaterial != null)
                        _runtimeStealthMats[i] = new Material(StealthMaterial);
                }
            }
        }
    }

    protected override void Update()
    {
        base.Update();

        if (_currentState == StealthState.Cloaked && _runtimeStealthMats != null && EnemyRenderers != null)
        {
            _currentAlpha = Mathf.Lerp(_currentAlpha, _targetAlpha, Time.deltaTime * FadeSpeed);

            for (int i = 0; i < EnemyRenderers.Length; i++)
            {
                if (EnemyRenderers[i] != null && _runtimeStealthMats[i] != null)
                {
                    Color c = _runtimeStealthMats[i].color;
                    c.a = _currentAlpha;
                    _runtimeStealthMats[i].color = c;

                    if (_runtimeStealthMats[i].HasProperty("_BaseColor"))
                        _runtimeStealthMats[i].SetColor("_BaseColor", c);
                }
            }
        }

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f) SwitchStealthState();

        HandleProximityDamage();
    }

    protected override void TryFireProjectile(float dist) { }

    private void SwitchStealthState()
    {
        if (EnemyRenderers == null || EnemyRenderers.Length == 0) return;

        if (_currentState == StealthState.Visible)
        {
            _currentState = StealthState.Cloaked;
            _stateTimer   = CloakedDuration;
            _currentAlpha = 1f;
            _targetAlpha  = 0.05f;

            for (int i = 0; i < EnemyRenderers.Length; i++)
                if (EnemyRenderers[i] != null && _runtimeStealthMats[i] != null)
                    EnemyRenderers[i].material = _runtimeStealthMats[i];
        }
        else
        {
            _currentState = StealthState.Visible;
            _stateTimer   = VisibleDuration;

            for (int i = 0; i < EnemyRenderers.Length; i++)
                if (EnemyRenderers[i] != null && _originalMaterials[i] != null)
                    EnemyRenderers[i].material = _originalMaterials[i];
        }
    }

    private void HandleProximityDamage()
    {
        if (_player == null) return;

        float distance = Vector3.Distance(transform.position, _player.position);

        if (distance <= DamageRange)
        {
            if (_player.TryGetComponent(out PlayerHealth ph))
                ph.TakeDamage(DamagePerSecond * Time.deltaTime);

            if (_activeBleedCoroutine != null)
            {
                StopCoroutine(_activeBleedCoroutine);
                _activeBleedCoroutine = null;
            }
            _playerWasInZone = true;
        }
        else
        {
            if (_playerWasInZone)
            {
                _playerWasInZone = false;
                if (_player.TryGetComponent(out PlayerHealth ph))
                {
                    if (_activeBleedCoroutine != null) StopCoroutine(_activeBleedCoroutine);
                    _activeBleedCoroutine = StartCoroutine(PlayerBleedingRoutine(ph));
                }
            }
        }
    }

    private IEnumerator PlayerBleedingRoutine(PlayerHealth targetHealth)
    {
        float elapsed = 0f;
        while (elapsed < BleedDuration)
        {
            yield return new WaitForSeconds(BleedInterval);
            elapsed += BleedInterval;

            if (targetHealth != null && targetHealth.HP > 0)
                targetHealth.TakeDamage(BleedDamagePerTick);
            else
                break;
        }
        _activeBleedCoroutine = null;
    }
}