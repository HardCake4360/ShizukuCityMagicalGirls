using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One mission's score. No singleton, saves, unlock writes, or character-specific bonuses.</summary>
[DisallowMultipleComponent]
public sealed class CombatScoreManager : MonoBehaviour, IGameOverScoreProvider
{
    [SerializeField] private CombatScoreSettings settings;
    [Header("Optional HUD")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text bonusText;
    [SerializeField] private Slider gaugeSlider;
    [SerializeField] private bool generateDefaultHUD = true;
    [SerializeField] private TMP_FontAsset defaultUIFont;
    [SerializeField, Min(0f)] private float bonusDisplaySeconds = 2f;

    private Player player;
    private int lastHealth;
    private bool ownsSettings;
    private float combatTime;
    private float inactivityTime;
    private float lastKillTime = float.NegativeInfinity;
    private float chargeGraceRemaining;
    private int usedBonusMask;
    private float bonusExpiresAt;
    private readonly HashSet<Enemy> enemies = new();
    private readonly Dictionary<long, AttackGroup> attackGroups = new();
    private readonly Dictionary<Bonus, Repetition> repetitions = new();
    private readonly List<long> expiredGroups = new();
    private enum Bonus { Chain, MultiKill, Flank }
    private struct AttackGroup { public int Count; public float LastKillTime; }
    private struct Repetition { public int Count; public float Time; }

    public long TotalScore { get; private set; }
    public float StyleGauge { get; private set; }
    public CombatStyleRank Rank => Settings.RankFor(StyleGauge);
    public CombatStyleRank HighestRank { get; private set; } = CombatStyleRank.None;
    public int ChainCount { get; private set; }
    public bool IsRecording { get; private set; }
    public string LastBonus { get; private set; } = string.Empty;
    public event Action Changed;
    public event Action<string, long> PointsAwarded;
    public long GetScore() => TotalScore;

    public CombatScoreSettings Settings
    {
        get
        {
            if (settings == null)
            {
                settings = Resources.Load<CombatScoreSettings>("Missions/CombatScoreSettings");
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<CombatScoreSettings>();
                    ownsSettings = true;
                }
            }
            return settings;
        }
    }

    private void OnEnable() => Enemy.DamageReceived += HandleEnemyDamage;

    private void OnDisable()
    {
        Enemy.DamageReceived -= HandleEnemyDamage;
        UnbindPlayer();
        IsRecording = false;
    }

    private void OnDestroy()
    {
        if (ownsSettings && settings != null)
            Destroy(settings);
    }

    public void BeginRun(Player activePlayer)
    {
        UnbindPlayer();
        player = activePlayer;
        TotalScore = 0;
        StyleGauge = 0f;
        HighestRank = CombatStyleRank.None;
        ChainCount = 0;
        LastBonus = string.Empty;
        combatTime = inactivityTime = 0f;
        lastKillTime = float.NegativeInfinity;
        chargeGraceRemaining = Settings.chargeGraceSeconds;
        enemies.Clear();
        attackGroups.Clear();
        repetitions.Clear();
        IsRecording = player != null;
        if (player != null)
        {
            lastHealth = player.CurrentHealth;
            player.ScoreMovementGraceSeconds = Settings.movementGraceSeconds;
            player.HealthChanged += HandleHealthChanged;
        }
        if (generateDefaultHUD && scoreText == null && rankText == null && bonusText == null && gaugeSlider == null)
            CreateDefaultHUD();
        NotifyChanged();
    }

    public void EndRun()
    {
        IsRecording = false;
        NotifyChanged();
    }

    private void UnbindPlayer()
    {
        if (player != null)
            player.HealthChanged -= HandleHealthChanged;
        player = null;
    }

    public void RegisterEnemy(Enemy enemy)
    {
        if (enemy != null)
            enemies.Add(enemy);
    }

