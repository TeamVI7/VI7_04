using UnityEngine;

[CreateAssetMenu(menuName = "FPS/Audio/Player Sound Data", fileName = "PlayerSoundData_New")]
public class PlayerSoundData : ScriptableObject
{
    [System.Serializable]
    public class SurfaceFootstepSet
    {
        [Tooltip("Must match the Tag on the ground collider (e.g. 'Metal', 'Grass', 'Water').")]
        public string surfaceTag = "Default";
        public AudioClip[] footstepClips;
        public AudioClip[] landClips;
    }

    // ── Footsteps ────────────────────────────────────────────────────────────
    [Header("Footsteps")]
    public AudioClip[] defaultFootstepClips;
    [Range(0f, 1f)] public float footstepVolume = 0.5f;
    public Vector2 footstepPitchRange = new Vector2(0.95f, 1.05f);
    [Tooltip("Optional per-surface overrides. Falls back to defaultFootstepClips/defaultLandClips if tag not found.")]
    public SurfaceFootstepSet[] surfaceOverrides;

    // ── Jump / Land ──────────────────────────────────────────────────────────
    [Header("Jump")]
    public AudioClip[] jumpClips;
    [Range(0f, 1f)] public float jumpVolume = 0.7f;

    [Header("Land")]
    public AudioClip[] defaultLandClips;
    [Range(0f, 1f)] public float landVolume = 0.7f;
    [Tooltip("Downward velocity (units/sec) at landing required to use hardLandClips instead.")]
    public float hardLandFallSpeedThreshold = 10f;
    public AudioClip[] hardLandClips;

    // ── Sprint ───────────────────────────────────────────────────────────────
    [Header("Sprint")]
    public AudioClip sprintBreathingLoop;
    [Range(0f, 1f)] public float sprintBreathingVolume = 0.4f;

    // ── Vault ────────────────────────────────────────────────────────────────
    [Header("Vault")]
    public AudioClip[] vaultClips;
    [Range(0f, 1f)] public float vaultVolume = 0.6f;

    // ── Dash ─────────────────────────────────────────────────────────────────
    [Header("Dash")]
    public AudioClip[] dashClips;
    [Range(0f, 1f)] public float dashVolume = 0.7f;

    // ── Slide ────────────────────────────────────────────────────────────────
    [Header("Slide")]
    public AudioClip slideStartClip;
    public AudioClip slideLoopClip;
    public AudioClip slideEndClip;
    [Range(0f, 1f)] public float slideVolume = 0.6f;

    // ── Wall Run / Wall Slide ────────────────────────────────────────────────
    [Header("Wall Run / Wall Slide")]
    public AudioClip wallRunLoopClip;
    public AudioClip wallSlideLoopClip;
    public AudioClip[] wallJumpClips;
    [Range(0f, 1f)] public float wallRunVolume = 0.5f;

    // ── Climbing ─────────────────────────────────────────────────────────────
    [Header("Climbing")]
    public AudioClip climbLoopClip;
    public AudioClip[] climbJumpClips;
    [Range(0f, 1f)] public float climbVolume = 0.5f;

    // ── Health ───────────────────────────────────────────────────────────────
    [Header("Hurt")]
    public AudioClip[] hurtClips;
    [Range(0f, 1f)] public float hurtVolume = 0.8f;
    [Tooltip("Minimum damage, as a fraction of MaxHP, required to trigger a hurt sound.")]
    [Range(0f, 1f)] public float minHurtDamageFraction = 0.03f;

    [Header("Death")]
    public AudioClip[] deathClips;
    [Range(0f, 1f)] public float deathVolume = 1f;

    // ── Melee ────────────────────────────────────────────────────────────────
    [Header("Melee")]
    public AudioClip[] meleeSwingClips;
    public AudioClip[] meleeHitClips;
    public AudioClip[] meleeMissClips;
    public AudioClip[] meleeExecuteClips;
    [Range(0f, 1f)] public float meleeVolume = 0.7f;
}