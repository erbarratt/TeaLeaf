# Core systems (`Assets/Scripts/Core/`, namespace `Core`)

Detail for the core systems. The root `CLAUDE.md` holds the project rules, the tick order and
the physics layers; this file is loaded when working in this folder. Keep it up to date with
every change to these systems, like the root file.

Planned here (Phase 2): noise events, shadow volumes, light-to-volume linking, surfaces and
footsteps. The game state/level manager, the screen fade and the shared debug drawing exist so
far.

## Game state and level manager (added 2026-10-03)

- **`GameState`** (enum) — `Playing`, `Caught`, `Won`: the single value for whether the level
  is still being played.
- **`LevelManager`** (one per scene, on its own `Level Manager` object at the scene root) —
  owns `State` and what ending the level does. `LevelManager.Instance` (set in `Awake()`, no
  scene search), `State`, `HasObjective`, and the instance event `StateChanged(GameState)`
  (instance, not static, so a reload leaves no subscribers from the old scene). Other systems
  report, this class decides: guards call `Caught()`; the objective item calls
  `SetObjectiveCarried(bool)` (picked up / dropped or thrown). The level ends only from
  `Playing`, so the first ending wins and nothing reloads twice: `EndLevel()` sets the state,
  raises `StateChanged`, then `ScreenFade.FadeOut(duration, ReloadLevel)` - the reload runs in
  the fade's callback, behind full black (`caughtFadeDuration` 1s, `wonFadeDuration` 2s), and
  the reloaded scene's `ScreenFade` fades back in by itself. Restart = `SceneManager.LoadScene`
  of the active scene (synchronous; the hitch is hidden by the black). **Win** is private:
  the manager's own `Update()` checks `ExitZone.AnyContains(player.position)` (`player` = the
  Player root transform, a plain `Transform` so `Core` doesn't depend on `Player`), and that
  `Update()` only runs while the objective is carried (component disabled otherwise). Won
  currently restarts like Caught; Phase 9 replaces that with the "Mission complete" panel.
  **The player's body is frozen during the end fade**: `PlayerController` subscribes to
  `StateChanged` and, once the level is over, runs only the hand systems (see the tick order
  in the root `CLAUDE.md`). Right-click
  "Test Caught"/"Test Take Objective" in Play Mode.
- **`ExitZone`** — a box (serialized `size`, centred on the object, following its
  position/rotation/scale) tested with `InverseTransformPoint`, not a trigger collider: no
  physics layer, nothing for hand rays to hit, and it works while a mantle has the
  CharacterController off. Self-registering static list; `AnyContains(worldPoint)`,
  `Contains(worldPoint)`. The point tested is the player's feet, so sink the box a little into
  the floor. `IDebugDrawable` (green wire box).
- **`GameStateDebug`** (`Core/Debug`, on the Debug object) — keyboard driver for testing in the
  headset before guards and the objective exist (Game view needs focus): `C` = caught, `O` =
  toggle objective carried; logs every state change.

## Screen fade

- **`ScreenFade`** (added 2026-10-03; on a `Screen Fade` child of Main Camera at the local
  origin, so head-locked with no code) — fades the whole view to `fadeColor` (black) and
  back. `FadeOut(duration, onComplete)` covers the view (caught, restart: reload in the
  callback, once nothing can be seen); `FadeIn(duration, onComplete)` clears it. A fade starts
  from the current `Alpha`, so interrupting one is smooth; a replaced fade's callback is
  dropped. `ScreenFade.Instance` (set in `Awake()`, no scene search), `Alpha`, `IsFading`.
  **Level start:** with `fadeInOnStart` (default on) `Awake()` makes the view black before the
  first frame renders, holds for `startHoldDuration` (0.3s, covers tracking settling and
  uneven first frames), then fades to clear over `startFadeDuration` (1.5s). A scene reload
  therefore fades back in by itself. Drawn as a runtime-built 1m cube around the head (from
  inside, every view direction crosses exactly one face, so the whole view is covered once in
  both eyes) with an `OverlayMaterial` at queue Overlay + 100, so it also covers the UI
  markers. Eased with SmoothStep on unscaled time (works at time scale 0), each frame's step
  capped at 0.05s so a loading hitch can't jump it. **Costs nothing while clear:** the
  renderer is off and the component disables itself, so `Update()` only runs during a fade -
  the reason it has its own `Update()` rather than a `Tick()` (it isn't a player system and
  has no ordering needs). The colour is set on the material it created, not via
  `renderer.material`. Right-click "Test Fade Out"/"Test Fade In" in Play Mode.
  **`Hold()`/`Release()`** (counted) freeze any fade until every hold is released - for work
  that must finish behind the black: `PlayerTracking` holds the level-start fade while it
  waits for tracking and calibrates the view (upright, standing eye height).
