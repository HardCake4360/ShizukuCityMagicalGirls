using UnityEngine;

public sealed class MissionLaunchRequest
{
    public MissionLaunchRequest(MissionDefinition mission, PlayableCharacterDefinition character,
        AnimationClip battleEntryTransformation = null)
    {
        Mission = mission;
        Character = character;
        BattleEntryTransformation = battleEntryTransformation;
    }

    public MissionDefinition Mission { get; }
    public PlayableCharacterDefinition Character { get; }
    public AnimationClip BattleEntryTransformation { get; }
}

[DefaultExecutionOrder(-950)]
public sealed class MissionSessionManager : MonoBehaviour
{
    private static MissionSessionManager instance;

    public static MissionSessionManager Instance
    {
        get
        {
            EnsureInstance();
            return instance;
        }
    }

    public MissionLaunchRequest CurrentRequest { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        if (instance != null)
        {
            return;
        }

        instance = FindFirstObjectByType<MissionSessionManager>();
        if (instance == null)
        {
            instance = new GameObject("Mission Session Manager").AddComponent<MissionSessionManager>();
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool BeginMission(MissionDefinition mission, PlayableCharacterDefinition character,
        AnimationClip battleEntryTransformation = null)
    {
        if (mission == null || character == null || !character.IsUnlocked || character.GameplayPrefab == null
            || character.GameplayPrefab.GetComponentInChildren<Player>(true) == null)
        {
            return false;
        }

        CurrentRequest = new MissionLaunchRequest(mission, character, battleEntryTransformation);
        return true;
    }

    public void ClearMission()
    {
        CurrentRequest = null;
    }
}
