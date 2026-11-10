using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum MissionRuntimeState
{
    Inactive = 0,
    Initializing = 1,
    FightingRegularEnemies = 2,
    FightingBoss = 3,
    Completed = 4,
    Failed = 5,
    AwaitingEntry = 6,
    AwaitingFirstInput = 7
}

public readonly struct MissionResult
{
    public MissionResult(
        MissionDefinition mission,
        int defeatedEnemies,
        BossDefeatProgressionResult progressionResult
    )
    {
        Mission = mission;
        DefeatedEnemies = defeatedEnemies;
        ProgressionResult = progressionResult;
    }

    public MissionDefinition Mission { get; }
    public int DefeatedEnemies { get; }
    public BossDefeatProgressionResult ProgressionResult { get; }
}

public sealed class MissionRuntimeController : MonoBehaviour
{
    [Header("Fallback (used when entering the scene directly)")]
    [SerializeField] private MissionDefinition fallbackMission;
    [SerializeField] private PlayableCharacterDefinition fallbackCharacter;

    [Header("Scene")]
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private Transform bossSpawnPoint;
    [SerializeField] private bool replaceScenePlayers = true;
    [SerializeField] private LayerMask blockedSpawnLayers;
    [SerializeField, Min(1)] private int spawnPositionAttempts = 12;
    [Tooltip("Spawn one of each registered regular enemy type first, within the alive enemy limit.")]
    [SerializeField] private bool spawnEachEnemyTypeInitially;
    [SerializeField] private BattleCameraRig battleCamera;
    [SerializeField] private BattleEntryDirector entryDirector;

    [Header("Optional HUD")]
    [SerializeField] private TMP_Text missionLabel;
    [SerializeField] private TMP_Text killProgressLabel;
    [SerializeField] private TMP_Text stateLabel;
    [SerializeField] private GameOverController gameOverController;
    [SerializeField] private CombatScoreManager scoreManager;
    [SerializeField] private PlayerHealthBar healthBar;

    private readonly Dictionary<Enemy, EnemyDefinition> spawnedEnemies = new();
    private MissionDefinition mission;
    private PlayableCharacterDefinition character;
    private Player player;
    private Coroutine spawnCoroutine;
    private Enemy spawnedBoss;
    private int defeatedEnemies;

    public MissionRuntimeState State { get; private set; } = MissionRuntimeState.Inactive;
    public MissionDefinition Mission => mission;
    public PlayableCharacterDefinition Character => character;
    public Player ActivePlayer => player;
    public CombatScoreManager ScoreManager => scoreManager;
    public int DefeatedEnemies => defeatedEnemies;
    public float Progress => mission != null
        ? Mathf.Clamp01((float)defeatedEnemies / mission.TargetKillCount)
        : 0f;

    public event Action<MissionRuntimeState> StateChanged;
    public event Action<int, int> KillProgressChanged;
    public event Action<MissionResult> MissionCompleted;
    public event Action<Player> MissionFailed;

    private void OnEnable()
    {
        Enemy.Defeated += HandleEnemyDefeated;
        if (player != null)
            player.Died += HandlePlayerDied;
    }

    private void Start()
    {
        // The audio manager survives scene loads; explicitly replace the Lobby track on battle entry.
        BgmManager.Instance.Play(BgmKey.Gameplay);
        InitializeMission();
    }

    private void OnDisable()
    {
        Enemy.Defeated -= HandleEnemyDefeated;
        if (player != null)
            player.Died -= HandlePlayerDied;
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
    }

    public void ReturnToLobby()
    {
        if (gameOverController != null && gameOverController.IsShowing)
        {
            gameOverController.ReturnToLobby();
            return;
        }
        MissionSessionManager.Instance.ClearMission();
        SceneLoadManager.Instance.LoadScene("Lobby");
    }