- **`OverlayMaterial`** — the static factory for overlay (drawn last, no depth test)
  materials, moved here from `Scripts/Player` on 2026-10-03 because `ScreenFade` needs it and
  `Core` shouldn't depend on `Player`. Detail in `Scripts/Player/CLAUDE.md` (its users are
  mostly the player's UI markers).

## Debug drawing (`Core/Debug`) - gizmos that also show in the headset

Unity's Gizmos can't be seen properly in VR: they're drawn for one flat camera, so they show in
one eye only, offset and distorted. So every component with gizmos writes its drawing **once**,
against `DebugLines`, and the same code feeds both the Scene view and the headset.

- **`DebugLines`** — wireframe drawing built only from straight lines: `Line`, `Circle`,
  `WireSphere` (three circles, like Gizmos), `WireCube`, `WireCapsule`, in `Matrix` space and
  `Color` (like `Gizmos.matrix`/`color`). Two outputs: `DebugLines.ForGizmos` draws with
  `Gizmos` (only valid inside `OnDrawGizmos*`); a `new DebugLines()` collects line vertices that
  `CopyTo(mesh)` turns into a line mesh. `Draw(drawable, detailed)` resets matrix and colour
  before each drawable. Nothing allocates once its lists have grown.
- **`IDebugDrawable`** — `DrawDebug(DebugLines lines, bool detailed)`; `detailed` is the
  "selected in the editor" view. Pattern for an implementer:
  `DebugDrawRegistry.Register(this)`/`Unregister(this)` in `OnEnable`/`OnDisable`,
  `DebugLines.ForGizmos.Draw(this, false)` in `OnDrawGizmos`, and
  `DebugLines.ForGizmos.Draw(this, true)` in `OnDrawGizmosSelected` if it has a detailed view.
  **Any new component with gizmos should do this rather than call `Gizmos` directly.**
  Implemented by: `PlayerHandVisuals` (hand collision capsules), `ClimbableEdge`, `Ladder`,
  `ClimbableRope`, `ExitZone`, `DefaultLayerGizmos` - every component with gizmos. `DebugLines` is now the only code that calls
  `Gizmos` directly.
- **`DebugDrawRegistry`** — static list of enabled drawables (self-registering, no scene
  search), read with `Count`/`Get(i)`.
- **`InHeadsetGizmos`** — put it on any scene object and enable it (it can be toggled in the
  Inspector while playing). Builds one line mesh from every registered drawable each frame
  (one draw call), on a `MeshRenderer` it creates at the scene root with identity transform and
  fixed huge bounds (never culled, no bounds recalculation). Rebuilt in
  `RenderPipelineManager.beginContextRendering`, guarded to once per frame, not `LateUpdate()`:
  the Tracked Pose Drivers move the controllers again just before rendering, and building
  earlier would draw the hand capsules trailing the hands. `showDetail` picks each drawable's
  detailed view (there's no selection in the headset); `seeThroughWalls` sets the depth test
  (Always / LessEqual). Disabled, it costs nothing.
- **`DefaultLayerGizmos`** (`IDebugDrawable`, on the Debug object) — outlines, in magenta,
  every enabled collider left on the Default layer (layer 0), in the Scene view in edit and
  play mode and in the headset: box/sphere/capsule as their real scaled shape, other colliders
  as their bounds. Colliders only: the layer matters for physics, and collider-less objects
  (lights, groups, runtime-built markers) are fine on Default. The one scene search in the
  debug code - these objects have none of our components to register - done every
  `rescanInterval` (1s) rather than per frame, and it allocates, so it's debug-only. Disable
  the component to turn it off (`OnDrawGizmos` checks `isActiveAndEnabled`).
- **Shader `TeaLeaf/DebugLines`** (`Assets/Art/Shaders/Resources/DebugLines.shader`) — like
  `TeaLeaf/Overlay` (unlit, Overlay queue, `ZWrite Off`, `Cull Off`, alpha blend, single-pass
  instanced stereo macros, `SRPDefaultUnlit` pass, in `Resources` for `Shader.Find()`) but
  coloured per vertex, with the depth test as a `_ZTest` material property. Lines are one pixel
  wide in the headset.
