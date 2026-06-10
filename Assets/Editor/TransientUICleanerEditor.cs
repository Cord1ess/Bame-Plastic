using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Editor companion to TransientUICleaner: runs the orphan sweep automatically whenever a scene is opened in
/// the editor, AND just before a scene is saved — so baked UI/preview junk is removed and can NEVER be saved
/// back in. Self-healing: you never have to manually clean the scene or delete ScriptAssemblies.
[InitializeOnLoad]
public static class TransientUICleanerEditor
{
    static TransientUICleanerEditor()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => SweepLogged("opened");
        EditorSceneManager.sceneSaving += (scene, path) => SweepLogged("saving");   // strip before it's written
    }

    static void SweepLogged(string when)
    {
        int n = TransientUICleaner.Sweep();
        if (n > 0) Debug.Log($"[TransientUICleaner] stripped {n} orphaned UI/preview object(s) from the scene ({when}).");
    }

    [MenuItem("Bame Plastic/Clean Orphaned UI From Scene")]
    static void Manual()
    {
        int n = TransientUICleaner.Sweep();
        Debug.Log($"[TransientUICleaner] stripped {n} orphaned UI/preview object(s).");
    }
}
