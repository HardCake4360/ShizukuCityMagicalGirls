using System;
using UnityEngine;

[Serializable]
public struct EnemySpawnEntry
{
    [SerializeField] private EnemyDefinition enemy;
    [SerializeField, Min(0f)] private float weight;

    public EnemyDefinition Enemy => enemy;
    public float Weight => Mathf.Max(0f, weight);
}

[CreateAssetMenu(fileName = "Mission", menuName = "Shizuku City/Missions/Mission Definition")]
public sealed class MissionDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string id;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite image;

    [Header("Progression")]
    [SerializeField] private ChapterDefinition chapter;

    [Header("Regular Enemies")]
    [SerializeField] private EnemySpawnEntry[] enemies;
    [SerializeField, Min(1)] private int targetKillCount = 10;
    [SerializeField, Min(0.05f)] private float spawnInterval = 1f;
    [SerializeField, Min(1)] private int maxAliveEnemies = 8;
    [SerializeField, Min(0f)] private float minSpawnRadius = 8f;
    [SerializeField, Min(0f)] private float maxSpawnRadius = 14f;

    [Header("Boss")]
    [SerializeField] private EnemyDefinition boss;

    public string Id => id;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Image => image;
    public ChapterDefinition Chapter => chapter;
    public EnemySpawnEntry[] Enemies => enemies;
    public int TargetKillCount => Mathf.Max(1, targetKillCount);
    public float SpawnInterval => Mathf.Max(0.05f, spawnInterval);
    public int MaxAliveEnemies => Mathf.Max(1, maxAliveEnemies);
    public float MinSpawnRadius => Mathf.Max(0f, minSpawnRadius);
    public float MaxSpawnRadius => Mathf.Max(MinSpawnRadius, maxSpawnRadius);
    public EnemyDefinition Boss => boss;

    private void OnValidate()
    {
        id = id?.Trim();
        targetKillCount = Mathf.Max(1, targetKillCount);
        spawnInterval = Mathf.Max(0.05f, spawnInterval);
        maxAliveEnemies = Mathf.Max(1, maxAliveEnemies);
        minSpawnRadius = Mathf.Max(0f, minSpawnRadius);
        maxSpawnRadius = Mathf.Max(minSpawnRadius, maxSpawnRadius);
    }
}
