using System.Collections.Generic;
using UnityEngine;

// Gắn script này vào bộ nguồn phát laser.
// Laser sẽ tự bắn, phản xạ qua các gương (Layer "Mirror"),
// và dừng lại khi trúng tường hoặc trúng cảm biến (component LaserSensor).
[RequireComponent(typeof(LineRenderer))]
public class LaserSource : MonoBehaviour
{
    [Header("Laser Settings")]
    public float maxLength = 50f;
    public int maxBounces = 5;

    [Tooltip("Layer của các gương (cố định + xoay được)")]
    public LayerMask mirrorMask;

    [Tooltip("Layer của TẤT CẢ vật cản laser có thể va vào: tường + gương + cảm biến")]
    public LayerMask blockMask;

    [Header("Visual")]
    public LineRenderer laser;
    public GameObject hitEffect; // hiệu ứng tại điểm laser dừng lại (tường hoặc cảm biến)

    private readonly List<Vector3> points = new List<Vector3>();
    private LaserSensor currentSensor;

    void Reset()
    {
        laser = GetComponent<LineRenderer>();
    }

    void Update()
    {
        CastLaser();
    }

    void CastLaser()
    {
        points.Clear();

        Vector3 currentPos = transform.position;
        Vector3 currentDir = transform.forward;
        points.Add(currentPos);

        float remainingLength = maxLength;
        LaserSensor sensorHitThisFrame = null;
        bool stopped = false;

        for (int i = 0; i < maxBounces && !stopped; i++)
        {
            if (Physics.Raycast(currentPos, currentDir, out RaycastHit hit, remainingLength, blockMask))
            {
                points.Add(hit.point);
                remainingLength -= Vector3.Distance(currentPos, hit.point);

                LaserSensor sensor = hit.collider.GetComponent<LaserSensor>();
                bool isMirror = ((1 << hit.collider.gameObject.layer) & mirrorMask) != 0;

                if (sensor != null)
                {
                    sensorHitThisFrame = sensor;
                    PlaceHitEffect(hit.point, hit.normal);
                    stopped = true;
                }
                else if (isMirror)
                {
                    // Phản xạ và tiếp tục bắn từ điểm này
                    currentDir = Vector3.Reflect(currentDir, hit.normal);
                    currentPos = hit.point;
                }
                else
                {
                    // Trúng tường / vật cản thường -> dừng
                    PlaceHitEffect(hit.point, hit.normal);
                    stopped = true;
                }
            }
            else
            {
                // Không trúng gì, đi hết tầm bắn
                points.Add(currentPos + currentDir * remainingLength);
                stopped = true;
            }
        }

        // Cập nhật trạng thái cảm biến (bật cảm biến mới, tắt cảm biến cũ nếu không còn trúng)
        if (currentSensor != null && currentSensor != sensorHitThisFrame)
            currentSensor.SetHit(false);
        if (sensorHitThisFrame != null)
            sensorHitThisFrame.SetHit(true);
        currentSensor = sensorHitThisFrame;

        laser.positionCount = points.Count;
        laser.SetPositions(points.ToArray());
    }

    void PlaceHitEffect(Vector3 pos, Vector3 normal)
    {
        if (hitEffect == null) return;
        hitEffect.transform.position = pos;
        hitEffect.transform.rotation = Quaternion.LookRotation(normal);
    }
}