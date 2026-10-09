# Core systems (`Assets/Scripts/Core/`, namespace `Core`)

Detail for the core systems. The root `CLAUDE.md` holds the project rules, the tick order and
the physics layers; this file is loaded when working in this folder. Keep it up to date with
every change to these systems, like the root file.

Planned here (Phase 2): the rest of the sound system (below). The game state/level manager,
the screen fade, noise events, sound, surfaces, gameplay light, the procedural night sky and
the shared debug drawing exist so far.

## Foveated rendering (added 2026-10-07; not yet run on a Quest)

- **`FoveatedRendering`** (static, no component, nothing per frame) — asks the XR display for
  fixed foveated rendering in a Quest build: the edges of each eye's view are drawn at lower
  resolution. `Apply()` runs by itself once, after the first scene loads
  (`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`), on Android only, and sets
  `XRDisplaySubsystem.foveatedRenderingLevel` to the `Level` constant (0.5, a first guess; 0 =
  off, 1 = strongest) with no flags (fixed, not eye-tracked). The level stays set across
  level restarts. It needs the OpenXR **Foveated Rendering** feature enabled for Android and
  the **Foveated Rendering API** set to **SRP Foveation** (both set 2026-10-07 in
  `Assets/XR/Settings/OpenXR Package Settings.asset`); without them the call does nothing.
  PCVR is left alone. Tune `Level` on the device - too high shimmers at the edges, thin bright
  things (the stars) first. If it ever needs changing at runtime (a settings menu), give it
  a public method rather than a component.
- Other Quest render settings changed the same day: HDR off in `Mobile_RPAsset` (nothing used
  it; PC keeps it), and the Main Camera's far clip plane 1000m → 200m (the sky is drawn at the
  far plane whatever its distance; `Moonlight.maxDistance` is a physics ray, unrelated).

## Procedural night sky (added 2026-10-07; compiles, not yet confirmed in the headset)

**No skybox**: the camera clears to a flat colour (black) and the stars and moon are drawn
over it as geometry. Two earlier versions the same day were replaced: a panoramic photo
skybox (seam, pinched pole), then a generated 1024 cubemap of stars and nebula (seamless, but
the stars were too soft - a cubemap pixel is ~0.11°, a Quest 3 pixel ~0.04° - and it took
24 MB). Don't bring a star cubemap back. **A picture only - nothing in it is emissive or
lights the scene**; brightness is the Directional Light's and the ambient setting's job
(ambient is Flat).

