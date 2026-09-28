# Core systems (`Assets/Scripts/Core/`, namespace `Core`)

Detail for the core systems. The root `CLAUDE.md` holds the project rules, the tick order and
the physics layers; this file is loaded when working in this folder. Keep it up to date with
every change to these systems, like the root file.

Planned here (Phase 2): game state/level manager, screen fade, noise events, shadow volumes,
light-to-volume linking, surfaces and footsteps. Only the shared debug drawing exists so far.

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
  `ClimbableRope`, `DefaultLayerGizmos` - every component with gizmos. `DebugLines` is now the only code that calls
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
