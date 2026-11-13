using System.Collections;
using System.Collections.Generic;
using InfimaGames.LowPolyShooterPack;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FrontierStrike
{
    /// <summary>
    /// Frontier Strike game loop: briefing screen, waves of enemy soldiers, intermissions,
    /// scoring, pause, and win/lose. Created automatically by <see cref="GameBootstrap"/>
    /// in any level that contains the player character.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public enum GameState { Briefing, Intermission, Combat, Paused, GameOver, Victory }

        public const int TotalWaves = 5;
        private const float IntermissionSeconds = 8.0f;
        private const float SpawnInterval = 1.2f;
        private const float SpawnMinDistance = 22.0f;
        private const float SpawnMaxDistance = 48.0f;
        private const string BestScoreKey = "FrontierStrike.BestScore";

        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; }
        public CharacterBehaviour Player { get; private set; }
        public PlayerHealth PlayerHealth { get; private set; }
        public int Score { get; private set; }
        public int Wave { get; private set; }

        private GameHUD hud;
        private RuntimeNavMesh navMesh;
        private readonly List<EnemySoldier> alive = new List<EnemySoldier>();
        private int toSpawn;
        private float nextSpawnTime;
        private float intermissionEnd;
        private GameState stateBeforePause;
        private int kills, headshots;
        private float combatStartTime;
#pragma warning disable 0414 // Only read in WebGL builds.
        private bool cursorWasLocked;
