# Shizuku City Magical Girls Development Notes

## Purpose

This document tracks implementation notes for gameplay and presentation systems that are not obvious from the Unity scene alone.

## Sprite Physics Rig

Status: Phases 10 and 11 optimization, diagnostics, and verification complete.

The coordinate system, MVP boundary, validation rules, Sprite compatibility
contract, and physics/rendering ownership rules are defined in
`Assets/SpritePhysicsRig/DESIGN.md`. New runtime and editor implementations must
use that document as their serialization contract.

Implemented runtime data:

- `SpriteRigAsset`
- Normalized vertices and optional UVs
- Edges and counter-clockwise triangle indices
- Stable-ID bone hierarchy and default per-bone physics settings
- Four influences per vertex
- Read-only validation and explicit stable-ID/weight repair operations

Validation covers geometry connectivity, indices, winding, bone hierarchy,
stable IDs, physics ranges, and vertex weights. Edit Mode regression tests are
under `Assets/SpritePhysicsRig/Tests/Editor`.

Implemented rendering and skinning:

- `SpriteRigCoordinateUtility` maps normalized rig positions through the
  Sprite rect, pivot, and pixels-per-unit.
- Rectangular Sprite texture regions are converted into texture/atlas UVs.
- `CpuSpriteSkinner` calculates bind matrices and applies up to four bone
  influences per vertex.
- `DeformableSpriteRenderer` owns a dynamic Mesh, mirrors SpriteRenderer flip,
  color, material, sorting layer, sorting order, and rendering layer settings.
- The generated `MeshFilter` and `MeshRenderer` are created on a hidden runtime
  child and removed on disable/destroy; only the source `SpriteRenderer` needs
  to exist on the authored GameObject.
- Invalid input restores the source SpriteRenderer instead of leaving the
  object invisible.
- Runtime Sprite, rig asset, and flip changes trigger a safe mesh rebuild.
- Dynamic mesh bounds are calculated in the existing vertex pass buffers
  without calling Unity's general-purpose bounds recalculation.

Quick setup:

1. Create `Create > 2D Rig > Sprite Rig Asset`.
2. Use the asset context menu `Initialize Default Quad`.
3. Add `DeformableSpriteRenderer` to a GameObject with a SpriteRenderer.
4. Assign the rig asset. The source SpriteRenderer is replaced visually by the
   generated mesh while the component is enabled.
5. Call `SetBoneLocalPose(index, position, rotation)` or its stable-ID overload
   to deform the Sprite. Call `ResetToBindPose()` to restore the authored pose.

Current compatibility boundary:

- Sprite draw mode must be `Simple`.
- Rotated or tight-packed Sprite Atlas entries are rejected with a targeted
  warning.
- SpriteMask and complete URP 2D lighting compatibility remain later work.

Physics implementation:

- `BonePhysicsSolver` owns endpoint Verlet state independently of rendering.
- Dynamic bones respond to gravity, persistent external force, object movement
  inertia, damping, mass, and rest-pose stiffness.
- Every solve enforces authored bone length, relative angle limits, and maximum
  angular speed.
- Fixed bones always use the authored bind pose. Animated bones follow their
  target pose without force integration, while Dynamic bones use Verlet state.
- Parent-first evaluation supports bone arrays whose parents do not appear
  before their children.
- Physics settings come from each rig bone and can be overridden per component
  using the bone stable ID.
- Stable bone IDs can bind Animated bones to scene Transforms. Position and
  rotation driving can be enabled independently per binding.
- Rotation binding is additive: the Target's local Z rotation is added to the
  bone's authored local rotation, so a zero-rotation Target preserves the bind
  pose instead of replacing it with a world-space direction.
- Translation, rotation, and scale of the component reference frame are
  reprojected into Verlet endpoint history so ordinary character motion retains
  inertia instead of being mistaken for local bone motion.
- Configurable distance, rotation, and scale-ratio thresholds reset simulation
  after teleports.
- Per-bone collision radius drives endpoint circles or bone capsules against
  Unity `Collider2D` environments.
- Endpoint collision uses a swept circle query before overlap resolution to
  reduce tunnelling. Collision layers, trigger inclusion, friction,
  bounciness, skin width, and solve iterations are component settings.
- Colliders in the rig owner's character hierarchy are ignored. Call
  `RefreshCollisionIgnoreList()` after adding or removing those colliders at
  runtime. Self-collision is not implemented.

Component simulation order:

1. `FixedUpdate` samples Transform bindings and reprojects prior Verlet state
   from the previous component frame into the current frame.
2. Teleports reset state; ordinary motion proceeds through force integration
   and bone constraints.
3. Continuous and overlap collision queries correct Dynamic bone endpoints,
   then length/angle constraints are solved again.
4. The solver writes the resulting local bone pose into `CpuSpriteSkinner`.
5. `LateUpdate` samples animation again, then deforms and uploads mesh vertices.

Global component physics fields:

- `Physics Enabled`: enables automatic Play Mode simulation.
- `Simulate When Offscreen`: when disabled, pauses physics while the generated
  renderer is culled and keeps the reference frame synchronized.
- `Gravity`: rig-space acceleration, with `(0, -1)` as the default.
- `External Force`: persistent rig-space force applied every fixed step.
- `Stiffness Multiplier`: scales every dynamic bone's rest-pose stiffness.
- `Damping Multiplier`: scales every dynamic bone's velocity damping.
- `Inertia Multiplier`: scales lag caused by GameObject movement.
- `Constraint Iterations`: parent-chain stabilization passes, from 1 to 16.
- `Teleport Distance/Rotation/Scale Ratio Threshold`: discontinuity limits that
  reset Verlet history.
- `Collision Enabled`: enables environment collision.
- `Collision Shape`: endpoint circles or full bone capsules.
- `Collision Layers` / `Include Trigger Colliders`: query filtering.
- `Collision Radius Multiplier`: scales each bone's authored radius.
- `Collision Friction` / `Collision Bounciness`: tangential damping and normal
  rebound applied to endpoint history.
- `Collision Skin Width` / `Collision Iterations`: separation margin and solve
  passes.

Runtime physics API:

