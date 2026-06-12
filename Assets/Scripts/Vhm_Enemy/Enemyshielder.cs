// ============================================================
//  EnemyShielder.cs  —  OutOfBullet
//  Heavy Shield System - FIXED: SONG HÀNH CẬN CHIẾN & BẮN VÒNG CUNG
// ============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using OutOfBullet.Core;
using OutOfBullet.Player;

namespace OutOfBullet.Enemy
{
    public class EnemyShielder : EnemyFodder
    {
        // ── Inspector ────────────────────────────────────────────

        [Header("Shielder — Armor")]
        public float MaxArmor = 100f;
        public float ShieldActivateRange = 10f;

        [Header("Shield Visual")]
        public GameObject ShieldObject;
        public Material   ShieldMaterial;

        [Tooltip("Prefab dissolve effect khi khiên vỡ.")]
        public GameObject ShieldBreakEffectPrefab;

        [Tooltip("Effect điện/chập khi shield vỡ.")]
        public GameObject ShieldBreakStaggerEffect;

        public Color ShieldColorFull = new Color(0.2f, 0.6f, 1f, 0.4f);
        public Color ShieldColorLow  = new Color(1f,   0.2f, 0.2f, 0.6f);

        [Header("Shield FX")]
        [Range(0f, 1f)]
        public float LowArmorThreshold = 0.3f;
        public float BlinkSpeed        = 10f;
        public float DissolveDuration  = 1.5f;

        [Header("Shield Break Recovery")]
        [Tooltip("Heavy đứng khựng sau khi mất khiên.")]
        public float ShieldBreakStunDuration = 1.2f;

        [Tooltip("Heavy hung hãn hơn sau khi mất khiên.")]
        public float AggressionSpeedMultiplier    = 1.15f;
        public float AggressionFireRateMultiplier = 1.2f;

        // ── Heavy Combat — Grenade Launcher (Arc, luôn luôn) ──────
        [Header("Heavy — Grenade Burst (Arc)")]
        public GameObject GrenadePrefab;

        [Tooltip("Điểm bắn lựu.")]
        public Transform   GrenadeFirePoint;

        [Tooltip("Tầm bắn lựu tối đa.")]
        public float GrenadeRange = 35f;

        [Tooltip("Số đạn mỗi đợt bắn.")]
        public int   BurstCount = 4;

        [Tooltip("Thời gian giữa mỗi viên trong một đợt (giây).")]
        public float BurstInterval = 0.25f;

        [Tooltip("Thời gian nghỉ giữa các đợt bắn (giây).")]
        public float BurstCooldown = 2.5f;

        [Tooltip("Góc ngẩng lên (độ) để đạn bay vòng cung — giá trị dương = ngẩng lên.")]
        [Range(10f, 45f)]
        public float GrenadeArcAngle = 25f;

        [Tooltip("Tốc độ đạn lựu — thấp hơn acid để bay rõ vòng cung.")]
        public float GrenadeSpeed = 14f;

        [Header("Heavy — Melee Mode Config")]
        [Tooltip("Bật chế độ cận chiến húc người gây mất máu khi ép sát.")]
        public bool isMeleeMode = true;
        
        [Tooltip("Sát thương gây ra mỗi lần húc trúng Player.")]
        public float MeleeDamage = 10f;
        
        [Tooltip("Khoảng thời gian (giây) giữa các lần gây sát thương khi ép sát.")]
        public float MeleeAttackCooldown = 1.0f;

        // ── Runtime ──────────────────────────────────────────────
        public float CurrentArmor  { get; private set; }
        public bool   HasShield     => CurrentArmor > 0f;
        public float ArmorFraction => CurrentArmor / MaxArmor;

        public bool IsShieldBreakRecovering => _shieldBreakRecovering;

        public System.Action<float> OnArmorChanged;

        private bool      _shieldVisible;
        private bool      _isDissolving;
        private bool      _shieldBreakRecovering;
        private bool      _aggressionBoostApplied;
        private Coroutine _blinkRoutine;

        private float     _grenadeFireTimer; 
        private bool      _isBursting; 
        private float     _nextMeleeAttackTime;

        private static readonly int _baseColorID     = Shader.PropertyToID("_BaseColor");
        private static readonly int _colorID         = Shader.PropertyToID("_Color");
        private static readonly int _dissolveAmountID = Shader.PropertyToID("_DissolveAmount");

