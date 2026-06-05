using UnityEngine;
using DG.Tweening;

public class DeathCamera : MonoBehaviour
{
    [SerializeField] GameObject    player;
    [SerializeField] Camera        cam;
    [SerializeField] MonoBehaviour mouseLook;
    [SerializeField] float         colliderRadius = 0.15f;
    [SerializeField] float         rollTorque     = 3f;
    [SerializeField] float         fadeDelay      = 2f;    // black fade after settle
    [SerializeField] CanvasGroup   deathFade;              // full-screen black CanvasGroup

    void OnEnable()  => PlayerHealth.OnDied += Play;
    void OnDisable() => PlayerHealth.OnDied -= Play;

    void Play()
    {
        if (!cam) cam = Camera.main;
        if (mouseLook) mouseLook.enabled = false;

        // Detach cam from player hierarchy
        cam.transform.SetParent(null);

        // Add physics
        var rb = cam.gameObject.AddComponent<Rigidbody>();
        var sc = cam.gameObject.AddComponent<SphereCollider>();
        sc.radius = colliderRadius;

        // Inherit player velocity so camera carries momentum
        var playerRb = player ? player.GetComponent<Rigidbody>() : null;
        if (playerRb) rb.linearVelocity = playerRb.linearVelocity;

        // Roll torque — tilts camera as it falls
        rb.angularVelocity = cam.transform.right * rollTorque;

        // Disable player
        if (player) player.SetActive(false);

        // Optional: fade to black after delay
        if (deathFade)
            deathFade.DOFade(1f, 0.8f).SetDelay(fadeDelay);
    }
}