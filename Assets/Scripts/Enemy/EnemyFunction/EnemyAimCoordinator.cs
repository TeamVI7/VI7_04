using UnityEngine;

// Single point of ownership for transform.rotation on an enemy. Collects every
// IEnemyAimController on this GameObject and, each frame, lets only the
// highest-priority one that currently wants control actually rotate the body.
// Runs in LateUpdate so every behaviour's own Update() (which decides what it
// wants this frame) has already run.
[RequireComponent(typeof(EnemyBrain))]
public class EnemyAimCoordinator : MonoBehaviour
{
    private IEnemyAimController[] _controllers;

    private void Awake()
    {
        _controllers = GetComponents<IEnemyAimController>();
        System.Array.Sort(_controllers, (a, b) => b.AimPriority.CompareTo(a.AimPriority));
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < _controllers.Length; i++)
        {
            if (_controllers[i].WantsAim)
            {
                _controllers[i].TickAim(dt);
                return;
            }
        }
    }
}
