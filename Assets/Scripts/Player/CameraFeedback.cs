// ============================================================
//  CameraFeedback.cs  —  Out of Bullet
//  GDD §8.2 — Kill feedback must land within 0.1s of execution.
//  Camera punch + hit stop are prioritized above all other
//  Month 3 work per GDD §11.4 Risk 3.
// ============================================================
using System.Collections;
using UnityEngine;
using OutOfBullet.Core;

namespace OutOfBullet.Player
{
    public class CameraFeedback : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────
        [Header("Camera Punch (GDD §8.2)")]
        public float PunchMagnitude   = 0.12f;
        public float PunchDuration    = 0.08f;
        public float PunchRecovery    = 0.15f;

        [Header("Hit Stop (GDD §8.2: ~0.05s freeze)")]
        public float HitStopDuration  = 0.05f;

        [Header("Execute Feedback")]
        public float ExecutePunchMul  = 2.0f;   // Executes punch harder than normal kills
        public float ExecuteHitStopMul = 1.5f;

        // ── Runtime ──────────────────────────────────────────────
        private Vector3   _punchOffset;
        private Coroutine _punchRoutine;
        private Coroutine _hitStopRoutine;

        // ── Unity ────────────────────────────────────────────────
        private void OnEnable()
        {
            EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Subscribe<EnemyExecutedEvent>(OnEnemyExecuted);
            EventBus.Subscribe<KatanaSwingEvent>(OnKatanaSwing);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Unsubscribe<EnemyExecutedEvent>(OnEnemyExecuted);
            EventBus.Unsubscribe<KatanaSwingEvent>(OnKatanaSwing);
        }

        private void LateUpdate()
        {
            // Apply and decay punch offset
            transform.localPosition = Vector3.Lerp(
                transform.localPosition, _punchOffset,
                20f * Time.deltaTime);

            _punchOffset = Vector3.Lerp(_punchOffset, Vector3.zero, 15f * Time.deltaTime);
        }

        // ── Event Handlers ───────────────────────────────────────
        private void OnEnemyKilled(EnemyKilledEvent evt)
        {
            TriggerPunch(PunchMagnitude, evt.PlayerVelocityAtKill.normalized);
            TriggerHitStop(HitStopDuration);
        }

        private void OnEnemyExecuted(EnemyExecutedEvent evt)
        {
            // Execute punch is stronger — GDD §11.4 Risk 3
            TriggerPunch(PunchMagnitude * ExecutePunchMul, Vector3.forward);
            TriggerHitStop(HitStopDuration * ExecuteHitStopMul);
        }

        private void OnKatanaSwing(KatanaSwingEvent evt)
        {
            if (evt.HitEnemy)
                TriggerPunch(PunchMagnitude * 0.5f, Vector3.forward);
        }

        // ── Camera Punch ─────────────────────────────────────────
        private void TriggerPunch(float magnitude, Vector3 direction)
        {
            if (_punchRoutine != null) StopCoroutine(_punchRoutine);
            _punchRoutine = StartCoroutine(PunchRoutine(magnitude, direction));
        }

        private IEnumerator PunchRoutine(float magnitude, Vector3 direction)
        {
            // Jolt
            _punchOffset = direction * magnitude;
            yield return new WaitForSeconds(PunchDuration);

            // Recovery handled by LateUpdate lerp — no action needed here
        }

        // ── Hit Stop ─────────────────────────────────────────────
        private void TriggerHitStop(float duration)
        {
            if (_hitStopRoutine != null) StopCoroutine(_hitStopRoutine);
            _hitStopRoutine = StartCoroutine(HitStopRoutine(duration));
        }

        private IEnumerator HitStopRoutine(float duration)
        {
            // GDD §8.2: ~0.05s freeze frame at moment of contact
            float prevTimeScale = Time.timeScale;
            Time.timeScale = 0.02f;     // Near-freeze — not full stop to avoid input issues
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = prevTimeScale;
        }
    }
}