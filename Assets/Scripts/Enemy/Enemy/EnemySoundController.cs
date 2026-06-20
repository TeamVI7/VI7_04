using UnityEngine;
using UnityEngine.AI;
using System;
 
[RequireComponent(typeof(EnemyHealth))]
public class EnemyAudio : MonoBehaviour
{
    [Header("Movement")]
    public AudioClip WalkClip;
    [Tooltip("Tốc độ tối thiểu để coi là đang di chuyển")]
    public float MinMoveSpeed = 0.1f;
 
    [Header("Weapons")]
    public AudioClip LaserClip;
    public AudioClip BombClip;
    public AudioClip SMGClip;
    public AudioClip ShotgunClip; 
    public AudioClip SniperClip; // THÊM MỚI: Tuyệt đối không chạm vào 4 ô trên
 
    [Header("Status")]
    public AudioClip DeathClip;
 
    [Header("3D Audio — Settings")]
    [Range(0f, 1f)] public float SpatialBlend = 1.0f;
    public float MinDistance = 5f;
    public float MaxDistance = 30f;
 
    // ── Sources ──────────────────────────────────────────────
    private AudioSource _loopSource;
    private AudioSource _oneShotSource;
 
    // ── References ───────────────────────────────────────────
    private EnemyHealth           _health;
    private EnemyBrain            _brain;
    private LaserBehaviour        _laser;
    private GrenadeBurstBehaviour _burst;
    private SMGAttackBehaviour    _smg;
    private NavMeshAgent          _nav; // Dòng này bị thiếu trong ảnh của cậu nè!
    private SniperAttackBehaviour _sniper; // THÊM MỚI: Tham chiếu xử lý đạn Sniper
    
    private Component             _shotgunComponent; 
    private bool _isAggro;
 
    private void Awake()
    {
      _health = GetComponent<EnemyHealth>();
      _brain  = GetComponent<EnemyBrain>();
      _laser  = GetComponent<LaserBehaviour>();
      _burst  = GetComponent<GrenadeBurstBehaviour>();
      _smg    = GetComponent<SMGAttackBehaviour>();
      _sniper = GetComponent<SniperAttackBehaviour>(); // THÊM MỚI: Khởi tạo mượt mà cùng các hệ khác
      _nav    = GetComponent<NavMeshAgent>(); // Khởi tạo mượt mà
 
      _loopSource = gameObject.AddComponent<AudioSource>();
      Configure3D(_loopSource, loop: true);
 
      _oneShotSource = gameObject.AddComponent<AudioSource>();
      Configure3D(_oneShotSource, loop: false);
    }
 
    private void Start()
    {
      _health.OnDied += HandleDeath;
 
      if (_brain != null)  _brain.OnStateChanged += HandleStateChanged;
      if (_laser != null)  _laser.OnLaserToggled += HandleLaserToggled;
      if (_burst != null)  _burst.OnBurstStarted += PlayBomb;
      if (_smg != null)    _smg.OnSMGFired       += PlaySMG;
      if (_sniper != null) _sniper.OnSniperShot  += PlaySniper; // THÊM MỚI: Lắng nghe Sniper bắn
 
      _shotgunComponent = GetComponent("ShotgunAttackBehaviour");
      if (_shotgunComponent != null)
      {
          var ev = _shotgunComponent.GetType().GetEvent("OnShotgunFired");
          if (ev != null)
          {
              Action handler = PlayShotgun;
              ev.AddMethod.Invoke(_shotgunComponent, new object[] { handler });
          }
      }
    }
 
    private void Update()
    {
      HandleWalking();
    }
 
    private void OnDestroy()
    {
      if (_health != null) _health.OnDied        -= HandleDeath;
      if (_brain  != null) _brain.OnStateChanged -= HandleStateChanged;
      if (_laser  != null) _laser.OnLaserToggled -= HandleLaserToggled;
      if (_burst  != null) _burst.OnBurstStarted -= PlayBomb;
      if (_smg    != null) _smg.OnSMGFired       -= PlaySMG;
      if (_sniper != null) _sniper.OnSniperShot  -= PlaySniper; // THÊM MỚI: Hủy lắng nghe an toàn
 
      if (_shotgunComponent != null)
      {
          var ev = _shotgunComponent.GetType().GetEvent("OnShotgunFired");
          if (ev != null)
          {
              Action handler = PlayShotgun;
              ev.RemoveMethod.Invoke(_shotgunComponent, new object[] { handler });
          }
      }
    }
 
    private void HandleWalking()
    {
      if (WalkClip == null || !_isAggro) return;
      bool isMoving = _nav != null && _nav.enabled && _nav.velocity.magnitude > MinMoveSpeed;
 
      if (isMoving && !_loopSource.isPlaying)
      {
          _loopSource.clip = WalkClip;
          _loopSource.Play();
      }
      else if (!isMoving && _loopSource.isPlaying)
      {
          _loopSource.Stop();
      }
    }
 
    private void HandleStateChanged(EnemyState state)
    {
      _isAggro = state == EnemyState.Aggro;
      if (!_isAggro) _loopSource.Stop();
    }
 
    private void HandleLaserToggled(bool isOn)
    {
      if (isOn && LaserClip != null && !_oneShotSource.isPlaying)
      {
          _oneShotSource.clip = LaserClip;
          _oneShotSource.Play();
      }
      else if (!isOn)
      {
          _oneShotSource.Stop();
      }
    }
 
    private void PlayBomb() { if (BombClip != null) _oneShotSource.PlayOneShot(BombClip); }
    private void PlaySMG() { if (SMGClip != null) _oneShotSource.PlayOneShot(SMGClip); }
    private void PlayShotgun() { if (ShotgunClip != null) _oneShotSource.PlayOneShot(ShotgunClip); }
    private void PlaySniper() { if (SniperClip != null) _oneShotSource.PlayOneShot(SniperClip); } // THÊM MỚI: Phát sound nổ xé gió
 
    private void HandleDeath(Vector3 impulse)
    {
      _loopSource.Stop();
      _oneShotSource.Stop();
      if (DeathClip == null) return;
 
      var go = new GameObject($"{gameObject.name}_Death");
      go.transform.position = transform.position;
      var src = go.AddComponent<AudioSource>();
      src.spatialBlend = 0f;
      src.clip = DeathClip;
      src.Play();
 
      Destroy(go, DeathClip.length + 0.1f);
    }
 
    private void Configure3D(AudioSource src, bool loop)
    {
      src.playOnAwake = false;
      src.loop        = loop;
      src.spatialBlend = SpatialBlend;
      src.rolloffMode = AudioRolloffMode.Linear;
      src.minDistance = MinDistance;
      src.maxDistance = MaxDistance;
      src.dopplerLevel = 0f;
    }
}