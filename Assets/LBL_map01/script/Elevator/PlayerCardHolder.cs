using System.Collections.Generic;
using UnityEngine;

public class PlayerCardHolder : MonoBehaviour
{
    [Header("Danh sách mã thẻ đang sở hữu")]
    public List<string> ownedCardIds = new List<string>();

    public bool HasCard(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return true;
        return ownedCardIds.Contains(cardId);
    }

    public void AddCard(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return;
        if (!ownedCardIds.Contains(cardId))
            ownedCardIds.Add(cardId);
    }

    public void RemoveCard(string cardId)
    {
        ownedCardIds.Remove(cardId);
    }
}