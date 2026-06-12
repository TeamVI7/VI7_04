using UnityEngine;
using System.Collections;

public class EnemySoundController : MonoBehaviour
{
    [Header("Capabilities")]
    [SerializeField] private bool hasWalking;
    [SerializeField] private bool hasLaser;
    [SerializeField] private bool hasBomb;

    private EnemyBase     _enemy;
    private EnemyDrone    _drone;
    private EnemyShielder _shielder;
    private AudioSource   _walkSource;
    private bool          _isDead;
    private EnemyState    _lastState;

    void Start()
    {
        _enemy    = GetComponent<EnemyBase>();
        _drone    = GetComponent<EnemyDrone>();
        _shielder = GetComponent<EnemyShielder>();

        if (hasWalking)
        {
            _walkSource = gameObject.AddComponent<AudioSource>();
            SoundManager.Instance.SetupWalkingSource(_walkSource);
        }

        // FIX: Subscribe to C# events — no more per-frame reflection
        if (hasLaser && _drone != null)
            _drone.OnLaserToggled += HandleLaserToggled;

        if (hasBomb && _shielder != null)
            _shielder.OnBurstStarted += HandleBurstStarted;

        if (_enemy != null) _lastState = _enemy.State;
    }

    void OnDestroy()
    {
        if (_drone    != null) _drone.OnLaserToggled   -= HandleLaserToggled;
        if (_shielder != null) _shielder.OnBurstStarted -= HandleBurstStarted;
    }

    void Update()
    {
        if (_enemy == null) return;
        HandleDie();
        if (_isDead) return;
        HandleWalking();
    }

    private void HandleDie()
    {
        if (_isDead) return;
        bool died = !_enemy.IsAlive || _enemy.State == EnemyState.Ragdoll;
        if (!died) return;

        _isDead = true;
        StopWalking();
        SoundManager.Instance.PlaySFX(SFXType.Die, transform.position);
    }

    private void HandleWalking()
    {
        if (!hasWalking) return;
        EnemyState current = _enemy.State;
        if (current == _lastState) return;
        _lastState = current;

        if (current == EnemyState.Aggro) StartWalking();
        else StopWalking();
    }

    private void StartWalking()
    {
        if (_walkSource == null) return;
        if (!_walkSource.isPlaying) _walkSource.Play();
    }

    private void StopWalking()
    {
        if (_walkSource != null && _walkSource.isPlaying) _walkSource.Stop();
    }

    // FIX: Called by event, not polled every frame
    private void HandleLaserToggled(bool isActive)
    {
        if (isActive)
            SoundManager.Instance.PlaySFX(SFXType.Laser, transform.position);
    }

    // FIX: Called by event, not polled every frame
    private void HandleBurstStarted()
    {
        if (_shielder == null) return;
        StartCoroutine(PlayBombBurst(_shielder.BurstCount, _shielder.BurstInterval));
    }

    private IEnumerator PlayBombBurst(int count, float interval)
    {
        for (int i = 0; i < count; i++)
        {
            if (_isDead) yield break;
            SoundManager.Instance.PlaySFX(SFXType.Bomb, transform.position);
            yield return new WaitForSeconds(interval);
        }
    }
}