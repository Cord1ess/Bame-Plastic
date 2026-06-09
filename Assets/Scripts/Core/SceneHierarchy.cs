using UnityEngine;

/// Keeps the runtime hierarchy tidy. Lots of systems spawn GameObjects at play start (HUD canvases,
/// GameInput, the passenger pool, audio, road tiles…). Instead of dumping them at the scene root, each
/// spawner routes its object through SceneHierarchy.Parent(go, Category) and it lands under a named
/// folder — "UI", "Logic", or "World" — created on demand.
///
/// Folders are plain empty GameObjects at the scene root. Cheap, editor-only-visible organisation; no
/// behaviour. Safe to call before/after the folders exist (they're lazily created).
public static class SceneHierarchy
{
    public enum Category { UI, Logic, World }

    static Transform _ui, _logic, _world;

    /// Reparent `go` under the folder for `category` (creating the folder if needed). Keeps world position.
    public static void Parent(GameObject go, Category category)
    {
        if (go == null) return;
        Transform folder = FolderFor(category);
        go.transform.SetParent(folder, true);
    }

    public static Transform FolderFor(Category category)
    {
        switch (category)
        {
            case Category.UI:    return _ui    != null ? _ui    : (_ui    = MakeFolder("UI"));
            case Category.Logic: return _logic != null ? _logic : (_logic = MakeFolder("Logic"));
            default:             return _world != null ? _world : (_world = MakeFolder("World"));
        }
    }

    static Transform MakeFolder(string name)
    {
        // reuse one if it already exists (e.g. after additive loads), else create at the scene root
        GameObject existing = GameObject.Find(name);
        if (existing != null && existing.transform.parent == null) return existing.transform;
        var go = new GameObject(name);
        return go.transform;
    }
}
