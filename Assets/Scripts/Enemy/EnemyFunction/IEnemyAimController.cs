using UnityEngine;

// Implemented by any behaviour that rotates the enemy's root transform during
// patrol/combat (facing movement, aiming to shoot, facing during a melee swing...).
// EnemyAimCoordinator polls these once per frame and lets only the highest-priority
// one that currently wants control actually turn the body. Without this, Patrol,
// ranged attacks, Sniper and Melee all fight over transform.rotation.
public interface IEnemyAimController
{
    // Higher wins. Committed actions (melee swing) should outrank active aiming
    // (ranged/sniper), which should outrank patrol/chase facing.
    int AimPriority { get; }

    // True this frame if this controller currently needs to drive rotation.
    bool WantsAim { get; }

    // Perform the transform.rotation write for this frame.
    void TickAim(float deltaTime);
}
