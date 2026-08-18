using System;
using UnityEngine;
using TMPro;

public class AbilityHUDController : MonoBehaviour
{
    [Serializable]
    public class AbilitySlot
    {
        public TMP_Text statusText;
        public string readyLabel = "READY";
        [Tooltip("Shown when the ability is unusable for a reason that isn't a cooldown " +
                 "(e.g. not enough stamina) — otherwise the slot would read a misleading '0.0'.")]
        public string blockedLabel = "---";

        public void SetState(bool ready, float cooldownRemaining)
        {
            if (statusText == null) return;

            if (ready)                      statusText.text = readyLabel;
            else if (cooldownRemaining > 0f) statusText.text = cooldownRemaining.ToString("0.0");
            else                            statusText.text = blockedLabel;
        }
    }

    [Header("Dash")]
    public Dashing dash;
    public AbilitySlot dashSlot;

    [Header("Wall Run")]
    public WallRunning wallRunning;
    public AbilitySlot wallRunSlot;

    [Header("Other Abilities")]
    [Tooltip("Drive these manually from other scripts via UpdateOtherSlot().")]
    public AbilitySlot[] otherSlots;

    void Update()
    {
        if (dash != null)
            dashSlot.SetState(dash.CanDash, dash.CooldownRemaining);

        if (wallRunning != null)
            wallRunSlot.SetState(wallRunning.IsWallRunning || wallRunning.IsWallSliding, 0f);
    }

    public void UpdateOtherSlot(int index, bool ready, float cooldownRemaining)
    {
        if (index < 0 || index >= otherSlots.Length) return;
        otherSlots[index].SetState(ready, cooldownRemaining);
    }
}