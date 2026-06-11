using UnityEngine;

/// <summary>
/// Placed on the Animator GameObject (or a child with the Animator).
/// Receives Animation Events from FBX clips and forwards them to WeaponsController.
/// 
/// Setup:
/// 1. Create this script (or use existing)
/// 2. Add it to the same GameObject as the Animator, or as a child
/// 3. Drag WeaponsController into the inspector
/// 4. In the FBX importer, add Animation Events on the appropriate frames
/// 5. Set the function name to the method name (e.g. AnimEvent_ReloadStart)
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimationEventReceiver : MonoBehaviour
{
    [SerializeField] private WeaponsController weaponsController;
    [SerializeField] private CasingEjector casingEjector;
    [SerializeField] private bool showDebugLogs = true;

    private void Start()
    {
        if (weaponsController == null)
            weaponsController = GetComponentInParent<WeaponsController>();

        if (weaponsController == null)
            LogWarning("WeaponsController not found!");
        
        // Subscribe to bolt action events
        if (weaponsController != null)
        {
            weaponsController.OnBoltOut += PlayBoltOutSound;
            weaponsController.OnBoltIn += PlayBoltInSound;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    #region Reload Receivers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Place this event on the FIRST FRAME of your Reload clip (when mag leaves hand).
    /// Function name: AnimEvent_ReloadStart
    /// </summary>
    public void AnimEvent_ReloadStart()
    {
        Log("AnimEvent_ReloadStart");
        // Handler already in WeaponsController
    }

    /// <summary>
    /// Place this event when the magazine physically leaves the gun.
    /// Function name: AnimEvent_MagOut
    /// </summary>
    public void AnimEvent_MagOut()
    {
        Log("AnimEvent_MagOut");
        weaponsController?.OnMagOut_AnimDriven();
    }

    /// <summary>
    /// Place this event when the fresh magazine seats into the gun.
    /// Function name: AnimEvent_MagIn
    /// </summary>
    public void AnimEvent_MagIn()
    {
        Log("AnimEvent_MagIn");
        weaponsController?.OnMagIn_AnimDriven();
    }

    /// <summary>
    /// Place this event when the bolt/slide chambers a round (if your weapon has this).
    /// Function name: AnimEvent_ChamberRound
    /// </summary>
    public void AnimEvent_ChamberRound()
    {
        Log("AnimEvent_ChamberRound");
        weaponsController?.OnChamberRound_AnimDriven();
    }

    /// <summary>
    /// Place this event on the LAST FRAME of your Reload clip (animation fully complete).
    /// Function name: AnimEvent_ReloadEnd
    /// </summary>
    public void AnimEvent_ReloadEnd()
    {
        Log("AnimEvent_ReloadEnd");
        weaponsController?.OnReloadEnd_AnimDriven();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Inspect Receivers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Place this event when the magazine physically leaves the gun during inspect.
    /// Function name: AnimEvent_InspectMagOut
    /// </summary>
    public void AnimEvent_InspectMagOut()
    {
        Log("AnimEvent_InspectMagOut");
        weaponsController?.OnInspectMagOut_AnimDriven();
    }

    /// <summary>
    /// Place this event when the magazine seats back into the gun during inspect.
    /// Function name: AnimEvent_InspectMagIn
    /// </summary>
    public void AnimEvent_InspectMagIn()
    {
        Log("AnimEvent_InspectMagIn");
        weaponsController?.OnInspectMagIn_AnimDriven();
    }

    /// <summary>
    /// Place this event on the LAST FRAME of your Inspect clip.
    /// Function name: AnimEvent_InspectEnd
    /// </summary>
    public void AnimEvent_InspectEnd()
    {
        Log("AnimEvent_InspectEnd");
        weaponsController?.OnInspectEnd_AnimDriven();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Switch Receivers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Place this event on the LAST FRAME of your Holster clip.
    /// Function name: AnimEvent_HolsterEnd
    /// </summary>
    public void AnimEvent_HolsterEnd()
    {
        Log("AnimEvent_HolsterEnd");
        weaponsController?.OnHolsterEnd_AnimDriven();
    }

    /// <summary>
    /// Place this event on the LAST FRAME of your Draw clip.
    /// Function name: AnimEvent_DrawEnd
    /// </summary>
    public void AnimEvent_DrawEnd()
    {
        Log("AnimEvent_DrawEnd");
        weaponsController?.OnDrawEnd_AnimDriven();
    }

    public void AnimEvent_BoltOut()
    {
        Log("AnimEvent_BoltOut");
        weaponsController?.OnBoltOut_AnimDriven();
    }

    public void AnimEvent_BoltIn()
    {
        Log("AnimEvent_BoltIn");
        weaponsController?.OnBoltIn_AnimDriven();
    }

    /// <summary>
    /// Triggers casing ejection via animation event.
    /// Or via AnimationEventReceiver for bolt-action/pump delayed eject
    /// AnimationEvent → "OnCasingEject" → routes to ejector.Eject()
    /// Function name: AnimEvent_OnCasingEject
    /// </summary>
    public void AnimEvent_OnCasingEject()
    {
        Log("AnimEvent_OnCasingEject");
        casingEjector?.Eject();
    }

    private void PlayBoltOutSound()
    {
        Log("PlayBoltOutSound");
        weaponsController?.PlayBoltOutSound();
    }

    private void PlayBoltInSound()
    {
        Log("PlayBoltInSound");
        weaponsController?.PlayBoltInSound();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    private void Log(string msg)
    {
        if (showDebugLogs)
            Debug.Log($"[AnimEventReceiver] {msg}", this);
    }

    private void LogWarning(string msg)
    {
        Debug.LogWarning($"[AnimEventReceiver] {msg}", this);
    }

    #endregion
}
