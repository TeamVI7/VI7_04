// ============================================================
//  DamagePopup.cs  —  Out of Bullet
//  Gắn trên Bleeding_Effect_Canvas prefab (World Space Canvas)
//  FIX: Destroy transform.root để xóa toàn bộ prefab
// ============================================================
using UnityEngine;
using TMPro;

public class DamagePopup : MonoBehaviour
{
    [Header("Settings")]
    public float LifeTime   = 0.8f;
    public float MoveYSpeed = 2f;
    public float FadeStart  = 0.4f;

    private TextMeshProUGUI _tmp;
    private float           _timer;

    private void Awake()
    {
        _tmp = GetComponentInChildren<TextMeshProUGUI>();
        if (_tmp == null)
            Debug.LogError("[DamagePopup] Không tìm thấy TextMeshProUGUI!");
    }

    public void Setup(int damageAmount)
    {
        if (_tmp == null) return;

        _tmp.text  = $"-{damageAmount}";
        var c      = _tmp.color;
        c.a        = 1f;
        _tmp.color = c;
        _timer     = LifeTime;

        // FIX: Destroy root thay vì gameObject để xóa toàn bộ prefab
        Destroy(transform.root.gameObject, LifeTime + 0.1f);
    }

    private void Update()
    {
        transform.position += Vector3.up * MoveYSpeed * Time.deltaTime;

        if (Camera.main != null)
            transform.forward = Camera.main.transform.forward;

        _timer -= Time.deltaTime;

        if (_timer <= FadeStart && _tmp != null)
        {
            var c      = _tmp.color;
            c.a        = Mathf.Clamp01(_timer / FadeStart);
            _tmp.color = c;
        }

        // FIX: Destroy root thay vì gameObject
        if (_timer <= 0f)
            Destroy(transform.root.gameObject);
    }
}