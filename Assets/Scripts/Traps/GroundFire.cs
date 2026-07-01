using UnityEngine;

// Attach to the trigger collider over a ground fire hazard (fire pool, burning debris, etc).
// Same DoT pattern as FlameThrowerDamage, plus optional "ignite" so the player
// keeps burning for a few seconds after stepping out.
public class GroundFireDamage : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] float dps = 12f;
    [SerializeField] float tickRate = 0.25f;

    [Header("Ignite (burn after leaving fire)")]
    [SerializeField] bool igniteOnContact = true;
    [SerializeField] float igniteDuration = 2f;
    [SerializeField] float igniteDps = 6f;

    [Header("Optional FX")]
    [SerializeField] ParticleSystem fireFX;
    [SerializeField] bool active = true;

    float _tickTimer;
    float _igniteTimer;
    bool _playerInside;

    void Awake()
    {
        if (fireFX && active) fireFX.Play();
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.transform != PlayerHealth.Transform) return;
        _playerInside = true;
        _tickTimer = 0f;

        if (igniteOnContact) _igniteTimer = igniteDuration;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.transform != PlayerHealth.Transform) return;
        _playerInside = false;
    }

    void Update()
    {
        if (!active) return;
        if (PlayerHealth.Instance == null) return;

        _tickTimer -= Time.deltaTime;
        if (_tickTimer > 0f) return;
        _tickTimer = tickRate;

        if (_playerInside)
        {
            PlayerHealth.Instance.TakeDamage(dps * tickRate);
            if (igniteOnContact) _igniteTimer = igniteDuration; // refresh while standing in it
        }
        else if (_igniteTimer > 0f)
        {
            PlayerHealth.Instance.TakeDamage(igniteDps * tickRate);
            _igniteTimer -= tickRate;
        }
    }

    public void SetActive(bool state)
    {
        active = state;
        if (fireFX)
        {
            if (state) fireFX.Play();
            else fireFX.Stop();
        }
    }
}