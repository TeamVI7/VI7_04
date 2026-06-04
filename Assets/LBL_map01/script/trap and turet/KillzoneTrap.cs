using UnityEngine;
using UnityEngine.SceneManagement;

public class KillzoneTrap : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        else if (other.CompareTag("Enemy"))
        {
            Destroy(other.gameObject);
        }
    }
}