using UnityEngine;

// Bridges a single MechAttackBehaviour's wind-up events to a TelegraphVisual
// preset. Explicit "attack" reference rather than GetComponent<MechAttackBehaviour>()
// on purpose — Stomp/Dash/Gatling all live as sibling components on the same
// boss GameObject, so GetComponent would be ambiguous about which one this
// adapter is for. Add one of these per attack that needs a visible telegraph.
public class MechAttackTelegraphAdapter : MonoBehaviour
{
    #region Inspector

    [SerializeField] private MechAttackBehaviour attack;
    [SerializeField] private TelegraphVisual visual;
    [Tooltip("Optional — origin used for laser/decal placement (e.g. muzzle, ground point). Defaults to this transform.")]
    [SerializeField] private Transform originOverride;
    [SerializeField] private bool debugLog;

    #endregion

    #region Unity

    private void OnEnable()
    {
        if (attack == null)
        {
            Log("No MechAttackBehaviour assigned, adapter is inert.");
            return;
        }

        attack.OnTelegraphStart     += HandleStart;
        attack.OnTelegraphResolved  += HandleEnd;
        attack.OnTelegraphCancelled += HandleEnd;
    }

    private void OnDisable()
    {
        if (attack == null) return;

        attack.OnTelegraphStart     -= HandleStart;
        attack.OnTelegraphResolved  -= HandleEnd;
        attack.OnTelegraphCancelled -= HandleEnd;
        HandleEnd();
    }

    #endregion

    #region Event Handlers

    private void HandleStart()
    {
        if (visual == null) return;

        Transform origin = originOverride != null ? originOverride : transform;

        if (visual is ITelegraphAimable aimable && PlayerHealth.Transform != null)
            aimable.SetEndpoints(origin, PlayerHealth.Transform);

        if (visual is ITelegraphPlaceable placeable)
            placeable.SetTransform(origin.position, origin.rotation);

        visual.Play(attack.TelegraphDuration);
        Log("Telegraph adapter started visual.");
    }

    private void HandleEnd()
    {
        if (visual != null)
            visual.Stop();
    }

    #endregion

    #region Debug

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog)
            Debug.Log($"[{nameof(MechAttackTelegraphAdapter)}] {name}: {msg}", this);
    }

    #endregion
}
