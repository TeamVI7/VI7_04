using UnityEngine;

public class Flashlight_Toggle : MonoBehaviour
{
    // Kéo đối tượng Light (Đèn) ở dưới Capsule vào ô này trong Inspector
    public GameObject flashlightLight;

    // Phím dùng để bật/tắt đèn (Mặc định là phím F, có thể đổi trong Inspector)
    public KeyCode toggleKey = KeyCode.E;

    // Trạng thái đèn hiện tại (true = đang bật, false = đang tắt)
    private bool isLightOn = true;

    void Start()
    {
        // Đảm bảo trạng thái thực tế của đèn trùng với biến ban đầu lúc vào game
        if (flashlightLight != null)
        {
            flashlightLight.SetActive(isLightOn);
        }
    }

    void Update()
    {
        // Kiểm tra nếu người chơi nhấn phím F
        if (Input.GetKeyDown(KeyCode.E))
        {
            ToggleLight();
        }
    }

    void ToggleLight()
    {
        if (flashlightLight != null)
        {
            isLightOn = !isLightOn; // Đảo trạng thái (Đang bật -> Tắt, Đang tắt -> Bật)
            flashlightLight.SetActive(isLightOn); // Áp dụng bật/tắt cho GameObject đèn
        }
    }
}