using UnityEngine;

[CreateAssetMenu(fileName = "Enemy", menuName = "Shizuku City/Missions/Enemy Definition")]
public sealed class EnemyDefinition : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] private string displayName;
    [SerializeField] private GameObject prefab;
    [SerializeField] private Sprite portrait;
    [SerializeField] private bool boss;

    public string Id => id;
    public string DisplayName => displayName;
    public GameObject Prefab => prefab;
    public Sprite Portrait => portrait;
    public bool IsBoss => boss;

    private void OnValidate()
    {
        id = id?.Trim();
    }
}