    private void Update()
    {
        bool hasTargets = false;
        foreach (Enemy enemy in enemies)
            if (enemy != null && !enemy.IsDead && enemy.isActiveAndEnabled)
            { hasTargets = true; break; }
        Tick(Time.deltaTime, hasTargets, player != null && player.IsChargingAttack);
    }

    /// <summary>Gameplay time only; target-free waits, pause, and ended missions cannot decay style.</summary>
    public void Tick(float deltaTime, bool hasTargets, bool isCharging)
    {
        if (!IsRecording || deltaTime <= 0f || !hasTargets)
            return;
        combatTime += deltaTime;
        float oldGauge = StyleGauge;
        float grace = isCharging ? Mathf.Min(deltaTime, chargeGraceRemaining) : 0f;
        chargeGraceRemaining -= grace;
        // Only a bounded amount of actual charging time is excluded from inactivity.
        // Cancel/restart and charging after decay has begun cannot reset this budget.
        float previousInactivity = inactivityTime;
        inactivityTime += deltaTime - grace;
        float decayTime = Mathf.Max(0f, inactivityTime - Settings.inactivityDelay)
            - Mathf.Max(0f, previousInactivity - Settings.inactivityDelay);
        StyleGauge = Mathf.Max(0f, StyleGauge - Settings.decayPerSecond * decayTime);
        bool bonusExpired = LastBonus.Length > 0 && combatTime >= bonusExpiresAt;
        if (bonusExpired)
            LastBonus = string.Empty;
        expiredGroups.Clear();
        foreach (var pair in attackGroups)
            if (combatTime - pair.Value.LastKillTime > Settings.multiKillWindow)
                expiredGroups.Add(pair.Key);
        foreach (long id in expiredGroups)
            attackGroups.Remove(id);
        if (oldGauge != StyleGauge || bonusExpired)
            NotifyChanged();
    }

    private bool IsPlayerDamage(Enemy enemy, GameObject source)
    {
        if (player == null)
            return false;
        AttackScoreContext context = enemy.LastScoreContext;
        if (context.Id != 0)
            return context.Player == player;
        return source != null && source.GetComponentInParent<Player>() == player;
    }

    private void HandleEnemyDamage(Enemy enemy, GameObject source)
    {
        if (IsRecording && enemy != null && enemies.Contains(enemy) && IsPlayerDamage(enemy, source))
        {
            inactivityTime = 0f;
            chargeGraceRemaining = Settings.chargeGraceSeconds;
        }
    }

    private void HandleHealthChanged(int health, int maximum)
    {
        if (IsRecording && health < lastHealth)
        {
            StyleGauge *= 1f - Settings.hitGaugeLossRatio;
            NotifyChanged();
        }
        lastHealth = health;
    }