        // ── Unity ────────────────────────────────================
        protected override void Awake()
        {
            Tier  = EnemyTier.Heavy;
            MaxHP = 250f;
            base.Awake();

            CurrentArmor = MaxArmor;

            if (ShieldObject != null)
            {
                Renderer rend = ShieldObject.GetComponentInChildren<Renderer>();
                if (rend != null)
                    ShieldMaterial = rend.material;

                ShieldObject.SetActive(false);
            }
        }

        protected override void Update()
        {
            base.Update();
            TickShieldVisibility();
        }

        // ── Chặn Đứng Súng Ngắn Của Lớp Cha Fodder ────────────────
        private new void TryFireHorizontal(float dist)
        {
            // Khóa hoàn toàn, không xài súng lục thường của lính quèn
        }

        // ── Override TickIdle: Vừa Patrol, Vừa Detect, Vừa Bắn Vòng Cung ──
        protected override void TickIdle()
        {
            if (_nav == null || !_nav.enabled || !_nav.isOnNavMesh) return;

            if (TryRadarDetectPlayer(out float radarDist))
            {
                if (radarDist <= AggroRadius)
                {
                    _nav.ResetPath();
                    TransitionTo(EnemyState.Aggro);
                    return;
                }

                if (radarDist <= GrenadeRange)
                {
                    FacePlayer();
                    if (!_isBursting)
                    {
                        TryStartGrenadeBurst(radarDist);
                    }
                    _nav.speed = PatrolSpeed;
                }
            }

            base.TickIdle();
        }

