using UnityEngine;

public class LightGridSpawner : MonoBehaviour
{
    [Header("Cấu hình đèn")]
    public GameObject lightPrefab;       // Kéo cái Prefab đèn trần của bạn vào đây

    [Header("Cấu hình số lượng (Ma trận đèn)")]
    public int rows = 10;                // Số lượng hàng đèn dọc (trục X)
    public int columns = 10;             // Số lượng hàng đèn ngang (trục Z)

    [Header("Khoảng cách & Độ cao")]
    public float spacing = 8.0f;         // Khoảng cách giữa các đèn (ví dụ cách nhau 8 mét)
    public float ceilingHeight = 6.0f;   // Độ cao của trần nhà nơi đặt đèn

    void Start()
    {
        SpawnLightGrid();
    }

    void SpawnLightGrid()
    {
        if (lightPrefab == null)
        {
            Debug.LogError("Chưa kéo Prefab đèn vào script LightGridSpawner kìa bạn ơi!");
            return;
        }

        // Vòng lặp tự động rải đèn theo hàng và cột
        for (int x = 0; x < rows; x++)
        {
            for (int z = 0; z < columns; z++)
            {
                // Tính toán vị trí cho từng cái đèn dựa theo vị trí của Object LightManager
                Vector3 spawnPosition = new Vector3(x * spacing, ceilingHeight, z * spacing) + transform.position;

                // Sinh ra đèn tại vị trí đã tính
                GameObject newLight = Instantiate(lightPrefab, spawnPosition, Quaternion.identity);

                // Gom gọn đèn mới vào làm con của LightManager cho sạch bảng Hierarchy
                newLight.transform.SetParent(transform);
            }
        }

        Debug.Log($"==> Đã tự động rải xong {rows * columns} cái đèn lên map!");
    }
}