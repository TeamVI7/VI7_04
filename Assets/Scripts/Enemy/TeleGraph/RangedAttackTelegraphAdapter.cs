using UnityEngine;

// Bridges EnemyRangedAttackBehaviour's built-in wind-up events to any
// TelegraphVisual preset (laser, decal, emission flash). Swap presets in
// the inspector per weapon — no code changes needed.
[RequireComponent(typeof(EnemyRangedAttackBehaviour))]
public class RangedAttackTelegraphAdapter : MonoBehaviour
{
    #region Inspector

    [SerializeField] private TelegraphVisual visual;

    #endregion

    #region State

    private EnemyRangedAttackBehaviour _owner;

    #endregion

    #region Unity

    private void Awake()
    {
        _owner = GetComponent<EnemyRangedAttackBehaviour>();
    }

    private void OnEnable()
    {
        _owner.OnTelegraphStart     += HandleStart;
        _owner.OnFired              += HandleEnd;
        _owner.OnTelegraphCancelled += HandleEnd;
    }

    private void OnDisable()
    {
        _owner.OnTelegraphStart     -= HandleStart;
        _owner.OnFired              -= HandleEnd;
        _owner.OnTelegraphCancelled -= HandleEnd;
        HandleEnd();
    }

    #endregion

    #region Event Handlers

    private void HandleStart()
    {
        if (visual == null) return;

        if (visual is ITelegraphAimable aimable && _owner.FirePoint != null && PlayerHealth.Transform != null)
            aimable.SetEndpoints(_owner.FirePoint, PlayerHealth.Transform);

        if (visual is ITelegraphPlaceable placeable && PlayerHealth.Transform != null)
            placeable.SetTransform(PlayerHealth.Transform.position, Quaternion.identity);

        visual.Play(_owner.TelegraphTime);
    }

    private void HandleEnd()
    {
        if (visual != null)
            visual.Stop();
    }

    #endregion
}
