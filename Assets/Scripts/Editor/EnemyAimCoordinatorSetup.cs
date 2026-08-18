using UnityEditor;
using UnityEngine;

// One-click fixup: RequireComponent only auto-adds when a component is freshly
// added via AddComponent — it doesn't retroactively touch prefabs that already
// had EnemyBrain before EnemyAimCoordinator existed. Run this once after adding
// the aim-ownership fix, and again any time a new enemy prefab is missing it.
public static class EnemyAimCoordinatorSetup
{
    [MenuItem("Tools/Enemies/Add Missing EnemyAimCoordinator")]
    private static void AddMissing()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int added = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            if (prefab.GetComponent<EnemyBrain>() == null) continue;
            if (prefab.GetComponent<EnemyAimCoordinator>() != null) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            root.AddComponent<EnemyAimCoordinator>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            added++;
            Debug.Log($"[EnemyAimCoordinatorSetup] Added EnemyAimCoordinator to {path}");
        }

        Debug.Log($"[EnemyAimCoordinatorSetup] Done — added to {added} prefab(s).");
    }

    // Adds EnemyDodgeBehaviour to any prefab that has PatrolBehaviour but not the
    // dodge component yet. Harmless to add broadly — Aggressive-style rushers just
    // never trigger it (gated in EnemyDodgeBehaviour.Update), so it's safe to run
    // across every enemy prefab rather than picking and choosing.
    [MenuItem("Tools/Enemies/Add Missing EnemyDodgeBehaviour")]
    private static void AddMissingDodge()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int added = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            if (prefab.GetComponent<PatrolBehaviour>() == null) continue;
            if (prefab.GetComponent<EnemyDodgeBehaviour>() != null) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            root.AddComponent<EnemyDodgeBehaviour>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            added++;
            Debug.Log($"[EnemyAimCoordinatorSetup] Added EnemyDodgeBehaviour to {path}");
        }

        Debug.Log($"[EnemyAimCoordinatorSetup] Done — added to {added} prefab(s).");
    }

    // Adds EnemyAvengeReaction to any prefab that has EnemyHealth but not the
    // reaction yet. Safe to add broadly — it only ever does something in response
    // to a nearby registered squadmate dying.
    [MenuItem("Tools/Enemies/Add Missing EnemyAvengeReaction")]
    private static void AddMissingAvenge()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int added = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            if (prefab.GetComponent<EnemyHealth>() == null) continue;
            if (prefab.GetComponent<EnemyAvengeReaction>() != null) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            root.AddComponent<EnemyAvengeReaction>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            added++;
            Debug.Log($"[EnemyAimCoordinatorSetup] Added EnemyAvengeReaction to {path}");
        }

        Debug.Log($"[EnemyAimCoordinatorSetup] Done — added to {added} prefab(s).");
    }
}