        // ── Override TickAggro: Đuổi theo Player ──────────────────
        protected override void TickAggro()
        {
            if (_shieldBreakRecovering)
            {
                if (_nav != null)
                {
                    _nav.ResetPath();
                    _nav.velocity  = Vector3.zero;
                    _nav.isStopped = true;
                }
                return;
            }
            
            if (_nav != null && _nav.isStopped)
                _nav.isStopped = false;

            if (_player == null) return;

            float dist = Vector3.Distance(transform.position, _player.position);

            if (dist > RadarRange)
            {
                _nav.ResetPath();
                
                System.Reflection.FieldInfo hasTargetField = typeof(EnemyFodder).GetField("_hasPatrolTarget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (hasTargetField != null) hasTargetField.SetValue(this, false);

                TransitionTo(EnemyState.Idle);
                return;
            }

            if (_nav == null || !_nav.enabled || !_nav.isOnNavMesh) return;

            TickHeavyCombat(dist);
        }

        private void TickHeavyCombat(float dist)
        {
            float currentChaseSpeed = ChaseSpeed;

            if (dist > GrenadeRange)
            {
                _nav.speed = currentChaseSpeed;
                _nav.SetDestination(_player.position);
            }
            else if (dist > PreferredRange)
            {
                _nav.speed = MoveSpeed;
                _nav.SetDestination(_player.position);
                FacePlayer();
                if (!_isBursting) TryStartGrenadeBurst(dist);
            }
            else
            {
                // Khi đứng siêu gần (ở PreferredRange hoặc nhỏ hơn), quái áp sát húc người đồng thời vẫn nhả đạn vòm cung
                _nav.speed = MoveSpeed;
                _nav.SetDestination(_player.position); // FIX: Tiếp tục đuổi để húc cận chiến chứ không ResetPath đứng yên nữa!
                FacePlayer();
                if (!_isBursting) TryStartGrenadeBurst(dist);
            }
        }

        private void TryStartGrenadeBurst(float dist)
        {
            // ĐÃ FIX LỖI: Loại bỏ hoàn toàn dòng chặn "if (isMeleeMode) return;"
            // Giờ đây, dù có đang bật chế độ húc cận chiến hay không, quái vẫn xả đạn vòm cung bình thường!

            float actualCooldown = _aggressionBoostApplied ? (BurstCooldown / AggressionFireRateMultiplier) : BurstCooldown;

            _grenadeFireTimer += Time.deltaTime;
            if (_grenadeFireTimer >= actualCooldown)
            {
                _grenadeFireTimer = 0f;
                if (!_isBursting && IsAlive && !_shieldBreakRecovering && !IsStaggered)
                {
                    StartCoroutine(GrenadeBurstCoroutine());
                }
            }
        }

        private IEnumerator GrenadeBurstCoroutine()
        {
            _isBursting = true;

            for (int i = 0; i < BurstCount; i++)
            {
                if (_player == null || !IsAlive || _shieldBreakRecovering || IsStaggered) break;

                FireGrenadeArc();

                float interval = _aggressionBoostApplied ? (BurstInterval / AggressionFireRateMultiplier) : BurstInterval;
                yield return new WaitForSeconds(interval);
            }

            _isBursting = false;
        }

        // ── TỐI ƯU GÓC BẮN CHUẨN XÁC TRỰC GIAO TOÁN HỌC ──
        private void FireGrenadeArc()
        {
            if (GrenadePrefab == null && AcidProjectilePrefab == null) return;

            GameObject prefab = GrenadePrefab != null ? GrenadePrefab : AcidProjectilePrefab;
            Transform fp = GrenadeFirePoint != null ? GrenadeFirePoint : FirePoint;
            
            Vector3 origin = fp != null ? fp.position : transform.position + Vector3.up * 1.6f;
            Vector3 targetCenter = _player.position + Vector3.up * 1.0f; 

            Vector3 diff = targetCenter - origin;
            Vector3 flatDir = new Vector3(diff.x, 0f, diff.z).normalized;

            if (flatDir == Vector3.zero) flatDir = transform.forward;

            Vector3 localRightAxis = Vector3.Cross(Vector3.up, flatDir).normalized;

            Quaternion arcRotation = Quaternion.AngleAxis(-GrenadeArcAngle, localRightAxis);
            Vector3 finalVelocityDirection = arcRotation * flatDir;

            GameObject grenadeGO = Instantiate(prefab, origin, Quaternion.LookRotation(finalVelocityDirection));
            AcidProjectile grenade = grenadeGO.GetComponent<AcidProjectile>();
            
            if (grenade != null)
            {
                grenade.Speed = GrenadeSpeed; 
                grenade.Init(finalVelocityDirection);
            }

            Debug.DrawRay(origin, finalVelocityDirection * 6f, Color.green, 2.0f);
        }

        // ── Shield Visibility ────────────────────────────────────
        private void TickShieldVisibility()
        {
            if (ShieldObject == null) return;

            if (_player == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) _player = go.transform;
                else return;
            }

            float dist      = Vector3.Distance(transform.position, _player.position);
            bool shouldShow = dist <= ShieldActivateRange && HasShield;

            if (shouldShow && !_shieldVisible)
            {
                _shieldVisible = true;
                ShieldObject.SetActive(true);
            }
            else if (!shouldShow && _shieldVisible && !_isDissolving)
            {
                _shieldVisible = false;
                ShieldObject.SetActive(false);
            }

            if (_shieldVisible)
                UpdateShieldColor();
        }

        // ── Damage ───────────────────────────────────────────────
        public override void ApplyDamage(float amount, PlayerController instigator)
        {
            if (!IsAlive) return;

            if (HasShield)
            {
                CurrentArmor = Mathf.Max(0f, CurrentArmor - amount);
                OnArmorChanged?.Invoke(CurrentArmor);
                FlashShield();

                if (ArmorFraction <= LowArmorThreshold && _blinkRoutine == null)
                    _blinkRoutine = StartCoroutine(BlinkShield());

                if (CurrentArmor <= 0f)
                {
                    StartCoroutine(DissolveShield());
                    SpawnShieldBreakEffect();
                    StartCoroutine(ShieldBreakRecovery());
                    ApplyAggressionBoost();
                }
                return;
            }

            base.ApplyDamage(amount, instigator);
        }

        private void UpdateShieldColor()
        {
            if (ShieldMaterial == null) return;

            Color targetColor = Color.Lerp(ShieldColorLow, ShieldColorFull, ArmorFraction);
            targetColor.a     = Mathf.Lerp(0.6f, 0.3f, ArmorFraction);

            ShieldMaterial.SetColor(_baseColorID, targetColor);
            ShieldMaterial.SetColor(_colorID,     targetColor);
            ShieldMaterial.color = targetColor;
        }

        private void FlashShield()
        {
            if (ShieldMaterial == null) return;
            StopCoroutine(nameof(HitFlashRoutine));
            StartCoroutine(nameof(HitFlashRoutine));
        }

