using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the screen-edge arrow prefab so WaypointHUD can drive it without a
/// GetComponentInChildren every frame per marker.
///
/// PREFAB SETUP (all UI, under the arrow container on the PlayerUI canvas):
///   Root (this script, RectTransform, pivot centred)
///   ├─ Arrow          Image — the pointer. MUST point UP at zero rotation.
///   ├─ Icon           Image — optional objective icon, stays upright
///   └─ DistanceLabel  TMP_Text — optional, stays upright
///
/// Only the Arrow child is rotated to the bearing. The root, icon and label are left
/// unrotated so the icon and text stay readable while the pointer spins around them.
/// </summary>
public class WaypointArrowView : MonoBehaviour
{
    [Header("Bound Children")]
    [SerializeField] private RectTransform arrow;
    [SerializeField] private Image arrowImage;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text distanceLabel;

    private RectTransform _rect;

    /// <summary>This view's own RectTransform, cached.</summary>
    public RectTransform Rect => _rect != null ? _rect : (_rect = (RectTransform)transform);

    /// <summary>
    /// Points the arrow at a screen-space bearing in degrees, where 0 is up and
    /// positive is counter-clockwise — i.e. already in UI rotation terms, so
    /// WaypointHUD does the world-to-UI sign flip, not this class.
    /// </summary>
    public void SetBearing(float uiDegrees)
    {
        if (arrow != null) arrow.localEulerAngles = new Vector3(0f, 0f, uiDegrees);
    }

    /// <summary>Pushes one frame of state onto the prefab.</summary>
    public void Apply(Sprite sprite, Color tint, string distanceText)
    {
        if (arrowImage != null) arrowImage.color = tint;

        if (icon != null)
        {
            bool show = sprite != null;
            if (icon.gameObject.activeSelf != show) icon.gameObject.SetActive(show);
            if (show)
            {
                icon.sprite = sprite;
                icon.color  = tint;
            }
        }

        if (distanceLabel != null)
        {
            bool show = !string.IsNullOrEmpty(distanceText);
            if (distanceLabel.gameObject.activeSelf != show) distanceLabel.gameObject.SetActive(show);
            if (show)
            {
                distanceLabel.text  = distanceText;
                distanceLabel.color = tint;
            }
        }
    }
}