    private void InitializeMission()
    {
        SetState(MissionRuntimeState.Initializing);
        MissionLaunchRequest request = MissionSessionManager.Instance.CurrentRequest;
        mission = request != null ? request.Mission : fallbackMission;
        character = request != null ? request.Character : fallbackCharacter;

        if (mission == null)
        {
            Debug.LogWarning("No mission launch request or fallback mission was assigned.", this);
            SetState(MissionRuntimeState.Inactive);
            return;
        }

        player = PreparePlayer(character);
        if (player == null)
        {
            Debug.LogError("Mission could not find or create a Player.", this);
            SetState(MissionRuntimeState.Inactive);
            return;
        }

        if (battleCamera == null)
            foreach (BattleCameraRig rig in FindObjectsByType<BattleCameraRig>(FindObjectsSortMode.None))
                if (rig.gameObject.scene == gameObject.scene) { battleCamera = rig; break; }
        if (battleCamera != null) battleCamera.BindPlayer(player);
        BindPlayerBackgrounds(player);
        BindPlayerHealthBar(player);
        player.Died += HandlePlayerDied;
        if (gameOverController == null)
            gameOverController = GetComponent<GameOverController>();
        if (gameOverController == null)
            gameOverController = gameObject.AddComponent<GameOverController>();
        if (scoreManager == null)
            scoreManager = GetComponent<CombatScoreManager>();
        if (scoreManager == null)
            scoreManager = gameObject.AddComponent<CombatScoreManager>();
        gameOverController.SetScoreProvider(scoreManager);
        defeatedEnemies = 0;
        RefreshHud();
        if (SceneLoadManager.IsBattleEntryLoading)
        {
            if (entryDirector == null) entryDirector = GetComponent<BattleEntryDirector>();
            SetState(MissionRuntimeState.AwaitingEntry);
            if (entryDirector == null || !entryDirector.Prepare(player, battleCamera, BeginCombat,
                request?.BattleEntryTransformation))
                Debug.LogError("Battle entry requires a scene BattleCameraRig and BattleEntryDirector.", this);
            return;
        }
        BeginCombat();
    }

    public void MarkAwaitingFirstInput()
    {
        if (State == MissionRuntimeState.AwaitingEntry) SetState(MissionRuntimeState.AwaitingFirstInput);
    }

    private void BeginCombat()
    {
        if (mission == null || player == null || State == MissionRuntimeState.FightingRegularEnemies
            || State == MissionRuntimeState.Failed || State == MissionRuntimeState.Completed) return;
        scoreManager.BeginRun(player);
        SetState(MissionRuntimeState.FightingRegularEnemies);
        spawnCoroutine = StartCoroutine(SpawnRegularEnemies());
    }

    private void HandlePlayerDied(Player deadPlayer)
    {
        if (deadPlayer != player || (State != MissionRuntimeState.FightingRegularEnemies
            && State != MissionRuntimeState.FightingBoss))
            return;

        scoreManager?.EndRun();
        SetState(MissionRuntimeState.Failed);
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
        gameOverController.Show();
        MissionFailed?.Invoke(deadPlayer);
    }

    private Player PreparePlayer(PlayableCharacterDefinition selectedCharacter)
    {
        Player[] scenePlayers = FindObjectsByType<Player>(FindObjectsSortMode.None);
        if (selectedCharacter == null)
        {
            return scenePlayers.Length > 0 ? scenePlayers[0] : null;
        }

        if (selectedCharacter.GameplayPrefab == null
            || selectedCharacter.GameplayPrefab.GetComponentInChildren<Player>(true) == null)
        {
            Debug.LogError($"Selected character '{selectedCharacter.Id}' has no valid gameplay Player prefab.", selectedCharacter);
            return null;
        }

        if (replaceScenePlayers)
        {
            scenePlayers = FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < scenePlayers.Length; i++)
            {
                scenePlayers[i].gameObject.SetActive(false);
            }
        }

        Vector3 position = playerSpawnPoint != null ? playerSpawnPoint.position : Vector3.zero;
        Quaternion rotation = playerSpawnPoint != null ? playerSpawnPoint.rotation : Quaternion.identity;
        GameObject instance = Instantiate(selectedCharacter.GameplayPrefab, position, rotation);
        instance.SetActive(true);
        Player spawnedPlayer = instance.GetComponentInChildren<Player>();
        if (spawnedPlayer == null)
        {
            Debug.LogError(
                $"Character prefab '{selectedCharacter.GameplayPrefab.name}' has no Player component.",
                selectedCharacter
            );
            Destroy(instance);
            return null;
        }

