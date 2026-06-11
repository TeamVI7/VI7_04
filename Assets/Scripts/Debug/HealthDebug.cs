using UnityEngine;

public class HealthDebug : MonoBehaviour
{
    [SerializeField] PlayerHealth playerHealth;
    [SerializeField] float debugDamage = 20f;
    [SerializeField] KeyCode damageKey = KeyCode.H;

    void Update()
    {
        if (Input.GetKeyDown(damageKey))
            playerHealth.TakeDamage(debugDamage);
    }
}