- `PhysicsEnabled`
- `Gravity`
- `ExternalForce`
- `SimulatePhysics(float deltaTime)`
- `ResetSimulation()`
- `SetBonePhysicsSettings(index/id, settings)`
- `TryGetBonePhysicsSettings(index, out settings)`
- `RefreshCollisionIgnoreList()`

Diagnostics and authoring support:

- Selecting a component draws its current bone pose in the Scene view.
- Optional collision gizmos show endpoint circles or capsule outlines using
  the effective per-bone radius.
- The custom Inspector reports accumulated simulation steps plus collision
  query and correction counts from the latest step.
- `Diagnostics` exposes the same values to runtime debug UI without allocating
  a mutable diagnostics object.
- Endpoint and capsule environment collision are both covered by Edit Mode
  regression tests.

## Sprite Rig Editor

Open the editor from `Window > 2D Rig > Sprite Rig Editor`, or use `Open Rig
Editor` in a `SpriteRigAsset` or `DeformableSpriteRenderer` Inspector.

Editor modes:

- `Mesh`: generate a grid or reuse the Sprite Importer's geometry, drag
  vertices, optionally snap to preview pixels, add/delete vertices, and edit
  edges and counter-clockwise triangles.
- `Bone`: select bones in the canvas, edit bind transforms, add roots or child
  bones, and delete bones with parent/weight index remapping.
- `Weight`: edit four numeric influences per vertex, normalize a vertex, paint
  the selected bone with Shift-drag, generate distance weights, or mirror
  weights horizontally.
- `Physics`: edit the selected bone's simulation mode, mass, gravity,
  stiffness, damping, inertia, angle limits, angular speed, and collision
  radius.
- `Preview`: bind a scene `DeformableSpriteRenderer`, then Play, Pause, Step,
  Reset, or Rebuild its simulation without entering Play Mode.

Automatic geometry:

- `Generate Grid` creates a regular normalized mesh with boundary, internal,
  and triangulation edges.
- `Generate From Sprite Geometry` converts `Sprite.vertices` and
  `Sprite.triangles` into normalized rig coordinates and repairs triangle
  winding. This reuses geometry authored/generated by the Sprite Importer; it
  does not perform a new alpha-outline trace.

Automatic weights use inverse squared distance from each vertex to each bind
pose bone segment. The nearest four bones are retained and normalized. All
automatic and destructive editor operations participate in Unity Undo/Redo.

## Pixel Chain Hair Rig

Status: Current approach.

Goal:
- Preserve the original sharp pixel-art hair silhouette by rendering hair as connected sprite pieces.
- Simulate hair as a physics chain instead of drawing procedural lines.
- Support middle-piece sprites, one final tip sprite, inspector-controlled piece count, physics tuning, and sprite deformation.

Implemented file:
- `Assets/Scripts/PixelChainHairRig.cs`

How it works:
- One `PixelChainHairRig` controls one hair clump.
- `Piece Count` is the number of visible sprite pieces and includes the final tip piece.
- The component creates one `SpriteRenderer` child per piece.
- Middle pieces randomly choose from `Middle Sprites`; the final piece uses `Tip Sprite`.
- Random middle-sprite choices are stored so they do not flicker during physics updates.
- A Verlet-style point chain drives the piece positions.
- Each sprite piece has two virtual connection points: one above its sprite center and one below it.
- The upper connection point attaches to the previous joint, and the lower connection point attaches to the next joint.
- Sprite deformation is transform-based: pieces can stretch between those connection points and scale width by index.

Recommended setup:
- Create a child object under the moving character, such as `BackHairRig`.
- Add `PixelChainHairRig` to that object.
- Set `Root` to the hair attachment point on the head.
- Set `Segment Container` to the rig object itself unless there is a separate visual container.
- Add one or more `Middle Sprites`.
- Add one `Tip Sprite`.
- Sprite pivots do not need to be at the connection point; the rig compensates from the sprite bounds center.
- Keep `Motion Source Override` empty so the rig measures parent transform velocity automatically.
- Use the `Rebuild Segments` context menu if child renderers need to be regenerated and re-randomized manually.
- Use `Randomize Middle Sprites` to keep the same piece count but reroll the middle-piece sprite choices.
- Use `SetPieceCount(...)` from code when the hair length must change during runtime.
- Rebuild removes stale managed child objects such as old tip pieces that are no longer referenced.

Important inspector fields:
- `Piece Count`: visible hair piece count, including the final tip.
- `Middle Sprites`: random source sprites for every non-tip piece.
- `Connection Point Units`: inspector-friendly connection distance value.
- `Connection Point Unit Scale`: multiplier for the units value. Effective connection distance is `Units * Unit Scale`, and the physics joint rest distance is twice that result.
- `Rest Direction`: default hang direction, usually `(0, -1)`.
- `Inertia`: how strongly parent movement pulls hair backward.
- `Gravity`: downward force.
- `Stiffness`: force pulling the chain back toward its rest pose.
- `Damping`: how quickly motion settles.
- `Constraint Iterations`: rope-length stability.
- `Enable Lateral Flutter`: toggles side-to-side oscillation layered on top of the chain physics.
- `Flutter Strength`: side-force amplitude.
- `Flutter Frequency`: oscillation speed.
- `Flutter Phase Offset`: phase difference between chain points.
- `Flutter Min Motion Speed`: parent speed where flutter weight is 0.
- `Flutter Max Motion Speed`: parent speed where flutter weight is 1.
- `Flutter Tip Exponent`: makes flutter stronger near the tip.
- `Stretch Sprites To Joint Distance`: scales pieces along their length to stay connected.
- `Width Scale By Piece`: tapers or widens pieces from root to tip.
- `Rotation Offset Degrees`: adjusts sprite alignment if the source art points in a slightly different direction.
- `Show Connection Point Gizmos`: shows the virtual upper and lower connection points in the Scene view while the rig is selected.
- `Connection Point Gizmo Radius`: controls the visible debug point size.

Runtime API:
- `PieceCount`: read-only current visible piece count.
- `SetPieceCount(int newPieceCount)`: changes piece count, keeps existing random middle-sprite choices where possible.
- `SetPieceCount(int newPieceCount, bool randomizeMiddleSprites)`: changes piece count and optionally rerolls middle sprites.
- `SetPieceCount(int newPieceCount, bool randomizeMiddleSprites, bool resetSimulation)`: optionally resets the whole chain pose after resizing.
- If `Piece Count` is changed in the Inspector during Play Mode, the rig detects the mismatch and rebuilds on the next `LateUpdate`.

