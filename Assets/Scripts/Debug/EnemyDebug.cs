using UnityEngine;

/// <summary>
/// Renders the current FSM state above the enemy's head in the Game view.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public class EnemyStateDebugger : MonoBehaviour
{
    [Header("Display Settings")]
    public bool    ShowDebug = true;
    public Vector3 Offset    = new Vector3(0f, 2.2f, 0f);
    public Color   TextColor = Color.yellow;
    
    [Range(10, 30)] 
    public int FontSize = 16;

    private EnemyBrain _brain;
    private Camera     _mainCamera;
    private GUIStyle   _style;

    private void Awake()
    {
        _brain      = GetComponent<EnemyBrain>();
        _mainCamera = Camera.main;
    }

    private void OnGUI()
    {
        // Skip rendering if disabled, brain is missing, or camera is unassigned
        if (!ShowDebug || _brain == null || _mainCamera == null) return;

        // Initialize style once
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = FontSize,
                fontStyle = FontStyle.Bold
            };
        }

        // Calculate screen position based on world coordinates + offset
        Vector3 worldPosition = transform.position + Offset;
        Vector3 screenPos     = _mainCamera.WorldToScreenPoint(worldPosition);

        // Only draw if the enemy is in front of the camera (z > 0)
        if (screenPos.z > 0)
        {
            // Unity GUI uses an inverted Y axis compared to screen coordinates
            screenPos.y = Screen.height - screenPos.y;
            
            string stateText = $"State: {_brain.State}"; 
            
            Rect rect = new Rect(screenPos.x - 100, screenPos.y - 15, 200, 30);

            // Draw a black drop shadow for readability against bright backgrounds
            _style.normal.textColor = Color.black;
            Rect shadowRect = new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height);
            GUI.Label(shadowRect, stateText, _style);
            
            // Draw the actual text
            _style.normal.textColor = TextColor;
            GUI.Label(rect, stateText, _style);
        }
    }
}