using InfimaGames.LowPolyShooterPack;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrontierStrike
{
    /// <summary>
    /// Adds the <see cref="GameManager"/> to every loaded level that has a player character,
    /// so the game loop works without editing the scenes.
    /// </summary>
    public static class GameBootstrap
    {
        // Scenes that keep their original sandbox behaviour (no waves, no menus).
        private static readonly string[] ExcludedScenes = { "S_Content_Overview" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
                return;
            foreach (string excluded in ExcludedScenes)
                if (scene.name == excluded)
                    return;

            if (Object.FindFirstObjectByType<GameManager>() != null)
                return;
            if (Object.FindFirstObjectByType<CharacterBehaviour>() == null)
                return;

            new GameObject("Game Manager").AddComponent<GameManager>();
        }
    }
}