## Speed Scaled Hair Length

Status: Implemented.

Implemented file:
- `Assets/Scripts/PixelChainHairLengthBySpeed.cs`

How it works:
- `PixelChainHairLengthBySpeed` drives one or more child `PixelChainHairRig` components from motion speed.
- At speed `0`, each assigned rig uses `Min Hair Piece Count`.
- At `Max Motion Speed`, each assigned rig uses `Max Hair Piece Count`.
- Speeds between those values interpolate the target piece count.
- The component measures its own transform velocity by default, or `Motion Source Override` when assigned.

Setup notes:
- Add `PixelChainHairLengthBySpeed` to the character object that owns the hair children.
- Keep `Auto Find Child Hair Rigs` enabled to reuse all child `PixelChainHairRig` components.
- Use `Refresh Hair Rigs` if child hair objects are added or removed in edit mode.
- Set `Min Hair Piece Count` for idle hair length.
- Set `Max Hair Piece Count` for full-speed hair length.
- Set `Max Motion Speed` to the speed that should produce the maximum hair length.
- Enable `Randomize Sprites On Length Change` only if changing length should reroll middle-piece sprites.

Recommended first values:
- `Piece Count`: `5`
- `Connection Point Units`: `4`
- `Connection Point Unit Scale`: `0.01`
- `Rest Direction`: `(0, -1)`
- `Inertia`: `0.7`
- `Gravity`: `0.55`
- `Stiffness`: `8`
- `Damping`: `0.86`
- `Constraint Iterations`: `6`
- `Flutter Strength`: `0.9`
- `Flutter Frequency`: `7`
- `Flutter Phase Offset`: `0.75`
- `Flutter Min Motion Speed`: `0.05`
- `Flutter Max Motion Speed`: `4`
- `Flutter Tip Exponent`: `1.4`

Motion note:
- Lateral flutter uses the direction perpendicular to the current hair chain.
- Flutter is weighted directly by current parent speed: below `Flutter Min Motion Speed` it is off, and at `Flutter Max Motion Speed` it reaches full strength.
- The speed weight is applied immediately, so stopping the parent object also stops the procedural wave force immediately.
- When vertical movement pulls the hair into a mostly vertical shape, the flutter appears as horizontal sway.
- The tip receives more flutter than the root, so the visible motion reads as a wave passing through the hair.

## Pixel Rail Hair Rig

Status: Experimental rail-follow approach.

Implemented file:
- `Assets/Scripts/PixelRailHairRig.cs`

How it works:
- `PixelRailHairRig` keeps the segmented sprite hair idea but removes physics simulation.
- The parent/root object lays down a distance-based rail as it moves.
- Each hair joint samples a point farther back on that rail, like train cars following track.
- Curved player movement produces curved hair because the visible pieces are placed along the recorded path.
- Middle sprites are randomly selected and the final piece uses `Tip Sprite`, like `PixelChainHairRig`.

Setup notes:
- Add `PixelRailHairRig` to a hair object under the moving character.
- Set `Root` to the hair attachment point, or leave it as the rig transform.
- Use `Rail Source Override` only when a different transform should lay down the rail.
- Set `Piece Count`, `Connection Point Units`, and `Connection Point Unit Scale` to define length.
- Use `Reset Rail` when teleporting or when the path should be cleared.
- Rebuild removes stale managed child objects such as old tip pieces that are no longer referenced.

Important inspector fields:
- `Rail Sample Min Distance`: minimum travel distance before recording a new rail point.
- `Rail History Padding`: extra stored rail distance beyond current hair length.
- `Show Rail Gizmos`: displays the recorded rail in Scene view.

## Legacy Hair Experiments

Legacy files:
- `Assets/Scripts/legacy/PixelHairRig.cs`
- `Assets/Scripts/legacy/PixelRopeHairRig.cs`

Notes:
- `PixelHairRig` rotated whole sprite clumps and did not actually deform hair.
- `PixelRopeHairRig` rendered thick procedural lines, but it could not preserve the intended sharp sprite silhouette.
- These are kept for reference only; new hair work should use `PixelChainHairRig`.

## Validation

- `dotnet build ShizukuCityMagicalGirls.sln` passes with 0 warnings and 0 errors after adding `PixelChainHairRig`.

## Mission and Chapter Progression

Runtime definitions are under `Assets/Scripts/Missions`, while authored mission data is loaded from
`Resources/Missions`.

Core ownership:

- `MissionDefinition` owns regular enemy weights, the per-attempt kill target, spawn pacing, and boss.
- `ChapterDefinition` owns its missions, prerequisite achievements, boss achievement gate, and next chapter.
- `MissionSessionManager` carries the selected mission and character from Lobby to GamePlayScene.
- `MissionRuntimeController` resets attempt progress, spawns enemies around the player, spawns one boss at
  the kill target, and reports the result.
- `ChapterProgressionManager` activates the boss achievement only after enough chapter prerequisites are
  `Completed`. A boss kill advances the chapter only while that boss achievement is active.

Boss kills before the gate is active intentionally do not report progress to the boss achievement and do
not advance the chapter. Per-attempt kill counts are never saved; chapter and achievement state are saved.

Prototype assets:

- `Resources/Missions/PrototypeMission.asset`
- `Resources/Missions/Chapters/Chapter01.asset`
- `Resources/Missions/Enemies/PrototypeEnemy.asset`
- `Resources/Missions/Enemies/PrototypeBoss.asset`
- `Resources/Missions/Characters/DefaultCharacter.asset`

The prototype boss currently reuses the regular Enemy prefab and must be replaced when a real boss prefab
is available.

Lobby mission UI setup:

1. Under the Lobby Canvas, author a mission-ready panel with two `TMP_Dropdown` controls for mission and
   character, a launch `Button`, and any desired TMP labels/images.
2. Add `MissionSelectionPanelController` to the panel and assign the dropdowns and launch button. Labels and
   images are optional. Leaving Mission Overrides empty uses the current chapter's missions; leaving
   Characters empty loads unlocked definitions from `Resources/Missions/Characters`.
