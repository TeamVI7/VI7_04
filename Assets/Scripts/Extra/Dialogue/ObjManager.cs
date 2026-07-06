using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    [SerializeField] private TMP_Text objectiveLabel;
    [SerializeField] private MinimapRoomHighlight[] roomHighlights;

    private Dictionary<string, MinimapRoomHighlight> roomLookup;

    private void Awake()
    {
        Instance = this;
        roomLookup = roomHighlights.ToDictionary(r => r.RoomId, r => r);
    }

    public void SetObjective(ObjectiveData data)
    {
        objectiveLabel.text = data.objectiveText;

        foreach (var id in data.roomIdsToHighlight)
            if (roomLookup.TryGetValue(id, out var room))
                room.SetLit(true);
    }
}