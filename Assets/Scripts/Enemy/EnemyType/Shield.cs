using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Armor that absorbs damage first. Shield mesh dissolves on break.
/// Break stagger effect is a material flash on the enemy body, not a prefab.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(EnemyBrain))]
public class ShieldBehaviour : MonoBehaviour, IDamageable
{
    [Header("Armor")]
    public float MaxArmor       = 100f;
    public float ActivateRange  = 10f;

    [Header("Shield Mesh")]
    [Tooltip("The shield GameObject with a Renderer on it.")]
    public GameObject ShieldObject;
    public Color      ColorFull = new Color(0.2f, 0.6f, 1f, 0.4f);
    public Color      ColorLow  = new Color(1f, 0.2f, 0.2f, 0.6f);

    [Header("Shield FX")]
    [Range(0f, 1f)]
    public float LowArmorThreshold = 0.3f;
    public float DissolveDuration  = 1.5f;
    [Tooltip("Material applied directly to the shield mesh when it breaks.")]
    public Material BrokenShieldMaterial;

    [Header("Break — Body Flash")]
    [Tooltip("Temporary material swapped onto the enemy body when shield breaks.")]
    public Material BreakFlashMaterial;
    public float    BreakFlashDuration = 0.3f;

    [Header("Break Recovery")]
    public float StunDuration = 1.2f;

    [Header("Aggression Boost")]
    public float SpeedMultiplier    = 1.15f;
    public float FireRateMultiplier = 1.2f;

    public float CurrentArmor  { get; private set; }
    public bool  HasShield     => CurrentArmor > 0f;
    public float ArmorFraction => CurrentArmor / MaxArmor;

    public System.Action<float> OnArmorChanged;

    private EnemyHealth           _health;
    private EnemyBrain            _brain;
    private PatrolBehaviour       _patrol;
    private GrenadeBurstBehaviour _burst;

    // Shield mesh runtime data
    private Renderer _shieldRenderer;
    private Material _shieldMat;
    private bool     _shieldVisible;
    private bool     _isDissolving;
    private bool     _recovering;
    private bool     _boostApplied;

    // Body renderers — for break flash
    private Renderer[] _bodyRenderers;
    private Material[][] _bodyOriginalMats;

    private Coroutine _blinkRoutine;
    private Coroutine _dissolveRoutine;
    private Coroutine _flashRoutine;
    private Coroutine _recoveryRoutine;

    private static readonly int _baseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int _colorID     = Shader.PropertyToID("_Color");
    private static readonly int _dissolveID  = Shader.PropertyToID("_DissolveAmount");

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _brain  = GetComponent<EnemyBrain>();
        _patrol = GetComponent<PatrolBehaviour>();
        _burst  = GetComponent<GrenadeBurstBehaviour>();

        CurrentArmor = MaxArmor;

        if (ShieldObject != null)
        {
            _shieldRenderer = ShieldObject.GetComponentInChildren<Renderer>();
            if (_shieldRenderer != null) 
            {
                _shieldMat = _shieldRenderer.material; // Create runtime instance
            }
            ShieldObject.SetActive(false);
        }