3. Assign the panel root to `LobbyMissionReadyController.Mission Selection Panel`. It appears only after the
   `mission_ready` animation completes and hides during the reverse transition.
   Code transitions now fade the SpriteRenderer on `SceneAnimator/BlackTile` instead of moving a tile.
   Its Inspector reference can be assigned explicitly; an empty reference resolves the child by name.
   Lobby entry starts at alpha 0. Camera ascent keeps the current X and fades BlackTile 0 to 1 over
   Code Transition Duration (default 0.35s), using the same curve as the camera and lobbyUI fade.
   UA transition 0 (Lobby -> launch_ready) is dispatched only AFTER the code ascent and BlackTile
   fade have reached their endpoints, not when Launch is clicked. It starts before mission_ready/salute,
   so the ready UI can animate alongside the authored reveal. Buttons remain locked until salute ends.
   Failed UA dispatch rolls the camera/BlackTile/lobbyUI back and restores input rather than leaving
   the lobby covered and locked. Return first plays the authored return clip,
   then lowers the camera to its saved lobby position while fading BlackTile 1 to 0 and lobbyUI to 1.
   BlackTile position, rotation, scale and RGB are never modified. The legacy Square remains keyed
   by salute/return and cached/restored as an animation target, but has no code-driven movement.
   No animation clips, launch/return button states or transformation timing are changed.
   Camera Transition Points (Optional) adds two Inspector Transform references: Ascent Destination
   overrides the code ascent target (while preserving the launch-time camera local X), and Descent
   Start Point overrides only the world Y of the code descent start after return finishes, preserving
   the camera's return-end world X/Z. Descent marker X/Z are ignored, even with rotated/scaled parents.
   Positions are converted into camera-parent local coordinates, so markers can have a different parent;
   keep the ascent marker Z at the camera depth. Markers are read on each transition and never moved.
   Empty fields preserve the salute first-frame / return end-position behavior. The descent marker
   is applied while BlackTile alpha is 1, before moving back to the saved lobby camera position.
   These markers affect only code-controlled movement, not the authored salute/return camera curves;
   put them on independent empty GameObjects rather than animated objects or children of the camera.
4. The final launch button requires no persistent `On Click` entry; the panel controller registers it and
   loads `GamePlayScene` through `SceneLoadManager`.

GamePlayScene contains a `Mission Runtime` root. Directly entering that scene runs the prototype fallback
mission with an existing scene Player. Entering through Lobby uses the selected character prefab and disables
the pre-placed Player objects when `Replace Scene Players` is enabled.

## Lobby Character Handoff

### Quest summaries and character unlocks

Achievement teardown must not create singleton objects. `AchievementManager.TryGetExisting` is a
non-creating read-only lookup; character unlock count queries use it instead of Instance. Panel/list
unsubscribe callbacks retain the actual manager they subscribed to. This prevents cursor OnDisable ->
RestoreOpacity -> IsUnlocked from spawning an Achievement Manager after the original was destroyed.
OnApplicationQuit guards against further auto-creation while still saving, and SubsystemRegistration
resets that guard before the next Play session, including when domain reload is disabled.

The saved `SceneAnimator/Canvas/DownSide/Quest` has `QuestSummaryController` attached. Its four TMP text
slots and optional ALM reference are left unassigned for manual Inspector wiring. Assign Progress Text,
Completion Text, Chapter Text, and Character Unlock Text to the corresponding labels. Leaving ALM empty
counts all unlocked achievements; assigning ALM restricts summary counts to that list. Characters can
remain empty to use the Resources definitions. It displays completion percentage and completed/total counts for unique, registered,
non-Inactive achievements in that ALM. ConditionCleared is not Completed until claimed through the AE
button. Summary refreshes subscribe to achievement and current-chapter events and never rebuild/reorder
the AE list. The chapter label only reads `CurrentChapter.ChapterNumber`; no chapter unlock hint is shown.

`QuestSummaryController.Value Colors` exposes six independent Color fields: Progress Percent Color,
Total Achievement Count Color, Completed Achievement Count Color, Chapter Number Color, Remaining
Achievement Count Color, and Character Name Color. Only the corresponding number/name receives a
scoped TMP `<color=#RRGGBBAA>` tag; the surrounding labels retain their TMP text color. Default value
colors are white. Rich Text is enabled and Override Color Tags disabled on assigned labels so the colors
render correctly. Character names are wrapped in noparse tags. Runtime Inspector changes are refreshed
on the next normal frame instead of writing TMP text directly from OnValidate. Existing scene references
are preserved; reassigning the text UI is not required.

Character unlocks are derived from **all** saved Completed achievements (not only the visible Quest list):
Rikka starts unlocked; Yukari requires 5, Sera 10, Arisa 15, and Mayu 20. These thresholds are editable on
each `PlayableCharacterDefinition.Required Completed Achievements`. The existing achievement save data
is reused; there is no second unlock save file and unclaimed conditions do not contribute. The Quest hint
shows the remaining count and next character, e.g. `3개 클리어 시 유카리 언락` after two completions, or
`모든 캐릭터 언락 완료` when finished. Display names on these five definitions are now Korean.

The five saved portrait `TraverseTarget` components bind their `Required Character` to these definitions.
Locked targets cannot be hovered/selected/navigated by the cursor. Standing launch preparation,
the legacy dropdown and MissionSessionManager also validate current unlock state. Locked icons remain
visible at `TraverseTarget.Locked Character Alpha` (default 0.4). Lock and selection dim reasons are
combined using the lower multiplier, not multiplied together. Canceling selection, disabling/refreshing
the cursor, and re-enabling an icon preserve lock dimming. Achievement events fade the lock dim away
over 0.2 unscaled seconds on unlock, retaining any still-applicable selection dim. Original Graphic color
or CanvasGroup alpha is preserved. This does not add lock artwork. Legacy
DefaultCharacter is retained as an unconfigured, locked prototype, excluded from actual selection and
unlock hints. Direct battle-scene testing still uses its pre-placed Player.

There are currently only five authored achievements: completing them unlocks Yukari, but another 15
unique achievements must be authored to reach all four unlock milestones. The original Chapter01 gate
configuration is unchanged, because chapter progression is outside this character-unlock feature.
`CharacterLaunchTests` includes threshold boundaries, unclaimed-state exclusion, selection/launch guards,
summary math, chapter-only display, and retaining in-place list behavior. Its test achievement manager is
isolated from the user's persistent saves.

