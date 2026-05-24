using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class GunController : MonoBehaviour
{
    [Header("Gun Settings")]
    public float fireRate = 0.1f;
    public int clipSize = 30;
    public int reservedAmmoCapacity = 270;

    private bool _canShoot;
    private int _currentAmmoInClip;
    private int _ammoInReserve;
    private bool _roundInChamber;

    [Header("Visuals & Animation")]
    public Animator gunAnimator;
    public PlayerMovement playerMovement;

    [Header("Spawner Points")]
    public Transform muzzleFlashPoint;
    public Transform casingEjectPoint;

    [Header("Prefabs")]
    public GameObject bulletCasingPrefab;
    public GameObject bulletTrailPrefab;
    public GameObject muzzleFlashPrefab;

    [Header("Hitscan")]
    public LayerMask aimColliderLayerMask;
    public Transform spawnBulletPosition;
    public int weaponDamage = 1;

    [Header("Spread")]
    [SerializeField] private float baseSpread = 0.02f;
    [SerializeField] private float adsSpreadMultiplier = 0.2f;
    [SerializeField] private float moveSpreadMultiplier = 2.5f;
    [SerializeField] private float sprintSpreadMultiplier = 5f;
    [SerializeField] private float crouchSpreadMultiplier = 0.6f;
    [SerializeField] private float spreadBuildPerShot = 0.01f;
    [SerializeField] private float spreadRecoveryRate = 0.04f;
    [SerializeField] private float maxSpreadBuildup = 0.08f;
    private float _currentSpreadBuildup = 0f;

    [Header("Audio")]
    [SerializeField] private AudioClip[] shootSounds;
    [SerializeField] private AudioClip dryFireSound;
    [SerializeField] private AudioClip magInSound;
    [SerializeField] private AudioClip magOutSound;
    [SerializeField][Range(0f, 1f)] private float shootVolume = 1f;
    [SerializeField][Range(0f, 1f)] private float reloadVolume = 0.8f;
    [SerializeField][Range(0f, 1f)] private float dryFireVolume = 0.8f;

    [Header("Casing Physics")]
    [SerializeField] private float casingEjectForce = 2f;
    [SerializeField] private float casingDestroyTime = 5f;

    private float _dryFireCooldown = 0f;
    private const float DryFireCooldownTime = 0.3f;
    private bool _isReloading = false;
    private bool _isFiring = false;

    private AudioSource _weaponAudioSource;
    private Camera _mainCamera;
    private static AudioSource _sharedImpactAudio;

    private void Start()
    {
        _currentAmmoInClip = clipSize;
        _ammoInReserve = reservedAmmoCapacity;
        _roundInChamber = true;
        _canShoot = true;

        _weaponAudioSource = GetComponent<AudioSource>();
        if (_weaponAudioSource == null)
            _weaponAudioSource = gameObject.AddComponent<AudioSource>();
        _weaponAudioSource.spatialBlend = 1f;
        _weaponAudioSource.playOnAwake = false;

        EnsureSharedImpactAudio();
    }

    private void Update()
    {
        HandleShootingInput();
        UpdateSpread();
        UpdateDryFireCooldown();
        HandleAnimations();
    }

    private void UpdateSpread()
    {
        _currentSpreadBuildup = Mathf.Max(0f, _currentSpreadBuildup - spreadRecoveryRate * Time.deltaTime);
    }

    private void UpdateDryFireCooldown()
    {
        if (_dryFireCooldown > 0f)
            _dryFireCooldown -= Time.deltaTime;
    }

    private void HandleShootingInput()
    {
        if (Input.GetMouseButtonDown(0) && _canShoot && (_currentAmmoInClip > 0 || _roundInChamber))
        {
            _canShoot = false;
            StartCoroutine(ShootGun());
        }
        else if (Input.GetKeyDown(KeyCode.R) && _currentAmmoInClip < clipSize && _ammoInReserve > 0)
        {
            StartCoroutine(ReloadGun());
        }
        else if (Input.GetMouseButtonDown(0) && (_currentAmmoInClip <= 0 && !_roundInChamber))
        {
            if (_dryFireCooldown <= 0f)
            {
                PlaySound(dryFireSound, dryFireVolume);
                _dryFireCooldown = DryFireCooldownTime;
            }
        }
    }

    private void HandleAnimations()
    {
        if (gunAnimator != null && playerMovement != null)
        {
            bool isWalking = playerMovement.state == PlayerMovement.MovementState.walking;
            gunAnimator.SetBool("isWalking", isWalking);
        }
    }

    private IEnumerator ShootGun()
    {
        if (!_roundInChamber)
        {
            _canShoot = true;
            yield break;
        }

        _isFiring = true;
        _roundInChamber = false;

        if (_currentAmmoInClip > 0)
        {
            _currentAmmoInClip--;
            _roundInChamber = true;
        }

        if (gunAnimator != null)
            gunAnimator.SetTrigger("IsShooting");

        // Spawn muzzle flash
        SpawnMuzzleFlash();

        // Spawn casing
        SpawnBulletCasing();

        // Calculate aim with spread
        Vector3 aimDir = CalculateAimWithSpread();

        // Spawn bullet trail
        SpawnBulletTrail(aimDir);

        // Raycast for enemy
        RaycastForEnemy(aimDir);

        // Play random shoot sound
        PlayRandomSound(shootSounds, shootVolume);

        yield return new WaitForSeconds(fireRate);
        _isFiring = false;
        _canShoot = true;
    }

    private Vector3 CalculateAimWithSpread()
    {
        // Get camera center raycast
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null)
            return spawnBulletPosition != null ? spawnBulletPosition.forward : Vector3.forward;

        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = _mainCamera.ScreenPointToRay(screenCenter);

        Vector3 targetPoint = ray.origin + ray.direction * 100f;
        if (Physics.Raycast(ray, out RaycastHit hit, 999f, aimColliderLayerMask, QueryTriggerInteraction.Ignore))
            targetPoint = hit.point;

        Vector3 aimDir = (targetPoint - (spawnBulletPosition != null ? spawnBulletPosition.position : transform.position)).normalized;

        // Calculate spread
        float spread = baseSpread + _currentSpreadBuildup;

        // Apply modifiers based on movement state
        if (playerMovement != null)
        {
            if (playerMovement.state == PlayerMovement.MovementState.standing)
                spread *= 0.8f; // Standing is more accurate
            else if (playerMovement.state == PlayerMovement.MovementState.walking)
                spread *= moveSpreadMultiplier;
            else if (playerMovement.state == PlayerMovement.MovementState.crouching)
                spread *= crouchSpreadMultiplier;
        }

        // Apply spread cone
        if (spread > 0f)
        {
            Vector3 spreadRight = Vector3.Cross(Vector3.up, aimDir).normalized;
            if (spreadRight.magnitude < 0.1f)
                spreadRight = Vector3.Cross(Vector3.forward, aimDir).normalized;
            Vector3 spreadUp = Vector3.Cross(aimDir, spreadRight).normalized;

            Vector3 spreadOffset = spreadRight * Random.Range(-spread, spread) +
                                   spreadUp * Random.Range(-spread, spread);
            aimDir = (aimDir + spreadOffset).normalized;
        }

        _currentSpreadBuildup = Mathf.Min(_currentSpreadBuildup + spreadBuildPerShot, maxSpreadBuildup);

        return aimDir;
    }

    private void SpawnBulletTrail(Vector3 aimDir)
    {
        if (bulletTrailPrefab == null || spawnBulletPosition == null) return;

        Vector3 endPoint = spawnBulletPosition.position + aimDir * 999f;
        if (Physics.Raycast(spawnBulletPosition.position, aimDir, out RaycastHit hit, 999f, aimColliderLayerMask, QueryTriggerInteraction.Ignore))
            endPoint = hit.point;

        GameObject trailObj = Instantiate(bulletTrailPrefab, spawnBulletPosition.position, Quaternion.identity);
        TrailRenderer trail = trailObj.GetComponent<TrailRenderer>();

        if (trail == null)
        {
            return;
        }

        StartCoroutine(MoveTrailCoroutine(trail, spawnBulletPosition.position, endPoint));
    }

    private IEnumerator MoveTrailCoroutine(TrailRenderer trail, Vector3 startPos, Vector3 endPos)
    {
        float time = 0;
        while (time < 1)
        {
            trail.transform.position = Vector3.Lerp(startPos, endPos, time);
            time += Time.deltaTime / Mathf.Max(trail.time, 0.001f);
            yield return null;
        }
        trail.transform.position = endPos;
        Destroy(trail.gameObject, trail.time);
    }

    private void SpawnMuzzleFlash()
    {
        if (muzzleFlashPrefab == null || muzzleFlashPoint == null) return;

        GameObject muzzleFlash = Instantiate(muzzleFlashPrefab, muzzleFlashPoint.position, muzzleFlashPoint.rotation);

        Destroy(muzzleFlash, 0.1f);
    }

    private void SpawnBulletCasing()
    {
        if (bulletCasingPrefab == null || casingEjectPoint == null) return;

        GameObject casing = Instantiate(bulletCasingPrefab, casingEjectPoint.position, casingEjectPoint.rotation);
        IgnorePlayerColliders(casing);

        if (casing.TryGetComponent(out Rigidbody rb))
        {
            Vector3 ejectForce = casingEjectPoint.right * casingEjectForce + Vector3.up * (casingEjectForce * 0.5f);
            rb.AddForce(ejectForce, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * casingEjectForce, ForceMode.Impulse);
        }

        Destroy(casing, casingDestroyTime);
    }

    private void RaycastForEnemy(Vector3 aimDir)
    {
        if (spawnBulletPosition == null) return;

        RaycastHit hit;
        int enemyLayerMask = 1 << LayerMask.NameToLayer("Enemy");

        if (Physics.Raycast(spawnBulletPosition.position, aimDir, out hit, 999f, enemyLayerMask))
        {
            Debug.Log("Hit an Enemy!");
            Rigidbody rb = hit.transform.GetComponent<Rigidbody>();

            if (rb != null)
            {
                rb.constraints = RigidbodyConstraints.None;
                rb.AddForce(aimDir * weaponDamage * 10f, ForceMode.Impulse);
            }
        }
    }

    private IEnumerator ReloadGun()
    {
        _isReloading = true;

        if (gunAnimator != null)
            gunAnimator.SetTrigger("Reload");

        yield return new WaitForSeconds(1.5f);

        int ammoNeeded = clipSize - _currentAmmoInClip;
        int ammoToAdd = Mathf.Min(ammoNeeded, _ammoInReserve);

        _currentAmmoInClip += ammoToAdd;
        _ammoInReserve -= ammoToAdd;

        _roundInChamber = _currentAmmoInClip > 0;

        _isReloading = false;
    }

    private void PlaySound(AudioClip clip, float volume)
    {
        if (clip == null) return;
        _weaponAudioSource.pitch = Random.Range(0.95f, 1.05f);
        _weaponAudioSource.PlayOneShot(clip, volume);
    }

    private void PlayRandomSound(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0) return;
        PlaySound(clips[Random.Range(0, clips.Length)], volume);
    }

    private void IgnorePlayerColliders(GameObject obj)
    {
        if (obj == null) return;
        Collider[] objCols = obj.GetComponentsInChildren<Collider>();
        Collider[] playerCols = transform.root.GetComponentsInChildren<Collider>();

        foreach (var a in objCols)
            foreach (var b in playerCols)
                Physics.IgnoreCollision(a, b);
    }

    private static void EnsureSharedImpactAudio()
    {
        if (_sharedImpactAudio != null) return;
        GameObject go = new GameObject("BulletImpactAudio_Shared");
        _sharedImpactAudio = go.AddComponent<AudioSource>();
        _sharedImpactAudio.spatialBlend = 1f;
        _sharedImpactAudio.playOnAwake = false;
        DontDestroyOnLoad(go);
    }

    // Public getters for UI
    public int CurrentAmmo => _currentAmmoInClip;
    public int ReserveAmmo => _ammoInReserve;
    public bool RoundInChamber => _roundInChamber;
    public bool IsReloading => _isReloading;
}
