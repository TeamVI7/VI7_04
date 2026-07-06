using UnityEngine;

public class WeaponPodDoorAnim : MonoBehaviour
{
    [SerializeField] Animator anim;
    bool triggered;
    bool playerInRange;

    void Update()
    {
        if (playerInRange && !triggered && Input.GetKeyDown(KeyCode.F))
        {
            anim.SetTrigger("Open");
            triggered = true;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) playerInRange = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) playerInRange = false;
    }
}