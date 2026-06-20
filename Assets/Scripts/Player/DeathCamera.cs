using UnityEngine;
using DG.Tweening;

public class DeathCamera : MonoBehaviour
{
    [SerializeField] GameObject    player;
    [SerializeField] Camera        cam;
    [SerializeField] MonoBehaviour mouseLook;
    [SerializeField] float         colliderRadius = 0.15f;
    [SerializeField] float         rollTorque     = 3f;
    [SerializeField] float         fadeDelay      = 2f;
    [SerializeField] CanvasGroup   deathFade;

    // FIX: Start/OnDestroy — stays subscribed even when camera GameObject is disabled.
    // OnEnable/OnDisable was wrong: ComputerInteraction disables the camera GO,
    // which fired OnDisable and silently unsubscribed from OnDied.
    void Start()     => PlayerHealth.OnDied += Play;
    void OnDestroy() => PlayerHealth.OnDied -= Play;

    void Play()
    {
        if (!cam) cam = Camera.main;

        // FIX: Re-enable camera in case ComputerInteraction had it disabled.
        if (cam != null && !cam.gameObject.activeSelf)
            cam.gameObject.SetActive(true);

        if (mouseLook) mouseLook.enabled = false;

        cam.transform.SetParent(null);

        var rb = cam.gameObject.AddComponent<Rigidbody>();
        var sc = cam.gameObject.AddComponent<SphereCollider>();
        sc.radius = colliderRadius;

        var playerRb = player ? player.GetComponent<Rigidbody>() : null;
        if (playerRb) rb.linearVelocity = playerRb.linearVelocity;

        rb.angularVelocity = cam.transform.right * rollTorque;

        if (player) player.SetActive(false);

        if (deathFade)
            deathFade.DOFade(1f, 0.8f).SetDelay(fadeDelay);
    }
}