`LobbyMissionReadyController.Lobby Ui Canvas Group` controls the opacity of the lobby-only UI parent.
When unassigned, Awake searches this scene for a RectTransform named `lobbyUI` (case-insensitive), reuses
its CanvasGroup or adds one. Lobby entry starts at alpha 1. The code camera/cover movement fades it to 0
when entering launch-ready and to 1 during the final return-to-Lobby movement, using the existing
transition duration/curve. It stays at 0 during launch-ready, transformation and the reverse scene clip.
Interaction/raycast blocking is disabled during movement and while hidden, and restored after returning.
Objects are not deactivated, preserving UAE state and open panel contents. Keep the sortie/return buttons,
launch-ready UI, and cut-in outside this lobby-only group. No whole-Canvas alpha is modified.

On entering GamePlayScene, `MissionRuntimeController.Start` requests `BgmKey.Gameplay` from the persistent
`BgmManager`, using its default crossfade (0.5 seconds). The existing library currently maps this key to
Colorful Mess. This works for both Lobby launches and direct battle-scene entry; BGM volume is preserved.
Returning to Lobby uses the existing playlist controller's initial track, Lobby/Funky Road.

The authored Lobby uses the portrait cursor, not the draft character dropdown, as the character source.
`LobbyStandingCharacterController.Characters` binds each `TraverseTarget` to a
`PlayableCharacterDefinition` as well as its standing sprite and transformation clips. The five saved
entries are already wired to Rikka, Yukari, Sera, Arisa, and Mayu.

On the second sortie click, `LobbyMissionReadyController` snapshots the selected target (or the focused
target when Idle) before the launch UI transition hides the cursor. The same entry drives the before/after
transformation, and its definition is retained by `MissionSessionManager` across scene loading.
`MissionRuntimeController` then disables pre-placed Players and creates that definition's gameplay prefab
at `Player Spawn Point`, or at the origin when no spawn point is assigned. An invalid selected prefab
blocks launch instead of silently using a different character.

After preparing the Player, the runtime rebinds every `HorizontalTiledBackground` in that scene to its
active orthographic MainCamera (falling back to its active orthographic camera). This includes inactive
background objects, so later enabling one cannot restore the old scene Player's camera. The background
also reacquires Camera.main when its tracked camera becomes disabled during gameplay. Its existing
horizontal-only following, authored height/depth, padding, and tiled width behavior are preserved.

Character definitions are in `Assets/Resources/Missions/Characters`; the five gameplay prefabs in
`Assets/Prefab/PlayableCharacters` preserve the existing battle characters' visuals, animation controllers,
weapons, physics, and cameras. The original scene Players and legacy DefaultCharacter are preserved for
direct-scene testing. Editing the new prefabs changes the characters used by Lobby launches; editing the
old scene objects no longer changes those launches. For additional characters, create a definition with
a Player prefab and assign it to the matching standing entry's `Playable Character` field.

`Assets/Editor/CharacterLaunchTests.cs` covers all five definitions/loadouts, saved Lobby icon bindings,
session handoff, and preserving the launch choice when cursor focus is cleared during the UI transition.

## Battle Enemy Archetypes

`Assets/Prefab/Enemies` contains three independent `Enemy` subclasses. They reuse the existing
health, hit feedback, defeat event, achievement reporting, and mission kill counter.
The body sprites are existing temporary art tinted orange (regular), mint (speed), and purple (defensive).

- `RegularEnemy`: approaches the active mission player at 2.5 units/sec, firing one projectile straight
  forward every 0.8 seconds. Stopping distance and fire rate are editable; no spread or multi-shot loop.
- `SpeedEnemy`: snapshots a destination 3–6 units from the player, dashes at 16 units/sec, pauses to aim,
  and fires a 24 units/sec projectile. Three dashes/shots are followed by a stationary 1.5-second rest.
  Orbit radii, minimum travel, aim/inter-dash delays, dash count, and rest duration are editable.
- `DefensiveEnemy`: moves along its current facing at 1.5 units/sec and turns at 30 degrees/sec.
  Its `Force Field` child covers the front 160 degrees; side/rear hits damage its health normally.
  Shield radius, thickness, angle, color, and hit flash are editable on `EnemyDirectionalShield`.
  Front protection also gates body hits, so penetrating beams and explosion overlap hits cannot simply
  bypass the shield collider. The shield currently has no separate durability/recharge system.
  Shield colliders resolve to the carrier's single `IDamageable` identity, preventing duplicate
  sword/explosion damage and duplicate missile locks. Hitscan also deduplicates damage per target
  for each shot/tick when both the body and shield are overlapped.

`PrototypeMission.asset` now registers these three `EnemyDefinition` assets with equal spawn weights.
GamePlayScene's `Mission Runtime` enables `Spawn Each Enemy Type Initially`: one of each registered,
positive-weight regular type is spawned first (respecting Max Alive Enemies), then normal weighted
spawning resumes. Disable that checkbox to use only the existing weighted spawn behavior.
The spawner explicitly assigns its prepared Player to every enemy, including the existing boss.
The old prototype enemy/boss assets and unrelated scene objects are preserved.

`EnemyBullet.prefab` uses `EnemyProjectile`, reusing the sniper projectile's swept high-speed collision
while ignoring other enemies and projectiles. Its hit mask is Default + Ground. Damage is delivered
through the existing `IDamageable` interface, now implemented by Player's health system below.

`Assets/Editor/EnemyArchetypeTests.cs` verifies the prefab/mission/scene references, forward barrage,
fixed dash destinations and three-dash rest cycle, friendly projectile filtering, shield geometry,
front protection (including body hits), rear vulnerability, rotation, and shield disabling.

## Player Health and Game Over

`Player` implements `IDamageable`. Its `Health / Max Health` Inspector field defaults to 10; each
character starts at full health in `Awake`. Non-positive damage and damage after death are ignored.
`CurrentHealth`, `MaxHealth`, `IsDead`, `HealthChanged(current, maximum)`, and `Died(player)` are
available for future health HUDs. A lethal hit clamps HP to zero and raises death once. Movement,
attack input, and committed weapon attacks are disabled, but the character GameObject and camera
remain active. Releasing a charged weapon during death does not fire it.

