using System.Collections;
using UnityEngine;

/// <summary>
/// Handles a single ejected casing: physics, bounce audio, fade, and pool return.
/// Attach to the casing prefab.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class BulletCasing : MonoBehaviour
{
    // ── Runtime refs ──────────────────────────────────────────────────────────
    private Rigidbody     _rb;
    private AudioSource   _audio;
    private Renderer[]    _renderers;
    private BulletCasingData _data;

    // ── State ─────────────────────────────────────────────────────────────────
    private int   _bounceCount;
    private bool  _returning;
    private Coroutine _lifetimeCoroutine;

    // ── Pool callback ─────────────────────────────────────────────────────────
    /// <summary>Set by BulletCasingPool after spawning.</summary>
    public System.Action<BulletCasing> OnReturnToPool;

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        _rb        = GetComponent<Rigidbody>();
        _audio     = GetComponent<AudioSource>();
        _renderers = GetComponentsInChildren<Renderer>();

        _audio.spatialBlend = 1f;  // always 3-D
        _audio.playOnAwake  = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>Called by CasingEjector to (re)initialise after pool retrieval.</summary>
    public void Initialise(BulletCasingData data, Vector3 position, Quaternion rotation,
                           Vector3 ejectionVelocity, Vector3 torque)
    {
        _data         = data;
        _bounceCount  = 0;
        _returning    = false;

        // Reset transform
        transform.SetPositionAndRotation(position, rotation);

        // Reset physics. Unity does not allow setting angularVelocity while the body is kinematic.
        _rb.isKinematic     = false;
        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.AddForce(ejectionVelocity, ForceMode.Impulse);
        _rb.AddTorque(torque, ForceMode.Impulse);

        // Reset visuals
        SetAlpha(1f);

        // Start lifetime countdown
        if (_lifetimeCoroutine != null) StopCoroutine(_lifetimeCoroutine);
        _lifetimeCoroutine = StartCoroutine(LifetimeRoutine());

#if UNITY_EDITOR
        if (_data.debugDrawEjection)
            Debug.DrawRay(position, ejectionVelocity, Color.yellow, 1f);
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void OnCollisionEnter(Collision col)
    {
        if (_data == null || _bounceCount >= _data.maxBounceSounds) return;

        float speed = col.relativeVelocity.magnitude;
        if (speed < _data.minBounceVelocity) return;

        PlayBounceSound(speed);
        _bounceCount++;
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void PlayBounceSound(float speed)
    {
        if (_data.bounceClips == null || _data.bounceClips.Length == 0) return;

        var clip   = _data.bounceClips[Random.Range(0, _data.bounceClips.Length)];
        float vol  = _data.bounceVolume * Mathf.Clamp01(speed / 5f);
        float pitch = Random.Range(0.9f, 1.1f);

        _audio.pitch = pitch;
        _audio.PlayOneShot(clip, vol);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private IEnumerator LifetimeRoutine()
    {
        float wait = _data.lifetime - _data.fadeDuration;
        if (wait > 0f) yield return new WaitForSeconds(wait);

        // Fade out
        if (_data.fadeDuration > 0f)
        {
            float t = 0f;
            while (t < _data.fadeDuration)
            {
                t += Time.deltaTime;
                SetAlpha(1f - Mathf.Clamp01(t / _data.fadeDuration));
                yield return null;
            }
        }

        ReturnToPool();
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void ReturnToPool()
    {
        if (_returning) return;
        _returning = true;

        // Unity does not allow setting velocity/angularVelocity while the body is kinematic.
        _rb.isKinematic    = false;
        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic     = true;

        OnReturnToPool?.Invoke(this);
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>Sets alpha on all renderers — works with URP/HDRP Lit and Standard.</summary>
    private void SetAlpha(float alpha)
    {
        foreach (var r in _renderers)
        {
            foreach (var mat in r.materials)
            {
                Color c = mat.color;
                c.a = alpha;
                mat.color = c;
            }
        }
    }

    // ── Forced cleanup if object is destroyed mid-life ────────────────────────
    private void OnDestroy()
    {
        if (_lifetimeCoroutine != null) StopCoroutine(_lifetimeCoroutine);
    }
}