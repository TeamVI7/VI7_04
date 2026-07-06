using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// Cycles between visible and cloaked. Handles ALL materials on ALL renderers —
/// each slot gets its own runtime stealth instance so multi-mat meshes work correctly.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class StealthBehaviour : MonoBehaviour
{
    [Header("Timing")]
    public float VisibleDuration  = 3f;
    public float CloakedDuration  = 20f;

    [Header("Render")]
    [Tooltip("Drag every Renderer on the enemy here — including child meshes.")]
    public Renderer[] Renderers;
    [Tooltip("One stealth material. A unique runtime instance is made per material slot.")]
    public Material   StealthMaterial;
    public float      FadeSpeed    = 2f;

    [Header("Bleed on Exit")]
    public float BleedDuration      = 5f;
    public float BleedDamagePerTick = 10f;
    public float BleedInterval      = 1f;

    private enum Phase { Visible, Cloaked }

    // Per-renderer, per-slot storage so every material slot is covered
    private Material[][] _originalMats;   // [rendererIndex][slotIndex]
    private Material[][] _stealthMats;    // [rendererIndex][slotIndex]

    private Phase      _phase        = Phase.Visible;
    private float      _phaseTimer;
    private float      _currentAlpha = 1f;
    private float      _targetAlpha  = 1f;
    private Coroutine  _bleedRoutine;
    private EnemyHealth _health;

    private void Awake()
    {
        _health     = GetComponent<EnemyHealth>();
        _phaseTimer = VisibleDuration + Random.Range(0f, CloakedDuration);

        // Ensure a trigger collider exists for the bleed effect to fire
        if (GetComponents<Collider>().All(c => !c.isTrigger))
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 1.2f;
        }
    }

    private void Start()
    {
        if (Renderers == null || Renderers.Length == 0) return;

        _originalMats = new Material[Renderers.Length][];
        _stealthMats  = new Material[Renderers.Length][];

        for (int r = 0; r < Renderers.Length; r++)
        {
            if (Renderers[r] == null) continue;

            // sharedMaterials gives every slot on this renderer
            Material[] slots = Renderers[r].sharedMaterials;
            _originalMats[r] = slots;                           // keep originals
            _stealthMats[r]  = new Material[slots.Length];

            for (int s = 0; s < slots.Length; s++)
            {
                // One runtime stealth instance per slot — alpha can differ per slot if needed
                _stealthMats[r][s] = StealthMaterial != null
                    ? new Material(StealthMaterial)
                    : new Material(slots[s]);                   // fallback: clone the original
            }
        }
    }

    private void Update()
    {
        if (!_health.IsAlive) return;

        _phaseTimer -= Time.deltaTime;
        if (_phaseTimer <= 0f) TogglePhase();

        if (_phase == Phase.Cloaked) UpdateFade();
    }

    private void TogglePhase()
    {
        if (_phase == Phase.Visible)
        {
            _phase        = Phase.Cloaked;
            _phaseTimer   = CloakedDuration;
            _currentAlpha = 1f;
            _targetAlpha  = 0.05f;
            ApplyMats(_stealthMats, false);
        }
        else
        {
            _phase      = Phase.Visible;
            _phaseTimer = VisibleDuration;
            ApplyMats(_originalMats, true);
        }
    }

    // Assign a full material array to each renderer in one shot
    private void ApplyMats(Material[][] source, bool useShared)
    {
        for (int r = 0; r < Renderers.Length; r++)
        {
            if (Renderers[r] == null || source[r] == null) continue;

            if (useShared)
                Renderers[r].sharedMaterials = source[r];
            else
                Renderers[r].materials = source[r];
        }
    }

    private void UpdateFade()
    {
        _currentAlpha = Mathf.Lerp(_currentAlpha, _targetAlpha, Time.deltaTime * FadeSpeed);

        for (int r = 0; r < Renderers.Length; r++)
        {
            if (_stealthMats[r] == null) continue;
            for (int s = 0; s < _stealthMats[r].Length; s++)
            {
                var mat = _stealthMats[r][s];
                if (mat == null) continue;

                Color c = mat.color;
                c.a = _currentAlpha;
                mat.color = c;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (!other.TryGetComponent(out PlayerHealth ph)) return;

        if (_bleedRoutine != null) StopCoroutine(_bleedRoutine);
        _bleedRoutine = StartCoroutine(BleedRoutine(ph));
    }

    private IEnumerator BleedRoutine(PlayerHealth target)
    {
        float elapsed = 0f;
        while (elapsed < BleedDuration)
        {
            yield return new WaitForSeconds(BleedInterval);
            elapsed += BleedInterval;
            if (target == null || target.HP <= 0f) break;
            target.TakeDamage(BleedDamagePerTick);
        }
        _bleedRoutine = null;
    }

    private void OnDestroy()
    {
        if (_stealthMats == null) return;
        foreach (var row in _stealthMats)
        {
            if (row == null) continue;
            foreach (var mat in row)
            {
                if (mat != null) Destroy(mat);
            }
        }
    }
}