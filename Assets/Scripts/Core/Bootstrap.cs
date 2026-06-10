using UnityEngine;

/// Guarantees the persistent SessionContext exists as soon as the game runs, whether you start from the
/// MainMenu scene or hit Play directly in the game scene. No scene object needed.
public static class Bootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SessionContext.Ensure();
    }
}
