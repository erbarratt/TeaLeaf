# Core systems (`Assets/Scripts/Core/`, namespace `Core`)

Detail for the core systems. The root `CLAUDE.md` holds the project rules, the tick order and
the physics layers; this file is loaded when working in this folder. Keep it up to date with
every change to these systems, like the root file.

Planned here (Phase 2): the rest of the sound system (below), shadow volumes, light-to-volume
linking, surfaces and footsteps. The game state/level manager, the screen fade, noise events
and the shared debug drawing exist so far.

## Noise and sound (started 2026-10-03)

Two systems fed from one asset, so what the player hears and what guards hear are tuned
together: **noise** is the gameplay event guards listen to; **sound** is the audio the player
hears. Both travel the same way - **Thief-style rooms and portals**: straight within a room,
room to room only through portals, never through walls. The aim is that a sound always tells
the player truthfully where it is, on stereo headphones, cheaply enough for Quest (the
maintainer's complaint about Thief VR: guard sounds had little distance or blocking
processing). Noise events, the cue asset, rooms, portals, propagation and one-shot audio
playback exist; loops, reverb and the spatialiser are the remaining design below.

- **`Noise`** (readonly struct) — `Position`, `Radius` (metres it carries), `Type`
  (`NoiseType`), `Source` (the `Transform` that made it, may be null; lets a listener ignore
  its own noises). A struct passed with `in`, so emitting allocates nothing.
- **`NoiseType`** (enum) — `Footstep`, `Landing`, `Impact`, `Mechanism` (doors, chests,
  locks), `Voice` (barks), `Distraction` (noisemaker bolt).
- **`INoiseListener`** — `EarPosition` and `OnNoiseHeard(in Noise, float loudness, Vector3
  heardFrom)`: loudness is 1 at the noise, falling linearly to 0 when the *path* is as long as
  the radius; `heardFrom` is the noise itself in the same room, otherwise the point of the
  last portal it came through (where a guard should turn to look). Guards will implement it.
  `NoiseSystem.Register(this)`/`Unregister(this)` in `OnEnable`/`OnDisable`.
- **`NoiseSystem`** (static) — `Emit(position, radius, type, source)`: per listener, a
  straight-line squared-distance reject first (a path is never shorter), then
  `SoundPropagation.TryGetPath()` with the radius as the limit (the noise's room is looked up
  once). Listeners are looped backwards so one may unregister while reacting. Nothing per
  frame, no scene search. Static event `Emitted(Noise)` is raised for every noise, heard or
  not - for debug drawing and UI (a sound direction indicator is being considered), not for
  guards; subscribers must unsubscribe in `OnDisable` because a static event outlives a scene
  reload.
