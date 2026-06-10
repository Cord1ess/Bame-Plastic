using UnityEngine;
using UnityEngine.SceneManagement;

/// Central scene names + transitions. The game is a SINGLE scene now — it opens as a living menu (see
/// MenuMode) and transitions to gameplay in-place, so there's no menu↔game scene loading. GoToGame remains
/// only as a fallback (e.g. if the menu ever runs outside the game scene).
public static class SceneFlow
{
    public const string Game = "BamePlastic";

    public static void GoToGame()
    {
        SessionContext.Ensure();
        SceneManager.LoadScene(Game);
    }
}
