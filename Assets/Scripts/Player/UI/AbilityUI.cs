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

        public void SetState(bool ready, float cooldownRemaining)
        {
            if (statusText == null) return;
            statusText.text = ready ? readyLabel : cooldownRemaining.ToString("0.0");
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