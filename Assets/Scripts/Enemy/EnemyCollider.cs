using UnityEngine;

/// <summary>
/// Auto-configures colliders for the enemy based on Stats SO.
/// Run this once in editor via the context menu button, or let it run in Awake.
/// No need to manually add/size colliders in the Inspector.
/// </summary>
[RequireComponent(typeof(EnemySetup))]
[DefaultExecutionOrder(-99)] // after EnemySetup(-100), before everything else
public class EnemyColliderSetup : MonoBehaviour
{
    public enum EnemyShape { Humanoid, Drone, Spider }

    [Header("Shape")]
    public EnemyShape Shape = EnemyShape.Humanoid;

    [Header("Main Collider — Humanoid")]
    public float CapsuleHeight = 1.8f;
    public float CapsuleRadius = 0.35f;
    public float CapsuleCenterY = 0.9f;

    [Header("Main Collider — Drone / Spider")]
    public float SphereRadius = 0.5f;

    [Header("Melee Trigger")]
    [Tooltip("Only created if MeleeBehaviour is present.")]
    public float MeleeTriggerRadius = 1.5f;

    [Header("Layer")]
    [Tooltip("The enemy's layer. Pick your 'Enemy' layer here — shown as a name, not a raw index, so it's obvious if this is still on Default.")]
    public LayerMask EnemyLayerMask = 0;

    private void Awake() => Configure();

    // ── Editor helper ────────────────────────────────────────────────────────
#if UNITY_EDITOR
    [ContextMenu("Configure Colliders Now")]
    private void ConfigureInEditor()
    {
        Configure();
        UnityEditor.EditorUtility.SetDirty(gameObject);
    }
#endif

    // ── Runtime ──────────────────────────────────────────────────────────────
    private void Configure()
    {
        int resolvedLayer = LayerMaskToLayer(EnemyLayerMask);

        if (resolvedLayer == 0)
            Debug.LogWarning($"[EnemyColliderSetup] {gameObject.name}: EnemyLayerMask resolves to 'Default' — " +
                              "raycasts filtered by layer (e.g. weapon aimColliderLayerMask) may miss this enemy. " +
                              "Set EnemyLayerMask to your actual Enemy layer in the Inspector.", this);

        gameObject.layer = resolvedLayer;
        gameObject.tag   = "Enemy";

        EnsureMainCollider();
        EnsureMeleeTrigger();
    }

    private void EnsureMainCollider()
    {
        switch (Shape)
        {
            case EnemyShape.Humanoid:
                var cap = GetOrAdd<CapsuleCollider>();
                cap.height   = CapsuleHeight;
                cap.radius   = CapsuleRadius;
                cap.center   = new Vector3(0f, CapsuleCenterY, 0f);
                cap.isTrigger = false;
                break;

            case EnemyShape.Drone:
            case EnemyShape.Spider:
                // Remove capsule if one exists from a previous config
                var existingCap = GetComponent<CapsuleCollider>();
                if (existingCap != null) DestroyImmediate(existingCap);

                var sph = GetOrAdd<SphereCollider>();
                sph.radius    = SphereRadius;
                sph.center    = Vector3.up * SphereRadius;
                sph.isTrigger = false;
                break;
        }
    }

    /// <summary>
    /// MeleeAttackBehaviour (EnemyMelee.cs) checks range via Vector3.Distance in Update() —
    /// it needs no trigger collider, so this method is now a no-op left here only as a
    /// placeholder in case a future trigger-based ability needs the same slot.
    /// </summary>
    private void EnsureMeleeTrigger()
    {
        // Intentionally empty — MeleeAttackBehaviour does not use a trigger collider.
        // If you bring back a tick-damage melee variant later, rebuild this method
        // around that component instead.
    }

    private T GetOrAdd<T>() where T : Component
    {
        return TryGetComponent(out T existing) ? existing : gameObject.AddComponent<T>();
    }

    /// <summary>
    /// Converts a single-layer LayerMask (as picked in the Inspector dropdown) into the
    /// int layer index gameObject.layer expects. Returns 0 (Default) if EnemyLayerMask
    /// was left unset or has multiple layers checked.
    /// </summary>
    private static int LayerMaskToLayer(LayerMask mask)
    {
        int value = mask.value;
        if (value == 0) return 0;
        int layer = 0;
        while ((value & 1) == 0) { value >>= 1; layer++; }
        return layer;
    }
}