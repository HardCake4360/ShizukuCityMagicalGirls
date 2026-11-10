using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class BattleEntryTests
{
    private Scene scene;
    private Gamepad pad;
    private readonly List<InputAction> actions = new();
    private Player player;
    private BattleCameraRig rig;
    private BattleEntryDirector entry;
    private PlayerWeaponController weapons;
    private int starts;
    private readonly List<Object> animationAssets = new();

    private GameObject Create(string name)
    {
        GameObject result = new(name);
        SceneManager.MoveGameObjectToScene(result, scene);
        return result;
    }
    private static void Set(object target, string field, object value)
    {
        System.Type type = target.GetType();
        FieldInfo info = null;
        while (type != null && info == null)
        { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
        Assert.That(info, Is.Not.Null, field);
        info.SetValue(target, value);
    }
    private static void Call(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    private void Input(GamepadState state)
    {
        InputSystem.QueueStateEvent(pad, state);
        InputSystem.Update();
    }

    [SetUp]
    public void Setup()
    {
        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        pad = InputSystem.AddDevice<Gamepad>();
        InputSystem.SetDeviceUsage(pad, "BattleEntryTest");
        player = Create("Player").AddComponent<Player>();
        Call(player, "Awake");
        player.SetMovementDirection(Vector2.up);
        weapons = player.gameObject.AddComponent<PlayerWeaponController>();
        Call(player, "OnEnable");
        Call(weapons, "Awake");
        Call(weapons, "OnEnable");
        foreach (InputAction action in player.GetGameplayInputActions())
        {
            action.Disable();
            for (int i = 0; i < action.bindings.Count; i++)
            {
                string path = action.bindings[i].path;
                action.ApplyBindingOverride(i, new InputBinding
                {
                    overridePath = path.StartsWith("<Gamepad>")
                        ? path.Replace("<Gamepad>", "<Gamepad>{BattleEntryTest}") : string.Empty
                });
            }
            action.Enable();
            actions.Add(action);
        }
        Camera camera = Create("Battle Camera").AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 4f;
        rig = camera.gameObject.AddComponent<BattleCameraRig>();
        rig.BindPlayer(player);
        entry = Create("Entry").AddComponent<BattleEntryDirector>();
        Assert.That(entry.Prepare(player, rig, () => starts++), Is.True);
        starts = 0;
    }

    [TearDown]
    public void Cleanup()
    {
        if (entry != null) entry.Abort();
        if (player != null) Call(player, "OnDisable");
        if (weapons != null) Call(weapons, "OnDisable");
        foreach (InputAction action in actions) action.Disable();
        if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        foreach (Object asset in animationAssets) Object.DestroyImmediate(asset);
        animationAssets.Clear();
        foreach (InputAction action in actions) action.Dispose();
        actions.Clear();
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
    }

    private void RevealAndArm()
    {
        entry.BeginReveal();
        entry.Advance(1f);
        entry.ArmFirstInput();
    }

    private AnimationClip CreateAfterClip(float duration, bool loop, out SpriteRenderer body, out Sprite first, out Sprite last)
    {
        body = Create("Body").AddComponent<SpriteRenderer>();
        body.transform.SetParent(player.transform);
        Animator animator = body.gameObject.AddComponent<Animator>();
        Set(player, "spriteAnimator", animator);
        Texture2D texture = new(8, 8);
        first = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
        last = Sprite.Create(texture, new Rect(4, 4, 4, 4), Vector2.one * 0.5f);
        AnimationClip clip = new() { frameRate = 12f };
        animationAssets.Add(texture);
        animationAssets.Add(first);
        animationAssets.Add(last);
        animationAssets.Add(clip);
        AnimationUtility.SetObjectReferenceCurve(clip,
            EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite"),
            new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = first },
                new ObjectReferenceKeyframe { time = 0.25f, value = last },
                new ObjectReferenceKeyframe { time = duration, value = last }
            });
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    [Test]
    public void AfterClipStartsAtRevealOnlyOnceAndHoldsLastSpriteUntilInput()
    {
        AnimationClip clip = CreateAfterClip(0.5f, true, out SpriteRenderer body, out Sprite first, out Sprite last);
        entry.Prepare(player, rig, () => starts++, clip);
        Assert.That(player.IsPlayingBattleEntryTransformation, Is.False);
        entry.BeginReveal();
        Assert.That(player.IsPlayingBattleEntryTransformation, Is.True);
        Assert.That(body.sprite, Is.SameAs(first));
        entry.Advance(0.2f);
        entry.BeginReveal(); // A repeated request must not restart the clip or zoom.
        entry.Advance(0.1f);
        Assert.That(body.sprite, Is.SameAs(last));
        entry.Advance(2f);
        entry.ArmFirstInput();
        Assert.That(player.IsPlayingBattleEntryTransformation, Is.False);
        Call(player, "Update");
        entry.Advance(2f);
        Assert.That(body.sprite, Is.SameAs(last));
        Assert.That(player.IsBattleEntryLocked, Is.True);
        Input(new GamepadState().WithButton(GamepadButton.South));
        Call(entry, "Update");
        Assert.That(player.IsBattleEntryLocked, Is.False);
        Assert.That((bool)typeof(Player).GetField("hasEntryAnimationGraph", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(player), Is.False);
    }

    [Test]
    public void LongAfterClipKeepsInputLockedBeyondZoomCompletionAndAbortCleansUp()
    {
        AnimationClip clip = CreateAfterClip(2f, false, out _, out _, out _);
        entry.Prepare(player, rig, () => starts++, clip);
        entry.BeginReveal();
        entry.Advance(1f);
        entry.ArmFirstInput();
        Assert.That(entry.IsRevealing, Is.True);
        Assert.That(entry.IsWaitingForInput, Is.False);
        Assert.That(player.IsPlayingBattleEntryTransformation, Is.True);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(2.2f).Within(0.001f));
        entry.Abort();
        Assert.That(player.IsPlayingBattleEntryTransformation, Is.False);
        Assert.That(player.IsBattleEntryLocked, Is.False);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(4f));
    }

    [Test]
    public void LaunchRequestCarriesTheAssignedAfterClipAcrossScenes()
    {
        AnimationClip clip = new();
        animationAssets.Add(clip);
        MissionLaunchRequest request = new(null, null, clip);
        Assert.That(request.BattleEntryTransformation, Is.SameAs(clip));
        string lobby = File.ReadAllText("Assets/Scripts/Lobby/LobbyMissionReadyController.cs");
        StringAssert.DoesNotContain("standingCharacterController.StartAfterTransformation()", lobby);
        StringAssert.Contains("standingCharacterController.FocusedCharacter?.TransformationAfter", lobby);
    }

    [Test]
    public void NoInputHoldsPlayerAndCloseUpWithoutStartingCombat()
    {
        Vector3 origin = player.transform.position;
        RevealAndArm();
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(2.2f).Within(0.001f));
        Assert.That(player.IsBattleEntryLocked, Is.True);
        Assert.That(player.StartVelocityOverride(this, Vector2.right, 5f, 1f, 1f), Is.False);
        player.transform.position = origin + Vector3.right;
        Call(player, "Update");
        Call(entry, "Update");
        Assert.That(player.transform.position, Is.EqualTo(origin));
        Assert.That(player.Velocity, Is.EqualTo(Vector2.zero));
        Assert.That(starts, Is.Zero);
    }

    [Test]
    public void FreshInputReleasesStandingPoseAndRestoresExactAuthoredSize()
    {
        Transform visual = Create("Visual").transform;
        visual.SetParent(player.transform);
        Set(player, "spriteZRotationTransform", visual);
        Set(player, "spriteXRotationTransform", visual);
        Set(player, "spriteRotationOffset", 90f);
        player.LockBattleEntry(entry, -90f);
        Assert.That(Mathf.DeltaAngle(visual.localEulerAngles.z, 0f), Is.EqualTo(0f).Within(0.001f));
        RevealAndArm();
        Input(new GamepadState().WithButton(GamepadButton.South));
        Call(entry, "Update");
        Assert.That(player.IsBattleEntryLocked, Is.False);
        Assert.That(player.HasVisualDirectionOverride, Is.False);
        Assert.That(Mathf.DeltaAngle(visual.localEulerAngles.z, 90f), Is.EqualTo(0f).Within(0.001f));
        Assert.That(starts, Is.EqualTo(1));
        entry.Advance(0.2f);
        Assert.That(rig.OutputCamera.orthographicSize, Is.GreaterThan(2.2f).And.LessThan(4f));
        entry.Advance(1f);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(4f));
        Call(entry, "Update");
        Assert.That(starts, Is.EqualTo(1));
    }

    [Test]
    public void HeldInputAcrossLoadingNeedsReleaseAndNewPress()
    {
        Input(new GamepadState().WithButton(GamepadButton.South));
        RevealAndArm();
        Call(entry, "Update");
        Assert.That(starts, Is.Zero);
        Input(new GamepadState());
        Call(entry, "Update");
        Assert.That(starts, Is.Zero);
        Input(new GamepadState().WithButton(GamepadButton.South));
        Call(entry, "Update");
        Assert.That(starts, Is.EqualTo(1));
    }

    [Test]
    public void HeldBombCanBeUsedAfterAnotherControlStartsCombat()
    {
        InputAction bomb = actions.Find(action => action.name == "Bomb");
        Assert.That(bomb, Is.Not.Null);
        Input(new GamepadState().WithButton(GamepadButton.LeftShoulder));
        RevealAndArm();
        Input(new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.South));
        Call(entry, "Update");
        Assert.That(starts, Is.EqualTo(1));
        Assert.That(player.TryAcceptGameplayInput(bomb), Is.False);
        Input(new GamepadState());
        Assert.That(player.TryAcceptGameplayInput(bomb), Is.True);
    }

    [Test]
    public void FirstAttackInputStartsCombatAndIsNotLostAtAnalogPressThreshold()
    {
        RevealAndArm();
        Input(new GamepadState { rightTrigger = 0.1f });
        Call(entry, "Update");
        Assert.That(starts, Is.Zero);
        Input(new GamepadState { rightTrigger = 1f });
        Assert.That(starts, Is.EqualTo(1));
        Assert.That(player.IsBattleEntryLocked, Is.False);
        Assert.That(weapons.HasMainAttackInput, Is.True);
        Assert.That((bool)typeof(PlayerWeaponController).GetField("mainPressAccepted",
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(weapons), Is.True);
    }

    [Test]
    public void RevealCannotBeInterruptedAndAbortRestoresCamera()
    {
        entry.BeginReveal();
        entry.Advance(0.3f);
        entry.ArmFirstInput();
        Input(new GamepadState().WithButton(GamepadButton.South));
        Call(entry, "Update");
        Assert.That(starts, Is.Zero);
        Assert.That(player.IsBattleEntryLocked, Is.True);
        entry.Abort();
        Assert.That(player.IsBattleEntryLocked, Is.False);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(4f));
    }

    [Test]
    public void DamageIgnoredOnlyUntilFirstInput()
    {
        int hp = player.CurrentHealth;
        player.TakeDamage(1, Vector2.zero, Vector2.left, null);
        Assert.That(player.CurrentHealth, Is.EqualTo(hp));
        RevealAndArm();
        Input(new GamepadState().WithButton(GamepadButton.South));
        Call(entry, "Update");
        player.TakeDamage(1, Vector2.zero, Vector2.left, null);
        Assert.That(player.CurrentHealth, Is.EqualTo(hp - 1));
    }

    [Test]
    public void SceneCameraDisablesLegacyOutputButPreservesRenderTextureCamera()
    {
        Camera legacy = Create("Old Camera").AddComponent<Camera>();
        legacy.transform.SetParent(player.transform);
        Camera textureCamera = Create("Texture Camera").AddComponent<Camera>();
        textureCamera.transform.SetParent(player.transform);
        RenderTexture texture = new(16, 16, 0);
        try
        {
            textureCamera.targetTexture = texture;
            rig.BindPlayer(player);
            Assert.That(legacy.enabled, Is.False);
            Assert.That(textureCamera.enabled, Is.True);
            Assert.That(rig.OutputCamera.enabled, Is.True);
            Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(4f));
            player.transform.position = new Vector3(2f, 3f, 0f);
            rig.FollowNow();
            Assert.That(rig.transform.position.x, Is.EqualTo(2f));
            Assert.That(rig.transform.position.y, Is.EqualTo(3f));
        }
        finally { textureCamera.targetTexture = null; Object.DestroyImmediate(texture); }
    }

    private void ImmediateCameraFollow()
    {
        entry.Abort();
        Set(rig, "positionSmoothTime", 0f);
        Set(rig, "lookAheadSmoothTime", 0f);
    }

    [Test]
    public void CameraLeadsVelocityThenUsesHeadingWhenStopped()
    {
        ImmediateCameraFollow();
        Set(player, "velocity", Vector3.right * 3f);
        rig.Advance(1f);
        Assert.That(rig.CurrentLookAhead.x, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(rig.CurrentLookAhead.y, Is.Zero.Within(0.001f));
        Set(player, "velocity", Vector3.zero);
        player.SetMovementDirection(Vector2.left);
        player.transform.position = new Vector3(2f, 3f, 0f);
        rig.Advance(1f);
        Assert.That(rig.transform.position.x, Is.EqualTo(1.4f).Within(0.001f));
        Assert.That(rig.transform.position.y, Is.EqualTo(3f).Within(0.001f));
        Assert.That(rig.transform.position.z, Is.EqualTo(-1.482f).Within(0.001f));
        player.LockBattleEntry(entry, -90f);
        rig.Advance(1f);
        Assert.That(rig.CurrentLookAhead, Is.EqualTo(Vector2.zero));
        Assert.That(rig.transform.position.x, Is.EqualTo(2f).Within(0.001f));
    }

    [TestCase(WeaponAnimationPhase.Ready)]
    [TestCase(WeaponAnimationPhase.Charge)]
    [TestCase(WeaponAnimationPhase.Hold)]
    [TestCase(WeaponAnimationPhase.Release)]
    [TestCase(WeaponAnimationPhase.Attack)]
    public void AttackAndChargeAnimationPhasesLeadAimInsteadOfMovement(WeaponAnimationPhase phase)
    {
        ImmediateCameraFollow();
        HomingMissileWeapon weapon = Create("Aim Weapon").AddComponent<HomingMissileWeapon>();
        weapon.transform.SetParent(player.transform);
        Set(weapons, "specialWeapon", weapon);
        Set(weapon, "animationPhase", phase);
        Set(weapons, "currentAimDirection", Vector2.right);
        Set(player, "velocity", Vector3.up * 2f);
        rig.Advance(1f);
        Assert.That(weapons.IsAimingForCamera, Is.True);
        Assert.That(rig.CurrentLookAhead.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(rig.CurrentLookAhead.y, Is.Zero.Within(0.001f));
        Set(weapon, "animationPhase", WeaponAnimationPhase.None);
        rig.Advance(1f);
        Assert.That(weapons.IsAimingForCamera, Is.False);
        Assert.That(rig.CurrentLookAhead.y, Is.EqualTo(0.6f).Within(0.001f));
    }

    [Test]
    public void ChargingFlagAlsoAimsAndInactiveWeaponDoesNot()
    {
        ImmediateCameraFollow();
        HomingMissileWeapon weapon = Create("Charge Weapon").AddComponent<HomingMissileWeapon>();
        weapon.transform.SetParent(player.transform);
        Set(weapons, "specialWeapon", weapon);
        Set(weapon, "isCharging", true);
        Assert.That(weapons.IsAimingForCamera, Is.True);
        weapon.enabled = false;
        Assert.That(weapons.IsAimingForCamera, Is.False);
    }

    [Test]
    public void CameraSmoothingSwitchesLeadGraduallyAndDoesNotAdvanceWhilePaused()
    {
        ImmediateCameraFollow();
        Set(rig, "lookAheadSmoothTime", 0.2f);
        Set(player, "velocity", Vector3.right);
        rig.FollowNow();
        Set(player, "velocity", Vector3.up);
        rig.Advance(0.05f);
        Assert.That(rig.CurrentLookAhead.x, Is.GreaterThan(0f).And.LessThan(0.6f));
        Assert.That(rig.CurrentLookAhead.y, Is.GreaterThan(0f).And.LessThan(0.6f));
        Vector2 offset = rig.CurrentLookAhead;
        rig.Advance(0f);
        Assert.That(rig.CurrentLookAhead, Is.EqualTo(offset));
        for (int i = 0; i < 60; i++) rig.Advance(0.05f);
        Assert.That(rig.CurrentLookAhead.y, Is.EqualTo(0.6f).Within(0.001f));
    }

    [Test]
    public void SceneCameraDoesNotCopyLegacyPoseProjectionOrZoom()
    {
        Camera old = Create("Old Player Output").AddComponent<Camera>();
        old.transform.SetParent(player.transform);
        old.transform.localPosition = new Vector3(100f, 200f, -300f);
        old.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
        old.orthographic = false;
        old.orthographicSize = 99f;
        rig.BindPlayer(player);
        Assert.That(old.enabled, Is.False);
        Assert.That(rig.OutputCamera.orthographic, Is.True);
        Assert.That(rig.NormalSize, Is.EqualTo(4f));
        Assert.That(rig.transform.position.z, Is.EqualTo(-1.482f).Within(0.001f));
        Assert.That(rig.transform.rotation, Is.EqualTo(Quaternion.identity));
    }

    [Test]
    public void ChargeWideningUsesSceneCameraAndComposesWithEntryPresentation()
    {
        HomingMissileWeapon weapon = Create("Missile Weapon").AddComponent<HomingMissileWeapon>();
        weapon.transform.SetParent(player.transform);
        weapon.Initialize(player);
        Camera old = Create("Old Charge Camera").AddComponent<Camera>();
        old.transform.SetParent(player.transform);
        old.orthographic = true;
        old.orthographicSize = 17f;
        Set(weapon, "chargeCamera", old);
        Set(weapon, "chargeViewSizeMultiplier", 3.8f);
        Set(weapon, "chargeTimer", 2f);
        Set(weapon, "maxChargeTime", 2f);
        Set(weapon, "viewLerpSpeed", 100f);
        rig.BindPlayer(player);
        Assert.That(typeof(HomingMissileWeapon).GetField("chargeCamera", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(weapon), Is.SameAs(rig.OutputCamera));
        Assert.That(typeof(HomingMissileWeapon).GetField("lockOnCamera", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(weapon), Is.SameAs(rig.OutputCamera));
        rig.SetSize(2.2f);
        Call(weapon, "ApplyChargeView");
        rig.Advance(1f);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(2.2f * 3.8f).Within(0.001f));
        Assert.That(old.orthographicSize, Is.EqualTo(17f));
        rig.RestoreNormalSize();
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(4f * 3.8f).Within(0.001f));
        Call(weapon, "RestoreChargeView");
        rig.Advance(1f);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(4f));
    }

    [Test]
    public void ZoomRequestsDoNotCompoundAndDeathCancelsRestorationEvenIfPaused()
    {
        object first = new();
        object second = new();
        rig.RequestViewMultiplier(first, 2f, 100f);
        rig.RequestViewMultiplier(first, 2f, 100f);
        rig.RequestViewMultiplier(second, 3f, 100f);
        rig.Advance(1f);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(12f).Within(0.001f));
        rig.RemoveViewMultiplier(second, 1f);
        rig.Advance(1f);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(8f).Within(0.001f));
        rig.RemoveViewMultiplier(first, 1f);
        rig.Advance(0.1f);
        Assert.That(rig.OutputCamera.orthographicSize, Is.GreaterThan(4f));
        rig.RemoveViewMultiplier(first, 1f, true);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(4f));
        rig.Advance(0f);
        Assert.That(rig.OutputCamera.orthographicSize, Is.EqualTo(4f));
    }

    [TestCase("Assets/Scripts/Missions/BattleEntryDirector.cs.meta")]
    [TestCase("Assets/Scripts/Missions/BattleCameraRig.cs.meta")]
    public void EntryScriptMetadataHasValidUnityGuid(string path)
    {
        string metadata = File.ReadAllText(path);
        Assert.That(Regex.IsMatch(metadata, @"(?m)^guid: [0-9a-fA-F]{32}\r?$"), Is.True, path);
    }

    [Test]
    public void BackgroundValidationDefersGeometryChangesUntilLateUpdate()
    {
        SpriteRenderer renderer = Create("Background").AddComponent<SpriteRenderer>();
        Texture2D texture = new(8, 8);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * 0.5f,
            8f, 0, SpriteMeshType.FullRect);
        animationAssets.Add(texture);
        animationAssets.Add(sprite);
        renderer.sprite = sprite;
        HorizontalTiledBackground background = renderer.gameObject.AddComponent<HorizontalTiledBackground>();
        Set(background, "spriteRenderer", renderer);
        Set(background, "targetCamera", rig.OutputCamera);
        Set(background, "horizontalPadding", -1f);
        renderer.drawMode = SpriteDrawMode.Simple;
        renderer.size = new Vector2(3f, 5f);
        renderer.transform.position = new Vector3(20f, 7f, 2f);
        Call(background, "OnValidate");
        Assert.That(renderer.drawMode, Is.EqualTo(SpriteDrawMode.Simple));
        Assert.That(renderer.size, Is.EqualTo(new Vector2(3f, 5f)));
        Assert.That(renderer.transform.position, Is.EqualTo(new Vector3(20f, 7f, 2f)));

        Call(background, "LateUpdate");
        Assert.That(renderer.drawMode, Is.EqualTo(SpriteDrawMode.Tiled));
        Assert.That(renderer.size.x, Is.EqualTo(2f * rig.OutputCamera.orthographicSize
            * rig.OutputCamera.aspect).Within(0.001f));
        Assert.That(renderer.size.y, Is.EqualTo(5f));
        Assert.That(renderer.bounds.center.x, Is.EqualTo(rig.transform.position.x).Within(0.001f));
        Assert.That(renderer.transform.position.y, Is.EqualTo(7f));
        Assert.That(renderer.transform.position.z, Is.EqualTo(2f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisabledAnimatorCannotPlayAndDoesNotHoldBusyAnimationOpen(bool disableObject)
    {
        Animator animator = Create("Inactive Body").AddComponent<Animator>();
        string controllerPath = AssetDatabase.GUIDToAssetPath("6a03ff938a0e6a145a6cc90afb0a3ef3");
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
        Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
        MethodInfo canUse = typeof(Player).GetMethod("CanUseAnimator", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That((bool)canUse.Invoke(null, new object[] { animator }), Is.True);
        if (disableObject) animator.gameObject.SetActive(false);
        else animator.enabled = false;
        Assert.That((bool)canUse.Invoke(null, new object[] { animator }), Is.False);
        MethodInfo play = typeof(Player).GetMethod("TryPlayAnimatorState", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That((bool)play.Invoke(null, new object[] { animator, "neutral_idle", 0, true }), Is.False);
        MethodInfo finished = typeof(Player).GetMethod("IsAnimationFinished", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That((bool)finished.Invoke(null, new object[] { animator, "neutral_idle", 0 }), Is.True);
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void DisabledPlayerStillClearsAllWeaponBusyBookkeeping()
    {
        HomingMissileWeapon weapon = Create("Disabling Weapon").AddComponent<HomingMissileWeapon>();
        player.enabled = false;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        HashSet<object> sources = (HashSet<object>)typeof(Player).GetField("busySources", flags).GetValue(player);
        HashSet<Weapon> pending = (HashSet<Weapon>)typeof(Player).GetField("pendingBusyAnimationExits", flags).GetValue(player);
        Dictionary<Weapon, int> frames = (Dictionary<Weapon, int>)typeof(Player)
            .GetField("pendingBusyAnimationExitFrames", flags).GetValue(player);
        Dictionary<Weapon, WeaponAnimationPhase> phases = (Dictionary<Weapon, WeaponAnimationPhase>)typeof(Player)
            .GetField("weaponAnimationPhases", flags).GetValue(player);
        sources.Add(weapon);
        pending.Add(weapon);
        frames[weapon] = Time.frameCount;
        phases[weapon] = WeaponAnimationPhase.Charge;
        player.ExitBusyStateImmediately(weapon);
        Assert.That(sources.Contains(weapon), Is.False);
        Assert.That(pending.Contains(weapon), Is.False);
        Assert.That(frames.ContainsKey(weapon), Is.False);
        Assert.That(phases.ContainsKey(weapon), Is.False);
        LogAssert.NoUnexpectedReceived();
    }

    [TestCase("Assets/Resources/Missions/Enemies/PrototypeBoss.asset", "prefab", "Assets/Prefab/projectiles/Enemy.prefab", "6103461773506205544")]
    [TestCase("Assets/Resources/Missions/Enemies/PrototypeEnemy.asset", "prefab", "Assets/Prefab/projectiles/Enemy.prefab", "6103461773506205544")]
    [TestCase("Assets/Resources/Missions/Characters/DefaultCharacter.asset", "gameplayPrefab", "Assets/Prefab/Player_default.prefab", "6247497842821643870")]
    public void PrototypeDefinitionsReferenceActualPrefabRoot(string definitionPath, string field, string prefabPath, string expectedFileId)
    {
        Object definition = AssetDatabase.LoadAssetAtPath<ScriptableObject>(definitionPath);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(definition, Is.Not.Null);
        Assert.That(prefab, Is.Not.Null);
        using SerializedObject serialized = new(definition);
        Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.SameAs(prefab));
        Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab, out string _, out long fileId), Is.True);
        Assert.That(fileId.ToString(), Is.EqualTo(expectedFileId));
        if (field == "prefab") Assert.That(prefab.GetComponentInChildren<Enemy>(true), Is.Not.Null);
        else Assert.That(prefab.GetComponentInChildren<Player>(true), Is.Not.Null);
    }

    [Test]
    public void FailedBossSpawnDoesNotEnterBossFightState()
    {
        EnemyDefinition boss = ScriptableObject.CreateInstance<EnemyDefinition>();
        MissionDefinition mission = ScriptableObject.CreateInstance<MissionDefinition>();
        animationAssets.Add(boss);
        animationAssets.Add(mission);
        Set(boss, "id", "invalid-boss");
        Set(boss, "prefab", Create("Missing Enemy Component"));
        Set(mission, "boss", boss);
        MissionRuntimeController runtime = Create("Mission").AddComponent<MissionRuntimeController>();
        Set(runtime, "mission", mission);
        Set(runtime, "bossSpawnPoint", Create("Boss Point").transform);
        Set(runtime, "<State>k__BackingField", MissionRuntimeState.FightingRegularEnemies);
        LogAssert.Expect(LogType.Error, "Enemy definition 'invalid-boss' needs a valid Enemy prefab root GameObject. Reassign its Prefab field.");
        Call(runtime, "SpawnBoss");
        Assert.That(runtime.State, Is.EqualTo(MissionRuntimeState.FightingRegularEnemies));
    }

    [Test]
    public void SavedScenesReferenceIndependentCameraAndEntryDirector()
    {
        string battle = File.ReadAllText("Assets/Scenes/GamePlayScene.unity");
        StringAssert.Contains("m_Name: Battle Camera", battle);
        StringAssert.Contains("battleCamera: {fileID: 2302000006}", battle);
        StringAssert.Contains("entryDirector: {fileID: 2200000006}", battle);
        StringAssert.Contains("guid: 88616a94f1a94feca177eb2cf4d77a95", battle);
        StringAssert.Contains("movementLookAheadDistance: 0.6", battle);
        StringAssert.Contains("aimLookAheadDistance: 1", battle);
        StringAssert.DoesNotContain("importPlayerCameraSettings:", battle);
        StringAssert.Contains("whiteOutDuration: 0.4", File.ReadAllText("Assets/Scenes/Lobby.unity"));
    }
}