`MissionRuntimeController` subscribes to its actual prepared Player (including Lobby-launched
characters). Death switches to `Failed`, stops the spawn coroutine, and shows `GameOverController`;
failure cannot award boss completion or chapter progression. `MissionFailed(player)` is available
for future result consumers. Combat is paused with `Time.timeScale = 0`, while the result fade and
existing scene loader use unscaled time. The original time scale is restored on scene unload or
controller disable, without creating managers in cleanup callbacks.

GamePlayScene's `Mission Runtime` now has `GameOverController` assigned, with the existing Korean
TMP font. Its default UI is generated only when needed: a full-screen black Dim (alpha 0.75),
GAME OVER, score, and a Lobby-return button, fading in over 0.3 seconds. The overlay renders above
combat UI but below the scene loader and blocks clicks behind it. Mouse and EventSystem UI
navigation remain available while paused. Returning clears the launch request and uses the existing
Lobby loading flow; a rejected loading request leaves the button usable and combat paused.

For a custom UI, assign `Panel` (CanvasGroup), `Game Over Text`, `Score Text`, and `Return To Lobby
Button` on the controller. Put the controller on an always-active scene object, not the panel itself;
the Panel should cover the whole screen, have a GraphicRaycaster, and sit above the battle HUD.
The common combat score system below is now connected. An unassigned provider still defaults to zero.
To replace it with another score system:

- Implement `IGameOverScoreProvider.GetScore()` (returns `long`) on a MonoBehaviour and assign
  `Score Provider`; it is sampled once when game over is shown.
- Alternatively call `SetScoreProvider(provider)` or `SetScore(long score)`. `SetScore` also updates
  an already-open result panel, allowing `MissionFailed` listeners to supply a final result.

`Assets/Editor/PlayerGameOverTests.cs` covers damage through child colliders, lethal-hit deduplication,
camera retention, result/pause cleanup, score hooks, failed-mission gating, and saved scene bindings.

## Common Combat Score and Style

`CombatScoreManager` on GamePlayScene's `Mission Runtime` implements `IGameOverScoreProvider`.
It resets per sortie, binds the actually spawned/selected Player, and only scores registered mission
enemies defeated by that Player. MissionRuntime awards the boss before ending the score run; death
freezes results before Game Over reads them. No character-specific techniques, final stage grading,
score persistence, achievement conditions, or unlock rules are added in this phase.

Tuning data is `Assets/Resources/Missions/CombatScoreSettings.asset` (assigned in the scene):

- Base points: regular 100, speed 150, defensive 200, boss 1000.
- Chain: consecutive kills within 3 combat seconds add 20/40/60 (capped at 60).
- Multi-kill: the second and following distinct enemies killed by the same attack ID within
  0.3 seconds of its previous kill add 50 each. Only one award is possible per registered enemy.
- Flank: an accepted killing hit bypassing an active defensive shield adds 50. Front blocked hits
  and kills on a disabled/missing shield do not grant this bonus.
- Common-bonus repetition: 100%/80%/60%, with a 60% floor. It recovers after 5 unused combat seconds
  or when other successful bonus categories replace it. Repeated compound bonuses do not recover
  each other inside one kill. This never reduces base points or penalizes using the same weapon.
- Movement: normal maximum-speed ratios below .35 are x1; .35+ x1.25; .8+ x1.5. A weapon can preserve
  its attack-start movement for 1.5 seconds during aim/charge. Emitted attacks snapshot their ratio;
  moving after launching a stationary missile does not retroactively earn a movement bonus.

Award = round((base + repetition-adjusted common bonuses) * movement multiplier).
Cumulative score never decays and is never reduced by player hits. The separate style gauge caps at
1300. Ranks are D / D+ / C / C+ / B / B+ / A / A+ / S / S+, beginning at
0/100/220/350/500/650/800/950/1100/1250. Older eight-rank tuning assets preserve their thresholds
and append S/S+ using the previous rank interval, extending the cap as necessary.
Zero gauge is unranked (`CombatStyleRank.None = -1`, displayed as a dash), not D.
D through S+ retain enum indices 0..9 and all ten sprite/threshold slots. By default any positive
gauge below 100 is D. The first threshold can be raised to configure a positive D minimum;
gauge below that minimum is also unranked. HighestRank starts/resets to None until a rank is earned.
Kills grow the gauge by awarded points, but a boss's gauge gain is capped at 200 independently of
its full cumulative award. HighestRank records the peak even after later penalties or decay.

Three seconds without accepted player damage/defeats starts a 40/sec gauge decay. Hits reduce the
current gauge by 25%. Actual non-lethal damage maintains flow; blocked hits and empty fire do not.
Charge/ready/hold phases exclude at most two seconds of inactivity between successful hits; repeatedly
canceling/restarting an empty charge cannot renew that budget. No-target waits and zero gameplay
delta time pause timers. EndRun freezes scoring, gauge growth/decay, and penalties.

Damage attribution is an additive `AttackScoreContext` carried through `CombatDamage.Apply`, keeping
the existing IDamageable interface and Enemy.Defeated killer GameObject unchanged. Projectiles retain
their context until impact; explosion hits share their projectile's ID, including sniper direct hit
and follow-up explosion. Each rapid-fire round, missile, and beam damage tick has a new ID; all
targets of a slash share an ID, and a thrust keeps its ID along the path. Thus holding a trigger
indefinitely does not make all future bullets into one multi-kill attack. Reconfiguring a projectile
owner clears stale attribution. Enemy records only accepted hits, leaving shield blocking intact.

The default HUD is generated at runtime in the upper-right: cumulative points, current style rank
and gauge, and short common-bonus notices. It does not block UI raycasts and uses the saved Korean
TMP font. For a custom HUD, assign Score Text / Rank Text / Bonus Text / Gauge Slider on the manager
and turn off Generate Default HUD. PointsAwarded(notice, points), Changed, TotalScore, StyleGauge,
Rank, HighestRank, and ChainCount are available for presentation. All tuning belongs to the shared
settings asset and does not modify existing weapon damage, enemy behavior, or mission progression.

