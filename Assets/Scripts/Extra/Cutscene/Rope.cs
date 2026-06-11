using UnityEngine;

public class RopeSegmented : MonoBehaviour
{
    [Header("References")]
    public Transform ropeTop;        // LineAnchor on heli
    public Transform ropeBottom;     // RopeGrip on hands

    [Header("Rope Settings")]
    public int segments = 20;        // more = smoother
    public float sagAmount = 0.5f;   // meters of sag in middle
    public float swaySpeed = 1.2f;   // how fast rope sways
    public float swayAmount = 0.05f; // how much rope sways sideways

    private LineRenderer _lr;
    private float _swayTimer;

    void Start()
    {
        _lr = GetComponent<LineRenderer>();
        _lr.positionCount = segments;
        _lr.useWorldSpace = true;
        _lr.startWidth = 0.02f;
        _lr.endWidth = 0.02f;
    }

    void Update()
    {
        if (ropeTop == null || ropeBottom == null) return;

        _swayTimer += Time.deltaTime * swaySpeed;

        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)(segments - 1);

            // Lerp from top to bottom
            Vector3 point = Vector3.Lerp(ropeTop.position, ropeBottom.position, t);

            // Catenary sag — highest in middle
            float sag = Mathf.Sin(t * Mathf.PI) * sagAmount;
            point.y -= sag;

            // Gentle sway
            float sway = Mathf.Sin(_swayTimer + t * Mathf.PI) * swayAmount;
            point.x += sway;

            _lr.SetPosition(i, point);
        }
    }
}