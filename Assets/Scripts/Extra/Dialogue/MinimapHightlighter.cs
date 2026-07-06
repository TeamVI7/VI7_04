// MinimapRoomHighlight.cs
using TMPro;
using UnityEngine;

public class MinimapRoomHighlight : MonoBehaviour
{
    [SerializeField] private string roomId;
    [SerializeField] private TMP_Text roomLabel;
    [SerializeField] private Color alertColor = Color.red;
    [SerializeField] private Color defaultColor = Color.white;

    public string RoomId => roomId;

    public void SetLit(bool lit) => roomLabel.color = lit ? alertColor : defaultColor;
}