`Assets/Editor/CombatScoreTests.cs` covers rank boundaries/names, common bonuses and recovery,
attack grouping and snapshot movement, shield flank conditions, score ownership/deduplication,
boss limits, damage/decay/charge grace, result freezing, restart/reset, and saved scene wiring.

## Player Health Bar: Immediate and Delayed Damage Segment

GamePlayScene's existing `Canvas/healthBarMask` now has `PlayerHealthBar`, with its existing
`health` and `healthDelay` RectTransforms assigned. The custom polygon mask, vertex children,
colors, sibling order, and authored layout are preserved. MissionRuntime references this component
and binds its actual prepared Player, so Lobby character selection and direct battle testing use
the correct health source rather than a disabled scene placeholder. If a new scene supplies the same
Canvas/healthBarMask/health + healthDelay hierarchy without the component, MissionRuntime can attach
it at runtime. An Inspector-assigned Health Bar reference takes priority.

The health display is NOT a full-width delayed health bar behind the immediate one:

- `health` spans from the original full-health left edge to current HP, shrinking from right to left.
- `healthDelay` spans only the lost-health interval: current HP to its delayed right edge.
- Initially the delay has zero width at full HP. Damage immediately moves its left edge to new HP,
  preserving its old right edge. It holds for 0.6 seconds by default, then the right edge catches up
  over 0.4 seconds using the Inspector curve, until the interval has zero width again.
- Another hit during either hold or catch-up extends the left edge immediately and restarts the full
  hold, without resetting/jumping the right edge. Multiple hits thus accumulate the still-visible loss.
- Lethal damage shrinks health to zero. Unscaled time is enabled by default so the UI catch-up can
  finish even after Game Over sets gameplay time scale to zero.

`Hold Duration`, `Catch Up Duration`, `Catch Up Curve`, and `Use Unscaled Time` are editable on
healthBarMask's component. No Image fill mode, sprite replacement, hierarchy rewrite, or mask change
is needed. Horizontal anchors and offsets interpolate across the authored health span, preserving
its full-health padding and either fixed-size or stretched layout; vertical geometry is unchanged.
Curve overshoot/reversal cannot invert the interval or make its right edge grow backwards.
HealthChanged subscriptions use the assigned Player and are removed on disable without singleton
lookups. Rebinding/re-enabling snaps to the new/current HP using the original full-width baseline.

`Assets/Editor/PlayerHealthBarTests.cs` covers instant damage edges, hold/catch-up, repeated hits in
both phases, zero HP, large delta/zero duration, curve clamping, selected-player rebind, disable/enable,
fixed and stretch layouts, and saved scene references. Runtime Play Mode verification is separate
from the successful compile checks.

## Sprite-Based Style Rank Display

GamePlayScene's authored `Canvas/UpSide/styleScore` Image now has `StyleScoreDisplay`.
Its Score Manager, Rank Image and Pulse Target reference the mission score and existing UI;
position, size, anchors, hierarchy and Image settings are unchanged. Assign its ten Rank Sprites
fields (D through S+) in the Inspector. Missing sprites fall back to the authored Image sprite.

On each actual visible rank change, including rank drops to D, the sprite changes and the original local scale
pulses to 1.2x and back over 0.3 seconds. Gauge/score updates within the same rank do not restart
the animation. Rapid rank changes restart from the original scale without compounding it.
Pulse Scale, Duration, Curve and Use Unscaled Time are configurable; initial binding/re-enabling
snaps to the current rank without a pulse. Dropping below D stops the pulse and retains the last
visible sprite while fading it. Disabling unsubscribes and restores authored scale/color.
Rebinding uses `BindScoreManager` and removes the previous subscription.

Below D (None), Image alpha smoothly fades to zero over Fade Out Duration (default 0.3 seconds).
Repeated unranked notifications do not restart the fade. Initial binding/re-enabling while unranked
starts fully hidden, avoiding a scene-load flash. D and higher restore the authored alpha immediately,
cancel any fade, and play the usual rank-change pulse. No dim multiplier remains; RGB, parent
CanvasGroup alpha and GameObject/component active state are preserved. Unscaled time also drives fades.
The cumulative score, default text HUD, combat timing and final result provider remain unchanged.
Rank thresholds and gauge cap can be tuned in `Resources/Missions/CombatScoreSettings`.

`Assets/Editor/StyleScoreDisplayTests.cs` covers all ten sprite mappings, initial unranked hiding,
full-alpha D, below-D fade completion/deduplication/recovery, configured D minimum/zero-duration fade,
rank-change versus same-rank updates, pulse completion/restarts/clamping, disable/re-enable,
source rebinds, missing-sprite fallback and saved scene connections. CombatScoreTests now covers
S/S+ boundaries, gauge capping and legacy eight-rank migration. Play Mode verification is separate.

## White-Covered Battle Entry and First-Input Camera Return

Lobby plays the before transformation and cut-in, then calls LoadBattleScene rather than the
ordinary black-fade loader. The existing Cutin To After Delay now delays white-out/loading, not
Lobby after-clip playback. White Out Duration defaults to 0.4 seconds. SceneLoadManager's
persistent screen-space overlay becomes fully white before loading/activation, remains opaque across
the scene switch and selected-player initialization, then fades out over Reveal Duration (0.6 seconds).
The player simultaneously zooms in over Zoom In Duration (0.8 seconds) and plays the selected
after transformation once. Input is armed only once the reveal, zoom and after clip finish.
Preparation failure times out after ten seconds and returns to Lobby
under cover rather than leaving a permanent white screen. Other scene loads retain the black fade.

GamePlayScene has a new independent root `Battle Camera`, with Camera, AudioListener, URP data,
and BattleCameraRig. Its authored orthographic Size (currently 2.07) is the normal gameplay view.
The selected character's old output-camera pose/projection/size is not imported. Only one same-scene
screen-output camera/listener remains enabled; character RenderTexture cameras and display meshes
stay active. Selected-player aiming and the tiled background use Battle Camera. The rig performs
gameplay follow/look-ahead/charge widening, separate from BattleEntryDirector's arrival presentation.

