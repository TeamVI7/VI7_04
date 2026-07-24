// MechGatlingAttack.cs — sustained hitscan
using System;
using System.Collections;
using UnityEngine;

public class MechGatlingAttack : MechAttackBehaviour
{
    public Animator animator;
    public Transform muzzle;
    public float spinUpTime = 0.8f;
    public float fireDuration = 3f;
    public float fireRate = 0.08f;
    public float damagePerShot = 6f;
    public float spread = 2f;
    public LayerMask hitMask;
    public GameObject muzzleFlashPrefab;

    private static readonly int AnimSpin = Animator.StringToHash("GatlingSpin");
    private static readonly int AnimFire = Animator.StringToHash("GatlingFire");

    public override void Execute(Action onComplete) => StartCoroutine(Co_Execute(onComplete));

    private IEnumerator Co_Execute(Action onComplete)
    {
        IsExecuting = true;
        if (animator != null) animator.SetBool(AnimSpin, true);
        yield return new WaitForSeconds(spinUpTime);

        if (animator != null) animator.SetBool(AnimFire, true);

        float elapsed = 0f;
        while (elapsed < fireDuration)
        {
            FireShot();
            elapsed += fireRate;
            yield return new WaitForSeconds(fireRate);
        }

        if (animator != null)
        {
            animator.SetBool(AnimFire, false);
            animator.SetBool(AnimSpin, false);
        }

        IsExecuting = false;
        onComplete?.Invoke();
    }

    private void FireShot()
    {
        if (muzzle == null || PlayerHealth.Transform == null) return;

        Vector3 dir = (PlayerHealth.Transform.position + Vector3.up - muzzle.position).normalized;
        dir += UnityEngine.Random.insideUnitSphere * (spread * 0.05f);
        dir.Normalize();

        if (muzzleFlashPrefab != null)
            Destroy(Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation), 0.1f);

        if (Physics.Raycast(muzzle.position, dir, out RaycastHit hit, 60f, hitMask))
            if (hit.collider.TryGetComponent(out PlayerHealth ph)) ph.TakeDamage(damagePerShot);
    }
}