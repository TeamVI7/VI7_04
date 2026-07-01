using UnityEngine;

// Attach to the trigger collider (e.g. TriggerZone child of FlameThrower).
// Deals DoT to PlayerHealth.Instance while player stands in the flame volume.
public class FlameThrowerDamage : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] float dps = 20f;
    [SerializeField] float tickRate = 0.25f;

    [Header("Optional Emission Toggle")]
    [SerializeField] ParticleSystem flameFX;
    [SerializeField] bool active = true;

    float _tickTimer;
    bool _playerInside;

    void OnTriggerEnter(Collider other)
    {
        if (other.transform != PlayerHealth.Transform) return;
        _playerInside = true;
        _tickTimer = 0f;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.transform != PlayerHealth.Transform) return;
        _playerInside = false;
    }

    void Update()
    {
        if (!active || !_playerInside) return;
        if (PlayerHealth.Instance == null) return;

        _tickTimer -= Time.deltaTime;
        if (_tickTimer > 0f) return;

        _tickTimer = tickRate;
        PlayerHealth.Instance.TakeDamage(dps * tickRate);
    }

    public void SetActive(bool state)
    {
        active = state;
        if (flameFX)
        {
            if (state) flameFX.Play();
            else flameFX.Stop();
        }
    }
}