    /// <summary>Called by MissionRuntime before completion/progression callbacks freeze the score.</summary>
    public void ReportDefeat(Enemy enemy, bool isBoss, GameObject source)
    {
        if (enemy == null || !enemy.IsDead || !enemies.Remove(enemy) || !IsRecording || !IsPlayerDamage(enemy, source))
            return;

        int basePoints = isBoss ? Settings.bossPoints : enemy is DefensiveEnemy ? Settings.defensivePoints
            : enemy is SpeedEnemy ? Settings.speedPoints : Settings.regularPoints;
        ChainCount = combatTime - lastKillTime <= Settings.chainWindow ? ChainCount + 1 : 1;
        lastKillTime = combatTime;
        inactivityTime = 0f;
        chargeGraceRemaining = Settings.chargeGraceSeconds;
        usedBonusMask = 0;
        double bonus = 0;
        string notice = string.Empty;
        if (ChainCount >= 2)
        {
            bonus += AdjustBonus(Bonus.Chain, Math.Min((long)(ChainCount - 1) * Settings.chainStepPoints,
                Settings.maxChainBonus));
            notice = $"+연속 격추 {ChainCount}";
        }

        AttackScoreContext context = enemy.LastScoreContext;
        if (context.Id != 0)
        {
            attackGroups.TryGetValue(context.Id, out AttackGroup group);
            group.Count = combatTime - group.LastKillTime <= Settings.multiKillWindow ? group.Count + 1 : 1;
            group.LastKillTime = combatTime;
            attackGroups[context.Id] = group;
            if (group.Count >= 2)
            {
                bonus += AdjustBonus(Bonus.MultiKill, Settings.multiKillPoints);
                notice = AppendNotice(notice, $"+다중 격추 {group.Count}");
            }
        }
        if (enemy.LastHitWasFlank)
        {
            bonus += AdjustBonus(Bonus.Flank, Settings.flankPoints);
            notice = AppendNotice(notice, "+측후방 공략");
        }
        // A different successful bonus recovers unused categories. Repeating the same
        // compound action must not make its bonuses recover each other within one kill.
        if (usedBonusMask != 0)
            for (int i = 0; i < 3; i++)
                if ((usedBonusMask & (1 << i)) == 0)
                    repetitions.Remove((Bonus)i);

        float multiplier = Settings.MovementMultiplier(context.MovementRatio);
        if (multiplier > 1f)
            notice = AppendNotice(notice, $"+기동 ×{multiplier:0.##}");
        long points = (long)Math.Round((basePoints + bonus) * multiplier, MidpointRounding.AwayFromZero);
        TotalScore += points;
        float styleGain = isBoss ? Mathf.Min(points, Settings.bossGaugeGainLimit) : points;
        StyleGauge = Mathf.Min(Settings.maxGauge, StyleGauge + styleGain);
        if (Rank > HighestRank)
            HighestRank = Rank;
        LastBonus = notice;
        bonusExpiresAt = combatTime + bonusDisplaySeconds;
        NotifyChanged();
        PointsAwarded?.Invoke(notice, points);
    }

    private double AdjustBonus(Bonus kind, long points)
    {
        usedBonusMask |= 1 << (int)kind;
        if (!Settings.useBonusRepetition)
            return points;
        repetitions.TryGetValue(kind, out Repetition repetition);
        if (combatTime - repetition.Time > Settings.repetitionRecoverySeconds)
            repetition.Count = 0;
        float multiplier = Mathf.Max(Settings.minimumBonusMultiplier, 1f - repetition.Count * Settings.repetitionStep);
        repetition.Count = Math.Min(repetition.Count + 1, 1000);
        repetition.Time = combatTime;
        repetitions[kind] = repetition;
        return points * multiplier;
    }

    private static string AppendNotice(string existing, string added) =>
        existing.Length == 0 ? added : existing + "\n" + added;

    private void NotifyChanged()
    {
        if (scoreText != null)
            scoreText.text = $"점수 {TotalScore:N0}";
        if (rankText != null)
            rankText.text = $"STYLE {CombatScoreSettings.RankName(Rank)}  {StyleGauge:0}/{Settings.maxGauge:0}";
        if (bonusText != null)
            bonusText.text = LastBonus;
        if (gaugeSlider != null)
        {
            gaugeSlider.minValue = 0f;
            gaugeSlider.maxValue = Settings.maxGauge;
            gaugeSlider.SetValueWithoutNotify(StyleGauge);
            gaugeSlider.interactable = false;
        }
        Changed?.Invoke();
    }

    private void CreateDefaultHUD()
    {
        GameObject root = new GameObject("Combat Score HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        scoreText = CreateHUDText(root.transform, "Score", -30f, 32f);
        rankText = CreateHUDText(root.transform, "Style Rank", -75f, 30f);
        bonusText = CreateHUDText(root.transform, "Common Bonuses", -150f, 24f);
    }

    private TMP_Text CreateHUDText(Transform root, string name, float y, float fontSize)
    {
        GameObject label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(root, false);
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-30f, y);
        rect.sizeDelta = new Vector2(500f, name == "Common Bonuses" ? 150f : 45f);
        TMP_Text text = label.GetComponent<TMP_Text>();
        if (defaultUIFont != null)
            text.font = defaultUIFont;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.TopRight;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }
}