        private IEnumerator HitFlashRoutine()
        {
            ShieldMaterial.SetColor(_baseColorID, Color.white);
            yield return new WaitForSeconds(0.08f);
            UpdateShieldColor();
        }

        private IEnumerator BlinkShield()
        {
            while (HasShield && ArmorFraction <= LowArmorThreshold)
            {
                ShieldMaterial.SetColor(_baseColorID, Color.red);
                yield return new WaitForSeconds(0.08f);
                UpdateShieldColor();
                yield return new WaitForSeconds(0.08f);
            }
            _blinkRoutine = null;
        }

        private IEnumerator DissolveShield()
        {
            if (_isDissolving) yield break;
            _isDissolving = true;

            float timer = 0f;
            while (timer < DissolveDuration)
            {
                timer += Time.deltaTime;
                if (ShieldMaterial != null)
                    ShieldMaterial.SetFloat(_dissolveAmountID, Mathf.Lerp(0f, 1f, timer / DissolveDuration));
                yield return null;
            }

            if (ShieldObject != null)
                ShieldObject.SetActive(false);

            _shieldVisible = false;
            _isDissolving  = false;
        }

        private void SpawnShieldBreakEffect()
        {
            if (ShieldBreakEffectPrefab == null) return;
            Instantiate(ShieldBreakEffectPrefab, ShieldObject.transform.position, Quaternion.identity);
        }

        private IEnumerator ShieldBreakRecovery()
        {
            _shieldBreakRecovering = true;

            NavMeshAgent nav  = GetComponent<NavMeshAgent>();
            Rigidbody   rb   = GetComponent<Rigidbody>();
            Animator    anim = GetComponent<Animator>();

            if (nav  != null) { nav.ResetPath(); nav.velocity = Vector3.zero; nav.isStopped = true; nav.enabled = false; }
            if (rb   != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
            if (anim != null) { anim.enabled = false; }

            if (ShieldBreakStaggerEffect != null)
            {
                GameObject fx = Instantiate(ShieldBreakStaggerEffect, transform.position + Vector3.up * 1.5f, Quaternion.identity);
                Destroy(fx, 2.5f);
            }

            float timer = 0f;
            while (timer < ShieldBreakStunDuration)
            {
                timer += Time.deltaTime;
                if (nav != null) { nav.ResetPath(); nav.velocity = Vector3.zero; nav.isStopped = true; }
                if (rb  != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                yield return null;
            }

            if (rb   != null) { rb.isKinematic = false; }
            if (nav  != null) { nav.enabled = true; nav.isStopped = false; }
            if (anim != null) { anim.enabled = true; }

            _shieldBreakRecovering = false;
        }

        private void ApplyAggressionBoost()
        {
            if (_aggressionBoostApplied) return;
            ChaseSpeed   *= AggressionSpeedMultiplier;
            AcidFireRate *= AggressionFireRateMultiplier;
            _aggressionBoostApplied = true;
        }

        // ── MELEE DAMAGE OVERLAP (TỰ MẤT MÁU KHI CHẠM NGƯỜI CÓ COOLDOWN) ──────────────────
        private void OnTriggerStay(Collider other)
        {
            if (!isMeleeMode || !IsAlive || _shieldBreakRecovering) return;

            if (Time.time >= _nextMeleeAttackTime && other.CompareTag("Player"))
            {
                var health = other.GetComponentInParent<PlayerHealth>();
                if (health != null)
                {
                    _nextMeleeAttackTime = Time.time + MeleeAttackCooldown;
                    health.TakeDamage(MeleeDamage);
                    
                    if (ShieldBreakStaggerEffect != null)
                    {
                        GameObject fx = Instantiate(ShieldBreakStaggerEffect, other.transform.position + Vector3.up * 1f, Quaternion.identity);
                        Destroy(fx, 1f);
                    }
                    GameManager.Instance?.DebugLog($"[Heavy Melee] Đang ép sát và húc trúng Player! Trừ {MeleeDamage} HP.");
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = HasShield ? Color.cyan : Color.red;
            Gizmos.DrawWireSphere(transform.position, ShieldActivateRange);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, GrenadeRange);

            Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, RadarRange);
        }
#endif
    }
}