using UnityEngine;

/// <summary>
/// Gắn vào mỗi khối server.
/// Player lại gần + bấm E + có SSD trong tay → gắn SSD vào.
/// Khi đủ SSD → báo cho ServerMinigameManager.
/// </summary>
public class ServerBlock : MonoBehaviour
{
    [Header("Settings")]
    public float interactRange = 2f;

    [Header("Visual")]
    public Renderer blockRenderer;
    public Color emptyColor  = new Color(0.2f, 0.2f, 0.2f);
    public Color filledColor = new Color(0.0f, 1.0f, 0.4f);

    [Header("Hint")]
    public GameObject interactHint; // "[E] Gắn SSD"
    public GameObject noSSDHint;    // "[!] Cần SSD"

    public bool IsFilled { get; private set; } = false;

    private PlayerInventory       _inventory;
    private ServerMinigameManager _manager;
    private bool                  _playerNearby = false;

    private void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) _inventory = p.GetComponent<PlayerInventory>();

        _manager = FindFirstObjectByType<ServerMinigameManager>();

        SetColor(emptyColor);
        if (interactHint) interactHint.SetActive(false);
        if (noSSDHint)    noSSDHint.SetActive(false);
    }

    private void Update()
{
    if (IsFilled) return;

    bool hasSSD = _inventory != null && _inventory.HasSSD;
    Debug.Log($"[Server] nearby={_playerNearby}, hasSSD={hasSSD}, inv={_inventory}");

    if (interactHint) interactHint.SetActive(_playerNearby && hasSSD);
    if (noSSDHint)    noSSDHint.SetActive(_playerNearby && !hasSSD);

    if (_playerNearby && hasSSD && Input.GetKeyDown(KeyCode.E))
        InsertSSD();
}

   private void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("Player"))
    {
        _playerNearby = true;
        _inventory = other.GetComponent<PlayerInventory>(); // lấy trực tiếp từ collider
        Debug.Log($"[Server] Player vào gần {gameObject.name}, inv={_inventory}");
    }
}

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerNearby = false;
            if (interactHint) interactHint.SetActive(false);
            if (noSSDHint)    noSSDHint.SetActive(false);
        }
    }

    private void InsertSSD()
    {
        if (_inventory == null || !_inventory.UseSSD()) return;

        IsFilled = true;
        SetColor(filledColor);

        if (interactHint) interactHint.SetActive(false);
        if (noSSDHint)    noSSDHint.SetActive(false);

        Debug.Log($"[Server] {gameObject.name} đã gắn SSD!");
        _manager?.CheckAllFilled();
    }

    private void SetColor(Color c)
    {
        if (blockRenderer == null) return;
        var mpb = new MaterialPropertyBlock();
        blockRenderer.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        mpb.SetColor("_Color", c);
        blockRenderer.SetPropertyBlock(mpb);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsFilled ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}