        var allRenderers = GetComponentsInChildren<Renderer>();
        var bodyList = new System.Collections.Generic.List<Renderer>();
        foreach (var r in allRenderers)
        {
            if (ShieldObject != null && r.transform.IsChildOf(ShieldObject.transform)) continue;
            bodyList.Add(r);
        }
        _bodyRenderers    = bodyList.ToArray();
        _bodyOriginalMats = new Material[_bodyRenderers.Length][];
        for (int i = 0; i < _bodyRenderers.Length; i++)
            _bodyOriginalMats[i] = _bodyRenderers[i].sharedMaterials;
    }

    private void Update()
    {
        if (!_health.IsAlive) { HideShield(); return; }
        TickVisibility();
    }

    public void TakeDamage(float amount, Vector3 hitDir, Vector3 hitPoint)
    {
        if (!_health.IsAlive) return;

        if (HasShield)
        {
            float before = CurrentArmor;
            CurrentArmor = Mathf.Max(0f, CurrentArmor - amount);
            OnArmorChanged?.Invoke(CurrentArmor);
            
            FlashShield();

            if (ArmorFraction <= LowArmorThreshold && _blinkRoutine == null)
                _blinkRoutine = StartCoroutine(Blink());

            if (CurrentArmor <= 0f)
            {
                HandleShieldBreak();

                float overflow = amount - before;
                if (overflow > 0f) _health.TakeDamage(overflow, hitDir, hitPoint);
            }
            return;
        }

        _health.TakeDamage(amount, hitDir, hitPoint);
    }

    private void HandleShieldBreak()
    {
        // Swap the shield material to the broken variant before dissolving
        if (BrokenShieldMaterial != null && _shieldRenderer != null)
        {
            _shieldRenderer.material = BrokenShieldMaterial;
            _shieldMat = _shieldRenderer.material; // Update reference for the dissolve shader properties
        }

        if (_dissolveRoutine != null) StopCoroutine(_dissolveRoutine);
        _dissolveRoutine = StartCoroutine(Dissolve());

        if (_recoveryRoutine != null) StopCoroutine(_recoveryRoutine);
        _recoveryRoutine = StartCoroutine(BreakRecovery());

        ApplyBoost();
    }

    private void TickVisibility()
    {
        if (ShieldObject == null || PlayerHealth.Transform == null) return;

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        bool  show = dist <= ActivateRange && HasShield;

        if (show  && !_shieldVisible) ShowShield();
        else if (!show && _shieldVisible && !_isDissolving) HideShield();

        if (_shieldVisible) UpdateShieldColor();
    }

    private void ShowShield()
    {
        _shieldVisible = true;
        ShieldObject.SetActive(true);
    }

    private void HideShield()
    {
        if (!_shieldVisible) return;
        _shieldVisible = false;
        if (ShieldObject != null) ShieldObject.SetActive(false);
    }

    private void UpdateShieldColor()
    {
        if (_shieldMat == null || CurrentArmor <= 0f) return; // Prevent color override if broken
        Color c = Color.Lerp(ColorLow, ColorFull, ArmorFraction);
        c.a = Mathf.Lerp(0.6f, 0.3f, ArmorFraction);
        _shieldMat.SetColor(_baseColorID, c);
        _shieldMat.SetColor(_colorID, c);
        _shieldMat.color = c;
    }

    private void FlashShield()
    {
        if (_shieldMat == null || CurrentArmor <= 0f) return;
        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(ShieldFlashRoutine());
    }

    private IEnumerator ShieldFlashRoutine()
    {
        _shieldMat.SetColor(_baseColorID, Color.white);
        yield return new WaitForSeconds(0.08f);
        UpdateShieldColor();
        _flashRoutine = null;
    }

    private IEnumerator Blink()
    {
        while (HasShield && ArmorFraction <= LowArmorThreshold)
        {
            _shieldMat?.SetColor(_baseColorID, Color.red);
            yield return new WaitForSeconds(0.08f);
            UpdateShieldColor();
            yield return new WaitForSeconds(0.08f);
        }
        _blinkRoutine = null;
    }

    private IEnumerator Dissolve()
    {
        if (_isDissolving) yield break;
        _isDissolving = true;
        
        for (float t = 0f; t < DissolveDuration; t += Time.deltaTime)
        {
            _shieldMat?.SetFloat(_dissolveID, t / DissolveDuration);
            yield return null;
        }
        
        HideShield();
        _isDissolving    = false;
        _dissolveRoutine = null;
    }

    private IEnumerator BodyBreakFlash()
    {
        if (BreakFlashMaterial == null) yield break;

        for (int i = 0; i < _bodyRenderers.Length; i++)
        {
            if (_bodyRenderers[i] == null) continue;
            var flashMats = new Material[_bodyOriginalMats[i].Length];
            for (int s = 0; s < flashMats.Length; s++) flashMats[s] = BreakFlashMaterial;
            _bodyRenderers[i].sharedMaterials = flashMats;
        }

        yield return new WaitForSeconds(BreakFlashDuration);

        for (int i = 0; i < _bodyRenderers.Length; i++)
        {
            if (_bodyRenderers[i] != null)
                _bodyRenderers[i].sharedMaterials = _bodyOriginalMats[i]; 
        }
    }

    private IEnumerator BreakRecovery()
    {
        _recovering = true;
        
        _health.EnterStagger();
        StartCoroutine(BodyBreakFlash());

        var nav  = GetComponent<NavMeshAgent>();
        var rb   = GetComponent<Rigidbody>();
        var anim = GetComponent<Animator>();

        if (nav != null && nav.isOnNavMesh) { nav.ResetPath(); nav.isStopped = true; nav.enabled = false; }
        if (rb  != null) { rb.linearVelocity = rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
        if (anim != null) anim.enabled = false;

        yield return new WaitForSeconds(StunDuration);

        if (!_health.IsAlive) { _recovering = false; yield break; }

        if (rb   != null) rb.isKinematic = false;
        if (nav  != null) { nav.enabled = true; if (nav.isOnNavMesh) nav.isStopped = false; }
        if (anim != null) anim.enabled = true;

        _recovering      = false;
        _recoveryRoutine = null;
        _brain.SetState(EnemyState.Aggro);
    }

    private void ApplyBoost()
    {
        if (_boostApplied) return;
        _boostApplied = true;
        if (_patrol != null) _patrol.ChaseSpeed *= SpeedMultiplier;
        if (_burst  != null)
        {
            _burst.BurstCooldown /= FireRateMultiplier;
            _burst.BurstInterval /= FireRateMultiplier;
        }
    }

    private void OnDestroy()
    {
        if (_blinkRoutine     != null) StopCoroutine(_blinkRoutine);
        if (_dissolveRoutine  != null) StopCoroutine(_dissolveRoutine);
        if (_flashRoutine     != null) StopCoroutine(_flashRoutine);
        if (_recoveryRoutine  != null) StopCoroutine(_recoveryRoutine);
    }
}