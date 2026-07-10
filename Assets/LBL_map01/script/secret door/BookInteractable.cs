using UnityEngine;
using TMPro;

public class BookInteractable : MonoBehaviour
{
    [Header("Tham chiếu")]
    public SecretDoorController secretDoor;      // Kéo script cửa bí mật vào đây
    public TMP_Text hintText;                    // Text "Nhấn F" (TextMeshPro)
    public Renderer bookRenderer;                // Renderer của cuốn sách

    [Header("Cài đặt tương tác")]
    public float interactRange = 3f;
    public KeyCode interactKey = KeyCode.F;
    public LayerMask interactLayer;               // Layer chứa cuốn sách

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
    private bool isLookingAtBook = false;
    private bool isUsed = false;

    void Start()
    {
        cam = Camera.main;
        mpb = new MaterialPropertyBlock();

        if (hintText != null)
            hintText.enabled = false;

        if (bookRenderer != null)
            bookRenderer.material.EnableKeyword("_EMISSION");
    }

    void Update()
    {
        if (isUsed) return;

        CheckLookAtBook();

        if (isLookingAtBook && Input.GetKeyDown(interactKey))
        {
            PressBook();
        }
    }

    void CheckLookAtBook()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)); // tia từ giữa màn hình
        bool nowLooking = false;

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactLayer))
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                nowLooking = true;
        }

        if (nowLooking != isLookingAtBook)
        {
            isLookingAtBook = nowLooking;
            SetGlow(isLookingAtBook);

            if (hintText != null)
                hintText.enabled = isLookingAtBook;
        }
    }

    void SetGlow(bool isOn)
    {
        if (bookRenderer == null) return;

        bookRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(EmissionColor, isOn ? glowColor * glowIntensity : Color.black);
        bookRenderer.SetPropertyBlock(mpb);
    }

    void PressBook()
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