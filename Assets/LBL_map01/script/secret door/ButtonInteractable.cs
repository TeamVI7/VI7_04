using UnityEngine;
using TMPro;

public class ButtonInteractable : MonoBehaviour
{
    [Header("Tham chiếu")]
    public SecretDoorController secretDoor;      // Kéo script cửa bí mật vào đây (CÙNG 1 cửa với phía bên kia)
    public TMP_Text hintText;                    // Text "Nhấn F" (TextMeshPro)
    public Renderer buttonRenderer;               // Renderer của cái nút

    [Header("Cài đặt tương tác")]
    public float interactRange = 3f;
    public KeyCode interactKey = KeyCode.F;
    public LayerMask interactLayer;               // Layer chứa cái nút

    [Header("Hiệu ứng sáng vàng")]
    public Color glowColor = Color.yellow;
    public float glowIntensity = 2f;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock mpb;

    [Header("Âm thanh & VFX")]
    public AudioSource audioSource;
    public AudioClip pressSound;
    public ParticleSystem pressVFX;

    private Camera cam;
    private bool isLookingAtButton = false;
    private bool isUsed = false;

    void Start()
    {
        cam = Camera.main;
        mpb = new MaterialPropertyBlock();

        if (hintText != null)
            hintText.enabled = false;

        if (buttonRenderer != null)
            buttonRenderer.material.EnableKeyword("_EMISSION");
    }

    void Update()
    {
        // Nếu cửa đã được mở từ phía bên kia (sách), tự vô hiệu hoá nút luôn
        if (isUsed || (secretDoor != null && secretDoor.IsOpen))
        {
            if (!isUsed)
            {
                SetGlow(false);
                if (hintText != null)
                    hintText.enabled = false;
            }
            return;
        }

        if (PauseMenuController.IsPaused) return;   // Update still runs at timeScale 0

        CheckLookAtButton();

        if (isLookingAtButton && Input.GetKeyDown(interactKey))
        {
            PressButton();
        }
    }

    void CheckLookAtButton()
    {
        // The player camera lives on the DontDestroyOnLoad root, so it is not guaranteed to
        // exist yet when this scene's Start runs — re-resolve instead of dereferencing null
        // every frame.
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)); // tia từ giữa màn hình
        bool nowLooking = false;

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactLayer))
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                nowLooking = true;
        }

        if (nowLooking != isLookingAtButton)
        {
            isLookingAtButton = nowLooking;
            SetGlow(isLookingAtButton);

            if (hintText != null)
                hintText.enabled = isLookingAtButton;
        }
    }

    void SetGlow(bool isOn)
    {
        if (buttonRenderer == null) return;

        buttonRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(EmissionColor, isOn ? glowColor * glowIntensity : Color.black);
        buttonRenderer.SetPropertyBlock(mpb);
    }

    void PressButton()
    {
        isUsed = true;

        SetGlow(false);
        if (hintText != null)
            hintText.enabled = false;

        if (audioSource != null && pressSound != null)
            audioSource.PlayOneShot(pressSound);

        if (pressVFX != null)
            pressVFX.Play();

        if (secretDoor != null)
            secretDoor.OpenDoor();
    }
}