#pragma warning restore 0414
        private AudioClip enemyFireClip;

        /// <summary>Gunshot sound for enemies, borrowed from the player's first weapon.</summary>
        public AudioClip EnemyFireClip
        {
            get
            {
                if (enemyFireClip == null && Player != null)
                {
                    WeaponBehaviour weapon = Player.GetInventory() != null ? Player.GetInventory().GetEquipped() : null;
                    WeaponAttachmentManagerBehaviour attachments = weapon != null ? weapon.GetAttachmentManager() : null;
                    MuzzleBehaviour muzzle = attachments != null ? attachments.GetEquippedMuzzle() : null;
                    if (muzzle != null)
                        enemyFireClip = muzzle.GetAudioClipFire();
                }
                return enemyFireClip;
            }
        }

        private bool NavMeshReady => navMesh != null && navMesh.IsDone && navMesh.IsValid;

        #region Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            Player = FindFirstObjectByType<CharacterBehaviour>();
            if (Player == null)
            {
                Debug.LogWarning("[Frontier Strike] No player character in the scene; game loop disabled.");
                Destroy(gameObject);
                return;
            }

            PlayerHealth = Player.GetComponent<PlayerHealth>();
            if (PlayerHealth == null)
                PlayerHealth = Player.gameObject.AddComponent<PlayerHealth>();
            PlayerHealth.Damaged += OnPlayerDamaged;
            PlayerHealth.Died += OnPlayerDied;

            DisableStrayCameras();

            hud = GameHUD.Create();
            hud.StartClicked += BeginGame;
            hud.ResumeClicked += Resume;
            hud.RestartClicked += Restart;
            hud.QuitClicked += Quit;
            hud.SetHealth(PlayerHealth.Current, PlayerHealth.Max);
            hud.SetScore(0);
            hud.SetWave(0, TotalWaves);
            hud.SetEnemies("");

            navMesh = new RuntimeNavMesh();
            (Vector3 center, float size) = RuntimeNavMesh.GetLevelArea(Player.transform.position);
            navMesh.BuildAsync(center, size);

            EnterBriefing();
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            Instance = null;
            navMesh?.Dispose();
            Time.timeScale = 1.0f;
        }

        private void Update()
        {
            if (Player == null)
                return;

            hud.SetHealth(PlayerHealth.Current, PlayerHealth.Max);

            switch (State)
            {
                case GameState.Briefing:
                    KeepMenuCursor();
                    Time.timeScale = 0.0f;
                    bool ready = navMesh.IsDone;
                    hud.SetStartReady(ready, ready ? (NavMeshReady ? "" : "Navigation unavailable, enemies will use direct routes.") : "Preparing the battlefield...");
                    break;

                case GameState.Combat:
                case GameState.Intermission:
                    if (CheckPauseRequested())
                        break;
                    if (State == GameState.Combat)
                        UpdateCombat();
                    else
                        UpdateIntermission();
                    break;

                case GameState.Paused:
                    Time.timeScale = 0.0f;
                    // Esc toggles the cursor back to locked in Character, which means "resume".
                    if (Player.IsCursorLocked())
                        Resume();
                    break;

                case GameState.GameOver:
                case GameState.Victory:
                    KeepMenuCursor();
                    break;
            }
        }

        #endregion

        #region States

        private void EnterBriefing()
        {
            State = GameState.Briefing;
            PlayerHealth.Invulnerable = true;
            hud.SetHudVisible(false);
            hud.ShowStart(PlayerPrefs.GetInt(BestScoreKey, 0));
            KeepMenuCursor();
        }

        private void BeginGame()
        {
            if (State != GameState.Briefing)
                return;
            hud.HideStart();
            hud.SetHudVisible(true);
            Time.timeScale = 1.0f;
            Player.SetCursorLocked(true);
            cursorWasLocked = false;
            PlayerHealth.Invulnerable = false;
            combatStartTime = Time.time;
            StartWave(1);
        }

        private void StartWave(int wave)
        {
            Wave = wave;
            toSpawn = 4 + 3 * (wave - 1);
            nextSpawnTime = Time.time + 1.5f;
            State = GameState.Combat;
            hud.SetWave(Wave, TotalWaves);
            hud.ShowBanner($"WAVE {wave}", wave == TotalWaves ? $"Final wave: {toSpawn} hostiles inbound" : $"{toSpawn} hostiles inbound");
        }

        private void UpdateCombat()
        {
            alive.RemoveAll(e => e == null || e.IsDead);

            int maxAlive = Mathf.Min(3 + Wave, 8);
            if (toSpawn > 0 && alive.Count < maxAlive && Time.time >= nextSpawnTime)
            {
                if (TrySpawnEnemy())
                    toSpawn--;
                nextSpawnTime = Time.time + SpawnInterval;
            }

            hud.SetEnemies($"HOSTILES  {toSpawn + alive.Count}");

            if (toSpawn == 0 && alive.Count == 0)
                OnWaveCleared();
        }

        private void OnWaveCleared()
        {
            int bonus = 250 * Wave;
            AddScore(bonus);

            if (Wave >= TotalWaves)
            {
                hud.ShowBanner("BASE SECURED", $"Wave bonus +{bonus}", 3.0f);
                StartCoroutine(EndAfter(true, 2.5f));
                State = GameState.Intermission;
                intermissionEnd = float.PositiveInfinity;
                return;
            }

            hud.ShowBanner($"WAVE {Wave} CLEARED", $"Wave bonus +{bonus}");
            State = GameState.Intermission;
            intermissionEnd = Time.time + IntermissionSeconds;
        }

        private void UpdateIntermission()
        {
            if (float.IsPositiveInfinity(intermissionEnd))
            {
                hud.SetEnemies("");
                return;
            }

            float remaining = intermissionEnd - Time.time;
            hud.SetEnemies($"NEXT WAVE IN  {Mathf.CeilToInt(Mathf.Max(0, remaining))}");
            if (remaining <= 0.0f)
                StartWave(Wave + 1);
        }

        private bool CheckPauseRequested()
        {
            bool locked = Player.IsCursorLocked();

#if UNITY_WEBGL && !UNITY_EDITOR
            // The browser can release pointer lock on its own (Esc never reaches Unity), so watch the real state.
            if (Cursor.lockState == CursorLockMode.Locked)
                cursorWasLocked = true;
            else if (cursorWasLocked && locked)
            {
                Player.SetCursorLocked(false);
                locked = false;
            }
#endif

            if (locked)
                return false;
            Pause();
            return true;
        }

        private void Pause()
        {
            stateBeforePause = State;
            State = GameState.Paused;
            Time.timeScale = 0.0f;
            hud.ShowPause(true);
        }

        private void Resume()
        {
            if (State != GameState.Paused)
                return;
            hud.ShowPause(false);
            State = stateBeforePause;
            Time.timeScale = 1.0f;
            Player.SetCursorLocked(true);
            cursorWasLocked = false;
        }

        private IEnumerator EndAfter(bool victory, float realtimeDelay)
        {
            if (!victory)
                Time.timeScale = 0.3f;
            yield return new WaitForSecondsRealtime(realtimeDelay);
            EndGame(victory);
        }

        private void EndGame(bool victory)
        {
            State = victory ? GameState.Victory : GameState.GameOver;
            Time.timeScale = 0.0f;
            PlayerHealth.Invulnerable = true;
            KeepMenuCursor();

            int best = PlayerPrefs.GetInt(BestScoreKey, 0);
            bool newBest = Score > best;
            if (newBest)
            {
                PlayerPrefs.SetInt(BestScoreKey, Score);
                PlayerPrefs.Save();
            }

            int wavesSurvived = victory ? TotalWaves : Mathf.Max(0, Wave - 1);
            float seconds = Time.time - combatStartTime;
            string stats =
                $"SCORE  <b>{Score:N0}</b>{(newBest ? "   <color=#FFB82E>NEW BEST</color>" : "")}\n" +
                $"Waves cleared  {wavesSurvived} / {TotalWaves}\n" +
                $"Kills  {kills}     Headshots  {headshots}\n" +
                $"Time  {(int)(seconds / 60):00}:{(int)(seconds % 60):00}";

            hud.SetHudVisible(false);
            hud.ShowEnd(victory, stats);
        }

        private void Restart()
        {
            Time.timeScale = 1.0f;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
                SceneManager.LoadScene(scene.buildIndex);
            else
                SceneManager.LoadScene(scene.name);
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>While a menu is up, keep the cursor free (Esc would otherwise re-lock it).</summary>
        private void KeepMenuCursor()
        {
            if (Player.IsCursorLocked() || Cursor.lockState != CursorLockMode.None)
                Player.SetCursorLocked(false);
        }

        #endregion

        #region Events

        private void OnPlayerDamaged(DamageInfo info)
        {
            Vector3 source = info.Source != null ? info.Source.transform.position : info.Point - info.Direction * 10.0f;
            hud.ShowDamage(source, info.Amount);
        }

        private void OnPlayerDied()
        {
            // Switch state right away so input stops and Esc can't open the pause menu over the death screen.
            State = GameState.GameOver;
            KeepMenuCursor();
            hud.ShowBanner("K.I.A.", "", 2.0f);
            StartCoroutine(EndAfter(false, 1.6f));
        }

        public void OnEnemyHit(EnemySoldier enemy, bool headshot, bool killed)
        {
            hud.ShowHitMarker(killed, headshot);
        }

        public void OnEnemyKilled(EnemySoldier enemy, bool headshot)
        {
            kills++;
            int points = 100;
            if (headshot)
            {
                headshots++;
                points += 50;
            }
            AddScore(points);
            hud.ShowPopup(headshot ? $"+{points}  HEADSHOT" : $"+{points}", headshot ? new Color(1.0f, 0.72f, 0.18f) : Color.white);

            float dropChance = PlayerHealth.Normalized < 0.5f ? 0.35f : 0.18f;
            if (Random.value < dropChance)
                HealthPickup.Spawn(enemy.transform.position);
        }

        public void OnPickupCollected(Vector3 position)
        {
            hud.ShowPopup("+30 HEALTH", new Color(0.42f, 0.85f, 0.38f));
        }

        private void AddScore(int amount)
        {
            Score += amount;
            hud.SetScore(Score);
        }

        #endregion

        #region Spawning

        private bool TrySpawnEnemy()
        {
            if (!FindSpawnPoint(out Vector3 position))
                return false;

            Vector3 toPlayer = Player.transform.position - position;
            toPlayer.y = 0.0f;
            Quaternion rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
            EnemySoldier enemy = EnemySoldier.Spawn(position, rotation, EnemyStats.ForWave(Wave), NavMeshReady);
            alive.Add(enemy);
            return true;
        }

        /// <summary>
        /// Picks a spawn point in a ring around the player, preferring spots the player cannot see
        /// and (with a NavMesh) spots that have a walkable path to the player.
        /// </summary>
        private bool FindSpawnPoint(out Vector3 position)
        {
            Vector3 playerPos = Player.transform.position;
            Camera cam = Player.GetCameraWorld();
            Vector3 eye = cam != null ? cam.transform.position : playerPos + Vector3.up * 1.6f;

            NavMeshHit playerNav = default;
            bool hasPlayerNav = NavMeshReady && NavMesh.SamplePosition(playerPos, out playerNav, 3.0f, NavMesh.AllAreas);
            var path = new NavMeshPath();

            Vector3? visibleFallback = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(SpawnMinDistance, SpawnMaxDistance);
                Vector3 candidate = playerPos + new Vector3(circle.x, 0.0f, circle.y);

                if (NavMeshReady)
                {
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4.0f, NavMesh.AllAreas))
                        continue;
                    candidate = hit.position;
                    if (hasPlayerNav)
                    {
                        if (!NavMesh.CalculatePath(candidate, playerNav.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                            continue;
                    }
                }
                else if (!FindGroundWithoutNavMesh(candidate, out candidate))
                    continue;

                if (IsVisibleFrom(eye, candidate + Vector3.up * 1.6f))
                {
                    visibleFallback ??= candidate;
                    continue;
                }

                position = candidate;
                return true;
            }

            position = visibleFallback ?? default;
            return visibleFallback.HasValue;
        }

        private static bool IsVisibleFrom(Vector3 eye, Vector3 point)
        {
            Vector3 dir = point - eye;
            const int mask = ~((1 << 2) | (1 << 9) | (1 << 10) | (1 << 15));
            return !Physics.Raycast(eye, dir.normalized, dir.magnitude, mask, QueryTriggerInteraction.Ignore);
        }

        private static bool FindGroundWithoutNavMesh(Vector3 candidate, out Vector3 ground)
        {
            ground = candidate;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                Vector3 tp = terrain.transform.position;
                Vector3 ts = terrain.terrainData.size;
                if (candidate.x < tp.x + 5 || candidate.z < tp.z + 5 || candidate.x > tp.x + ts.x - 5 || candidate.z > tp.z + ts.z - 5)
                    return false;
            }

            if (!Physics.Raycast(candidate + Vector3.up * 80.0f, Vector3.down, out RaycastHit hit, 200.0f, ~((1 << 2) | (1 << 15)), QueryTriggerInteraction.Ignore))
                return false;
            // Only spawn on roughly flat ground, with room for a soldier.
            if (hit.normal.y < 0.8f)
                return false;
            Vector3 bottom = hit.point + Vector3.up * 0.4f;
            if (Physics.CheckCapsule(bottom, bottom + Vector3.up * 1.2f, 0.35f, ~((1 << 2) | (1 << 15)), QueryTriggerInteraction.Ignore))
                return false;
            ground = hit.point;
            return true;
        }

        #endregion

        /// <summary>
        /// The military base demo scene still contains the pack's fly-through camera. Turn off any
        /// camera that is not the player's so it can't override the first-person view.
        /// </summary>
        private void DisableStrayCameras()
        {
            foreach (Camera cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.targetTexture != null || cam.transform.IsChildOf(Player.transform))
                    continue;
                cam.enabled = false;
                if (cam.TryGetComponent(out AudioListener listener))
                    listener.enabled = false;
                if (cam.TryGetComponent(out Animator animator))
                    animator.enabled = false;
                Debug.Log($"[Frontier Strike] Disabled non-player camera '{cam.name}'.");
            }
        }
    }
}
