using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives in the Bootstrap scene only. Hands off to the real first scene
/// (usually your main menu) right after Bootstrap finishes loading.
///
/// Bootstrap itself should contain nothing but persistent singletons
/// (LoadingScreenController, MenuAudio, BulletCasingPool, etc.) — there's
/// nothing worth showing a progress bar for here, so this is a plain
/// synchronous SceneManager.LoadScene call, not routed through the
/// loading screen.
///
/// SETUP:
///   1. Create an empty GameObject in the Bootstrap scene, name it "BootstrapLoader".
///   2. Attach this script.
///   3. Set firstSceneName to your menu scene's exact name (Build Settings).
/// </summary>
public class BootstrapLoader : MonoBehaviour
{
    [Tooltip("Exact scene name to load right after Bootstrap finishes — usually your main menu.")]
    public string firstSceneName = "MainMenu";

    private void Start()
    {
        if (string.IsNullOrEmpty(firstSceneName))
        {
            Debug.LogError("[BootstrapLoader] firstSceneName is empty — nothing to load.", this);
            return;
        }

        SceneManager.LoadScene(firstSceneName);
    }
}