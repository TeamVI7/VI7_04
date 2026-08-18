using System.Collections;
using UnityEngine;

public class DebugTeleport : MonoBehaviour
{
    public Transform target;
    public Vector3 targetPos;
    public bool useTransform = true;
    public bool copyRotation = true;
    public KeyCode teleportKey = KeyCode.F9;
    public Rigidbody body;
    public CharacterController controller;   // optional, for non-Rigidbody movers

    void Reset()
    {
        body = GetComponent<Rigidbody>();
        controller = GetComponent<CharacterController>();
    }

    void Awake()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (controller == null) controller = GetComponent<CharacterController>();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void Update()
    {
        if (Input.GetKeyDown(teleportKey))
        {
            Teleport();
        }
    }
#endif

    void Teleport()
    {
        bool fromTarget = useTransform && target != null;
        Vector3 pos = fromTarget ? target.position : targetPos;
        Quaternion rot = fromTarget && copyRotation ? target.rotation : transform.rotation;

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = pos;
            body.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);

            if (body.interpolation != RigidbodyInterpolation.None)
                StartCoroutine(SuppressInterpolation());
        }
        else if (controller != null)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            controller.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(pos, rot);
        }

        Debug.Log($"[Teleport] {gameObject.name} -> {pos}");
    }

    // Interpolation would lerp the visual from the old pose to the new one,
    // smearing the player across the map for a frame. Mute it for one step.
    IEnumerator SuppressInterpolation()
    {
        RigidbodyInterpolation prev = body.interpolation;
        body.interpolation = RigidbodyInterpolation.None;
        yield return new WaitForFixedUpdate();
        body.interpolation = prev;
    }
}
