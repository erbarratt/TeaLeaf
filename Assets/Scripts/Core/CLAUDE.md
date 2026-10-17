# Core systems (`Assets/Scripts/Core/`, namespace `Core`)

Detail for the core systems. The root `CLAUDE.md` holds the project rules, the tick order and
the physics layers; this file is loaded when working in this folder. Keep it up to date with
every change to these systems, like the root file.

Here: the game state/level manager, the screen fade, noise events, sound, surfaces, gameplay
light, the procedural night sky, the level surface shaders and the shared debug drawing.

## Render settings

- **PC** (`Assets/Settings/PC_RPAsset.asset` and `PC_Renderer.asset`, the `PC` quality level,
  the default for Standalone): no depth texture, no opaque texture, HDR off, 2x MSAA
  (unmeasured; 4x is the aim), render scale 1, lens flares off, SRP Batcher on, GPU
  Resident Drawer on (Instanced); main light shadows 2048, 25m, **two cascades, hard
  edges** (soft shadows off) - with the level's shadows baked they cover only moving
  things; the renderer's **ambient occlusion feature is switched off**
  (`m_Active: 0` - still listed, not removed); no volume profile; rendering mode 2 (to be
  checked in the Inspector: it appears to be Forward+). Additional-light shadows, light
  cookies and reflection probe blending are still enabled and unused. The Main Camera has
  post-processing off.
- **Baked lighting** (`Main.unity`): the moon (`Moon and Sky`) is a **Mixed** Directional
  Light and the lighting mode is **Shadowmask** - plain Shadowmask in the `PC` quality
  level, not Distance Shadowmask, so static pieces are never drawn into the realtime
  shadow map; their shadows come from the bake. Every point light in the pack's prefabs
  (buildings, lampposts, the tower bridge's torch) is overridden to **Baked** in the scene
  - the `TL_Lampost` variant's own light is **Mixed** (Indirect Multiplier 0, soft
  shadows, 5m) with a `LightSource` and a `Flame`, the level generator's gameplay lamp
  (`Assets/Scripts/LevelGen/CLAUDE.md`), and `Main.unity`'s three instances override
  it to Baked while keeping the `LightSource` and `Flame`.
  Settings asset `Assets/Settings/Docks Lighting.lighting` (GPU lightmapper, 10 texels/m,
  2048 maps, Non-Directional, AO); the bake's output is in `Assets/Scenes/Main/` (two
  lightmaps with shadowmasks, about 85 MB, plus the occlusion data). Small pieces and
  everything that moves are lit by the light probes (`EnvironmentVariants`,
  `LightProbePlacer`, below). Moving, adding or removing a static piece, or changing a
  light, needs Place Light Probes run again and a rebake.
- The Main Camera's far clip plane is 200m (the sky is drawn at the far plane whatever its
  distance; `Moonlight.maxDistance` is a physics ray, unrelated).

## Foveated rendering (Quest builds only; never run on a device)

- **`FoveatedRendering`** (static, no component, nothing per frame) — asks the XR display for
  fixed foveated rendering in a Quest build. `Apply()` runs by itself once, after the first
  scene loads (`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`), on Android only, and sets
  `XRDisplaySubsystem.foveatedRenderingLevel` to the `Level` constant (0.5; 0 = off, 1 =
  strongest) with no flags (fixed, not eye-tracked). It needs the OpenXR **Foveated
  Rendering** feature enabled for Android and the **Foveated Rendering API** set to **SRP
  Foveation** (both set in `Assets/XR/Settings/OpenXR Package Settings.asset`). It does
  nothing on PC. Only relevant to a possible Quest port.
- `Mobile_RPAsset` (the `Mobile` quality level) has HDR off, MSAA off and render scale 0.8.

## Procedural night sky (not yet confirmed in the headset)

**No skybox**: the camera clears to a flat colour (black) and the stars and moon are drawn
over it as geometry. No star cubemap or panorama (a cubemap pixel is far coarser than a
headset pixel, so stars go soft). **A picture only - nothing in it is emissive or lights the
scene**; brightness is the Directional Light's and the ambient setting's job (ambient is
Flat).

