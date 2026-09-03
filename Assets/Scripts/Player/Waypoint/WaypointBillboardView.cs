using TMPro;
using UnityEngine;

/// <summary>
/// Binds the billboard waypoint prefab's child renderers so WaypointHUD can drive them
/// without a GetComponentInChildren every frame per marker.
///
/// PREFAB SETUP:
///   Root (this script)
///   ├─ Icon           SpriteRenderer
///   ├─ DistanceLabel  TMP_Text  (TextMeshPro 3D, not UI)
///   └─ NameLabel      TMP_Text  (optional)
///
/// The root is what gets rotated to face the camera, so keep the children's local
/// rotations at zero — WaypointHUD only touches the root's transform.
/// </summary>
public class WaypointBillboardView : MonoBehaviour
{
    [Header("Bound Children")]
    [SerializeField] private SpriteRenderer icon;
    [SerializeField] private TMP_Text distanceLabel;
    [SerializeField] private TMP_Text nameLabel;

    /// <summary>Baseline local scale, captured before WaypointHUD applies distance scaling.</summary>
    public Vector3 BaseScale { get; private set; }

    private void Awake() => BaseScale = transform.localScale;

    /// <summary>
    /// Pushes one frame of state onto the prefab. Null or empty text hides the label
    /// outright rather than leaving an empty TMP object doing layout work.
    /// </summary>
    public void Apply(Sprite sprite, Color tint, string distanceText, string nameText)
    {
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.color  = tint;
        }

        SetLabel(distanceLabel, distanceText, tint);
        SetLabel(nameLabel, nameText, tint);
    }

    /// <summary>Fades the whole marker — used for the occlusion dim when a wall is in the way.</summary>
    public void SetAlpha(float alpha)
    {
        if (icon != null)
        {
            Color c = icon.color;
            c.a = alpha;
            icon.color = c;
        }

        SetLabelAlpha(distanceLabel, alpha);
        SetLabelAlpha(nameLabel, alpha);
    }

    private static void SetLabel(TMP_Text label, string text, Color tint)
    {
        if (label == null) return;

        bool show = !string.IsNullOrEmpty(text);
        if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
        if (!show) return;

        label.text  = text;
        label.color = tint;
    }

    private static void SetLabelAlpha(TMP_Text label, float alpha)
    {
        if (label == null || !label.gameObject.activeSelf) return;
        Color c = label.color;
        c.a = alpha;
        label.color = c;
    }
}
