using UnityEngine;

namespace Enemy
{
    public class AcidProjectile : MonoBehaviour
    {
        [Header("Stats")]
        public float Damage          = 10f;
        public float Speed           = 12f;
        public float LifeTime        = 3f;

        [Header("Homing")]
        public float HomingStrength  = 5f;
        public float DelayHomingTime = 0.4f;

        [Header("VFX")]
        public GameObject HitEffectPrefab;

        [Header("Collision")]
        public LayerMask HitLayers   = ~0;

        private Rigidbody _rb;
        private float     _spawnTime;
        private bool      _hit;

        public void Init(Vector3 direction)
        {
            _rb        = GetComponent<Rigidbody>();
            _spawnTime = Time.time;

            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.useGravity  = true;
                _rb.AddForce(direction.normalized * Speed, ForceMode.VelocityChange);
            }

            Destroy(gameObject, LifeTime);
        }

        private void FixedUpdate()
        {
            if (_hit || _rb == null || PlayerHealth.Transform == null) return;

            if (Time.time - _spawnTime > DelayHomingTime)
            {
                _rb.useGravity = false;
                Vector3 desired = (PlayerHealth.Transform.position - transform.position).normalized * Speed;
                _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, desired,
                                                   HomingStrength * Time.fixedDeltaTime);
            }

            if (_rb.linearVelocity != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(_rb.linearVelocity);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hit) return;
            if (other.CompareTag("Enemy")) return;
            if ((HitLayers.value & (1 << other.gameObject.layer)) == 0) return;

            _hit = true;

            other.GetComponentInParent<PlayerHealth>()?.TakeDamage(Damage);

            if (HitEffectPrefab != null)
            {
                var fx = Instantiate(HitEffectPrefab, transform.position, Quaternion.identity);
                Destroy(fx, 2f);
            }

            Destroy(gameObject);
        }
    }
}