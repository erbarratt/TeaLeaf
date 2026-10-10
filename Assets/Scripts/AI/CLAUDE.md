# AI systems (`Assets/Scripts/AI/`, namespace `AI`)

Detail for the guards. The root `CLAUDE.md` holds the project rules, the physics layers and
the slice design; this file is loaded when working in this folder. Keep it up to date with
every change to these systems, like the root file.

Phase 6 of `Assets/DEVROADMAP.txt`. **Built and set up in the scene; patrolling and catching seen in the headset, the rest not yet
tried**: a guard that patrols, sees, hears, becomes suspicious, searches, chases, catches,
notices bodies and can be knocked out. Every number is a first guess.

## The guard

- **`GuardState`** (enum) — `Patrol`, `Suspicious`, `Search`, `Chase`, `KnockedOut`.
- **`Guard`** (on the guard's root, `[RequireComponent(NavMeshAgent)]`; `INoiseListener`,
  `IDebugDrawable`) — the whole guard: an explicit state machine (a switch on `State`),
  its senses and its sounds. **It has its own `Update()`**: guards aren't part of the
  player's tick order. Walking is Unity's `NavMeshAgent` (`GoTo()`, `Stop()`,
  `HasArrived()`); the guard only turns itself when standing (`FaceTowards()`).
  - **The detection meter** (`Detection`, 0-1) is what moves it between states. Seeing the
    player fills it, a noise adds to it at once, and it drains over `decayTime` (6s) with
    nothing to notice (not while chasing). At `suspiciousThreshold` (0.3) a patrolling
    guard becomes Suspicious; at 1 it chases, from any state.
  - **Sight** (`Look()`): **not per frame** - every `lookInterval` (0.15s), each guard
    starting at a random moment so they never all look in one frame, and every
    `farLookInterval` (0.5s) with no rays at all while the player is more than `viewRange`
    + 2m away. Two points on the player (`Player.PlayerVisibility.Instance`:
    `HeadPosition`, `BodyPosition`), each seen if within `viewRange` (14m), inside the
    cone (`viewAngle` 110° in all, by dot product against a cosine worked out once - or
    within `senseRange` 0.9m whichever way the guard faces) and one `Physics.Linecast` on
    `sightLayers` (Environment + Interactable, triggers ignored) is clear
    (`CanSeePoint()`). The fill rate = share of points seen x `PlayerVisibility.Visibility`
    (light and stance; raised to `closeVisibility` 0.5 within `closeRange` 2.5m, so nobody
    is invisible at arm's length; under `minVisibility` 0.05 = unseen) x a distance scale
    (1 close up to `farDetectionScale` 0.25 at the view range) / `detectionTime` (1s).
  - **Hearing** (`OnNoiseHeard()`, from `Core.NoiseSystem`, which has already routed the
    noise through rooms and portals): adds loudness x `hearingGain` (0.7; `distractionGain`
    1.5 for a noisemaker) to the meter, and the heard-from point becomes where to look.
    **Ignored: another guard's noises** (the noise's source is a guard's root - a static
    dictionary) and everything during a chase. In a search, a noise that takes the meter
    to `investigateThreshold` moves the search there - **only if it is more than 1.5m
    from the place being searched**: restarting the search for every noise from the same
    place would root the guard there for as long as the noise went on.
  - **Patrol:** walks its `PatrolRoute` at `patrolSpeed` (1.2 m/s), waiting the route's
    wait time at each point; with no route it stands at the place and facing it was put.
    Entering Patrol takes the route up at its nearest point.
  - **Suspicious:** stops, turns towards the stimulus, barks, and after `suspiciousTime`
    (3s) goes to Search if the meter's peak reached `investigateThreshold` (0.6), back to
    Patrol if not - **with the meter emptied**, or the next look would make it suspicious
    again at once.
  - **Search:** walks (`searchSpeed` 1.8) to the place, looks about for `searchLookTime`
    (2.5s, turning slowly), then tries `searchPointCount` (3) random places within
    `searchRadius` (4m) put onto the NavMesh (`NavMesh.SamplePosition`), then gives up
    (bark) and patrols - or after `searchTimeout` (30s), doubled (`alertedSearchScale`)
    once any guard has found a body.
  - **Chase:** runs (`chaseSpeed` 3.6) at the player; the destination is renewed at each
    look while they are seen, not per frame. **Caught** = seen, within `catchRange` (1.1m,
    level) and on the same floor: `LevelManager.Caught()`. Unseen for `chaseLostTime` (4s)
    it searches from where it last saw them.
  - **Knocking out:** `TryKnockOut(attackerPosition)` (called by `Player.PlayerBlackjack`)
    - the guard goes down only if it isn't chasing and the attacker is behind it (the dot
    product of its facing and the level direction from attacker to guard is at least
    `behindDot` 0.2); otherwise it turns on them (Chase). `KnockOut()`: agent off, both
    colliders off, the `visual` laid flat, the component disabled (so no more looking,
    hearing or `Update()`). It stays down. `Guard.FindByHead(collider)` (a static
    dictionary) is how the blackjack finds the guard a head collider belongs to.
  - **Bodies:** in each look, a knocked-out guard that is in view and lying in at least
    `bodyLightThreshold` (0.15) of light (`Core.SceneLight.LevelAt()`) is found once: the
    finder barks and searches there, and every guard's searches are longer from then on
    (a static flag, cleared when the scene's last guard is destroyed).
  - **Sounds:** footsteps through `Core.SurfaceSounds.PlayStep()` on the floor under it,
    one per `walkStride` (1.4m) or `runStride` (1.8m), **sound only** (noise scale 0).
    Barks (`suspiciousBark`, `searchBark`, `gaveUpBark`, `spottedBark`, `foundBodyBark`)
    played from the head, sound only. Both show on the player's compass.
  - **State tint:** `stateRenderer` (the head, on the test guard) is tinted by state
    through a property block, on a change only - grey patrol, yellow suspicious, orange
    search, red chase, dark knocked out - until there are animations.
  - **The level ending** (`LevelManager.State` not `Playing`) stops it where it is.
  - Gizmo: the view cone's edges in the state's colour and a line above the head as long
    as the meter is full; selected, the close range and a line to the stimulus.
- **`PatrolRoute`** — a route's points are **its child objects, in Hierarchy order**
  (positions read once in `Awake()`). `waitTime` (2s at each point), `loops` (off = there
  and back). `Count`, `GetPoint(i)`, `NearestIndex(position)`. `IDebugDrawable`: the route,
  drawn from the children so it shows while editing.
- **How a guard object is put together:** root on the `Guard` layer with `NavMeshAgent`,
  a kinematic `Rigidbody` and a `CapsuleCollider` (radius 0.3, height 1.8); a `Head` child
  on `GuardHead` with a `SphereCollider` - the blackjack's target, and the guard's `eye`
  (its forward is the way the guard looks); a `Visual` child holding what is drawn.
  Greybox: a capsule, a ball and a nose.

## Navigation

- **The NavMesh agent is guard-sized**: radius 0.3, height 1.8, step 0.4, slope 45°
  (`ProjectSettings/NavMeshAreas.asset`), so 0.9m doorways are walkable.
- **The NavMesh is baked in the editor** by a `NavMeshSurface` (the AI Navigation package)
  on a `NavMesh` object at the scene root: every physics collider on the **Environment**
  layer. The data is `Assets/Scenes/Main-NavMesh.asset`. **Bake again after changing
  level geometry** (TeaLeaf > Set Up Test Guard does it).
- **Doors aren't in the NavMesh**: they are on Interactable, so a guard walks through a
  shut door.

## Debug and set-up

- **`GuardDebug`** (`AI/Debug`, on a guard's root) — logs to the Console, prefixed
  `[Guard]`: where the guard starts and whether it is on the NavMesh, every change of
  state (with the meter and position) and, with `logMovement`, each time it sets off or
  stops. **Read these in the editor log before guessing why a guard did something.**
- **A guard that hears the player stops**: one footstep of about 0.45 loudness at its ears
  makes it Suspicious, so a guard standing still near a walking player is usually
  listening, not broken. Left alone it walks the whole test route (checked by playing the
  scene headless: about 40s from the Bedroom to the Hall).
- **`GuardTestSetup`** (`AI/Debug/Editor`, menu **TeaLeaf > Set Up Test Guard**) — makes
  the `Test Guard` and bakes the NavMesh. With a Sound Test House in the scene the guard
  patrols a `Test Guard Route` copied from the house's `Guard Path` (Bedroom, Landing,
  stairs, Hall, Kitchen, there and back); without one it stands 4m ahead of the camera.
  The guard gets a `GuardDebug`. Also runs **Add Hand Tools To
  Player** and gives the blackjack the placeholder impact cue. **TeaLeaf > Check Test
  Guard Route** asks the NavMesh for a path between each pair of route points and logs any
  that fail. Both have command-line entry points (`BuildInBatch()`, `CheckRouteInBatch()`)
  for `Unity.exe -batchmode -quit -projectPath ... -executeMethod AI.GuardTestSetup.…`,
  which open and save `Main.unity` with the editor closed.

## Not built

Dragging and hiding a knocked-out guard; voice lines that don't repeat (the barks are one
placeholder grunt cue), idle chatter and several voices; a humanoid model and animations;
doors on the NavMesh and guards opening them; guards thinking less often when far from the
player beyond the sight cut-off; any reaction to a fire going out or to a bolt hitting
them; guards telling each other anything.
