using UnityEngine;
using TMPro;

[RequireComponent(typeof(Collider))]
public class CardItem : MonoBehaviour
{
    [Header("Dữ liệu thẻ")]
    public string cardId = "card_vip";
    public string cardDisplayName = "Thẻ VIP";      // tên hiện trên UI

    [Header("Âm thanh & Hiệu ứng")]
    public AudioClip pickupSound;
    public GameObject pickupEffectPrefab;           // particle khi nhặt (tuỳ chọn)

    [Header("Tooltip 3D (World Space)")]
    public GameObject tooltipRoot;                  // object UI nổi trên đầu thẻ
public TMP_Text tooltipText;
    public KeyCode interactKey = KeyCode.E;

    private bool playerInRange = false;
    private PlayerCardHolder playerCardHolder;
    private AudioSource audioSource;

    void Start()
    {
        // Collider phải là Trigger
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        // Ẩn tooltip lúc đầu
        if (tooltipRoot) tooltipRoot.SetActive(false);
        if (tooltipText) tooltipText.text = $"[E] Nhặt {cardDisplayName}";
    }

    void Update()
    {
        // Tooltip luôn nhìn về camera
        if (tooltipRoot && tooltipRoot.activeSelf && Camera.main != null)
            tooltipRoot.transform.LookAt(Camera.main.transform);

        // Nhấn E khi ở gần
        if (playerInRange && Input.GetKeyDown(interactKey))
            Pickup();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        playerCardHolder = other.GetComponent<PlayerCardHolder>();
        if (tooltipRoot) tooltipRoot.SetActive(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        playerCardHolder = null;
        if (tooltipRoot) tooltipRoot.SetActive(false);
    }

    void Pickup()
    {
        if (playerCardHolder == null) return;

        playerCardHolder.AddCard(cardId);

        // Âm thanh
        if (pickupSound != null)
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);

        // Hiệu ứng particle
        if (pickupEffectPrefab != null)
            Instantiate(pickupEffectPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}