The after clip is snapshotted from the chosen LobbyStandingCharacterEntry into MissionLaunchRequest
before the launch UI hides. No second Inspector clip assignment is needed. BattleEntryDirector starts
it on Player's body Sprite Animator at BeginReveal, using a manually timed AnimationClipPlayable.
Repeated BeginReveal calls are ignored; even looped clips play only once. The graph pauses on its
last frame until the first input, suppressing ordinary idle animation updates. Release/abort/disable
destroys the graph and restores the ordinary player animation controller. An unassigned after clip
skips this step without blocking entry. The Lobby's reusable standalone transformation helper remains
available, but the actual sortie path no longer plays the after clip in Lobby.

Mission Runtime has BattleEntryDirector, explicitly linked to the camera rig. Its configurable
Close Up Size Multiplier defaults to 0.55; Return Zoom Duration defaults to 0.45 seconds. During
arrival/waiting, Player holds its spawn position, ignores damage and gameplay input, and applies
a visual-only clockwise 90-degree offset (Standing Rotation Offset = -90). Movement direction
remains up. Gravity/inertia/velocity overrides cannot move it. Enemy actions, mission spawning,
and score-run timing wait until the first fresh gameplay input. Waiting does not set global time
scale to zero, so audio, UI and presentation keep working.

The first enabled rotation, thrust, bomb, main-attack or special-attack input releases the pose,
starts combat once, and smoothly returns the camera to its exact original Size. Attack input can
be the first input and still performs the attack. Pointer motion/UI navigation do not start combat.
Controls held through the reveal must return to neutral before a new press can start combat or
reach the player/weapon. Input actions are reused from Player/PlayerWeaponController rather than
creating another independently bound action set. Direct editor entry into GamePlayScene skips the
Lobby arrival gate and starts combat normally, while still using the independent camera.

BattleEntryTests covers no-input hold, standing pose restoration, fresh/held input, reveal lock,
damage protection, exact camera-size return, abort cleanup, legacy versus RenderTexture cameras,
held-bomb release after another control starts combat, first analog-trigger attack acceptance,
after-clip handoff/one-shot/final-frame hold, long-clip input gating and animation cleanup,
and saved-scene references. These tests are compiled. Batch execution was refused by Unity's
project-already-open guard; the existing editor was not closed or modified. Actual Unity Test
Runner/Play Mode execution must be verified separately.

## Battle Camera Directional Follow and Charging Vision

GamePlayScene's `Battle Camera / BattleCameraRig` is now the gameplay camera authority; character
output Cameras/AudioListeners are disabled at binding and are never used as camera-setting sources.
RenderTexture cameras remain enabled because they render the character's pixel/mesh presentation,
not the screen-output view. Camera follow offset, projection, culling and default Size come from
the scene camera. PlayerWeaponController aim, HomingMissileWeapon charge/lock-on, and background
tracking are all bound to this scene camera, including when launching a selected character.

Normal look-ahead uses normalized actual Player velocity, falling back to MovementDirection below
Movement Direction Min Speed (0.05). Default Movement Look Ahead Distance is 0.6 world units.
During weapon attacks or Ready/Charge/Hold/Release/Attack animation phases (including special
weapon charge and missile queues), look-ahead switches to the weapon controller's cached world aim
direction, which already respects weapon turn-speed limits. Default Aim Look Ahead Distance is 1.
Releasing the aim/attack state switches back to travel direction; inactive weapons do not request
aiming. Look Ahead Smooth Time (0.18 seconds) and Position Smooth Time (0.08 seconds) prevent
snaps. Zero smooth time means immediate following. These four fields and Follow Offset are editable
on BattleCameraRig. Entry-locked and dead players request no look-ahead. Gameplay smoothing uses
scaled delta time; the existing entry presentation continues to use its own unscaled time.

HomingMissileWeapon retains its existing charge curve, maximum view multiplier (the Yukari prefab
currently uses 3.8), missile-queue view hold, post-fire hold and return speed. When bound to the rig,
it submits a keyed view multiplier instead of directly editing Camera Size; lock screen bounds and
laser pointer use the same output camera. BattleCameraRig is the only Size writer in this path:
actual Size = presentation Size * smoothed requested weapon multiplier. The presentation Size is
the normal view or an entry/return zoom, so charging on the very first input cannot capture the
close-up as a new normal view or fight the return zoom. Multiple requests take the maximum rather
than stacking, and repeating one source replaces its request. Disable/death removes that source
immediately, including a partially completed restore; a player rebind clears old modifiers. The
legacy direct-camera implementation remains only as a fallback for isolated weapons outside a rig.

BattleEntryTests additionally covers velocity/heading look-ahead, aiming phases and charging flags,
inactive-weapon exclusion, smooth direction changes, entry centering, scene-camera ownership,
retargeted charge/lock-on, composed zoom, non-compounding requests and paused cleanup. Compile
verification does not imply Unity Test Runner or visual Play Mode verification.

## Lobby-to-Battle Lifecycle Log Fixes

HorizontalTiledBackground no longer changes SpriteRenderer geometry from Reset, OnEnable or
OnValidate. Validation only clamps padding; ExecuteAlways LateUpdate performs tiling, width and
horizontal centering, and skips unchanged size/position writes. Runtime camera binding can still
refresh immediately. This avoids native sprite notifications during Unity serialization validation.

Player animation helpers now require an active, enabled Animator with a controller and usable
layers. Disabled Animators count as animation-finished, so busy animation cleanup cannot stall.
ExitBusyStateImmediately still clears all weapon bookkeeping but does not update animation or
activity while the Player is disabled/dead. This covers scene-character replacement and shutdown.

PrototypeBoss and PrototypeEnemy used an obsolete prefab fileID (100100000), not the actual Enemy
root GameObject (6103461773506205544). DefaultCharacter had the same stale reference; its correct
Player_default root is 6247497842821643870. These references are corrected without changing the
prefab assets. SpawnEnemy validates the referenced GameObject/Enemy before instantiation; SpawnBoss
enters FightingBoss and stops regular spawning only after successful boss creation.

All 11 mission enemy/character prefab references were checked against their serialized GameObject
IDs. BattleEntryTests adds validation-deferral, inactive Animator, disabled-player busy cleanup,
prototype prefab-reference and failed-boss-spawn regressions. The full solution and tests compile
with zero warnings/errors; these new tests and visual Play Mode have not been executed in Unity.