- **`ProceduralSky`** (`[ExecuteAlways]`; on the Directional Light or any scene object -
  `Reset()` fills `viewCamera` with the main camera and `moonLight` from its own `Light`, else
  the scene's `Moonlight`) — in `OnEnable()` makes a `TeaLeaf/Sky` material and two objects
  at the scene root (`Sky Stars`, `Sky Moon`; `HideFlags.DontSave`, identity rotation - not
  children, which would turn with the light), sets `RenderSettings.skybox` to null and the
  camera (`viewCamera`, or `Camera.main` if empty) to clear to `skyColor`; `OnDisable()` puts
  both back and destroys what it made. `Rebuild()` builds the meshes and sets the material:
  about a millisecond, synchronous, before the first frame, behind `ScreenFade`'s level-start
  black. Nothing per frame in a build: `OnValidate()` (flags a rebuild after an Inspector
  change) and `Update()` (does it, and keeps the moon on the light while it's rotated) are
  `#if UNITY_EDITOR`.
- **Stars** — one mesh, one draw call: a quad per star. Settings: `seed`, `starCount` (2500),
  `starSize` (degrees, faintest to brightest), `minStarPixels`,
  `starMinBrightness`/`starMaxBrightness`, `starFaintBias` (higher = fewer bright ones),
  `starColors` (Gradient, a random point per star), `starColorAmount`, `horizonFade`. One
  random "magnitude" per star drives both brightness and size. **No star is made below the
  horizon**: directions are picked on the upper hemisphere only, fading in over
  `horizonFade` degrees (6) above the horizon.
- **Moon** — a second one-quad mesh pointing +Z, on its own object that `AimMoon()` (public,
  cheap) turns to `-moonLight.forward` (the same direction as `Moonlight.DirectionToMoon`);
  call it if anything ever turns the light at runtime. Sorting order 1, so it's drawn after
  the stars and covers them. `showMoon`, `moonSize` (degrees across, 4), `moonColor`. It has
  its own material (same shader, `_SURFACE_TEXTURE` keyword on) when `showMoonSurface` is
  set; off, a plain disc.
- **Moon surface** (`MoonSurfaceBuilder`, static; settings in `MoonSurfaceSettings`, the
  component's `moonSurface` field) — `Build(settings)` paints a greyscale RGBA32 texture
  (`textureSize` 512, **with mipmaps**, trilinear) that the shader multiplies `moonColor` by.
  Baked at level load rather than computed per pixel in the shader, because mipmaps stop
  sub-pixel crater detail flickering with head movement. The texture is the ball seen
  straight on: each pixel becomes a point on a unit sphere (`PointOnBall()`, z towards the
  viewer) and every feature is worked out there, so craters foreshorten at the limb. Burst
  jobs: `GroundJob` (parallel) - highlands, seas (`seaAmount`, `seaDarkness`, `seaScale`;
  layered simplex noise over a threshold) and mottling (`roughness`); `CratersJob` (single
  thread) - craters largest first so small ones overlie big (`craterCount`, `craterSize` as
  a fraction of the moon's radius, `craterSmallBias`, `craterContrast`), then `rayCraters`
  mid-sized craters with streaks (`rayBrightness`, `rayLength`), then `edgeDarkening` and the
  8-bit encode. Lit flat as a full moon; no phases. A few milliseconds. Jobs must not read
  static fields of the builder (it has none, for Burst).
- **Shader `TeaLeaf/Sky`** (`Assets/Art/Shaders/Resources/Sky.shader`; in `Resources` for
  `Shader.Find()`, stereo-instancing macros, `SRPDefaultUnlit`) — the mesh stores each
  disc's centre direction (position), linear colour (alpha 0 = add, 1 = cover), corner +
  radius + twinkle phase (UV0), twinkle speed + weight (UV1); the vertex shader builds the
  quad. **Camera-locked**: corners are placed 1m from `_WorldSpaceCameraPos` in the star's
  direction, then depth forced to the far plane - no parallax, identical direction in both
  eyes, far clip distance irrelevant. **Minimum size**: a star under `_MinPixelRadius` pixels
  is drawn at that size and dimmed by the area ratio instead (sub-pixel stars flicker with
  head movement). **Twinkle**: three sines at unrelated speeds off `_Time`, per-star phase
  and speed, `_TwinkleAmount`/`_TwinkleSpeed`, scaled from full at the horizon to
  `_TwinkleOverhead` at the zenith (`twinkleAmount` 0.2, `twinkleSpeed` 5, `twinkleOverhead`
  0.3 on the component); brightness only, all per vertex. Fragment: a disc with a one-pixel
  `fwidth()` edge. Queue `Transparent-400` with `ZTest LEqual`, `ZWrite Off`: after opaques,
  before other transparents. `Blend One OneMinusSrcAlpha` so one shader both adds (stars)
  and covers (moon). `#pragma multi_compile_local _ _SURFACE_TEXTURE` (not `shader_feature`:
  the moon's material is made in code, so a build would strip the variant) multiplies the
  colour by `_SurfaceTex`, sampled with the quad's corner coordinates.
- **Nebula clouds: not built** (wanted later). The route: a small cloud-only cubemap (about
  256 per face) behind the stars - clouds are soft, so low resolution doesn't show.
- The scene's default reflection was baked from an old sky and stays until lighting is
  regenerated.

## Level surface shaders

- **`TeaLeaf/Cobblestone`** (`Assets/Art/Shaders/Cobblestone.shader`; not in `Resources`; no
  material uses it at present, and one made for it lives in `Assets/Art/Materials`) — a cobbled floor with no
  textures: rounded irregular stones, each a colour from a range, a separate gap colour, no
  repeat, the same cobble size on any object, everything tunable. **Don't rename existing
  properties** (the material stores its tuned values by property name).
  - **World-space**: the pattern is read from the pixel's world position along whichever
    world axis the surface faces most, divided by `_CobbleSize` (0.14m) - object scale
    doesn't matter and adjoining pieces join seamlessly, but the pattern slides across
    anything that moves (floors and walls, not props).
  - **Pattern**: Voronoi as a power diagram - a jittered point per grid cell
    (`_Irregularity` 0.7), the pixel belongs to the nearest by distance squared less a
    per-stone random weight (`_SizeVariation`; borders stay straight lines with an exact
    distance, so gap width doesn't change with stone size). The distance to each border is
    combined with a smooth minimum (`_CornerRoundness`), which rounds the corners.
  - **Search size**: `SEARCH_RADIUS` 2 = 5x5 cells by default, or 3x3 with the
    `_NARROW_SEARCH` toggle. 3x3 is only right with Size Variation near 0 and modest Corner
    Roundness: otherwise a border goes missing over part of a large stone and the height
    jumps at cell edges, drawn as thin lines by the derivative normal. Never hide missing
    neighbours by fading a point's pull - search far enough to have them.
  - **Gaps**: `_GapWidth` may be negative (range -1 to 0.4), growing the rounded outline
    back out; `_MinGap` (0.02) is a line always kept along the true (unsmoothed) border.
    `Cobbles()` returns how far inside the stone the pixel is:
    `min(rounded - GapWidth/2, border - MinGap/2)`. Height = 0 at the stone's edge and in
    the gap, rising over `_Bevel` to 1.
  - **Colour**: a per-cell random point along `_ColorA`-`_ColorB`-`_ColorC`,
    `_BrightnessVariation`, `_EdgeDarkening` down the shoulder, `_GapColor`; `_Seed` changes
    the layout.
  - **Crevice shadow** (stands in for ambient occlusion): colour and shine are multiplied by
    `1 - _OcclusionStrength * (1 - smoothstep(0, _OcclusionWidth, abs(edge)))` (0.6 and 0.12
    of a cobble). Applied after the dirt, faded out with the distance fade; a colour
    multiply, not `surfaceData.occlusion`. Not in BrickWall.
  - **Normal** from screen-space derivatives of the height (`_Depth` metres x
    `_NormalStrength`, Mikkelsen's method) - no tangents, no normal map.
  - **Lit** with URP's Blinn-Phong (`UniversalFragmentBlinnPhong`), optional shine
    (`_SPECULAR_COLOR` toggle); SRP Batcher compatible, stereo instanced, fog-aware;
    ShadowCaster, DepthOnly and DepthNormals passes; **no Meta pass** (a lightmap bake won't
    see its colours - add one before baking the level).
  - **Distance fade** (`_FadeStart`/`_FadeEnd`, cobbles per pixel): pattern and bump fade to
    a flat average colour before the cobbles get sub-pixel.
  - **Parallax** (`_PARALLAX` toggle, off, `_ParallaxDepth`): one offset-limited shift of
    the coordinates by the height, not parallax occlusion - it works the pattern out twice.
  - **Dirt layer** (`_DIRT` toggle, off): three two-channel value noises (`Noise2()`). A
    slow one places grime patches (`_GrimeAmount`, `_GrimeCoverage`, `_GrimeScale` metres;
    `_GrimeLowBias` puts dirt in gaps and down shoulders), tinting to `_DirtColor` and
    dulling the shine; applied after the distance fade. A second warps the coordinates
    before the cobbles are found (`_WarpAmount`, `_WarpScale`). A fine one ("flecks",
    `_MottleScale`) mottles the stones (`_MottleAmount`), pits them for the lighting
    (`_MottleBump`), varies the gap colour (`_GapNoiseAmount`) and chips the outline
    (`_EdgeRaggedness`). **Flecks grow with distance instead of fading**: whenever a fleck
    would be under `_FleckPixels` (4) pixels the noise is read at double the size, as many
    times as needed, blending the two nearest doublings. `FleckNoise2()` wraps each grid
    corner into 0-1023 before hashing, because `Hash22()` runs out of float precision at
    large grid coordinates.
  - **Cost** is per pixel on a view-filling surface (about 25 hashes and 24 border distances
    with the 5x5 search - 9 and 8 with Narrow Search - doubled with parallax, plus 16 hashes
    with dirt). Unmeasured; the fallback is baking the pattern to textures.
- **`TeaLeaf/BrickWall`** (`Assets/Art/Shaders/BrickWall.shader`; **not yet compiled or
  seen**) — the cobblestone shader's sister for brick and coursed-stone walls. Same frame
  (world-space, no textures, Blinn-Phong, derivative normal, distance fade, optional
  one-shift parallax, the same Dirt layer, the same four passes, no Meta pass) - **the shared
  code is duplicated, not in an include**, so a fix to one must be made in both. All sizes
  are in **metres**. **Pattern, with no search**: rows `_BrickHeight` high, bricks
  `_BrickWidth` long (each including its share of mortar); odd rows slid by `_RowOffset`
  (0.5 = running bond) plus a per-row random `_RowOffsetVariation`; the lines between rows
  and between bricks each nudged up to 0.45 of a brick (`_HeightVariation`,
  `_WidthVariation` - 0 for brick, raised for stone). `FindSpan()` finds a pixel's brick from
  the nearest dividing line and the next one out: two hashes per axis. Edge distance is a
  rounded-rectangle distance (`_CornerRadius`) less half `_MortarWidth`; height rises over
  `_Bevel`. `_FaceTilt` leans each brick's face a random way. Colours A-B-C per brick,
  `_MortarColor`. **Direction**: on a wall, u runs along the wall and level
  (`cross(up, normal)`), v straight up; a surface facing mostly up or down uses world X/Z.
  Bricks don't wrap round a corner between two walls. About 6 hashes a pixel.
- **`TeaLeaf/Water`** (`Assets/Art/Shaders/Water.shader`; material
  `Assets/Art/Materials/TeaLeaf_Water.mat`; **not yet confirmed in the headset**) — harbour water at night: opaque (Geometry queue), world-space, no
  textures but an optional foam mask. It never reads the camera's depth or opaque texture:
  no see-through shallows, no refraction, no depth-found foam. **Don't rename existing
  properties.**
  - **Swell, per vertex**: three sine waves (`_WaveA/B/C`: direction in degrees, length m,
    crest height m, speed m/s) move each vertex **up and down only**, so the water's edge
    stays put against a wall; the slope is worked out with the height and gives the vertex
    normal. `Swell()`/`SwellPositionWS()` are in the `HLSLINCLUDE` block and every pass uses
    them. Something that floats must work the same sum out on the CPU from the material's
    wave values and `Time.time` (not built).
  - **The mesh** must be level, face up, and have vertices across it: Unity's default
    Plane (10m, a vertex every metre), tiled rather than scaled - the pattern is in world
    space, so tiles join. A wave needs at least four vertices along its length.
  - **Ripples, per pixel**: two layers of value noise with exact derivatives
    (`RippleNoise()`, 4 hashes each), the second turned 37 degrees, 0.45 of the size
    (`_RippleSize`) and half the height (`_RippleHeight`), sliding across the first. Their
    coordinates are made per vertex. They only add to the slope the normal is made from.
    Each layer fades out by ripples per pixel (`_FadeStart`/`_FadeEnd`), and the glint is
    spread wider where they have faded.
  - **Lighting is its own**, not URP's Blinn-Phong: `_WaterColor` lit by probe ambient
    (`SampleSH` straight up, per vertex) and each light's Lambert, plus `_HorizonColor`
    by Fresnel (`_FresnelPower`; a fixed colour, no reflection probe), plus a Blinn-Phong
    glint per light (`_GlintColor`, `_GlintStrength`, `_Smoothness`) that `_GlintHardness`
    squeezes into hard-edged dabs. Main light with shadows (the glint stops in a shadow),
    additional lights per pixel through `LIGHT_LOOP_BEGIN` (Forward+ or not), shadowmask
    from the probes, fog. **No screen-space derivative inside the light loop** (it doesn't
    compile in a loop of varying length). No lightmap or probe-volume variants.
  - **Foam** (`_FOAM` toggle, off): `_FoamMask`, a top-down texture laid over the world
    rectangle `_FoamArea` (centre X, centre Z, size X, size Z), white at the wall fading to
    black; foam is drawn where the mask, scaled by the ripples (`_FoamBreakup`) and a slow
    per-vertex sine (`_FoamLap`, `_FoamLapSpeed`), passes one half. Matt: no reflection or
    glint. Import the mask with Clamp and a black border.
  - **Passes**: ForwardLit, DepthOnly, DepthNormals. No ShadowCaster (water casts none) and
    no Meta pass. SRP Batcher compatible, stereo instanced.
  - **In a level**: on `Environment`, **no collider** (the bed below it has one), Cast
    Shadows off, not Contribute GI.
  - **Cost**: 8 hashes a pixel, one texture read with Foam on, one `pow` a light.
    Unmeasured.
- **`TeaLeaf/DoorLeaf`** (`Assets/Art/Shaders/DoorLeaf.shader`; a material references it) —
  a door leaf with a keyhole opening cut out of it, for `Interaction.DoorKeyhole` (see
  `Scripts/Interaction/CLAUDE.md`). A flat `_BaseColor` (no texture), Blinn-Phong, the same
  frame and passes as Cobblestone (lighting code copied, not shared). `KeyholeDistance()` is
  the signed distance in metres to a keyhole outline - the smaller of a circle's and a
  slot's distances - measured across the face only, from
  `_KeyholeCentre`/`_KeyholeRight`/`_KeyholeUp` (leaf object space) and `_KeyholeSize` (x =
  the circle's radius; the circle's centre is one radius above the keyhole's middle, the
  slot one radius wide down to two radii below it; `DoorKeyhole.BuildOutline()` must
  match), all set per renderer by `DoorKeyhole`'s `MaterialPropertyBlock` (which takes that
  leaf out of the SRP Batcher); all zero = no opening. Pixels inside are clipped, in
  ForwardLit, DepthOnly and DepthNormals; **the ShadowCaster pass doesn't cut**, so the door
  casts a whole shadow. The edge is one pixel of coverage sent out as alpha with
  `AlphaToMask On`. `_RimColor`/`_RimWidth` darken a band round the opening. Back faces are
  culled; the opening's walls are a mesh `DoorKeyhole` makes, drawn with this shader. `clip`
  weakens early depth rejection: for door leaves, not walls. A lock plate on the door uses
  it too (its own material), with its own block from `DoorKeyhole`.
- **`TeaLeaf/LockFade`** (`Assets/Art/Shaders/Resources/LockFade.shader`; in Resources for
  `Shader.Find()`) — the lockpicking pieces, the pack and the keyring. **Colour per vertex**
  (set when the mesh is built, `Interaction.LockMeshBuilder`) times `_BaseColor`, so a piece
  is one mesh and one material with no texture. **Lit per vertex**: `SampleSH` ambient plus
  the main light's Lambert, no shadows, no extra lights, never below `_MinLight` (0.35). No
  fog. **One shader, two materials** (`LockMeshBuilder.CreateMaterial(seeThrough,
  minLight)`): solid = Geometry queue, `Blend One Zero`; see-through = Transparent queue,
  `SrcAlpha OneMinusSrcAlpha`, faded by `_Alpha`. `ZWrite On` both ways. The big lock wears
  the see-through one only while fading and the solid one otherwise. ForwardLit and
  DepthOnly passes; SRP Batcher compatible; stereo instancing macros.

## Gameplay light

How much light falls on a point, for visibility. **Worked out from the level's geometry;
only the lights are placed by hand.** No shadow volumes or light volumes. Levels are at
night. The player's side (`Player.PlayerVisibility`) is in `Scripts/Player/CLAUDE.md`.

- **`SceneLight`** (static) — `LevelAt(worldPoint)`: 0 = full shadow, 1 = fully lit; the
  **brightest** light reaching the point (they don't add). Moon first: one
  `Physics.Raycast` from the point towards the moon; nothing hit = `Moonlight.Level`. Then
  each lit `LightSource`: skipped if out of range or no brighter here than what's already
  found, otherwise one `Physics.Linecast` from the source to the point. Rays use
  Environment + Interactable (mask cached on first use), triggers ignored - so walls, roofs,
  props and closed doors block light, and gameplay shadows fall where the rendered ones do
  **provided every shadow-casting mesh has a collider on one of those layers**. The rendered
  shadow map is deliberately not read back from the GPU. No allocation; callers should
  sample a few times a second, not per frame.
- **`Moonlight`** — on the scene's Directional Light. `level` (0.4), `maxDistance` (200m, how
  far the ray looks), `DirectionToMoon` = `-transform.forward`. `Moonlight.Instance` set in
  `OnEnable`; with none in the scene nothing is moonlit.
- **`LightSource`** — a torch or lamp: `level` (0-1), `range` (metres) and
  `fullBrightnessFraction` (0.5): full level out to that fraction of the range, then a
  straight-line fade to 0 at the range (`LevelAtDistance()`). Self-registering static list
  (`Count`/`Get(i)`). **Disabling the component puts it out.** It's the gameplay light only;
  the visible light is an ordinary Unity `Light` on the same object. Place it a little out
  from the wall it hangs on. `IDebugDrawable`: orange wire sphere at the range, plus the
  full-brightness distance when selected.
- **`Flame`** — something burning that can be put out (a torch, a brazier): the one place
  that switches a fire's parts off, or on again, together - its `LightSource`, its Unity
  `Light`, its `SoundLoop` (each found on the same object if not wired) and an optional
  `flameVisual` object. `Extinguish()` (with an optional `extinguishCue`), `Relight()`,
  `IsLit`, `startsLit`. Self-registering static list; **`Flame.ExtinguishNear(point,
  radius)`** puts out every lit one within the radius (distance checks only) and returns
  how many - what the water bolt calls. No `Update()`.
- The Sound Test House has three sources with matching point lights (Hall torch, Kitchen
  torch, Lounge lamp at 0.7); moonlight reaches the Yard and the roofless upper floor; the
  Store is dark behind its closed door.

## Surfaces

What a floor is made of, for footsteps and landings - the player's
(`Player.PlayerFootsteps`, see `Scripts/Player/CLAUDE.md`), guards' later - the ramp
that stands in for a flight of steps, and the barrels' shared collision shape.

- **`SurfaceType`** (enum) — `Stone`, `Wood`, `Carpet`, `Metal`, `Water`, `Tile`, `Grass`,
  `Gravel`, `Plaster`. `Stone` is first, so it's what anything untagged is: only floors that differ need
  a tag. **Add new values at the end** - tags and `SurfaceSounds` entries are serialized as
  the number.
- **`SurfaceTag`** — data-only component holding a `SurfaceType`, on the collider's object or
  **any parent** (a tag covers everything beneath it; a nearer tag overrides).
  `SurfaceTag.Of(collider)` walks up the hierarchy with `TryGetComponent` and returns `Stone`
  if there is none - called only when a foot comes down, never per frame.
  `SurfaceTag.TryFindBelow(from, distance, layers, out surface)` is the shared floor lookup:
  one ray straight down (triggers ignored), false on a miss. `SurfaceTag.Find(collider)`
  is the same walk returning the tag itself, or null. **Crossbow bolts:**
  `SurfaceTag.HoldsBolt(surface)` says which kinds a bolt sticks into - `Wood`, `Carpet`,
  `Grass`, `Plaster`; it bounces off the rest, so off everything untagged - and the tag's
  **`supportsRopeBolt`** toggle (off by default) says whether a rope bolt stuck there
  bears a climber's weight (see the crossbow in `Scripts/Player/CLAUDE.md`).
- **`SurfaceSounds`** (ScriptableObject, Create > TeaLeaf > Surface Sounds) — one entry per
  surface: a footstep `SoundCue`. `GetFootstep(surface)` indexes a lookup array built on
  first use (rebuilt after an Inspector edit); a surface with no cue uses Stone's.
  `GetLoudness(surface)` = the surface's footstep noise radius over Stone's (1 for stone,
  about 0.4 carpet, 1.7 metal; 1 if a cue is missing): how loud a surface is for anything
  else landing on it, used by `Interaction.ImpactNoise`. **No landing cues**: a landing is
  the surface's step played loud, once per foot, by the walker. **A surface's loudness to
  guards is its cue's `noiseRadius`**; the walker scales it by stance through `Play()`'s
  `noiseScale`.
  **Heel and toe** (for hard surfaces; on for Stone and Tile): per entry, `heelAndToe`
  (toggle), `heelAndToeRatio` (share of steps that get it), `toeVolume` (the second sound's
  volume as a fraction of the first), `toeDelay` (seconds from heel to toe) and `toeMuffle`
  (0-1, passed as `Play()`'s `muffle`). `PlayStep(surface, position, source, noiseScale,
  volumeScale, ref cadence)` is how a walker makes a step: the cue with its noise, then the
  toe - the same cue again (its own random clip), audio only, through `Play()`'s `delay`.
  **Normal heel first, quieter toe second - never the reverse.** Which steps get a toe is
  decided by the walker's own **`HeelAndToeCadence`** (struct): each step adds the ratio to
  a credit, and a toe sounds when the credit reaches a target re-picked at random in
  0.5-1.5, spending 1 - the average is the ratio, with no long runs either way (not a
  per-step dice roll).
- **`RampCollider`** — the collision for a flight of steps: one convex `MeshCollider`
  holding a six-corner wedge, so the player walks up a smooth ramp rather than each step.
  `[RequireComponent(MeshCollider)]`, on an empty child of the steps' prefab variant, at
  the foot of the first step in the middle of its width, on `Environment` (the
  `SurfaceTag` on it or a parent). **Own axes:** X across the steps, Y up, +Z up the
  flight - the slope starts on the floor at the object's position and reaches `height` at
  `length` along Z, `width` centred on X; the wedge is solid (sides, back and bottom too),
  so it suits free-standing steps. Sizes are in the object's own units: keep it at scale
  1. **The mesh is never saved**: `Rebuild()` makes it from the three sizes in `Awake()`
  (once, at load) and in `OnValidate()` (scene load and Inspector edits, so editor bakes
  such as the NavMesh see it), reusing one `Mesh` per component; it sets the collider's
  mesh and `convex` itself. No gizmo of its own: the `MeshCollider` draws the wedge when
  selected.
- **`BarrelCollider`** — the tuning tool for a barrel's collision, **not a runtime
  collider builder**: a twelve-sided shape of four rings (`bottomRim`, `lowerBelly`,
  `upperBelly`, `topRim`, each a `height` above the object and a `radius`), baked to one
  mesh asset that every barrel's convex `MeshCollider` shares, so the shape is cooked at
  import and nothing is built at load. `[RequireComponent(MeshCollider)]`, on a
  `BarrelCollider` child of the `Prefabs/Containers/Barrel` variant (which replaces the
  pack's own collision child), at the middle of the barrel's base, on `Environment` with a
  Wood `SurfaceTag`; local +Y up the barrel, scale 1. **The collider only changes on a
  bake**: right-click > **Bake To Mesh Asset** writes the mesh to `meshAssetPath`
  (`Assets/Art/Colliders/BarrelCollider.asset`), filling the existing asset in again
  rather than replacing it, and assigns it to its own `MeshCollider`. The numbers are
  shown as a yellow outline while selected (`IDebugDrawable` through `DebugLines`, **not
  registered** with `DebugDrawRegistry`: it is a Scene view tool, and registering would
  cost every barrel something at runtime). Everything but the outline is
  `#if UNITY_EDITOR`; no `Awake()` or `Update()` - it stays on the prefab only to keep
  the numbers. **Other barrel prefabs get a plain convex `MeshCollider` with the baked
  mesh, no `BarrelCollider`.** Convex, so a ring narrower than the line between its
  neighbours is bridged over.
- **`EnvironmentVariants`** (`Core/Editor`, menu **TeaLeaf > Make Environment Variants**)
  — makes the prefab variants for static level geometry in bulk. Select pack prefabs, or
  folders holding them, in the Project window and run it: each gets a variant in
  `Assets/Prefabs/Environment/<category>/` under the pack's name with a leading
  pack prefix (`Medieval_`, `S_MVillage_`) swapped for `TL_` and a duplicate-name
  suffix such as ` (1)` dropped (other names are kept), with every object in it
  (LOD meshes and collision children too) on `Environment` and marked Static (Contribute
  GI, Occluder, Occludee, Batching, Reflection Probe). The category is the pack folder the
  prefab is in, or that folder's parent when it is only the pack's prefabs folder
  (`Walls/Prfbs` gives `Walls`; the pack's `Masonary` is filed as `Masonry`). **A pack
  prefab that already has a variant anywhere under `Assets/Prefabs` is skipped** - found
  from the variants themselves, so one that was renamed or moved still counts - so it can
  be run again without overwriting hand work. It adds no colliders, `SurfaceTag`s or
  scripts. **Not for things that move**: doors, and foliage that sways, get their variants
  by hand. **Small pieces are lit by light probes**: every static (Contribute GI) mesh
  renderer whose longest side is under 1m, or whose middle side is under 0.15m (a rod:
  rails, beams, brackets), gets Receive Global Illumination: Light Probes, measured on the
  mesh's own bounds times the object's scale (`SmallPieceSize`, `ThinPieceWidth`); thin
  flat pieces (floors, roofs, wall panels) keep their lightmaps. New variants get it as
  they are made; **TeaLeaf > Set Small Pieces To Light Probes** applies it to every prefab
  already under `Assets/Prefabs/Environment` (opened with `LoadPrefabContents`, saved only
  if something changed, so on a variant it is an override, nested pack pieces included).
  Pieces already on probes, and non-static ones, are left alone.
- **`ColliderRemover`** (`Core/Editor`, menu **TeaLeaf > Remove Colliders From
  Selection**, offered while something is selected) — removes every collider from the
  objects selected in the Hierarchy and all their children, inactive ones included, as
  one Undo step, logging the count. A `RampCollider` is removed too (first, since it
  requires its `MeshCollider`). On a prefab instance the removal is an override; the
  prefab is untouched.
- **`LightProbePlacer`** (`Core/Editor`, menu **TeaLeaf > Place Light Probes**) — fills the
  level's one `LightProbeGroup`, at `Environment/Sectors/Global/Lighting/Light Probes`
  (made, with any missing groups on the way, if absent). A grid every 2.5m (`Spacing`) over
  the box round every Environment collider; in each column, rays straight down find every
  floor (normal y at least 0.7, so ramps count and walls don't; a floor within 0.5m below
  the last gives nothing), and a probe goes 0.5m and 2m above each (`_heights`). A probe is
  left out if it is within 0.15m of geometry, if anything lies between it and its floor (a
  low ceiling), or if at least three of six rays out from it first hit the inside of a mesh
  collider (back faces on: shut inside a building with no interior). Each run replaces the
  group's probes (undoable); rerun after layout changes, then rebake. Environment layer only,
  so it also fills spaces the player can't reach (a sea bed under the water).

## Noise and sound

Two systems fed from one asset, so what the player hears and what guards hear are tuned
together: **noise** is the gameplay event guards listen to; **sound** is the audio the player
hears. Both travel the same way - **Thief-style rooms and portals**: straight within a room,
room to room only through portals, never through walls. The aim is that a sound always tells
the player truthfully where it is, on stereo headphones.

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
  not - for debug drawing and UI, not for guards; subscribers must unsubscribe in
  `OnDisable` because a static event outlives a scene reload.
- **`SoundCue`** (ScriptableObject, Create > TeaLeaf > Sound Cue) — one kind of sound. Audio
  half: `clips` (random pick), `volume`, `pitchVariation`, `audibleRange` (metres; beyond it
  the sound isn't played). Noise half: `noiseType`, `noiseRadius` (0 = guards never hear it).
  `EmitNoise(position, source, radiusScale)` emits the noise half (scale e.g. by stance). The
  audible range and the noise radius are separate on purpose.
- **`SoundRoom`** — a maths box like `ExitZone` (serialized `size`, `IDebugDrawable`, blue),
  self-registering, `[ExecuteAlways]` so rooms are also registered in Edit Mode.
  `SoundRoom.Find(worldPoint)` = the room containing a point, the smallest where rooms
  overlap, **null = outside, which acts as one big room**. Neighbouring rooms should meet in
  the middle of the wall between them. Outdoor areas that should block each other need their
  own rooms (two points both "outside" always hear each other directly). It also holds the
  room's `reverb` and `ambience` (below). **In Play Mode a room's box is worked out once and
  kept** (`Cache()`: world-to-local matrix, half size, volume), since `Find()` runs for every
  sound, loop check and noise listener; `OnEnable()` and `OnValidate()` throw the kept
  values away, and in Edit Mode nothing is kept. A room moved by code while playing would
  need re-enabling.
- **`SoundPortal`** — an opening between two rooms (doorway, stairwell, window): a rectangle
  (`size`) in the object's local X/Y plane, local Z through the opening, placed mid-wall. Its
  two rooms are found automatically by probing `probeDistance` (0.25m) out from each face
  (`FrontRoom`/`BackRoom`; warns if both are the same). **Open**: passing through adds
  `openMuffle` (0.3) to the path's muffle - except a source in the next room heard in a
  straight line through the opening, which stays clear (`CrossingPoint`'s
  `isStraightThrough`). **Closed** (`startsOpen` off, or `SetOpen(false)` - for doors to
  call): sound still passes, with `closedMuffle` (0.6) and `closedExtraDistance` (4m) added
  to the path. A portal that never opens is how a thin wall or window leaks sound - **walls
  never leak by themselves**. `CrossingPoint(from, to)` = where the straight line crosses
  the portal plane, clamped to the rectangle. Drawn cyan (open), red (closed) or **yellow if
  both sides are the same room** - checked live by `ProbeRooms()`, in Edit Mode too.
  **`SoundPortalEditor`** (`Core/Editor`) adds a box to the Inspector naming the two rooms it
  joins, or the same warning. Not baked: the links are worked out at runtime.
- **`SoundPropagation`** (static) — `TryGetPath(from, to, maxDistance, out SoundPath)` (and
  an overload taking the two rooms). Same room: the straight line. Otherwise the shortest
  route through portals: each portal of the source's room against each of the listener's,
  `|from - first| + table[first, last] + |last - to|` plus closed penalties, using portal
  centres - except a single portal joining the two rooms, which is measured through its exact
  crossing point. False if no route or longer than `maxDistance`. The portal-to-portal tables
  (distance, muffle, previous portal; flat arrays) are built with Floyd-Warshall, lazily, the
  first query after `MarkDirty()` (a portal opened or closed) or `MarkLayoutDirty()` (a room
  or portal enabled or disabled) - only the latter makes the portals look their rooms up
  again. Rooms and portals are assumed not to move while playing. No physics, no allocation
  per query. **`SoundPath`** (readonly struct): `Distance`, `Muffle` (0-1), `HeardFrom`,
  `IsDirect`.
- **`SoundPlayer`** (one per scene, on a `Sound Player` object at the scene root;
  `SoundPlayer.Instance`, set in `Awake()`) — plays every sound.
  `Play(cue, position, source = null, noiseScale = 1, volumeScale = 1, delay = 0, muffle = 0)`
  emits the cue's noise (scaled; 0 = none) and plays its audio if it reaches the `listener`
  (the Main Camera transform): a straight-line range reject, then
  `SoundPropagation.TryGetPath()` limited to the cue's `audibleRange` - no path = not played.
  `volumeScale` multiplies the cue's volume (a crouched footstep, a hard landing); `delay`
  starts the audio that many seconds from now via `AudioSource.PlayDelayed()` (two-part
  sounds, so callers need no timers); `muffle` (0-1) is added to the route's muffle for the
  low-pass cutoff only, not the volume. Same room: played at its real position. Through
  portals: played at `ears + direction to HeardFrom * path Distance`, so panning and the
  distance fade follow the route. The fade is `(1 - distance / audibleRange) ^
  falloffSharpness` (default 2), reaching silence at `audibleRange` - not linear, which made
  sounds fade in noticeably when moving fast. Muffle: volume towards `muffledVolume` (0.5)
  and the voice's `AudioLowPassFilter` cutoff from 22 kHz towards `muffledCutoff` (1200 Hz),
  interpolated by ratio (octaves); **the filter is disabled unless the sound is muffled**.
  A fixed pool of `voiceCount` (16) child `AudioSource`s made in `Awake()` (no doppler);
  when all are busy the oldest is reused. A one-shot's position and muffle are fixed when it
  starts. The voice caps (16 one-shot, 8 loop) are the main cost control.
  **It does the one-off sounds itself and hands the rest to three plain classes it makes
  and ticks**: `SoundLoopPlayer` (loops), `ListenerReverb` (reverb) and `RoomAmbience`
  (ambience), below. It keeps every serialized sound setting (the helpers are given values
  or read them through its properties, so they tune live), makes every voice
  (`CreateVoice()`, also used by the loop player), and owns `SetMuffle()`,
  `HeardOffset()` (static) and `RefreshListenerRoom(ears)`. Its own `Update()` (not a
  `Tick()`: it isn't a player system and has no ordering needs; unscaled time, steps capped
  at 0.05s): the spatialiser's one-off voices, the listener's room every
  `listenerCheckInterval` (0.2s) - which sets the reverb's and ambience's targets - then
  the three helpers' ticks. `ListenerRoom`, `Spatialiser`, `IsSpatialised`,
  `PlayingVoiceCount`, `PlayingLoopCount`. **`Heard`** (instance event: heard-from point,
  loudness, the cue's `NoiseType`, source) is raised by `Play()` for every one-off sound
  that actually reaches the player - the real point it is heard from (the sound itself in
  the same room, otherwise the portal it came through: `SoundPath.HeardFrom`) and its
  loudness at the ears (cue volume x `volumeScale` x the distance fade);
  not for loops or ambience. `Player.PlayerCompass` listens to it for its sound arrows.
- **`SoundLoop`** — a sound that never stops, from one place (a torch, a fire): a `cue` and
  a `volumeScale`, on the object that makes it. Self-registering static list
  (`Count`/`Get(i)`); **disabling it fades it out**. The cue's noise half is ignored; one
  clip is picked when it starts to be heard, started at a random point so two torches don't
  crackle in step. It also holds the `SoundPlayer`'s bookkeeping for it (`NextCheckTime`,
  `IsAudible`, `Path`, `Loudness`, `VoiceIndex`). **`SoundLoopPlayer`** (plain class) runs
  them: each
  loop's route is worked out again on its own timer - `loopCheckInterval` (0.2s) within
  straight-line earshot, `loopFarCheckInterval` (1s) beyond it, each interval ±10% at random
  so loops drift apart (`TickChecks()`; the listener's room is looked up once per frame
  that has a check). A loop that reaches the player takes one of `loopVoiceCount` (8) **loop
  voices, a separate pool** so one-shots can't cut a torch off; with none free it takes the
  quietest one's only if it is 1.25 times louder (`StealMargin`). Per frame, only for loops
  with a voice (`TickVoices()`): the voice is placed at `ears + offset`. In the same
  room and settled the offset is exact; through a portal, and for `4 x loopEaseTime` after
  changing between direct and through-a-portal, it is eased towards the target with
  `Vector3.Slerp` (turns the direction and slides the distance separately - a straight line
  from one side of the head to the other would pass through it and be loud on the way).
  Muffle is eased over `loopEaseTime` (0.25s), the fade in/out over `loopFadeTime` (0.4s);
  volume and filter are only written when changed.
- **Reverb** — **one shared reverb, set by the room the listener is in, not the sound's**:
  every sound heard while standing in an echoey room gets that room's reverb.
  **`ReverbSettings`** (serializable struct) is four numbers: `level` (0-2, 1 = as loud as
  Unity's presets), `decayTime` (seconds), `damping` (0 bright stone - 1 dull carpet: lowers
  `roomHF` and `decayHFRatio`) and `size` (metres to the walls: sets the two delays and
  quietens the first echoes). `ApplyTo(AudioReverbFilter, amount)` maps them to Unity's
  parameters; `Lerp()`, `Matches()`; static presets `None`, `SmallRoom`, `Room`,
  `StoneRoom`, `StoneHall`, `Cellar`, `Warehouse`, `Alley`, `Outdoors`. Each `SoundRoom` has a `reverb` field (default `Room`) with the presets
  on the component's right-click menu (**Reverb Preset/...**); outside every room uses
  `SoundPlayer.outsideReverb` (`Outdoors`). **`ListenerReverb`** (plain class) adds one
  `AudioReverbFilter` (User preset) to the object the `AudioListener` is on, where it works
  on the whole mix after spatialising; every 0.2s `SoundPlayer` gives it the listener's
  room's settings (`SetWanted()`) and, if they differ from the target (a new room, or a room tuned in the Inspector
  while playing), blends there over `reverbBlendTime` (0.6s, SmoothStep) - the filter is
  only written during a blend. `reverbAmount` (1, live) multiplies every room's level.
  **Never an `AudioReverbZone`**: with one riding on the listener and voices sending by
  `reverbZoneMix`, no reverb was heard at all. The listener filter has no per-source send,
  and the room ambience gets the reverb too. `reverb` off on the player = no filter added.
- **Room ambience** (**`RoomAmbience`**, plain class) — a background loop with no position,
  chosen by the listener's room:
  `SoundRoom.ambience` (a cue; its **first** clip) and `ambienceVolume`,
  `SoundPlayer.outsideAmbience`/`outsideAmbienceVolume` for no room. Two plain 2D sources; a
  change of room fades one out and the other in over `ambienceFadeTime` (1.5s). Rooms that
  share a cue carry on without a restart, and stepping back into the room just left turns
  its fade round rather than restarting the clip. Nothing runs once the fades have arrived.
  A sound that comes from somewhere is a `SoundLoop`, not this.
- **Spatialiser** — the project's own; no third-party audio package is installed.
  `SoundPlayer.spatialiserMode` (`SpatialiserMode`: `UnityPanning`, `BuiltIn` - the default
  - or `Plugin`; read in `Awake()`). **`SpatialVoice`** is one voice's spatialiser, added to
  each voice object after its low-pass filter, with the voice's `AudioSource` set to **2D**
  (`spatialBlend` 0) so Unity adds no panning or distance fade of its own - `SpatialVoice`
  does both. Three cues, from where the sound is in the head's own axes: the **time
  difference** between the ears (Woodworth's ball-head formula, up to about 0.7ms, a
  fractional delay line of 256 samples), the **head shadow** (the far ear quieter and with a
  one-pole low-pass mixed in, the near ear slightly up) and **behind** (a low-pass mixed
  into both ears, slightly quieter). "To the side" is the direction's x, so a sound above,
  ahead or behind on the middle plane is centred. **No cue for up and down** - that takes
  measured ear data (an HRTF). Direction fades out within 0.25m of the head. Two threads:
  `SetTarget(localOffset, range, falloffSharpness)` on the main thread works out per-ear
  targets (volume including the distance fade, delay, shadow mix, rear mix);
  `OnAudioFilterRead()` on the audio thread slides from the values in use to the targets
  across each buffer (no clicks) and **must never allocate**. `Begin()` (a new sound on the
  voice) jumps instead of sliding and clears the delay line and filter memories.
  `SetTarget` is called every frame for every playing voice (`SoundPlayer.TickSpatialVoices()`
  for one-shots, from each one's remembered world position, until the end time worked out
  when it started - clip length over pitch, plus the delay - so no voice is asked whether
  it is playing; `SoundLoopPlayer.TickVoices()` for loops), so the direction follows the head as it turns - most of how
  front and behind are told apart. **`SpatialiserSettings`** (serializable class,
  `SoundPlayer.spatialiser`, read every frame so it tunes live): `volume`, `headRadius`,
  `timeDifference`, `farEarVolume`, `nearEarVolume`, `shadowCutoff`, `shadowAmount`,
  `rearCutoff`, `rearAmount`, `rearVolume`; `Prepare(sampleRate)` once a frame
  works out the two filter coefficients. A script on each voice, not a native Unity
  spatialiser plugin. The ambience is 2D and not spatialised. `Plugin` mode sets
  `AudioSource.spatialize` (`spatialiseAfterMuffle` = `spatializePostEffects`) and logs once
  if no plugin is chosen - for trying Steam Audio (open source, every platform, real
  up/down) if the built-in one isn't enough. A spatialiser plugin's sources may not feed
  Unity reverb zones.
- **No line-of-sight ray muffling inside a room**: with portals handling walls, a guard in
  the same room is heard clearly.
- **Not done:** tuning the loop easing by ear; measuring the
  spatialiser's cost (C# on the audio thread); real loop and ambience recordings.

### Sound debug and test content

- **`NoiseDebug`** (`Core/Debug`, on the Debug object) — draws each noise as a wire sphere of
  its radius fading over `showDuration` (fixed ring of 16, no allocation), logs it, and `N`
  emits a test noise at `emitFrom`, then (`logReport`) writes one "NOISE REPORT" Console
  entry, no stack trace, listing every `NoiseListenerDebug`: room, straight distance, path
  distance, muffle, heard-from point and heard/silent - readable afterwards from
  `%LOCALAPPDATA%/Unity/Editor/Editor.log`.
- **`NoiseListenerDebug`** (`Core/Debug`) — stand-in ears for any object: the object turns
  yellow (`MaterialPropertyBlock` on `_BaseColor`, only on change), a 0.3m wire marker round
  it turns yellow with a line to where it heard the noise from and on to the noise, and it
  logs the loudness.
- **`SoundEmitterDebug`** (`Core/Debug`) — plays a cue from its position every `interval`
  seconds (noise off by default). With `surfaceSounds` assigned it's a walker: each play is
  a `SurfaceSounds.PlayStep()` on the floor beneath it (`SurfaceTag.TryFindBelow()`, 1m
  down), falling back to its own cue.
- **`SoundDebug`** (`Core/Debug`, on the Debug object) — `L` writes one "SOUND REPORT"
  Console entry: the listener's room, voices playing, the spatialiser, the room's reverb,
  every loop (room, path, muffle, loudness, voice) and each emitter's room, range, straight
  and path distance, muffle, heard-from point and played/not.
- **`NoiseTestListeners`** (`Core/Debug/Editor`, menu **TeaLeaf > Dev Room > Add Noise Test
  Listeners**) adds three collider-less listener cubes under one root, 3m, 6m and 12m ahead
  of the main camera at ear height.
- **`SoundTestArea`** (`Core/Debug/Editor`, menu **TeaLeaf > Dev Room > Build Sound Test Area**; not in
  the scene) builds three 4m rooms in a row at (-14, 0, 4), no ceilings: A (doorway to
  outside + doorway to B), B, and C behind a solid wall with a closed portal from B (the
  thin-wall case), with rooms, portals, listener cubes and two emitters. Its helpers
  (`AddRoom`, `AddPortal`, `AddEmitter`, `AddListener`, `EnsureSceneObjects`; internal) are
  reused by the test house. `EnsureSceneObjects()` makes sure the scene has a `Sound Player`
  object (with the wind as its outside ambience if it has none) and the Debug object a
  `SoundDebug`. Uses `Player.TestGeometry` (editor-only).
- **`SoundTestHouse`** (`Core/Debug/Editor`, menu **TeaLeaf > Dev Room > Build Sound Test House**) — a
  two-storey 12m x 8m house with its own ground, at (-36, 0, 12); a rebuild keeps where the
  old one was moved to. Ground floor: Hall (front door, a 35° stair ramp up its left wall),
  Kitchen, Store (behind a closed-door portal and a door slab) and Lounge (closed "window"
  portal in the back wall); upper floor: Landing and Bedroom, no roof; a walled Yard in
  front with a gate. Seven `SoundRoom`s (the Yard is one) and eight `SoundPortal`s,
  including a **stairwell portal lying flat in the stair hole** (rotated 90° about X), the
  only link between the floors. Nine listener cubes, a knock in the Store, and a `Guard Path`
  (points only, for the test guard - see `Scripts/AI/CLAUDE.md`): Bedroom → Landing → stairs →
  Hall → Kitchen. A `SoundLoop` on the Hall and Kitchen torches and a `Flame` on all three
  lights (in a house built since `Flame` was written; add it by hand to older ones); a different reverb per room
  (Hall `StoneHall`, Kitchen `StoneRoom`, Store `Cellar`, Lounge `SmallRoom`, upper floor and
  Yard `Alley`); the indoor tone on the ground floor and the wind upstairs and in the Yard.
  Footstep surfaces: upper floor and stairs tagged Wood, and 2cm floor coverings tagged
  Carpet (Lounge), Tile (Kitchen), Metal (Store) and, across the Yard, Water, Gravel and
  Grass; the Hall and the Yard's edges are untagged stone.
- **`PlaceholderSounds`** (`Core/Debug/Editor`, menu **TeaLeaf > Dev Room > Create Placeholder Sounds**)
  — generated stand-in `.wav` files in `Assets/Audio/Placeholder` and cues in `Assets/Data`.
  Existing files are kept. Cues: `FootstepPlaceholder` (stone; range 25m, noise 6m),
  `ImpactPlaceholder` (35m, 10m), `CreakPlaceholder` (`CreakCuePath`; volume 0.6, range 20m,
  `NoiseType.Mechanism`, noise 5m, given to the test doors by `DoorTestArea`), per-surface
  footsteps from one recipe (`SurfaceStep()`) for wood (noise 7m), carpet (2.5m, range 15m),
  metal (10m, range 30m) and water (8m), and `SurfaceSoundsPlaceholder.asset`
  (`SurfaceSoundsPath`) listing them. `BarkPlaceholder` (`BarkCuePath`; `Bark()`: a
  two-part grunt; `NoiseType.Voice`, noise 0, range 25m, pitch variation 0.12) stands in
  for every guard line. Three loops, each made seamless by `Seamless()` (the
  last half second blended into the first with square-root weights): `TorchLoopPlaceholder`
  (`Fire()`; volume 0.5, range 12m), `AmbienceOutsidePlaceholder` (`Wind()`, 0.3) and
  `AmbienceIndoorPlaceholder` (`RoomTone()`, 0.25), all noise radius 0 and no pitch
  variation (`TorchLoopCuePath`, `OutsideAmbienceCuePath`, `IndoorAmbienceCuePath`).
  **The footstep cues play real recordings** from `Assets/Audio/footsteps/<set>/` (`.ogg`,
  each folder with a `license.txt`) while keeping their "Placeholder" names (the test
  builders load them by path): `boots` (stone), `wood`, `metal`, `water`; carpet plays the
  `grass` clips. Three more cues were made by hand, not by the generator (which would
  recreate `SurfaceSoundsPlaceholder` with only the first five if it were deleted):
  `FootstepTile` (noise 9m, range 28m), `FootstepGrass` (3m, 18m, volume 0.6) and
  `FootstepGravel` (9m, 28m). Unused sets: `bones`, `dog`, `mech`. Most sets are CC-BY 3.0
  (swuing, Eelke, EminYILDIRIM, ceberation, sabotovat, Lee Barkovich - credit needed in a
  release); `bones` and `gravel` are CC0.

## Game state and level manager

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
  restarts like Caught until there is a "Mission complete" panel. **The player's body is
  frozen during the end fade**: `PlayerController` subscribes to `StateChanged` and, once the
  level is over, runs only the hand systems (see the tick order in the root `CLAUDE.md`).
  Right-click "Test Caught"/"Test Take Objective" in Play Mode.
- **`ExitZone`** — a box (serialized `size`, centred on the object, following its
  position/rotation/scale) tested with `InverseTransformPoint`, not a trigger collider: no
  physics layer, nothing for hand rays to hit, and it works while a mantle has the
  CharacterController off. Self-registering static list; `AnyContains(worldPoint)`,
  `Contains(worldPoint)`. The point tested is the player's feet, so sink the box a little into
  the floor. `IDebugDrawable` (green wire box).
- **`GameStateDebug`** (`Core/Debug`, on the Debug object) — keyboard driver (Game view needs
  focus): `C` = caught, `O` = toggle objective carried; logs every state change.

## Screen fade

- **`ScreenFade`** (on a `Screen Fade` child of Main Camera at the local origin, so
  head-locked with no code) — fades the whole view to `fadeColor` (black) and back.
  `FadeOut(duration, onComplete)` covers the view (caught, restart: reload in the callback,
  once nothing can be seen); `FadeIn(duration, onComplete)` clears it. A fade starts from the
  current `Alpha`, so interrupting one is smooth; a replaced fade's callback is dropped.
  `ScreenFade.Instance` (set in `Awake()`, no scene search), `Alpha`, `IsFading`.
  **Level start:** with `fadeInOnStart` (default on) `Awake()` makes the view black before the
  first frame renders, holds for `startHoldDuration` (0.3s), then fades to clear over
  `startFadeDuration` (1.5s). A scene reload therefore fades back in by itself. Drawn as a
  runtime-built 1m cube around the head (from inside, every view direction crosses exactly
  one face) with an `OverlayMaterial` at queue Overlay + 100, so it also covers the UI
  markers. Eased with SmoothStep on unscaled time, each frame's step capped at 0.05s so a
  loading hitch can't jump it. **Costs nothing while clear:** the renderer is off and the
  component disables itself, so `Update()` only runs during a fade. The colour is set on the
  material it created, not via `renderer.material`. Right-click "Test Fade Out"/"Test Fade
  In" in Play Mode. **`Hold()`/`Release()`** (counted) freeze any fade until every hold is
  released - for work that must finish behind the black: `PlayerTracking` holds the
  level-start fade while it waits for tracking and calibrates the view.
- **`OverlayMaterial`** — the static factory for overlay (drawn last, no depth test)
  materials; in `Core` because `ScreenFade` needs it and `Core` shouldn't depend on `Player`.
  Detail in `Scripts/Player/CLAUDE.md` (its users are mostly the player's UI markers).

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
  **Any new component with gizmos does this rather than call `Gizmos` directly.**
  `DebugLines` is the only code that calls `Gizmos`.
- **`DebugDrawRegistry`** — static list of enabled drawables (self-registering, no scene
  search), read with `Count`/`Get(i)`.
- **`InHeadsetGizmos`** — put it on any scene object and enable it (it can be toggled in the
  Inspector while playing). Builds one line mesh from every registered drawable each frame
  (one draw call), on a `MeshRenderer` it creates at the scene root with identity transform and
  fixed huge bounds (never culled). Rebuilt in
  `RenderPipelineManager.beginContextRendering`, guarded to once per frame, not
  `LateUpdate()`: the head's Tracked Pose Driver moves again just before rendering.
  `showDetail` picks each drawable's detailed view; `seeThroughWalls` sets the depth test
  (Always / LessEqual). Disabled, it costs nothing.
- **`DefaultLayerGizmos`** (`IDebugDrawable`, on the Debug object) — outlines, in magenta,
  every enabled collider left on the Default layer (layer 0), in the Scene view and in the
  headset: box/sphere/capsule as their real scaled shape, other colliders as their bounds.
  The one scene search in the debug code, done every `rescanInterval` (1s) rather than per
  frame, and it allocates, so it's debug-only. Disable the component to turn it off.
- **`FogTestDebug`** (`Core/Debug`; **temporary**, for judging the look of Silent Hill 2
  style fog - distance haze is only a reserve) — on the Debug object; while enabled in Play
  Mode it turns on Unity's built-in fog (`RenderSettings`: `fogColor` pale grey, `mode`
  Linear 1-18m, or `density` for the exponential modes), sets the main camera's clear colour
  to the fog colour and, with `hideSky`, deactivates `Sky Stars`/`Sky Moon`. Disabling it
  puts everything back; nothing is saved to the scene. Two untested extras:
  **`skyGradient`** (the sky stays, and a 100m ball of vertices round the head, `Fog Test Sky
  Haze`, is drawn over it in the fog colour - solid from straight down to `hazeFullAngle`
  (5°) above the horizon, thinning to `overheadFog` (0.35) at `hazeClearAngle` (50°); it
  borrows the `TeaLeaf/DebugLines` shader with its queue set to Transparent - 390) and
  **`clearIndoors`** (one ray straight up from the head every `roofCheckInterval` (0.2s) on
  `roofLayers` within `roofCheckHeight` (6m) = indoors; the fog then eases to
  `indoorFogAmount` (0.1) over `indoorBlendTime` (1.5s)). Fog is global, so from indoors the
  street through a window is clear too. Not a full-screen effect, and the project's own
  shaders (overlay, ghost, sky, debug lines) ignore fog. Remove it once the look has been
  judged.
- **Shader `TeaLeaf/DebugLines`** (`Assets/Art/Shaders/Resources/DebugLines.shader`) — like
  `TeaLeaf/Overlay` (unlit, Overlay queue, `ZWrite Off`, `Cull Off`, alpha blend, single-pass
  instanced stereo macros, `SRPDefaultUnlit` pass, in `Resources` for `Shader.Find()`) but
  coloured per vertex, with the depth test as a `_ZTest` material property. Lines are one pixel
  wide in the headset.
