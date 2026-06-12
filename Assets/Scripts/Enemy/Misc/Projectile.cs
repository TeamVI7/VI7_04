using UnityEngine;

namespace Enemy
{
    public class Projectile : MonoBehaviour
    {
        [Header("Stats")]
        public float Damage   = 10f;
        public float Speed    = 12f;
        public float LifeTime = 3f;

        [Header("VFX")]
        public GameObject HitEffectPrefab;

        [Header("Homing Settings")]
        public float HomingStrength = 5f;

        public float DelayHomingTime = 0.4f;

        [Header("Collision")]
        public LayerMask HitLayers = ~0;

        private Transform _playerTarget;
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

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) _playerTarget = player.transform;

            Destroy(gameObject, LifeTime);
        }

        private void FixedUpdate()
        {
            if (_hit || _playerTarget == null || _rb == null) return;

            if (Time.time - _spawnTime > DelayHomingTime)
            {
                _rb.useGravity = false;
                Vector3 targetDirection  = (_playerTarget.position - transform.position).normalized;
                Vector3 desiredVelocity  = targetDirection * Speed;
                _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, desiredVelocity,
                                                   HomingStrength * Time.fixedDeltaTime);
            }

            if (_rb.linearVelocity != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(_rb.linearVelocity);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hit) return;

            // FIX: Use tag + layer mask instead of fragile name.Contains("Enemy") string check
            if (other.CompareTag("Enemy")) return;

            // Check the collider's layer is in our allowed hit layers
            if ((HitLayers.value & (1 << other.gameObject.layer)) == 0) return;

            _hit = true;

            var health = other.GetComponentInParent<PlayerHealth>();
            if (health != null)
                health.TakeDamage(Damage);

            SpawnEffect(HitEffectPrefab);
            Destroy(gameObject);
        }

        private void SpawnEffect(GameObject prefab)
        {
            if (prefab == null) return;
            var fx = Instantiate(prefab, transform.position, Quaternion.identity);
            Destroy(fx, 2f);
        }
    }
}