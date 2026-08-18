using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Lives on a decal prefab (must have a <see cref="DecalProjector"/> on this object or a child).
/// Projects into whatever surface it's told to, holds for <see cref="lifetime"/>, fades out,
/// then returns itself to <see cref="ImpactDecalPool"/>.
/// </summary>
[RequireComponent(typeof(DecalProjector))]
public class ImpactDecal : MonoBehaviour
{
    [Header("Projector")]
    public DecalProjector projector;

    [Header("Timing")]
    public float lifetime       = 10f;
    public float fadeOutDuration = 1.5f;

    [Header("Random Variance")]
    [Tooltip("Random width/height applied evenly to projector.size.x and .y.")]
    public Vector2 randomSize = new Vector2(0.25f, 0.45f);

    private Coroutine _lifeRoutine;
    private GameObject _sourcePrefab;

    private void Awake()
    {
        if (projector == null) projector = GetComponent<DecalProjector>();
    }

    /// <summary>Called by <see cref="ImpactDecalPool"/> right after instantiation so Return() knows its own key.</summary>
    public void SetSourcePrefab(GameObject prefab) => _sourcePrefab = prefab;

    /// <summary>Projects the decal at <paramref name="point"/>, facing into <paramref name="normal"/>.
    /// Parented to <paramref name="attachTo"/> (if given) so it tracks moving/animated surfaces —
    /// e.g. a ragdolling enemy or a moving platform — instead of staying frozen in world space.</summary>
    public void Play(Vector3 point, Vector3 normal, Transform attachTo)
    {
        transform.SetParent(attachTo, true);

        Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        Quaternion rot = Quaternion.LookRotation(-normal, up)
                       * Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
        transform.SetPositionAndRotation(point, rot);

        float size = Random.Range(randomSize.x, randomSize.y);
        Vector3 s = projector.size;
        projector.size = new Vector3(size, size, s.z);
        projector.fadeFactor = 1f;

        gameObject.SetActive(true);

        if (_lifeRoutine != null) StopCoroutine(_lifeRoutine);
        _lifeRoutine = StartCoroutine(Co_Lifetime());
    }

    private IEnumerator Co_Lifetime()
    {
        yield return new WaitForSeconds(lifetime);

        float t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.deltaTime;
            projector.fadeFactor = 1f - Mathf.Clamp01(t / fadeOutDuration);
            yield return null;
        }

        _lifeRoutine = null;
        ImpactDecalPool.Instance.Return(_sourcePrefab, this);
    }

    /// <summary>Called by the pool right before this instance goes back on the shelf.</summary>
    public void ResetForPool()
    {
        if (_lifeRoutine != null) { StopCoroutine(_lifeRoutine); _lifeRoutine = null; }
        projector.fadeFactor = 1f;
        gameObject.SetActive(false);
    }
}