        return spawnedPlayer;
    }

    private void BindPlayerHealthBar(Player activePlayer)
    {
        if (healthBar == null)
            foreach (PlayerHealthBar candidate in FindObjectsByType<PlayerHealthBar>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene)
                { healthBar = candidate; break; }

        // Also supports a newly authored UI before a component was manually attached to it.
        if (healthBar == null)
            foreach (UnityEngine.Canvas canvas in FindObjectsByType<UnityEngine.Canvas>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.gameObject.scene != gameObject.scene)
                    continue;
                Transform mask = canvas.transform.Find("healthBarMask");
                if (mask is RectTransform && mask.Find("health") is RectTransform
                    && mask.Find("healthDelay") is RectTransform)
                {
                    healthBar = mask.GetComponent<PlayerHealthBar>();
                    if (healthBar == null)
                        healthBar = mask.gameObject.AddComponent<PlayerHealthBar>();
                    break;
                }
            }
        if (healthBar != null)
            healthBar.BindPlayer(activePlayer);
    }

    private void BindPlayerBackgrounds(Player activePlayer)
    {
        Camera[] cameras = activePlayer.GetComponentsInChildren<Camera>();
        Camera playerCamera = Array.Find(cameras,
            camera => camera.isActiveAndEnabled && camera.orthographic && camera.CompareTag("MainCamera"));
        if (playerCamera == null)
            playerCamera = Array.Find(cameras, camera => camera.isActiveAndEnabled && camera.orthographic);
        if (battleCamera != null) playerCamera = battleCamera.OutputCamera;
        if (playerCamera == null)
            return;

        HorizontalTiledBackground[] backgrounds = FindObjectsByType<HorizontalTiledBackground>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (HorizontalTiledBackground background in backgrounds)
        {
            if (background.gameObject.scene == activePlayer.gameObject.scene)
                background.SetTargetCamera(playerCamera);
        }
    }

    private IEnumerator SpawnRegularEnemies()
    {
        WaitForSeconds wait = new WaitForSeconds(mission.SpawnInterval);
        if (spawnEachEnemyTypeInitially)
        {
            HashSet<EnemyDefinition> initialTypes = new();
            foreach (EnemySpawnEntry entry in mission.Enemies ?? Array.Empty<EnemySpawnEntry>())
            {
                if (spawnedEnemies.Count >= mission.MaxAliveEnemies)
                    break;
                if (entry.Enemy != null && !entry.Enemy.IsBoss && entry.Enemy.Prefab != null
                    && entry.Weight > 0f && initialTypes.Add(entry.Enemy))
                    SpawnEnemy(entry.Enemy, FindRegularSpawnPosition());
            }
            yield return wait;
        }
        while (State == MissionRuntimeState.FightingRegularEnemies)
        {
            RemoveDestroyedEnemies();
            if (spawnedEnemies.Count < mission.MaxAliveEnemies)
            {
                EnemyDefinition definition = ChooseRegularEnemy();
                if (definition != null)
                {
                    SpawnEnemy(definition, FindRegularSpawnPosition());
                }
            }

            yield return wait;
        }

        spawnCoroutine = null;
    }

    private EnemyDefinition ChooseRegularEnemy()
    {
        EnemySpawnEntry[] entries = mission.Enemies;
        if (entries == null || entries.Length == 0)
        {
            return null;
        }

        float totalWeight = 0f;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Enemy != null && entries[i].Enemy.Prefab != null)
            {
                totalWeight += entries[i].Weight;
            }
        }

        if (totalWeight <= 0f)
        {
            return null;
        }

        float value = UnityEngine.Random.value * totalWeight;
        for (int i = 0; i < entries.Length; i++)
        {
            EnemyDefinition definition = entries[i].Enemy;
            if (definition == null || definition.Prefab == null)
            {
                continue;
            }

            value -= entries[i].Weight;
            if (value <= 0f)
            {
                return definition;
            }
        }

        return entries[entries.Length - 1].Enemy;
    }

    private Vector3 FindRegularSpawnPosition()
    {
        Vector2 center = player != null ? player.transform.position : Vector2.zero;
        for (int i = 0; i < Mathf.Max(1, spawnPositionAttempts); i++)
        {
            Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;
            float distance = UnityEngine.Random.Range(mission.MinSpawnRadius, mission.MaxSpawnRadius);
            Vector2 candidate = center + direction * distance;
            if (blockedSpawnLayers.value == 0
                || Physics2D.OverlapCircle(candidate, 0.5f, blockedSpawnLayers) == null)
            {
                return candidate;
            }
        }

        return center + Vector2.right * mission.MaxSpawnRadius;
    }

    private Enemy SpawnEnemy(EnemyDefinition definition, Vector3 position)
    {
        if (definition == null || definition.Prefab == null)
        {
            return null;
        }

        // Serialized references must resolve to a GameObject containing Enemy, not an obsolete
        // prefab fileID or another asset type. Validate before cloning/registering it.
        UnityEngine.Object prefabReference = definition.Prefab;
        if (prefabReference is not GameObject prefab || prefab.GetComponentInChildren<Enemy>(true) == null)
        {
            Debug.LogError($"Enemy definition '{definition.Id}' needs a valid Enemy prefab root GameObject. Reassign its Prefab field.", definition);
            return null;
        }
        GameObject instance = Instantiate(prefab, position, Quaternion.identity);
        Enemy enemy = instance.GetComponentInChildren<Enemy>();
        if (enemy == null)
        {
            Debug.LogError($"Enemy prefab '{definition.Prefab.name}' has no Enemy component.", definition);
            Destroy(instance);
            return null;
        }

        enemy.SetTarget(player);
        spawnedEnemies[enemy] = definition;
        scoreManager?.RegisterEnemy(enemy);
        return enemy;
    }

    private void HandleEnemyDefeated(Enemy enemy, GameObject killer)
    {
        if (enemy == null || !spawnedEnemies.Remove(enemy, out EnemyDefinition definition))
        {
            return;
        }

        scoreManager?.ReportDefeat(enemy, enemy == spawnedBoss || definition.IsBoss, killer);

        if (enemy == spawnedBoss || definition.IsBoss)
        {
            HandleBossDefeated();
            return;
        }

        if (State != MissionRuntimeState.FightingRegularEnemies)
        {
            return;
        }

        defeatedEnemies++;
        AchievementManager.Instance.ReportEvent(AchievementEventType.EnemyDefeated);
        KillProgressChanged?.Invoke(defeatedEnemies, mission.TargetKillCount);
        RefreshHud();

        if (defeatedEnemies >= mission.TargetKillCount)
        {
            SpawnBoss();
        }
    }

    private void SpawnBoss()
    {
        if (State != MissionRuntimeState.FightingRegularEnemies)
        {
            return;
        }

        EnemyDefinition bossDefinition = mission.Boss;
        if (bossDefinition == null || bossDefinition.Prefab == null)
        {
            Debug.LogError($"Mission '{mission.Id}' has no valid boss definition.", mission);
            return;
        }

        Vector3 position = bossSpawnPoint != null
            ? bossSpawnPoint.position
            : FindRegularSpawnPosition();
        spawnedBoss = SpawnEnemy(bossDefinition, position);
        // Do not strand the mission in FightingBoss with no boss after an invalid spawn request.
        if (spawnedBoss == null) return;
        SetState(MissionRuntimeState.FightingBoss);
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
    }

    private void HandleBossDefeated()
    {
        if (State != MissionRuntimeState.FightingBoss)
        {
            return;
        }

        BossDefeatProgressionResult progression =
            ChapterProgressionManager.Instance.HandleBossDefeated(mission.Chapter);
        scoreManager?.EndRun();
        SetState(MissionRuntimeState.Completed);
        MissionCompleted?.Invoke(new MissionResult(mission, defeatedEnemies, progression));
    }

    private void SetState(MissionRuntimeState newState)
    {
        if (State == newState)
        {
            return;
        }

        State = newState;
        RefreshHud();
        StateChanged?.Invoke(State);
    }

    private void RefreshHud()
    {
        if (missionLabel != null)
        {
            missionLabel.text = mission != null ? mission.DisplayName : string.Empty;
        }

        if (killProgressLabel != null)
        {
            int target = mission != null ? mission.TargetKillCount : 0;
            killProgressLabel.text = $"격추 {defeatedEnemies} / {target}";
        }

        if (stateLabel != null)
        {
            stateLabel.text = State == MissionRuntimeState.FightingBoss
                ? "보스 출현"
                : State == MissionRuntimeState.Completed ? "미션 완료"
                : State == MissionRuntimeState.Failed ? "GAME OVER" : string.Empty;
        }
    }

    private void RemoveDestroyedEnemies()
    {
        if (spawnedEnemies.Count == 0)
        {
            return;
        }

        List<Enemy> removed = null;
        foreach (KeyValuePair<Enemy, EnemyDefinition> pair in spawnedEnemies)
        {
            if (pair.Key == null)
            {
                removed ??= new List<Enemy>();
                removed.Add(pair.Key);
            }
        }

        if (removed == null)
        {
            return;
        }

        for (int i = 0; i < removed.Count; i++)
        {
            spawnedEnemies.Remove(removed[i]);
        }
    }
}