- **`SoundCue`** (ScriptableObject, Create > TeaLeaf > Sound Cue) — one kind of sound. Audio
  half: `clips` (random pick), `volume`, `pitchVariation`, `audibleRange` (metres; beyond it
  the sound isn't played). Noise half: `noiseType`, `noiseRadius` (0 = guards never hear it).
  `EmitNoise(position, source, radiusScale)` emits the noise half (scale e.g. by stance). The
  audible range and the noise radius are separate on purpose: how far the player hears a
  sound and how far guards react to it are tuned independently.
- **`SoundRoom`** — a maths box like `ExitZone` (serialized `size`, `IDebugDrawable`, blue),
  self-registering, `[ExecuteAlways]` so rooms are also registered in Edit Mode (portals can
  show what they join while the level is built). `SoundRoom.Find(worldPoint)` = the room containing a point, the smallest
  where rooms overlap, **null = outside, which acts as one big room**. Neighbouring rooms
  should meet in the middle of the wall between them. Outdoor areas that should block each
  other need their own rooms (two points both "outside" always hear each other directly).
- **`SoundPortal`** — an opening between two rooms (doorway, stairwell, window): a rectangle
  (`size`) in the object's local X/Y plane, local Z through the opening, placed mid-wall. Its
  two rooms are found automatically by probing `probeDistance` (0.25m) out from each face
  (`FrontRoom`/`BackRoom`; warns if both are the same). **Open**: passing through adds
  `openMuffle` (0.3) to the path's muffle, so a sound gets duller with every doorway it comes
  through (added 2026-10-03, maintainer's request) - except a source in the next room heard
  in a straight line through the opening, which stays clear (`CrossingPoint`'s
  `isStraightThrough`). **Closed** (`startsOpen` off, or
  `SetOpen(false)` - for doors to call): sound still passes, with `closedMuffle` (0.6) and
  `closedExtraDistance` (4m) added to the path. A portal that never opens is how a thin wall
  or window leaks sound - **walls never leak by themselves** (decided 2026-10-03).
  `CrossingPoint(from, to)` = where the straight line crosses the portal plane, clamped to
  the rectangle: the true line of sight when the source is visible through the opening,
  otherwise the edge of the doorway. Drawn cyan (open), red (closed) or **yellow if both
  sides are the same room** (it joins nothing) - checked live by `ProbeRooms()`, in Edit Mode
  too. **`SoundPortalEditor`** (`Core/Editor`) adds a box to the Inspector naming the two
  rooms it joins, or the same warning. Not baked: the links are worked out at runtime (once
  per layout change, well under a millisecond), so they can never go stale.
- **`SoundPropagation`** (static) — `TryGetPath(from, to, maxDistance, out SoundPath)` (and
  an overload taking the two rooms). Same room: the straight line. Otherwise the shortest
  route through portals: each portal of the source's room against each of the listener's,
  `|from - first| + table[first, last] + |last - to|` plus closed penalties, using portal
  centres - except a single portal joining the two rooms, which is measured through its exact
  crossing point. False if no route or longer than `maxDistance`. The portal-to-portal tables
  (distance, muffle, previous portal; flat arrays) are built with Floyd-Warshall, lazily, the
  first query after `MarkDirty()` (a portal opened or closed) or `MarkLayoutDirty()` (a room
  or portal enabled or disabled) - only the latter makes the portals look their rooms up again.
  Rooms and portals are assumed not to move while playing. No physics, no allocation per
  query. **`SoundPath`** (readonly struct):
  `Distance`, `Muffle` (0-1), `HeardFrom`, `IsDirect`.
- **`NoiseDebug`** (`Core/Debug`, on the Debug object) — draws each noise as a wire sphere of
  its radius fading over `showDuration` (fixed ring of 16, no allocation), logs it, and `N`
  emits a test noise at `emitFrom`, then (`logReport`) writes one "NOISE REPORT" Console
  entry, no stack trace, listing every `NoiseListenerDebug`: room, straight distance, path
  distance, muffle, heard-from point and heard/silent - readable afterwards from
  `%LOCALAPPDATA%/Unity/Editor/Editor.log`. **`NoiseListenerDebug`** (`Core/Debug`) — stand-in ears
  for any object: the object itself turns yellow (`MaterialPropertyBlock` on `_BaseColor`,
  only on change), a 0.3m wire marker round it turns yellow with a line to where it heard the
  noise from and on to the noise, and it logs the loudness.
  **`NoiseTestListeners`** (`Core/Debug/Editor`, menu **TeaLeaf > Add Noise Test Listeners**)
  adds three collider-less listener cubes under one root, 3m, 6m and 12m ahead of the main
  camera at ear height (two inside the 8m test noise, one outside); rebuilding asks to replace.
  **`SoundTestArea`** (`Core/Debug/Editor`, menu **TeaLeaf > Build Sound Test Area**) builds
  three 4m rooms in a row at (-14, 0, 4; a rebuild keeps where the old one was moved to), no
  ceilings: A (doorway to outside + doorway to B), B, and C behind a solid wall with a closed
  portal from B (the thin-wall case), with their `SoundRoom`s, three `SoundPortal`s, five
  listener cubes (A, B, C, outside the front door, behind B) and two `SoundEmitterDebug`
  cubes (footsteps every 0.5s in B, an impact every 2s in C). It also runs
  `PlaceholderSounds.Create()` and makes sure the scene has a `Sound Player` object and the
  Debug object a `SoundDebug`. Uses `Player.TestGeometry` (editor-only).
  **`SoundTestHouse`** (`Core/Debug/Editor`, menu **TeaLeaf > Build Sound Test House**, added
  after the test area worked) - the fuller test: a two-storey 12m x 8m house with its own
  ground, at (-36, 0, 12). Ground floor: Hall (front door, a 35° stair ramp up its left wall),
  Kitchen, Store (behind a closed-door portal and a door slab) and Lounge (closed "window"
  portal in the back wall); upper floor: Landing and Bedroom, no roof; a walled Yard in front
  with a gate. Seven `SoundRoom`s (the Yard is one - outdoor space only blocks sound as a
  room) and eight `SoundPortal`s, including a **stairwell portal lying flat in the stair
  hole** (rotated 90° about X), the only link between the floors. Nine listener cubes, a
  knock in the Store, and a stand-in guard: a footstep emitter with **`WaypointMoverDebug`**
  (`Core/Debug`; walks a list of points there and back at `speed`, no collision) going
  Bedroom → Landing → stairs → Hall → Kitchen. Reuses `SoundTestArea`'s helpers (internal).
- **`SoundPlayer`** (one per scene, on a `Sound Player` object at the scene root;
  `SoundPlayer.Instance`, set in `Awake()`) — plays every sound.
  `Play(cue, position, source = null, noiseScale = 1)` emits the cue's noise (scaled; 0 =
  none) and plays its audio if it reaches the `listener` (the Main Camera transform): a
  straight-line range reject, then `SoundPropagation.TryGetPath()` limited to the cue's
  `audibleRange` - no path = not played (a guard on the floor above is heard from the
  stairwell, or not at all - no vertical confusion on stereo). Same room: played at its real
  position. Through portals: played at `ears + direction to HeardFrom * path Distance`, so
  Unity's 3D panning and distance fade follow the route. The fade is a **custom
  rolloff curve** shared by every voice, built once in `Awake()`: volume = `(1 - distance /
  audibleRange) ^ falloffSharpness` (default 2; 1 = linear), reaching silence at
  `audibleRange` (set per play as `maxDistance`). Changed from linear on 2026-10-03: with a
  linear fade, sounds faded in noticeably as the player moved quickly; the curve drops fast
  close up and slowly far away, so distant sounds change little with movement.
  Muffle: volume towards `muffledVolume` (0.5) and the voice's `AudioLowPassFilter` cutoff
  from 22 kHz towards `muffledCutoff` (1200 Hz), interpolated by ratio (octaves); **the
  filter is disabled unless the sound is muffled** (so same-room and straight-through-a-
  doorway sounds cost no filter; anything round a corner does). A fixed pool of `voiceCount` (16) child
  `AudioSource`s made in `Awake()` (3D, no doppler); when all are busy the oldest is reused.
  No `Update()`. One-shots only so far: a sound's position and muffle are fixed when it
  starts. The voice cap is the main Quest cost control (per-voice spatialiser + filter).
- **`SoundEmitterDebug`** (`Core/Debug`) — plays a cue from its position every `interval`
  seconds (noise off by default). **`SoundDebug`** (`Core/Debug`, on the Debug object) — `L`
  writes one "SOUND REPORT" Console entry: the listener's room, voices playing, and each
  emitter's room, range, straight and path distance, muffle, heard-from point and
  played/not.
- **`PlaceholderSounds`** (`Core/Debug/Editor`, menu **TeaLeaf > Create Placeholder Sounds**)
  — generated stand-in audio: three footstep and two impact `.wav` files in
  `Assets/Audio/Placeholder` and the cues `Assets/Data/FootstepPlaceholder.asset` (range 25m,
  noise 6m) and `ImpactPlaceholder.asset` (range 35m, noise 10m; ranges raised 2026-10-03 from 12m/20m, where sounds faded in too abruptly when moving fast). Existing files are kept.
  Replace the clips in the cues with real recordings later.

**Remaining design (agreed 2026-10-03):**

- **Looping sounds** (torches, ambience) — not built: re-evaluate the path ~5 Hz, staggered,
  with the apparent position and muffle eased.
- **No line-of-sight ray muffling inside a room** (dropped 2026-10-03, maintainer's
  decision): with portals handling walls, a guard in the same room should be heard clearly -
  position and reverb matter more there.
- **Reverb** — per room, so a sound's space is audible. Approach not chosen yet; keep it to
  one shared reverb (set from the listener's room), not one per voice.
- **Spatialiser** — a head-related spatialiser plugin (Unity's built-in audio only pans left/
  right). Meta XR Audio SDK is the candidate; Unity 6000.3 support and PCVR through
  SteamVR/Virtual Desktop still to be verified. Steam Audio is the fallback.

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
  `ClimbableRope`, `ExitZone`, `SoundRoom`, `SoundPortal`, `NoiseDebug`, `NoiseListenerDebug`, `DefaultLayerGizmos` - every component with gizmos. `DebugLines` is now the only code that calls
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
