using UnityEngine;

/// <summary>
/// Quick test rig for the Animator states in the screenshot:
/// Idle <-> Running (bool "IsRunning"), plus one-shot Any State
/// transitions: PistolGunHolding, Punching, RifleGunHolding (triggers).
/// Attach to the humanoid, assign the Animator, mash keys in Play mode.
/// Adjust the parameter names below if yours differ.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimatorTestController : MonoBehaviour
{
    [SerializeField] private Animator animator;

    [Header("Parameter Names")]
    [SerializeField] private string runningBool = "IsRunning";
    [SerializeField] private string punchingTrigger = "Punching";
    [SerializeField] private string pistolTrigger = "PistolGunHolding";
    [SerializeField] private string rifleTrigger = "RifleGunHolding";

    [Header("Test Keybinds")]
    [SerializeField] private KeyCode runKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode punchKey = KeyCode.F;
    [SerializeField] private KeyCode pistolKey = KeyCode.Alpha1;
    [SerializeField] private KeyCode rifleKey = KeyCode.Alpha2;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    [Header("Upper Body Mask Layer (test)")]
    [SerializeField] private int upperBodyLayerIndex = 1; // set to your masked layer's index
    [SerializeField] private KeyCode toggleLayerWeightKey = KeyCode.L;
    private bool upperBodyLayerOn = true;

    private void Update()
    {
        // Running <-> Idle (base layer)
        bool running = Input.GetKey(runKey);
        animator.SetBool(runningBool, running);

        // Any State one-shots (masked upper body layer)
        if (Input.GetKeyDown(punchKey))
            animator.SetTrigger(punchingTrigger);

        if (Input.GetKeyDown(pistolKey))
            animator.SetTrigger(pistolTrigger);

        if (Input.GetKeyDown(rifleKey))
            animator.SetTrigger(rifleTrigger);

        // Toggle mask layer weight on/off to sanity-check the mask is actually isolating bones
        if (Input.GetKeyDown(toggleLayerWeightKey))
        {
            upperBodyLayerOn = !upperBodyLayerOn;
            animator.SetLayerWeight(upperBodyLayerIndex, upperBodyLayerOn ? 1f : 0f);
        }
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 300, 20), $"[Hold {runKey}] Run");
        GUI.Label(new Rect(10, 30, 300, 20), $"[{punchKey}] Punch");
        GUI.Label(new Rect(10, 50, 300, 20), $"[{pistolKey}] Pistol Hold");
        GUI.Label(new Rect(10, 70, 300, 20), $"[{rifleKey}] Rifle Hold");
    }
}