- **`ProceduralSky`** (`[ExecuteAlways]`; on the Directional Light or any scene object -
  `Reset()` fills `viewCamera` with the main camera and `moonLight` from its own `Light`, else
  the scene's `Moonlight`) — in `OnEnable()` makes a `TeaLeaf/Sky` material and two objects
  at the scene root (`Sky Stars`, `Sky Moon`; `HideFlags.DontSave`, identity rotation - not
  children, which would turn with the light), sets `RenderSettings.skybox` to null and the
  camera (`viewCamera`, or `Camera.main` if empty) to clear to `skyColor`; `OnDisable()` puts
  both back and destroys what it made. `Rebuild()` builds the meshes and sets the material:
  about a millisecond, synchronous, before the first frame, so it's already behind
  `ScreenFade`'s level-start black. Nothing per frame in a build: `OnValidate()` (flags a
  rebuild after an Inspector change) and `Update()` (does it, and keeps the moon on the light
  while it's rotated) are `#if UNITY_EDITOR`. No cache: there is nothing slow to save.
- **Stars** — one mesh, one draw call: a quad per star (4 vertices, 2 triangles). Settings:
  `seed`, `starCount` (2500), `starSize` (degrees, faintest to brightest), `minStarPixels`,
  `starMinBrightness`/`starMaxBrightness`, `starFaintBias` (higher = fewer bright ones),
  `starColors` (Gradient, a random point per star), `starColorAmount`, `horizonFade`. One
  random "magnitude" per star drives both brightness and size. **No star is made below the
  horizon** (maintainer's decision: they're never seen) - directions are picked on the upper
  hemisphere only, fading in over `horizonFade` degrees (6) above the horizon.
- **Moon** — a second one-quad mesh pointing +Z, on its own object that `AimMoon()` (public,
  cheap) turns to `-moonLight.forward` (the same direction as `Moonlight.DirectionToMoon`);
  call it if anything ever turns the light at runtime. Sorting order 1, so it's drawn after
  the stars and covers them. `showMoon`, `moonSize` (degrees across, 4), `moonColor`.
  It has its own material (same shader, `_SURFACE_TEXTURE` keyword on) when
  `showMoonSurface` is set; off, a plain disc.
- **Moon surface** (`MoonSurfaceBuilder`, static; settings in `MoonSurfaceSettings`, the
  component's `moonSurface` field) — `Build(settings)` paints a greyscale RGBA32 texture
  (`textureSize` 512, **with mipmaps**, trilinear) that the shader multiplies `moonColor` by.
  Baked at level load rather than computed per pixel in the shader (decided 2026-10-07)
  because mipmaps stop sub-pixel crater detail flickering with head movement. The texture is
  the ball seen straight on: each pixel becomes a point on a unit sphere (`PointOnBall()`,
  z towards the viewer) and every feature is worked out there, so craters foreshorten at the
  limb. Burst jobs: `GroundJob` (parallel) - highlands, seas (`seaAmount`, `seaDarkness`,
  `seaScale`; layered simplex noise over a threshold) and mottling (`roughness`);
  `CratersJob` (single thread) - craters largest first so small ones overlie big
  (`craterCount`, `craterSize` as a fraction of the moon's radius, `craterSmallBias`,
  `craterContrast`: darker floor, bright rim, one side of the rim lit for relief), then
  `rayCraters` mid-sized craters with streaks (`rayBrightness`, `rayLength`), then
  `edgeDarkening` and the 8-bit encode. Lit flat as a full moon; no phases yet. A few
  milliseconds. Jobs must not read static fields of the builder (it has none, for Burst).
- **Shader `TeaLeaf/Sky`** (`Assets/Art/Shaders/Resources/Sky.shader`; in `Resources` for
  `Shader.Find()`, stereo-instancing macros, `SRPDefaultUnlit`) — the mesh stores each
  disc's centre direction (position), linear colour (alpha 0 = add, 1 = cover), corner +
  radius + twinkle phase (UV0), twinkle speed + weight (UV1); the vertex shader builds the
  quad. **Camera-locked**: corners are placed 1m from `_WorldSpaceCameraPos` in the star's
  direction, then depth forced to the far plane - no parallax, identical direction in both
  eyes (infinity), far clip distance irrelevant. **Minimum size**: a star under
  `_MinPixelRadius` pixels is drawn at that size and dimmed by the area ratio instead
  (sub-pixel stars flicker with head movement). **Twinkle**: three sines at unrelated speeds
  off `_Time`, per-star phase and speed, `_TwinkleAmount`/`_TwinkleSpeed`, scaled from full
  at the horizon to `_TwinkleOverhead` at the zenith (`twinkleAmount` 0.2, `twinkleSpeed` 5,
  `twinkleOverhead` 0.3 on the component); brightness only, all per vertex. Fragment: a disc
  with a one-pixel `fwidth()` edge. Queue `Transparent-400` with `ZTest LEqual`, `ZWrite
  Off`: after opaques (hidden stars are depth-rejected), before other transparents.
  `Blend One OneMinusSrcAlpha` so one shader both adds (stars) and covers (moon).
  `#pragma multi_compile_local _ _SURFACE_TEXTURE` (not `shader_feature`: the moon's
  material is made in code, so a build would strip the variant) multiplies the colour by
  `_SurfaceTex`, sampled with the quad's corner coordinates.
- **Nebula clouds: not built** (dropped with the cubemap; the maintainer wants them later).
  The agreed route is a small cloud-only cubemap (about 256 per face) behind the stars -
  clouds are soft, so low resolution doesn't show.
- The old panorama (`qwantani_night_puresky_4k.exr`) and its `NightSky` material were
  deleted 2026-10-07. The scene's default reflection was baked from the old sky and stays
  until lighting is regenerated.

## Level surface shaders

- **`TeaLeaf/Cobblestone`** (`Assets/Art/Shaders/Cobblestone.shader`, written 2026-10-08;
  **not yet compiled or seen** - not in `Resources`, a material references it) — a cobbled
  floor with no textures, at the maintainer's request: rounded irregular stones, each a
  colour from a dark red / brown / grey range, a separate gap colour, no repeat, the same
  cobble size on any object, everything tunable. **World-space**: the pattern is read from
  the pixel's world position along whichever world axis the surface faces most (XZ for a
  floor), divided by `_CobbleSize` (0.14m) - so object scale doesn't matter and adjoining
  pieces join seamlessly, but the pattern slides across anything that moves (floors and
  walls, not props). **Pattern**: Voronoi - a jittered point per grid cell (`_Irregularity`
  0.7), the pixel belongs to the nearest - by "power", distance squared less a per-stone random
  weight (`_SizeVariation`, 0 = off, up to half a cell squared; added 2026-10-08 for mixed
  stone sizes, a power diagram: borders stay straight lines with an exact distance,
  `0.5 x power difference / distance between the points`, so the gap width doesn't change
  with stone size; a hex grid was considered and rejected - still one stone per cell, so
  no size variation); the distance to each border with
  the other 8 points is combined with a smooth minimum (`_CornerRoundness`), which rounds
  the corners. **Search size** (2026-10-08): the cells searched round each pixel are
  `SEARCH_RADIUS` 2 = 5x5 by default, or 3x3 with the `_NARROW_SEARCH` toggle ("Narrow
  Search (cheaper)", off by default so existing materials get the wide one). The maintainer
  saw thin dashed straight lines across the stones up close, on the cell grid, with
  wrongly-lit wedges beside them; they stayed with Dirt off and vanished at Lighting
  Strength 0, and a screenshot showed stones well over a cell across. Cause as read from
  that (the 5x5 fix is written, **not yet confirmed in the editor**): 3x3 doesn't contain
  every stone that borders a large one, so a border goes missing over part of the stone,
  and where the searched set changes at a cell edge the height jumps - the derivative
  normal draws the jump as a line. 3x3 is only right with Size Variation near 0 and modest
  Corner Roundness. **Two earlier fixes were wrong and reverted**: fading a point's pull
  on the smooth minimum by its distance from the pixel (removed all rounding - ordinary
  neighbours are about a cell away), and fading it as the pixel neared the cell edge that
  drops its row/column (made the lines worse - it removed more real borders). Don't try to
  hide missing neighbours; search far enough to have them. **Gaps** (reworked 2026-10-08): the maintainer settled on
  `_CornerRoundness` 0.5 for the stone shape, which pulls the rounded outline well inside
  the true border near corners, so the pockets where stones meet were too big even at gap
  0. `_GapWidth` may now be **negative** (range -1 to 0.4), growing the rounded outline
  back out; `_MinGap` (0.02) is a line always kept along the true (unsmoothed) border, or
  neighbours would fuse along their straight sides. `Cobbles()` returns how far inside the
  stone the pixel is: `min(rounded - GapWidth/2, border - MinGap/2)`. Height = 0 at the
  stone's edge and in the gap, rising over `_Bevel` to 1. Colour: a
  per-cell random point along `_ColorA`-`_ColorB`-`_ColorC`, `_BrightnessVariation`,
  `_EdgeDarkening` down the shoulder, `_GapColor`; `_Seed` changes the layout. **Crevice
  shadow** (added 2026-10-09, tried and looks right; stands in for ambient occlusion, after the
  maintainer found the step from stone to gap hard): the final colour and the shine are
  multiplied by `1 - _OcclusionStrength * (1 - smoothstep(0, _OcclusionWidth, abs(edge)))`
  (defaults 0.6 and 0.12 of a cobble; strength 0 = off) - darkest on the line where stone
  meets gap, easing off on both sides, so narrow gaps stay dark and wide pockets lighten in
  the middle. Applied after the dirt, faded out with the distance fade; it dims direct
  light too (a colour multiply, not `surfaceData.occlusion`). Not in BrickWall. **Normal**
  from screen-space derivatives of the height (`_Depth` metres x `_NormalStrength`,
  Mikkelsen's method) - no tangents, no normal map. **Lit** with URP's Blinn-Phong
  (`UniversalFragmentBlinnPhong`, the Simple Lit model; lighting keywords copied from
  Simple Lit), optional shine (`_SPECULAR_COLOR` toggle); SRP Batcher compatible, stereo
  instanced, fog-aware; ShadowCaster, DepthOnly and DepthNormals passes; **no Meta pass**
  (a lightmap bake won't see its colours - add one in Phase 8). **Distance fade**
  (`_FadeStart`/`_FadeEnd`, cobbles per pixel): pattern and bump fade to a flat average
  colour before the cobbles get sub-pixel, or they'd shimmer (no mipmaps on a procedural
  pattern). **Parallax** (`_PARALLAX` toggle, off by default, `_ParallaxDepth`): one
  offset-limited shift of the coordinates by the height, not parallax occlusion - it works
  the pattern out twice. **Dirt layer** (`_DIRT` toggle, off by default; added the same
  day, uncompiled, after the maintainer found the cobbles too uniform and clean): three
  two-channel value noises (`Noise2()`, 4 hashes each). A slow one places grime patches
  (`_GrimeAmount`, `_GrimeCoverage`, `_GrimeScale` metres; `_GrimeLowBias` puts dirt in
  gaps and down shoulders everywhere), tinting to `_DirtColor` and dulling the shine;
  applied after the distance fade, since patches are far bigger than a cobble. A second
  warps the coordinates before the cobbles are found (`_WarpAmount`, `_WarpScale`), so
  borders aren't straight. A fine one ("flecks", `_MottleScale`) mottles the stones (`_MottleAmount`), pits them for the lighting (`_MottleBump`),
  varies the gap colour (`_GapNoiseAmount`) and chips the outline by moving the border
  distance (`_EdgeRaggedness`). The parallax's first look-up ignores the chipping.
  **Flecks grow with distance instead of fading** (fixed 2026-10-08): first built fading
  the flecks out once sub-pixel, which - with the maintainer's 5mm flecks - left the dirt
  visible only within about a metre, and no slider changed it. Now, whenever a fleck
  would be under `_FleckPixels` (4; a slider since later that day, 1-8: lower = finer for
  longer but more shimmer) pixels, the noise is read at double the size, as many times as
  needed (`level = log2(metres per pixel x FleckPixels / fleck size)`), with
  `_MottleScale` allowed down to 0.5mm (it only shows that small within a few tens of
  centimetres). The flecks use `FleckNoise2()`, which wraps each grid corner into 0-1023
  before hashing: sub-millimetre flecks reach grid coordinates in the tens of thousands a
  few metres from the origin, where `Hash22()` runs out of float precision, blending the two nearest doublings
  and stretching the blend back to full contrast - a fourth noise read, always some
  texture, no shimmer. The material is `Assets/Materials/Cobblestone.mat`. The
  maintainer tuned the material's other values in the editor on 2026-10-08: don't rename
  existing properties (a material stores its values by property name). **Cost** is per pixel on a view-filling surface (about 25 hashes
  and 24 border distances with the default 5x5 search - 9 and 8 with Narrow Search -
  doubled with parallax, plus 16 hashes with dirt): against the "pixels are the tightest
  budget" rule for Quest until profiled - the fallback is baking the pattern to textures.

- **`TeaLeaf/BrickWall`** (`Assets/Art/Shaders/BrickWall.shader`, written 2026-10-08, **not
  yet compiled or seen**) — the cobblestone shader's sister for brick and coursed-stone
  walls, at the maintainer's request: rectangular, regular, every other row offset. Same
  frame as Cobblestone (world-space, no textures, Blinn-Phong, derivative normal, distance
  fade, optional one-shift parallax, the same Dirt layer with growing flecks, the same four
  passes, no Meta pass) - **the shared code is duplicated, not in an include**, so a fix to
  one (noise, lighting, passes) must be made in both. All sizes are in **metres**
  (Cobblestone's are fractions of a cell). **Pattern, with no search**: rows `_BrickHeight`
  high, bricks `_BrickWidth` long (each including its share of mortar); odd rows slid by
  `_RowOffset` (0.5 = running bond) plus a per-row random `_RowOffsetVariation`; the lines
  between rows and between bricks each nudged up to 0.45 of a brick (`_HeightVariation`,
  `_WidthVariation` - both 0 for brick, raised for stone). `FindSpan()` finds a pixel's
  brick from the nearest dividing line and the next one out: two hashes per axis, and
  right because a line never strays half a brick from its number. Edge distance is a
  rounded-rectangle distance (`_CornerRadius`) less half `_MortarWidth`; height rises over
  `_Bevel`. **`_FaceTilt`** leans each brick's face a random way (a plane through its
  middle, times the edge rise so there's no step) so faces catch the light unevenly.
  Colours A-B-C per brick, `_MortarColor`. **Direction**: on a wall, u runs along the wall
  and level (`cross(up, normal)`), v straight up, so a wall at any yaw gets true-length
  bricks; a surface facing mostly up or down uses world X/Z. Bricks don't wrap round a
  corner between two walls. About 6 hashes a pixel (doubled with parallax, +16 with dirt):
  far cheaper than Cobblestone's 5x5 search.

- **`TeaLeaf/DoorLeaf`** (`Assets/Art/Shaders/DoorLeaf.shader`, written 2026-10-09, compiled
  and seen working by the maintainer that day; a material references it) — a door leaf with a keyhole opening
  cut out of it, for `Interaction.DoorKeyhole` (see `Scripts/Interaction/CLAUDE.md`). A flat
  `_BaseColor`, Blinn-Phong, the same frame and passes as Cobblestone (lighting code copied
  again, not shared). `KeyholeDistance()` (in the shared `HLSLINCLUDE`) is the signed
  distance in metres to a keyhole outline - the smaller of a circle's and a slot's
  (rectangle's) distances - measured across the face only, from
  `_KeyholeCentre`/`_KeyholeRight`/`_KeyholeUp` (leaf object space) and `_KeyholeSize`
  (x = the circle's radius; the circle's centre is one radius above the keyhole's middle,
  the slot one radius wide down to two radii below it; `DoorKeyhole.BuildOutline()` must
  match) - all set per renderer by `DoorKeyhole`'s
  `MaterialPropertyBlock` (which takes that leaf out of the SRP Batcher); all zero = no
  opening. Pixels inside are clipped, in ForwardLit, DepthOnly and DepthNormals; **the
  ShadowCaster pass doesn't cut**, so the door casts a whole shadow. The edge is one pixel
  of coverage sent out as alpha with `AlphaToMask On` (smoothed under MSAA, hard without).
  `_RimColor`/`_RimWidth` darken a band round the opening. Back faces are culled, so
  through the near face's opening the room shows; the opening's walls are a mesh
  `DoorKeyhole` makes, drawn with this shader. **No texture**: flat colour only (a texture isn't planned).
  `clip` weakens early depth rejection: for door leaves, not walls. A lock plate on the
  door uses it too (its own material), with its own block from `DoorKeyhole`.
- **`TeaLeaf/LockFade`** (`Assets/Art/Shaders/Resources/LockFade.shader`, written
  2026-10-09, seen working in the headset that day; in Resources so `Shader.Find()` works in builds)
  — the lockpicking pieces: `Interaction.BigLock` and the picks. **Colour per vertex**
  (set when the mesh is built, `Interaction.LockMeshBuilder`) times `_BaseColor`, so a piece
  is one mesh and one material with no texture. **Lit per vertex**: `SampleSH` ambient plus
  the main light's Lambert, no shadows, no extra lights, never below `_MinLight` (0.35 -
  a lock in a dark room still has to be seen). No fog. **One shader, two materials**
  (`LockMeshBuilder.CreateMaterial(seeThrough, minLight)`): solid = Geometry queue,
  `Blend One Zero`; see-through = Transparent queue, `SrcAlpha OneMinusSrcAlpha`, faded by
  `_Alpha` (`_SrcBlend`/`_DstBlend` are material properties). `ZWrite On` both ways. The
  big lock wears the see-through one only while fading (a fraction of a second, a small
  object - within the overdraw rule) and the solid one otherwise. ForwardLit and DepthOnly
  passes; SRP Batcher compatible; stereo instancing macros.

## Gameplay light (added 2026-10-04)

How much light falls on a point, for visibility. **Worked out from the level's geometry;
only the lights are placed by hand** (maintainer's decision 2026-10-04). Two earlier versions
the same day were replaced before use: designer-placed shadow volumes in a lit world, then
light volumes in a dark one - don't bring either back. Levels are at night. The player's
side (`Player.PlayerVisibility`) is in `Scripts/Player/CLAUDE.md`.

- **`SceneLight`** (static) — `LevelAt(worldPoint)`: 0 = full shadow, 1 = fully lit; the
  **brightest** light reaching the point (they don't add). Moon first: one
  `Physics.Raycast` from the point towards the moon; nothing hit = `Moonlight.Level`. Then
  each lit `LightSource`: skipped if out of range or no brighter here than what's already
  found, otherwise one `Physics.Linecast` from the source to the point. Rays use
  Environment + Interactable (mask cached on first use), triggers ignored - so walls, roofs,
  props and closed doors block light, and gameplay shadows fall where the rendered ones do
  **provided every shadow-casting mesh has a collider on one of those layers**. The rendered
  shadow map is deliberately not read back from the GPU (slow, and a frame late). No
  allocation; callers should sample a few times a second, not per frame.
- **`Moonlight`** — on the scene's Directional Light. `level` (0.4), `maxDistance` (200m, how
  far the ray looks), `DirectionToMoon` = `-transform.forward`. `Moonlight.Instance` set in
  `OnEnable`; with none in the scene nothing is moonlit. Turning the Directional Light moves
  the gameplay shadows with the rendered ones.
- **`LightSource`** — a torch or lamp: `level` (0-1), `range` (metres) and
  `fullBrightnessFraction` (0.5): full level out to that fraction of the range, then a
  straight-line fade to 0 at the range (`LevelAtDistance()`). Self-registering static list
  (`Count`/`Get(i)`). **Disabling the component puts it out** - nothing else to update. It's
  the gameplay light only; the visible light is an ordinary Unity `Light` on the same object
  (toggling that, flame VFX and audio together is still to do, with the water bolt in
  Phase 5). Place it a little out from the wall it hangs on. `IDebugDrawable`: orange wire
  sphere at the range, plus the full-brightness distance when selected.
- The Sound Test House has three sources with matching point lights (Hall torch, Kitchen
  torch, Lounge lamp at 0.7); moonlight reaches the Yard and the roofless upper floor where
  the walls don't shadow them; the Store is dark behind its closed door.

## Surfaces (added 2026-10-04)

What a floor is made of, for footsteps and landings - the player's now
(`Player.PlayerFootsteps`, see `Scripts/Player/CLAUDE.md`), guards' later.

- **`SurfaceType`** (enum) — `Stone`, `Wood`, `Carpet`, `Metal`, `Water`, `Tile`, `Grass`,
  `Gravel`. `Stone` is first, so it's what anything untagged is: only floors that differ need
  a tag. **Add new values at the end** - tags and `SurfaceSounds` entries are serialized as
  the number.
- **`SurfaceTag`** — data-only component holding a `SurfaceType`, on the collider's object or
  **any parent** (a tag covers everything beneath it; a nearer tag overrides).
  `SurfaceTag.Of(collider)` walks up the hierarchy with `TryGetComponent` and returns `Stone`
  if there is none - called only when a foot comes down, never per frame.
  `SurfaceTag.TryFindBelow(from, distance, layers, out surface)` is the shared floor lookup:
  one ray straight down (triggers ignored), false on a miss. Chosen over a
  physics-material lookup (2026-10-04) because one tag on a group covers a whole floor.
- **`SurfaceSounds`** (ScriptableObject, Create > TeaLeaf > Surface Sounds) — one entry per
  surface: a footstep `SoundCue`. `GetFootstep(surface)` indexes a lookup array built on
  first use (rebuilt after an Inspector edit); a surface with no cue uses Stone's.
  `GetLoudness(surface)` (2026-10-07) = the surface's footstep noise radius over Stone's (1
  for stone, about 0.4 carpet, 1.7 metal; 1 if a cue is missing): how loud a surface is for
  anything else landing on it, used by `Interaction.ImpactNoise`, so a surface's loudness is
  still tuned in one place. **No
  landing cues** (removed 2026-10-04, maintainer's decision): a landing is the surface's
  step played loud, once per foot, by the walker.
  **Heel and toe** (added 2026-10-04, for hard surfaces; on for Stone and Tile): per entry,
  `heelAndToe` (toggle), `heelAndToeRatio` (share of steps that get it: 1 = every step, 0.6 =
  six in ten on average), `toeVolume` (the second sound's volume as a fraction of the first),
  `toeDelay` (seconds from heel to toe) and `toeMuffle` (0-1, how dull the toe is; passed as
  `Play()`'s `muffle`). `PlayStep(surface, position, source, noiseScale,
  volumeScale, ref cadence)` is how a walker makes a step: the cue with its noise, then the
  toe - the same cue again (its own random clip), audio only, through `Play()`'s `delay`.
  **Normal heel first, quieter toe second**: the reverse (quiet heel, then the toe at normal
  volume) was tried 2026-10-04 and rejected by ear - don't suggest it again; `toeVolume`
  carries `[FormerlySerializedAs("heelVolume")]` from that trial. Which steps get a
  toe is decided by the walker's own **`HeelAndToeCadence`** (struct): each step adds the
  ratio to a credit, and a toe sounds when the credit reaches a target re-picked at random in
  0.5-1.5, spending 1 - so the average is the ratio, a step early or late, never a long run
  either way (a per-step dice roll was rejected for that). **A surface's loudness to guards is its cue's `noiseRadius`**, not a
  separate table; the walker scales it by stance through `Play()`'s `noiseScale`.

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
  Also the footstep surface test (2026-10-04): upper floor and stairs tagged Wood, and 2cm
  floor coverings tagged Carpet (Lounge), Tile (Kitchen), Metal (Store) and, across the Yard
  left to right, Water (a puddle), Gravel (a path from the gate) and Grass (a lawn); the Hall
  and the Yard's edges are untagged stone.
- **`SoundPlayer`** (one per scene, on a `Sound Player` object at the scene root;
  `SoundPlayer.Instance`, set in `Awake()`) — plays every sound.
  `Play(cue, position, source = null, noiseScale = 1, volumeScale = 1, delay = 0, muffle = 0)`
  (`muffle` 0-1 is added to the route's muffle for the low-pass cutoff only, not the volume -
  a step's dull toe) emits the
  cue's noise (scaled; 0 = none) and plays its audio (volume times `volumeScale` - a crouched
  footstep, a hard landing; `delay` seconds from now via `AudioSource.PlayDelayed()`, for
  two-part sounds like a landing's second foot or a step's toe, so callers need no timers) if it reaches the `listener` (the Main Camera transform): a
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
  seconds (noise off by default). With `surfaceSounds` assigned it's a walker: each play
  is a `SurfaceSounds.PlayStep()` on the floor beneath it, heel and toe included
  (`SurfaceTag.TryFindBelow()`, 1m down, all layers by default), falling back to its own cue - so the test house's stand-in guard
  changes sound from wood to stone to tile along its route. **`SoundDebug`** (`Core/Debug`, on the Debug object) — `L`
  writes one "SOUND REPORT" Console entry: the listener's room, voices playing, and each
  emitter's room, range, straight and path distance, muffle, heard-from point and
  played/not.
- **`PlaceholderSounds`** (`Core/Debug/Editor`, menu **TeaLeaf > Create Placeholder Sounds**)
  — generated stand-in audio: three footstep and two impact `.wav` files in
  `Assets/Audio/Placeholder` and the cues `Assets/Data/FootstepPlaceholder.asset` (range 25m,
  noise 6m) and `ImpactPlaceholder.asset` (range 35m, noise 10m; ranges raised 2026-10-03 from 12m/20m, where sounds faded in too abruptly when moving fast). Existing files are kept.
  Added 2026-10-09: a door creak - three `CreakPlaceholder` clips (`Creak()`: a smoothed
  sawtooth whose pitch rises by half over 0.5s with a slow waver, faded in and out) and
  `CreakPlaceholder.asset` (`CreakCuePath`; volume 0.6, range 20m, `NoiseType.Mechanism`,
  noise 5m), given to the test doors by `DoorTestArea`.
  Replace the clips in the cues with real recordings later. Added 2026-10-04: per-surface
  footsteps from one recipe (`SurfaceStep()`: a tone with an inharmonic overtone, plus dull and
  sharp hiss) - three clips and a cue each for wood (noise 7m), carpet (2.5m, range 15m), metal
  (10m, range 30m) and water (8m), `FootstepPlaceholder` doubling as stone (6m); and
  `Assets/Data/SurfaceSoundsPlaceholder.asset` (`SurfaceSoundsPath`) listing them all.
  **Real clips swapped in 2026-10-04** from `Assets/Audio/footsteps/<set>/` (`.ogg`, each
  folder with a `license.txt`): the cues kept their "Placeholder" names (the test builders
  load them by path) but now play `boots` (stone, `FootstepPlaceholder`), `wood`, `metal` and
  `water`. Carpet has no recording of its own: its cue plays the `grass` clips for now (at
  carpet's own volume and 2.5m noise), leaving the generated carpet `.wav`s unused. Three
  more cues were made by hand for the added surfaces (not by the generator, which would
  recreate `SurfaceSoundsPlaceholder` with only the first five if it were deleted):
  `FootstepTile` (noise 9m, range 28m), `FootstepGrass` (3m, 18m, volume 0.6) and
  `FootstepGravel` (9m, 28m), all listed in `SurfaceSoundsPlaceholder`. Unused sets: `bones`,
  `dog`, `mech`.
  Most sets are CC-BY 3.0 (swuing, Eelke, EminYILDIRIM, ceberation, sabotovat, Lee Barkovich
  - credit needed in a release); `bones` and `gravel` are CC0.

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
- **`FogTestDebug`** (`Core/Debug`, added 2026-10-08; **temporary**, for judging the look of
  Silent Hill 2 style fog - not a decision to use it, distance haze is still only a reserve)
  — on the Debug object; while enabled in Play Mode it turns on Unity's built-in fog
  (`RenderSettings`: `fogColor` pale grey, `mode` Linear 1-18m, or `density` for the
  exponential modes), sets the main camera's clear colour to the fog colour and, with
  `hideSky`, deactivates `Sky Stars`/`Sky Moon` (found by name, once). Disabling it puts
  everything back; nothing is saved to the scene; values apply live from the Inspector.
  Two extras, added the same day at the maintainer's request, both untested.
  **`skyGradient`** (replaces the flat clear colour and `hideSky` while on): the sky stays,
  and a 100m ball of vertices round the head (`Fog Test Sky Haze`, re-centred in
  `LateUpdate()`) is drawn over it in the fog colour - solid from straight down to
  `hazeFullAngle` (5°) above the horizon, thinning to `overheadFog` (0.35) at
  `hazeClearAngle` (50°) and above. It borrows the `TeaLeaf/DebugLines` shader (vertex
  colours, `_ZTest` LessEqual) with its render queue set to Transparent - 390, just after
  the sky; the vertex colour is converted to linear by hand so it matches the fog.
  **`clearIndoors`**: one ray straight up from the head every `roofCheckInterval` (0.2s) on
  `roofLayers` (Environment) within `roofCheckHeight` (6m) = indoors; the fog then eases to
  `indoorFogAmount` (0.1) of itself over `indoorBlendTime` (1.5s). A roof ray rather than
  the sound rooms because it needs no setup and the test house's Yard is a room but
  outdoors; a kept version would use authored volumes or per-surface fog. Known flaws,
  accepted for a look test: fog is global, so from indoors the street through a window is
  clear too; and fog on geometry ignores height, so a tall distant building is fogged to
  its top against a cleared sky.
  Not a full-screen effect (each fog-aware shader blends by distance), and the project's
  own shaders (overlay, ghost, sky, debug lines) ignore fog. Remove it once the look has
  been judged.
- **Shader `TeaLeaf/DebugLines`** (`Assets/Art/Shaders/Resources/DebugLines.shader`) — like
  `TeaLeaf/Overlay` (unlit, Overlay queue, `ZWrite Off`, `Cull Off`, alpha blend, single-pass
  instanced stereo macros, `SRPDefaultUnlit` pass, in `Resources` for `Shader.Find()`) but
  coloured per vertex, with the depth test as a `_ZTest` material property. Lines are one pixel
  wide in the headset.
