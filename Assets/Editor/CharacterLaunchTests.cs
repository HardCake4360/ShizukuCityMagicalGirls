using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class CharacterLaunchTests
{
    private readonly List<GameObject> created = new();
    private readonly List<Scene> createdScenes = new();
    private readonly List<ScriptableObject> createdDefinitions = new();
    private AchievementManager previousAchievementManager;
    private AchievementManager testAchievementManager;
    private bool previousAchievementShutdown;
    private readonly List<Object> previousDebugUnlockSources = new();
    private static HashSet<Object> DebugUnlockSources => (HashSet<Object>)typeof(CharacterUnlockProgress)
        .GetField("debugUnlockSources", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    [SetUp]
    public void PrepareIsolatedAchievementState()
    {
        previousDebugUnlockSources.AddRange(DebugUnlockSources);
        DebugUnlockSources.Clear();
        FieldInfo singleton = typeof(AchievementManager).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        previousAchievementManager = (AchievementManager)singleton.GetValue(null);
        FieldInfo shutdown = typeof(AchievementManager).GetField("isShuttingDown", BindingFlags.Static | BindingFlags.NonPublic);
        previousAchievementShutdown = (bool)shutdown.GetValue(null);
        shutdown.SetValue(null, false);
        GameObject managerObject = Create("Test Achievement Manager");
        managerObject.SetActive(false);
        testAchievementManager = managerObject.AddComponent<AchievementManager>();
        singleton.SetValue(null, testAchievementManager);
        for (int i = 0; i < 20; i++)
            AddAchievement($"test.{i}", AchievementState.Completed);
    }

    private Dictionary<string, AchievementProgress> TestProgress =>
        (Dictionary<string, AchievementProgress>)typeof(AchievementManager)
            .GetField("progressById", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(testAchievementManager);

    private AchievementDefinition AddAchievement(string id, AchievementState state)
    {
        AchievementDefinition definition = ScriptableObject.CreateInstance<AchievementDefinition>();
        createdDefinitions.Add(definition);
        Set(definition, "id", id);
        TestProgress[id] = new AchievementProgress(definition, state, state == AchievementState.Completed ? 1 : 0);
        return definition;
    }

    [TearDown]
    public void Cleanup()
    {
        typeof(AchievementManager).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, previousAchievementManager);
        typeof(AchievementManager).GetField("isShuttingDown", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, previousAchievementShutdown);
        foreach (GameObject instance in created)
            if (instance != null)
                Object.DestroyImmediate(instance);
        created.Clear();
        foreach (Scene scene in createdScenes)
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        createdScenes.Clear();
        foreach (ScriptableObject definition in createdDefinitions)
            if (definition != null)
                Object.DestroyImmediate(definition);
        createdDefinitions.Clear();
        DebugUnlockSources.Clear();
        foreach (Object source in previousDebugUnlockSources)
            if (source != null) DebugUnlockSources.Add(source);
        previousDebugUnlockSources.Clear();
    }

    private GameObject Create(string name, params System.Type[] components)
    {
        GameObject instance = new GameObject(name, components);
        created.Add(instance);
        return instance;
    }

    private static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }

    private static void Call(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);

    private TraverseCursor CreateCursor()
    {
        TraverseCursor cursor = Create("Test Cursor", typeof(RectTransform)).AddComponent<TraverseCursor>();
        Set(cursor, "moveDuration", 0f);
        Set(cursor, "idlePulseDuration", 0f);
        Set(cursor, "dimTransitionDuration", 0f);
        Set(cursor, "syncEventSystemSelection", false);
        Call(cursor, "Awake");
        return cursor;
    }

    [Test]
    public void TargetOffsetUsesAnchorLocalCoordinatesForImmediateAnimatedAndContinuousFollow()
    {
        Transform parent = Create("Scaled Parent", typeof(RectTransform)).transform;
        parent.position = new Vector3(10f, 20f, 0f);
        parent.localScale = new Vector3(2f, 3f, 1f);
        TraverseTarget target = Create("Target", typeof(RectTransform)).AddComponent<TraverseTarget>();
        target.transform.SetParent(parent, false);
        target.transform.localPosition = new Vector3(5f, 7f, 0f);
        target.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Set(target, "cursorOffset", new Vector3(4f, -2f, 0f));
        Assert.That(Vector3.Distance(target.CursorWorldPosition, new Vector3(24f, 53f, 0f)), Is.LessThan(0.001f));
        TraverseCursor cursor = CreateCursor();
        cursor.SetTargetImmediate(target);
        Assert.That(Vector3.Distance(cursor.transform.position, target.CursorWorldPosition), Is.LessThan(0.001f));
        Transform anchor = Create("Custom Anchor", typeof(RectTransform)).transform;
        anchor.SetParent(target.transform, false);
        anchor.localPosition = Vector3.right;
        Set(target, "cursorAnchor", anchor);
        cursor.SetTarget(target);
        Assert.That(Vector3.Distance(cursor.transform.position, new Vector3(24f, 56f, 0f)), Is.LessThan(0.001f));
        parent.position += Vector3.right * 10f;
        Call(cursor, "LateUpdate");
        Assert.That(Vector3.Distance(cursor.transform.position, new Vector3(34f, 56f, 0f)), Is.LessThan(0.001f));
        cursor.transform.position = Vector3.zero;
        System.Collections.IEnumerator movement = (System.Collections.IEnumerator)typeof(TraverseCursor)
            .GetMethod("MoveToCurrentTarget", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cursor, null);
        Assert.That(movement.MoveNext(), Is.False);
        Assert.That(Vector3.Distance(cursor.transform.position, target.CursorWorldPosition), Is.LessThan(0.001f));
        Set(target, "cursorOffset", Vector3.zero);
        Assert.That(target.CursorWorldPosition, Is.EqualTo(anchor.position));
    }

    [Test]
    public void RuntimeOffsetEditsSnapDuringMovementWithoutRefocusingTarget()
    {
        TraverseTarget target = Create("Moving Offset Target", typeof(RectTransform)).AddComponent<TraverseTarget>();
        target.transform.position = new Vector3(200f, 100f, 0f);
        TraverseCursor cursor = CreateCursor();
        Set(cursor, "moveDuration", 10f);
        int targetChanges = 0;
        cursor.CurrentTargetChanged += _ => targetChanges++;
        cursor.SetTarget(target);
        Assert.That(cursor.IsMoving, Is.True);
        Assert.That(targetChanges, Is.EqualTo(1));
        // Simulate a serialized Inspector edit, with no API call or reselection.
        Set(target, "cursorOffset", new Vector3(7f, -3f, 0f));
        Call(cursor, "LateUpdate");
        Assert.That(cursor.IsMoving, Is.False);
        Assert.That(cursor.transform.position, Is.EqualTo(new Vector3(207f, 97f, 0f)));
        Assert.That(cursor.CurrentTarget, Is.SameAs(target));
        Assert.That(targetChanges, Is.EqualTo(1));
    }

    [Test]
    public void OffsetSetterAppliesDuringPausedSelectedStateWithoutCancelingSelection()
    {
        TraverseTarget target = Create("Selected Offset Target", typeof(RectTransform)).AddComponent<TraverseTarget>();
        target.transform.position = new Vector3(10f, 20f, 0f);
        TraverseCursor cursor = CreateCursor();
        cursor.SetTargetImmediate(target);
        Set(cursor, "selectedTarget", target);
        Set(cursor, "currentVisualState", TraverseCursorVisualState.Selected);
        float previousTimeScale = Time.timeScale;
        try
        {
            Time.timeScale = 0f;
            target.SetCursorOffset(new Vector3(-2f, 5f, 0f));
            Call(cursor, "LateUpdate");
            Assert.That(cursor.transform.position, Is.EqualTo(new Vector3(8f, 25f, 0f)));
            Assert.That(cursor.IsSelected, Is.True);
            Assert.That(cursor.SelectedTarget, Is.SameAs(target));
        }
        finally { Time.timeScale = previousTimeScale; }
    }

    [Test]
    public void DebugUnlockAllowsAllCharactersAndMissionLaunchWithoutChangingRealProgress()
    {
        TestProgress.Clear();
        TraverseCursor cursor = CreateCursor();
        MissionSessionManager session = Create("Debug Session").AddComponent<MissionSessionManager>();
        MissionDefinition mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>("Assets/Resources/Missions/PrototypeMission.asset");
        PlayableCharacterDefinition[] characters = { Character("Rikka"), Character("Yukari"), Character("Sera"), Character("Arisa"), Character("Mayu") };
        Assert.That(Character("Yukari").IsUnlocked, Is.False);
        cursor.SetDebugUnlockAllCharacters(true);
        foreach (PlayableCharacterDefinition character in characters)
        {
            Assert.That(character.IsUnlocked, Is.True);
            Assert.That(CharacterUnlockProgress.IsUnlocked(character, 0), Is.True);
            Assert.That(character.InitiallyUnlocked, Is.EqualTo(character == Character("Rikka")));
            Assert.That(session.BeginMission(mission, character), Is.True);
        }
        Assert.That(CharacterUnlockProgress.CompletedCount, Is.Zero);
        Assert.That(TestProgress.Count, Is.Zero);
        Assert.That(CharacterUnlockProgress.FindNextUnlock(characters, 0), Is.SameAs(Character("Yukari")));
        cursor.SetDebugUnlockAllCharacters(false);
        Assert.That(Character("Yukari").IsUnlocked, Is.False);
        Assert.That(session.BeginMission(mission, Character("Yukari")), Is.False);
        session.ClearMission();
    }

    [Test]
    public void DebugUnlockUpdatesLockDimmingWithoutRemovingSelectionDimming()
    {
        TestProgress.Clear();
        TraverseTarget target = Create("Debug Icon", typeof(RectTransform), typeof(Image)).AddComponent<TraverseTarget>();
        Image image = target.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.8f);
        Set(target, "requiredCharacter", Character("Yukari"));
        Set(target, "unlockDimTransitionDuration", 0f);
        Call(target, "OnEnable");
        Assert.That(target.IsTraversable, Is.False);
        Assert.That(image.color.a, Is.EqualTo(0.32f).Within(0.001f));
        target.SetDimmed(true, 0.2f);
        TraverseCursor cursor = CreateCursor();
        cursor.SetDebugUnlockAllCharacters(true);
        Assert.That(target.IsTraversable, Is.True);
        Assert.That(image.color.a, Is.EqualTo(0.16f).Within(0.001f));
        target.RestoreOpacity();
        Assert.That(image.color.a, Is.EqualTo(0.8f).Within(0.001f));
        cursor.SetDebugUnlockAllCharacters(false);
        Assert.That(target.IsTraversable, Is.False);
        Assert.That(image.color.a, Is.EqualTo(0.32f).Within(0.001f));
        Call(target, "OnDisable");
    }

    [Test]
    public void InspectorDebugToggleWorksWhileCursorHiddenAndMultipleOwnersReleaseIndependently()
    {
        TestProgress.Clear();
        TraverseCursor first = CreateCursor();
        first.Deactivate();
        Set(first, "debugUnlockAllCharacters", true);
        Call(first, "Update");
        Assert.That(CharacterUnlockProgress.DebugUnlockAllCharacters, Is.True);
        TraverseCursor second = CreateCursor();
        second.SetDebugUnlockAllCharacters(true);
        Set(first, "debugUnlockAllCharacters", false);
        Call(first, "Update");
        Assert.That(CharacterUnlockProgress.DebugUnlockAllCharacters, Is.True);
        second.enabled = false;
        Call(second, "OnDisable");
        Assert.That(CharacterUnlockProgress.DebugUnlockAllCharacters, Is.False);
        Assert.That(Character("Yukari").IsUnlocked, Is.False);
    }

    private static PlayableCharacterDefinition Character(string name) =>
        AssetDatabase.LoadAssetAtPath<PlayableCharacterDefinition>($"Assets/Resources/Missions/Characters/{name}.asset");

    private static string PlainText(TMP_Text text) => Regex.Replace(text.text, "<[^>]*>", string.Empty);

    [Test]
    public void CursorOpacityCleanupAndPanelUnsubscribeNeverRecreateMissingManager()
    {
        GameObject icon = Create("Closing Icon", typeof(RectTransform));
        icon.SetActive(false);
        icon.AddComponent<Image>();
        TraverseTarget target = icon.AddComponent<TraverseTarget>();
        Set(target, "requiredCharacter", Character("Yukari"));
        target.SetDimmed(true, 0.4f);
        GameObject cursorObject = Create("Closing Cursor", typeof(RectTransform));
        cursorObject.SetActive(false);
        TraverseCursor cursor = cursorObject.AddComponent<TraverseCursor>();
        var targets = (HashSet<TraverseTarget>)typeof(TraverseCursor)
            .GetField("subscribedTargets", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cursor);
        targets.Add(target);
        GameObject panelObject = Create("Closing Panel");
        panelObject.SetActive(false);
        AchievementPanelController panel = panelObject.AddComponent<AchievementPanelController>();
        Set(panel, "subscribed", true);
        Set(panel, "subscribedAchievementManager", testAchievementManager);
        FieldInfo singleton = typeof(AchievementManager).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        int before = Object.FindObjectsByType<AchievementManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        singleton.SetValue(null, null);
        try
        {
            Assert.That(AchievementManager.TryGetExisting(out _), Is.False);
            Assert.That(CharacterUnlockProgress.CompletedCount, Is.Zero);
            typeof(TraverseCursor).GetMethod("RestoreAllTargetOpacity", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(cursor, new object[] { true });
            typeof(AchievementPanelController).GetMethod("Unsubscribe", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(panel, null);
            Assert.That(singleton.GetValue(null), Is.Null);
            Assert.That(Object.FindObjectsByType<AchievementManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(before));
        }
        finally { singleton.SetValue(null, testAchievementManager); }
    }

    [Test]
    public void ShutdownBlocksManagerRecreationAndSubsystemRegistrationResetsTheGuard()
    {
        FieldInfo singleton = typeof(AchievementManager).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        int before = Object.FindObjectsByType<AchievementManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        try
        {
            typeof(AchievementManager).GetMethod("OnApplicationQuit", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(testAchievementManager, null); // This test manager has no save store.
            singleton.SetValue(null, null);
            Assert.That(AchievementManager.IsShuttingDown, Is.True);
            Assert.That(AchievementManager.Instance, Is.Null);
            Assert.That(Object.FindObjectsByType<AchievementManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(before));
            typeof(AchievementManager).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);
            Assert.That(AchievementManager.IsShuttingDown, Is.False);
        }
        finally
        {
            singleton.SetValue(null, testAchievementManager);
            typeof(AchievementManager).GetField("isShuttingDown", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, false);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LockedIconDimSurvivesSelectionRestoreAndUnlockPreservesSelectionDim(bool useCanvasGroup)
    {
        TestProgress.Clear();
        GameObject icon = Create("Locked Yukari", typeof(RectTransform));
        icon.SetActive(false);
        Image image = icon.AddComponent<Image>();
        Color originalColor = new Color(0.25f, 0.5f, 0.75f, 0.8f);
        image.color = originalColor;
        CanvasGroup group = useCanvasGroup ? icon.AddComponent<CanvasGroup>() : null;
        if (group != null)
            group.alpha = 0.6f;
        float originalAlpha = group != null ? group.alpha : image.color.a;
        TraverseTarget target = icon.AddComponent<TraverseTarget>();
        Set(target, "requiredCharacter", Character("Yukari"));
        Set(target, "unlockDimTransitionDuration", 0f);
        MethodInfo enable = typeof(TraverseTarget).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo disable = typeof(TraverseTarget).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);

        void CheckAlpha(float multiplier)
        {
            Assert.That(group != null ? group.alpha : image.color.a,
                Is.EqualTo(originalAlpha * multiplier).Within(0.0001f));
            Assert.That(image.color.r, Is.EqualTo(originalColor.r));
            Assert.That(image.color.g, Is.EqualTo(originalColor.g));
            Assert.That(image.color.b, Is.EqualTo(originalColor.b));
        }

        enable.Invoke(target, null);
        CheckAlpha(0.4f);
        target.SetDimmed(true, 0.4f);
        CheckAlpha(0.4f); // Both reasons must not double-dim to 0.16.
        target.SetDimmed(true, 0.2f);
        CheckAlpha(0.2f);
        target.RestoreOpacity();
        CheckAlpha(0.4f); // Cancel/disable/RefreshTargets removes selection dim only.
        disable.Invoke(target, null);
        enable.Invoke(target, null);
        CheckAlpha(0.4f); // Re-enable must not capture the dimmed alpha as a new baseline.

        target.SetDimmed(true, 0.4f);
        AchievementDefinition last = null;
        for (int i = 0; i < 5; i++)
            last = AddAchievement($"dim.{i}", AchievementState.Completed);
        var changed = (System.Action<AchievementProgress>)typeof(AchievementManager)
            .GetField("ProgressChanged", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(testAchievementManager);
        changed.Invoke(TestProgress[last.Id]);
        CheckAlpha(0.4f); // Newly unlocked but still unselected while another icon is selected.
        target.RestoreOpacity();
        CheckAlpha(1f);
        disable.Invoke(target, null);
    }

    [TestCase(0, "Yukari", 5, 1)]
    [TestCase(4, "Yukari", 1, 1)]
    [TestCase(5, "Sera", 5, 2)]
    [TestCase(9, "Sera", 1, 2)]
    [TestCase(10, "Arisa", 5, 3)]
    [TestCase(14, "Arisa", 1, 3)]
    [TestCase(15, "Mayu", 5, 4)]
    [TestCase(19, "Mayu", 1, 4)]
    [TestCase(20, null, 0, 5)]
    public void CharacterUnlockThresholdsAdvanceEveryFiveCompletions(int completed, string nextName, int remaining, int unlocked)
    {
        PlayableCharacterDefinition[] characters = { Character("Rikka"), Character("Yukari"), Character("Sera"), Character("Arisa"), Character("Mayu") };
        int[] thresholds = { 0, 5, 10, 15, 20 };
        int actualUnlocked = 0;
        for (int i = 0; i < characters.Length; i++)
        {
            Assert.That(characters[i].RequiredCompletedAchievements, Is.EqualTo(thresholds[i]));
            if (CharacterUnlockProgress.IsUnlocked(characters[i], completed))
                actualUnlocked++;
        }
        Assert.That(actualUnlocked, Is.EqualTo(unlocked));
        PlayableCharacterDefinition next = CharacterUnlockProgress.FindNextUnlock(characters, completed);
        Assert.That(next, Is.SameAs(nextName == null ? null : Character(nextName)));
        if (next != null)
            Assert.That(next.RequiredCompletedAchievements - completed, Is.EqualTo(remaining));
    }

    [Test]
    public void OnlyCompletedAchievementsUnlockIconsAndAllowLaunching()
    {
        TestProgress.Clear();
        for (int i = 0; i < 5; i++)
            AddAchievement($"claim.{i}", AchievementState.ConditionCleared);
        TraverseTarget target = Create("Yukari Icon", typeof(RectTransform)).AddComponent<TraverseTarget>();
        Set(target, "requiredCharacter", Character("Yukari"));
        MissionDefinition mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>("Assets/Resources/Missions/PrototypeMission.asset");
        MissionSessionManager session = Create("Test Session").AddComponent<MissionSessionManager>();
        Assert.That(CharacterUnlockProgress.CompletedCount, Is.Zero);
        Assert.That(target.IsTraversable, Is.False);
        Assert.That(session.BeginMission(mission, Character("Yukari")), Is.False);
        for (int i = 0; i < 5; i++)
        {
            AchievementProgress old = TestProgress[$"claim.{i}"];
            TestProgress[$"claim.{i}"] = new AchievementProgress(old.Definition, AchievementState.Completed, 1);
        }
        Assert.That(CharacterUnlockProgress.CompletedCount, Is.EqualTo(5));
        Assert.That(target.IsTraversable, Is.True);
        Assert.That(session.BeginMission(mission, Character("Yukari")), Is.True);
        List<AchievementProgress> duplicated = new(TestProgress.Values);
        duplicated.Add(duplicated[0]);
        Assert.That(CharacterUnlockProgress.CountCompleted(duplicated), Is.EqualTo(5));
    }

    [Test]
    public void QuestSummaryUsesSavedStatesAndDoesNotRebuildTheList()
    {
        TestProgress.Clear();
        List<AchievementDefinition> definitions = new()
        {
            AddAchievement("summary.0", AchievementState.Completed),
            AddAchievement("summary.1", AchievementState.Completed),
            AddAchievement("summary.2", AchievementState.ConditionCleared),
            AddAchievement("summary.3", AchievementState.Active),
            AddAchievement("summary.4", AchievementState.Inactive)
        };
        definitions.Add(definitions[0]);
        GameObject listObject = Create("Test List");
        listObject.SetActive(false);
        AchievementListManager list = listObject.AddComponent<AchievementListManager>();
        Set(list, "achievements", definitions);
        GameObject summaryObject = Create("Test Summary");
        summaryObject.SetActive(false);
        QuestSummaryController summary = summaryObject.AddComponent<QuestSummaryController>();
        Set(summary, "achievementManager", testAchievementManager);
        Set(summary, "listManager", list);
        Set(summary, "availableCharacters", new[] { Character("Rikka"), Character("Yukari"), Character("Sera"), Character("Arisa"), Character("Mayu") });
        TMP_Text percentage = Create("Percentage", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        TMP_Text count = Create("Count", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        TMP_Text chapter = Create("Chapter", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        TMP_Text unlock = Create("Unlock", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        Set(summary, "progressText", percentage);
        Set(summary, "completionText", count);
        Set(summary, "chapterText", chapter);
        Set(summary, "characterUnlockText", unlock);
        Set(summary, "progressPercentColor", Color.red);
        Set(summary, "totalAchievementCountColor", Color.green);
        Set(summary, "completedAchievementCountColor", Color.blue);
        Set(summary, "chapterNumberColor", Color.yellow);
        Set(summary, "remainingAchievementCountColor", Color.cyan);
        Set(summary, "characterNameColor", (Color)new Color32(255, 0, 255, 128));
        percentage.richText = false;
        percentage.overrideColorTags = true;
        summary.Refresh();
        Assert.That(PlainText(percentage), Is.EqualTo("진행도 50%"));
        Assert.That(PlainText(count), Is.EqualTo("4개중 2개 완료"));
        Assert.That(PlainText(chapter), Is.EqualTo("챕터 --"));
        Assert.That(PlainText(unlock), Is.EqualTo("3개 클리어 시 유카리 언락"));
        Assert.That(percentage.text, Is.EqualTo("진행도 <color=#FF0000FF>50</color>%"));
        Assert.That(count.text, Is.EqualTo("<color=#00FF00FF>4</color>개중 <color=#0000FFFF>2</color>개 완료"));
        Assert.That(unlock.text, Is.EqualTo("<color=#00FFFFFF>3</color>개 클리어 시 <color=#FF00FF80><noparse>유카리</noparse></color> 언락"));
        Assert.That(chapter.text, Is.EqualTo("챕터 --"));
        Assert.That(percentage.richText, Is.True);
        Assert.That(percentage.overrideColorTags, Is.False);
        Set(summary, "progressPercentColor", Color.cyan);
        typeof(QuestSummaryController).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(summary, null);
        Assert.That(percentage.text, Does.Contain("#FF0000FF"));
        typeof(QuestSummaryController).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(summary, null);
        Assert.That(percentage.text, Is.EqualTo("진행도 <color=#00FFFFFF>50</color>%"));
        Assert.That(list.HasBuiltList, Is.False);

        AchievementProgress active = TestProgress["summary.3"];
        TestProgress["summary.3"] = new AchievementProgress(active.Definition, AchievementState.Completed, 1);
        summary.Refresh();
        Assert.That(PlainText(percentage), Is.EqualTo("진행도 75%"));
        Assert.That(PlainText(count), Is.EqualTo("4개중 3개 완료"));
        Assert.That(PlainText(unlock), Is.EqualTo("2개 클리어 시 유카리 언락"));
        Assert.That(list.HasBuiltList, Is.False);
        Set(list, "achievements", new List<AchievementDefinition>());
        summary.Refresh();
        Assert.That(PlainText(percentage), Is.EqualTo("진행도 0%"));
        Assert.That(PlainText(count), Is.EqualTo("0개중 0개 완료"));

        GameObject chapterObject = Create("Test Chapter Manager");
        chapterObject.SetActive(false);
        ChapterProgressionManager chapterManager = chapterObject.AddComponent<ChapterProgressionManager>();
        ChapterDefinition definition = ScriptableObject.CreateInstance<ChapterDefinition>();
        createdDefinitions.Add(definition);
        Set(definition, "chapterNumber", 2);
        Set(chapterManager, "<CurrentChapter>k__BackingField", definition);
        Set(summary, "chapterManager", chapterManager);
        Set(summary, "availableCharacters", new[] { Character("Rikka"), Character("Yukari") });
        for (int i = 0; i < 2; i++)
            AddAchievement($"summary.extra.{i}", AchievementState.Completed);
        summary.Refresh();
        Assert.That(PlainText(chapter), Is.EqualTo("챕터 2"));
        Assert.That(chapter.text, Is.EqualTo("챕터 <color=#FFFF00FF>2</color>"));
        Assert.That(unlock.text, Is.EqualTo("모든 캐릭터 언락 완료"));
        Assert.That(chapterManager.CurrentChapter, Is.SameAs(definition));
    }

    [Test]
    public void LobbyUiHidesAndReturnsWithoutDisablingItsObjects()
    {
        CanvasGroup group = Create("lobbyUI", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>();
        GameObject child = Create("Lobby Child", typeof(RectTransform));
        child.transform.SetParent(group.transform, false);
        LobbyMissionReadyController controller = Create("Scene Animator", typeof(Animator))
            .AddComponent<LobbyMissionReadyController>();
        Transform camera = Create("Lobby Camera").transform;
        Transform tile = Create("Cover Tile").transform;
        SpriteRenderer blackTile = Create("BlackTile", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        Vector3 blackTilePosition = new Vector3(3f, 10f, 2f);
        blackTile.transform.localPosition = blackTilePosition;
        tile.localPosition = new Vector3(2f, 35f, 0f);
        Vector3 tilePosition = tile.localPosition;
        Color blackTileColor = new Color(0.2f, 0.3f, 0.4f, 0f);
        blackTile.color = blackTileColor;
        Set(controller, "lobbyUiCanvasGroup", group);
        Set(controller, "lobbyCamera", camera);
        Set(controller, "coverTile", tile);
        Set(controller, "blackTile", blackTile);
        Set(controller, "codeTransitionDuration", 0f);
        MethodInfo move = typeof(LobbyMissionReadyController).GetMethod("MoveCameraAndFadeBlackTile",
            BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (float alpha in new[] { 0f, 1f })
        {
            var routine = (System.Collections.IEnumerator)move.Invoke(controller,
                new object[] { Vector3.zero, Vector3.up, alpha, 1f - alpha, alpha });
            Assert.That(routine.MoveNext(), Is.False);
            Assert.That(group.alpha, Is.EqualTo(alpha));
            Assert.That(group.interactable, Is.EqualTo(alpha == 1f));
            Assert.That(group.blocksRaycasts, Is.EqualTo(alpha == 1f));
            Assert.That(group.gameObject.activeSelf, Is.True);
            Assert.That(child.activeInHierarchy, Is.True);
            Assert.That(camera.localPosition, Is.EqualTo(Vector3.up));
            Assert.That(blackTile.color.a, Is.EqualTo(1f - alpha));
            Assert.That(blackTile.color.r, Is.EqualTo(blackTileColor.r));
            Assert.That(blackTile.color.g, Is.EqualTo(blackTileColor.g));
            Assert.That(blackTile.color.b, Is.EqualTo(blackTileColor.b));
            Assert.That(blackTile.transform.localPosition, Is.EqualTo(blackTilePosition));
            Assert.That(tile.localPosition, Is.EqualTo(tilePosition));
        }
    }

    [Test]
    public void BlackTileAlphaDoesNotMoveOrResizeEitherCoverAndClampsOpacity()
    {
        LobbyMissionReadyController controller = Create("Scene Animator", typeof(Animator))
            .AddComponent<LobbyMissionReadyController>();
        SpriteRenderer blackTile = Create("BlackTile", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        blackTile.color = new Color(0.1f, 0.2f, 0.3f, 0f);
        blackTile.transform.localPosition = new Vector3(2f, 12f, 0f);
        blackTile.transform.localScale = new Vector3(25f, 30f, 1f);
        blackTile.transform.localRotation = Quaternion.Euler(0f, 0f, 15f);
        Vector3 position = blackTile.transform.localPosition;
        Vector3 scale = blackTile.transform.localScale;
        Quaternion rotation = blackTile.transform.localRotation;
        Set(controller, "blackTile", blackTile);
        MethodInfo setAlpha = typeof(LobbyMissionReadyController).GetMethod("SetBlackTileAlpha",
            BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (float alpha in new[] { 0f, 0.5f, 1f, 2f, -1f })
        {
            setAlpha.Invoke(controller, new object[] { alpha });
            Assert.That(blackTile.color, Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, Mathf.Clamp01(alpha))));
            Assert.That(blackTile.transform.localPosition, Is.EqualTo(position));
            Assert.That(blackTile.transform.localScale, Is.EqualTo(scale));
            Assert.That(blackTile.transform.localRotation, Is.EqualTo(rotation));
        }
    }

    [Test]
    public void SavedLobbyUsesBlackTileForCodeFadeAndKeepsSquareForAuthoredAnimations()
    {
        string scene = File.ReadAllText("Assets/Scenes/Lobby.unity");
        Assert.That(Regex.IsMatch(scene, @"blackTile: \{fileID: (0|1149599424)\}"), Is.True);
        StringAssert.Contains("m_Name: BlackTile", scene); // An empty reference resolves this child at runtime.
        StringAssert.Contains("coverTile: {fileID: 1222582388}", scene);
        StringAssert.Contains("ascentDestination:", scene);
        StringAssert.Contains("descentStartPoint:", scene);
        foreach (string clip in new[] { "salute", "return" })
        {
            string animation = File.ReadAllText($"Assets/Scenes/anim/{clip}.anim");
            StringAssert.Contains("path: Square", animation);
            StringAssert.DoesNotContain("path: BlackTile", animation);
        }
    }

    [Test]
    public void ReadyUiTransitionIsDispatchedAfterCodeAscentAndBeforeSalute()
    {
        string source = File.ReadAllText("Assets/Scripts/Lobby/LobbyMissionReadyController.cs");
        int enterStart = source.IndexOf("public void EnterMissionReady()", System.StringComparison.Ordinal);
        int launchReadyBranch = source.IndexOf("if (uiAnimator.CurrentStateId == LaunchReadyUiState", enterStart,
            System.StringComparison.Ordinal);
        string lobbyClick = source.Substring(enterStart, launchReadyBranch - enterStart);
        StringAssert.DoesNotContain("TryTransition(LaunchReadyTransitionIndex)", lobbyClick);
        StringAssert.Contains("StartCoroutine(EnterMissionReadyRoutine())", lobbyClick);

        int routineStart = source.IndexOf("private IEnumerator EnterMissionReadyRoutine()", System.StringComparison.Ordinal);
        int routineEnd = source.IndexOf("private IEnumerator LeaveMissionReadyRoutine()", routineStart,
            System.StringComparison.Ordinal);
        string routine = source.Substring(routineStart, routineEnd - routineStart);
        int movement = routine.IndexOf("yield return MoveCameraAndFadeBlackTile(", System.StringComparison.Ordinal);
        int uiTransition = routine.IndexOf("TryTransition(LaunchReadyTransitionIndex)", System.StringComparison.Ordinal);
        int salute = routine.IndexOf("PlayState(MissionReadyState", System.StringComparison.Ordinal);
        Assert.That(movement, Is.GreaterThanOrEqualTo(0));
        Assert.That(uiTransition, Is.GreaterThan(movement));
        Assert.That(salute, Is.GreaterThan(uiTransition));
        StringAssert.Contains("SetButtonState(false, true)", routine);
        StringAssert.Contains("SetLobbyCameraInputEnabled(true)", routine); // Failed dispatch restores input.
    }

    [Test]
    public void CameraTransitionMarkersKeepAscentXAndUseOnlyDescentWorldY()
    {
        LobbyMissionReadyController controller = Create("Scene Animator", typeof(Animator))
            .AddComponent<LobbyMissionReadyController>();
        Transform cameraParent = Create("Camera Parent").transform;
        cameraParent.position = new Vector3(20f, 10f, 3f);
        cameraParent.rotation = Quaternion.Euler(0f, 0f, 20f);
        cameraParent.localScale = new Vector3(2f, 3f, 1f);
        Transform camera = Create("Camera").transform;
        camera.SetParent(cameraParent, false);
        camera.localPosition = new Vector3(-3f, 1f, -10f);
        Transform markerParent = Create("Marker Parent").transform;
        markerParent.position = new Vector3(-20f, 5f, 1f);
        Transform ascent = Create("Ascent Destination").transform;
        Transform descent = Create("Descent Start").transform;
        ascent.SetParent(markerParent, false);
        descent.SetParent(markerParent, false);
        ascent.position = cameraParent.TransformPoint(new Vector3(99f, 40f, -10f));
        descent.position = cameraParent.TransformPoint(new Vector3(2f, 45f, 99f));
        Vector3 markerAscentPosition = ascent.position;
        Vector3 markerDescentPosition = descent.position;
        Set(controller, "lobbyCamera", camera);
        Set(controller, "lobbyCameraPositionAtEntry", camera.localPosition);
        Set(controller, "ascentDestination", ascent);
        Set(controller, "descentStartPoint", descent);
        MethodInfo getAscent = typeof(LobbyMissionReadyController).GetMethod("GetAscentDestinationPosition",
            BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo getDescent = typeof(LobbyMissionReadyController).GetMethod("GetDescentStartPosition",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Vector3 ascentPosition = (Vector3)getAscent.Invoke(controller, null);
        Vector3 descentPosition = (Vector3)getDescent.Invoke(controller, null);
        Assert.That(Vector3.Distance(ascentPosition, new Vector3(-3f, 40f, -10f)), Is.LessThan(0.0001f));
        Vector3 descentWorldPosition = cameraParent.TransformPoint(descentPosition);
        Assert.That(descentWorldPosition.x, Is.EqualTo(camera.position.x).Within(0.0001f));
        Assert.That(descentWorldPosition.y, Is.EqualTo(descent.position.y).Within(0.0001f));
        Assert.That(descentWorldPosition.z, Is.EqualTo(camera.position.z).Within(0.0001f));
        // Marker X/Z must not affect descent, even with a rotated/non-uniformly scaled camera parent.
        descent.position += new Vector3(300f, 0f, -500f);
        Vector3 nextDescentPosition = (Vector3)getDescent.Invoke(controller, null);
        Assert.That(Vector3.Distance(nextDescentPosition, descentPosition), Is.LessThan(0.0001f));
        descent.position = markerDescentPosition;
        // Resolve each time, allowing repositioning between transitions rather than caching markers at Start.
        ascent.position = cameraParent.TransformPoint(new Vector3(5f, 50f, -10f));
        ascentPosition = (Vector3)getAscent.Invoke(controller, null);
        Assert.That(ascentPosition.y, Is.EqualTo(50f).Within(0.0001f));
        Assert.That(descent.position, Is.EqualTo(markerDescentPosition));
        Assert.That(markerAscentPosition, Is.Not.EqualTo(ascent.position));
    }

    [Test]
    public void UnassignedCameraMarkersUseLegacyPositionsAndRootCameraUsesWorldPosition()
    {
        LobbyMissionReadyController controller = Create("Scene Animator", typeof(Animator))
            .AddComponent<LobbyMissionReadyController>();
        Transform camera = Create("Camera").transform;
        camera.position = new Vector3(2f, 35f, -10f);
        Set(controller, "lobbyCamera", camera);
        Set(controller, "lobbyCameraPositionAtEntry", new Vector3(-4f, 1f, -10f));
        Set(controller, "coveredCameraPosition", new Vector3(2f, 33f, -10f));
        MethodInfo getAscent = typeof(LobbyMissionReadyController).GetMethod("GetAscentDestinationPosition",
            BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo getDescent = typeof(LobbyMissionReadyController).GetMethod("GetDescentStartPosition",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That((Vector3)getAscent.Invoke(controller, null), Is.EqualTo(new Vector3(-4f, 33f, -10f)));
        Assert.That((Vector3)getDescent.Invoke(controller, null), Is.EqualTo(camera.localPosition));
        Transform marker = Create("Descent Start").transform;
        marker.position = new Vector3(5f, 40f, 99f);
        Set(controller, "descentStartPoint", marker);
        Assert.That((Vector3)getDescent.Invoke(controller, null), Is.EqualTo(new Vector3(2f, 40f, -10f)));
    }

    [Test]
    public void BattleBgmHasARegisteredClip()
    {
        BgmLibrary library = AssetDatabase.LoadAssetAtPath<BgmLibrary>("Assets/Resources/Audio/BgmLibrary.asset");
        Assert.That(library, Is.Not.Null);
        Assert.That(library.TryGetClip(BgmKey.Gameplay, out AudioClip clip), Is.True);
        Assert.That(clip, Is.Not.Null);
    }

    [Test]
    public void LobbyStandingEntriesBindEveryIconToTheMatchingCharacter()
    {
        string scene = File.ReadAllText("Assets/Scenes/Lobby.unity");
        Match standing = Regex.Match(scene,
            @"--- !u!114 &\d+\r?\n(?:(?!--- !u!).)*guid: 6ca8bff1ba8f4a75b9e496fd424c5617(?:(?!--- !u!).)*",
            RegexOptions.Singleline);
        Assert.That(standing.Success, Is.True);
        (string target, string character)[] bindings =
        {
            ("941336032", "Rikka"), ("1984026034", "Yukari"), ("2064126167", "Sera"),
            ("752278954", "Arisa"), ("1573026500", "Mayu")
        };
        foreach (var binding in bindings)
        {
            string guid = AssetDatabase.AssetPathToGUID($"Assets/Resources/Missions/Characters/{binding.character}.asset");
            Assert.That(guid, Is.Not.Empty);
            StringAssert.Contains($"- target: {{fileID: {binding.target}}}\n    playableCharacter: {{fileID: 11400000, guid: {guid}, type: 2}}",
                standing.Value.Replace("\r\n", "\n"));
        }
    }

    [TestCase("Rikka", typeof(SniperRifleWeapon))]
    [TestCase("Yukari", typeof(HomingMissileWeapon))]
    [TestCase("Sera", typeof(MachineGunWeapon))]
    [TestCase("Arisa", typeof(HitscanWeapon))]
    [TestCase("Mayu", typeof(SwordWeapon))]
    public void EachCharacterHasItsOwnPlayablePrefabAndLoadout(string name, System.Type weaponType)
    {
        PlayableCharacterDefinition character = Character(name);
        Assert.That(character, Is.Not.Null);
        Assert.That(character.InitiallyUnlocked, Is.EqualTo(name == "Rikka"));
        GameObject prefab = character.GameplayPrefab;
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.activeSelf, Is.True);
        Assert.That(prefab.GetComponentsInChildren<Player>(true), Has.Length.EqualTo(1));
        Assert.That(prefab.GetComponentInChildren<GroundChecker>(true), Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren<Camera>(true), Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren(weaponType, true), Is.Not.Null);
        PlayerWeaponController weapons = prefab.GetComponentInChildren<PlayerWeaponController>(true);
        Assert.That(new SerializedObject(weapons).FindProperty("mainWeapon").objectReferenceValue, Is.Not.Null);
    }

    [Test]
    public void SessionKeepsTheChosenDefinitionWithoutReplacingItWithDefault()
    {
        MissionSessionManager session = Create("Test Session").AddComponent<MissionSessionManager>();
        MissionDefinition mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>("Assets/Resources/Missions/PrototypeMission.asset");
        foreach (string name in new[] { "Rikka", "Yukari", "Sera", "Arisa", "Mayu" })
        {
            PlayableCharacterDefinition selected = Character(name);
            Assert.That(session.BeginMission(mission, selected), Is.True);
            Assert.That(session.CurrentRequest.Character, Is.SameAs(selected));
            Assert.That(session.CurrentRequest.Mission, Is.SameAs(mission));
        }
        session.ClearMission();
        PlayableCharacterDefinition invalid = ScriptableObject.CreateInstance<PlayableCharacterDefinition>();
        try
        {
            Assert.That(session.BeginMission(mission, invalid), Is.False);
            Assert.That(session.CurrentRequest, Is.Null);
        }
        finally { Object.DestroyImmediate(invalid); }
    }

    [TestCase("Rikka")]
    [TestCase("Yukari")]
    [TestCase("Sera")]
    [TestCase("Arisa")]
    [TestCase("Mayu")]
    public void BattleBackgroundRebindsFromOldSceneCameraToChosenPlayer(string name)
    {
        // Exercise the mission binding without touching the user's open scene backgrounds.
        Scene originalScene = SceneManager.GetActiveScene();
        Scene testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        createdScenes.Add(testScene);
        SceneManager.SetActiveScene(originalScene);
        Camera oldCamera = Create("Old Scene Camera", typeof(Camera)).GetComponent<Camera>();
        SceneManager.MoveGameObjectToScene(oldCamera.gameObject, testScene);
        oldCamera.orthographic = true;
        oldCamera.gameObject.SetActive(false);
        HorizontalTiledBackground background = Create("Background", typeof(SpriteRenderer))
            .AddComponent<HorizontalTiledBackground>();
        SceneManager.MoveGameObjectToScene(background.gameObject, testScene);
        Set(background, "targetCamera", oldCamera);

        GameObject instance = Object.Instantiate(Character(name).GameplayPrefab);
        created.Add(instance);
        SceneManager.MoveGameObjectToScene(instance, testScene);
        Player player = instance.GetComponentInChildren<Player>();
        MissionRuntimeController runtime = Create("Mission Runtime").AddComponent<MissionRuntimeController>();
        SceneManager.MoveGameObjectToScene(runtime.gameObject, testScene);
        typeof(MissionRuntimeController).GetMethod("BindPlayerBackgrounds", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(runtime, new object[] { player });

        Camera chosenCamera = background.TargetCamera;
        Assert.That(chosenCamera, Is.Not.Null);
        Assert.That(chosenCamera.isActiveAndEnabled, Is.True);
        Assert.That(chosenCamera.CompareTag("MainCamera"), Is.True);
        Assert.That(chosenCamera.transform.IsChildOf(player.transform), Is.True);
        Assert.That(chosenCamera, Is.Not.SameAs(oldCamera));

        Texture2D texture = new Texture2D(2, 2);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f,
            1f, 0, SpriteMeshType.FullRect);
        SpriteRenderer renderer = background.GetComponent<SpriteRenderer>();
        try
        {
            renderer.sprite = sprite;
            background.transform.position = new Vector3(-17f, 4f, 1f);
            instance.transform.position += Vector3.right * 12f;
            typeof(HorizontalTiledBackground).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(background, null);
            Assert.That(renderer.bounds.center.x, Is.EqualTo(chosenCamera.transform.position.x).Within(0.001f));
            Assert.That(background.transform.position.y, Is.EqualTo(4f));
            Assert.That(background.transform.position.z, Is.EqualTo(1f));
        }
        finally
        {
            renderer.sprite = null;
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void SelectedIconIsSnapshottedBeforeCursorIsHidden()
    {
        TraverseTarget first = Create("First Icon", typeof(RectTransform)).AddComponent<TraverseTarget>();
        TraverseTarget second = Create("Second Icon", typeof(RectTransform)).AddComponent<TraverseTarget>();
        TraverseCursor cursor = Create("Cursor", typeof(RectTransform)).AddComponent<TraverseCursor>();
        LobbyStandingCharacterController standing = Create("Standing", typeof(SpriteRenderer), typeof(Animator))
            .AddComponent<LobbyStandingCharacterController>();
        Set(standing, "standingRenderer", standing.GetComponent<SpriteRenderer>());
        Set(standing, "cursor", cursor);
        LobbyStandingCharacterEntry firstEntry = new();
        LobbyStandingCharacterEntry secondEntry = new();
        Set(firstEntry, "target", first);
        Set(firstEntry, "playableCharacter", Character("Rikka"));
        Set(firstEntry, "useCutinStartPosition", true);
        Set(firstEntry, "cutinStartPosition", new Vector3(-120f, 80f, 0f));
        Set(secondEntry, "target", second);
        Set(secondEntry, "playableCharacter", Character("Sera"));
        Set(secondEntry, "useCutinStartPosition", true);
        Set(secondEntry, "cutinStartPosition", new Vector3(220f, -40f, 0f));
        Set(standing, "characters", new List<LobbyStandingCharacterEntry> { firstEntry, secondEntry });
        RectTransform circle = Create("Cut-in Circle", typeof(RectTransform)).GetComponent<RectTransform>();
        Set(standing, "transformationCutinCircle", circle);
        // Selection wins over an unrelated focus value.
        Set(cursor, "currentTarget", second);
        Set(cursor, "selectedTarget", first);
        Set(cursor, "currentVisualState", TraverseCursorVisualState.Selected);
        Assert.That(standing.TryPrepareLaunchCharacter(out PlayableCharacterDefinition chosen), Is.True);
        Assert.That(chosen, Is.SameAs(Character("Rikka")));
        Assert.That(standing.ApplyFocusedCutinStartPosition(), Is.True);
        Assert.That(circle.anchoredPosition3D, Is.EqualTo(firstEntry.CutinStartPosition));
        Set(cursor, "currentTarget", null);
        Set(cursor, "selectedTarget", null);
        standing.ShowCharacter(null);
        Assert.That(standing.BeginFocusedTransformation(), Is.True);
        Assert.That(standing.FocusedCharacter, Is.SameAs(firstEntry));
        circle.anchoredPosition3D = Vector3.zero;
        Assert.That(standing.ApplyFocusedCutinStartPosition(), Is.True);
        Assert.That(circle.anchoredPosition3D, Is.EqualTo(firstEntry.CutinStartPosition));
        standing.CompleteFocusedTransformation();
        // An idle cursor launches its focused icon instead.
        Set(cursor, "currentVisualState", TraverseCursorVisualState.Idle);
        Set(cursor, "currentTarget", second);
        Assert.That(standing.TryPrepareLaunchCharacter(out chosen), Is.True);
        Assert.That(chosen, Is.SameAs(Character("Sera")));
        Assert.That(standing.ApplyFocusedCutinStartPosition(), Is.True);
        Assert.That(circle.anchoredPosition3D, Is.EqualTo(secondEntry.CutinStartPosition));
        standing.CancelPreparedLaunchCharacter();
    }

    [Test]
    public void SetPositonCapturesOnlyChosenEntryInUICoordinatesAndSupportsUndo()
    {
        LobbyStandingCharacterController standing = Create("Capture Standing", typeof(SpriteRenderer), typeof(Animator))
            .AddComponent<LobbyStandingCharacterController>();
        RectTransform parent = Create("Canvas Parent", typeof(RectTransform)).GetComponent<RectTransform>();
        parent.position = new Vector3(200f, 300f, 10f);
        parent.localScale = new Vector3(2f, 3f, 1f);
        RectTransform circle = Create("Capture Circle", typeof(RectTransform)).GetComponent<RectTransform>();
        circle.SetParent(parent, false);
        circle.anchoredPosition3D = new Vector3(22f, 33f, 4f);
        circle.localScale = Vector3.one * 0.5f;
        circle.localRotation = Quaternion.Euler(0f, 0f, 15f);
        Set(standing, "transformationCutinCircle", circle);
        LobbyStandingCharacterEntry first = new(), second = new();
        Set(first, "target", Create("Capture First", typeof(RectTransform)).AddComponent<TraverseTarget>());
        Set(second, "target", Create("Capture Second", typeof(RectTransform)).AddComponent<TraverseTarget>());
        Set(standing, "characters", new List<LobbyStandingCharacterEntry> { first, second });
        using SerializedObject serialized = new(standing);
        SerializedProperty entry = serialized.FindProperty("characters").GetArrayElementAtIndex(1);
        Undo.IncrementCurrentGroup();
        Assert.That(LobbyStandingCharacterEntryDrawer.CapturePosition(entry), Is.True);
        serialized.Update();
        Assert.That(entry.FindPropertyRelative("useCutinStartPosition").boolValue, Is.True);
        Assert.That(entry.FindPropertyRelative("cutinStartPosition").vector3Value, Is.EqualTo(new Vector3(22f, 33f, 4f)));
        SerializedProperty untouched = serialized.FindProperty("characters").GetArrayElementAtIndex(0);
        Assert.That(untouched.FindPropertyRelative("useCutinStartPosition").boolValue, Is.False);
        Assert.That(untouched.FindPropertyRelative("cutinStartPosition").vector3Value, Is.EqualTo(Vector3.zero));
        Assert.That(circle.localScale, Is.EqualTo(Vector3.one * 0.5f));
        Assert.That(Mathf.DeltaAngle(circle.localEulerAngles.z, 15f), Is.EqualTo(0f).Within(0.001f));
        Undo.PerformUndo();
        serialized.Update();
        Assert.That(entry.FindPropertyRelative("useCutinStartPosition").boolValue, Is.False);
        Assert.That(entry.FindPropertyRelative("cutinStartPosition").vector3Value, Is.EqualTo(Vector3.zero));
        Undo.IncrementCurrentGroup();
    }

    [Test]
    public void UnconfiguredCutinEntryLeavesExistingPositionUntouched()
    {
        LobbyStandingCharacterController standing = Create("No Cutin Override", typeof(SpriteRenderer), typeof(Animator))
            .AddComponent<LobbyStandingCharacterController>();
        RectTransform circle = Create("Default Circle", typeof(RectTransform)).GetComponent<RectTransform>();
        circle.anchoredPosition3D = new Vector3(-336f, 130f, 0f);
        Set(standing, "transformationCutinCircle", circle);
        Set(standing, "focusedCharacter", new LobbyStandingCharacterEntry());
        Assert.That(standing.ApplyFocusedCutinStartPosition(), Is.False);
        Assert.That(circle.anchoredPosition3D, Is.EqualTo(new Vector3(-336f, 130f, 0f)));
    }

    [Test]
    public void SceneCutinClipDoesNotOverwriteCharacterStartPosition()
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Scenes/anim/transformCutin.anim");
        Assert.That(clip, Is.Not.Null);
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.path != "Canvas/transformCutinCircle") continue;
            Assert.That(binding.propertyName.StartsWith("m_AnchoredPosition")
                || binding.propertyName.StartsWith("m_LocalPosition"), Is.False, binding.propertyName);
        }
        string source = File.ReadAllText("Assets/Scripts/Lobby/LobbyMissionReadyController.cs");
        int apply = source.IndexOf("standingCharacterController?.ApplyFocusedCutinStartPosition();");
        Assert.That(apply, Is.GreaterThanOrEqualTo(0));
        Assert.That(source.IndexOf("PlayState(stateName, 0f, 1f);", apply), Is.GreaterThan(apply));
    }
}
