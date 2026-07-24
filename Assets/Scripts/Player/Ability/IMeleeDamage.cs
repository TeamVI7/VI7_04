// IMeleeDamageable.cs
using UnityEngine;

public interface IMeleeDamageable
{
    bool TakeMeleeDamage(float amount, Vector3 direction, Vector3 hitPoint);
}