# Frontier Strike: 3D Shooter — Game Info

> Project scan as of 2026-11-13 (commit `b2f4abc`). Describes what is **actually in the repo**, not planned features.

## 1. Overview

| Item | Value |
|---|---|
| Product name | Frontier Strike 3D Shooter |
| Genre | First-person shooter (low-poly, arcade style) |
| Engine | Unity **6000.0.62f1** (Unity 6) |
| Render pipeline | Built-in Render Pipeline + Post Processing Stack v2 (`com.unity.postprocessing` 3.5.0) |
| Input | New Input System (`com.unity.inputsystem` 1.14.2), `activeInputHandler: 2` (Both) |
| Target platforms (per README) | WebGL and Windows |
| Web resolution default | 1920×1080 (standalone default 1024×768) |
| Company name | Still `DefaultCompany` (not set yet) |
| Version | `bundleVersion: 1` |

The game is built on top of two third-party packs:

1. **Infima Games — Low Poly Shooter Pack (Free Sample)**: the whole FPS gameplay layer (character, weapons, HUD, VFX, audio).
2. **Tiny Teacup Studio — Military Base Pack**: the playable environment (terrain, hangars, towers, fences, containers, barrels, lamps).

The FPS mechanics come from the Infima pack. On top of them, the project's own code in `Assets/Scripts/` (namespace `FrontierStrike`) adds the game loop: player health, enemy soldiers, 5 waves, scoring, and menus. See **section 6b**. Other project work: placing the player in the military base, a player prefab variant, the cover image, and README.

## 2. Scenes

Build order (`ProjectSettings/EditorBuildSettings.asset`):

| # | Scene | Purpose |
|---|---|---|
| 0 | `Assets/Tiny Teacup Studio/Military Base Pack/Demo/DemoScene.unity` | **Main game level.** Military base on a terrain, with the player (`P_LPSP_FP_CH Variant`, unpacked), `P_LPSP_UI_Canvas` HUD, baked lightmaps, reflection probe and post-processing profile. |
| 1 | `Assets/Infima Games/Low Poly Shooter Pack - Free Sample/Scenes/S_Content_Overview.unity` | Infima's demo/showcase range: grid rooms, pop-up targets, explosive barrels, gas tanks, material/weapon showcases. Uses the player variant. |
| – | `Assets/Scenes/SampleScene.unity` | Empty default Unity scene (camera + light). Not in build. |
| – | `Assets/TextMesh Pro/Examples & Extras/Scenes/*` | 30 TMP example scenes. Not in build; can be deleted. |

Military Base level contents (prefab instance counts, approximate): Fence ×~6.3k refs, Block2, Lamp1, Barrel1, Tile, Garage1, Tower1, Container1, Hangar1/2/4. The base also includes a second `Camera` with an Animator (`Demo/Animations/Camera.controller`) left over from the pack's fly-through demo — worth checking that it is disabled so it does not fight with the player camera.

## 3. Player Character

Prefab: `Assets/Prefabs/P_LPSP_FP_CH Variant.prefab` (variant of Infima's `P_LPSP_FP_CH`). The variant rewires the `PlayerInput` action events (16 actions) and offsets the arms (`x 0.5, y -0.75`).

Components on the player:

| Script | Role |
|---|---|
| `Character` | Central controller. Receives input events, manages states (aiming, running, reloading, inspecting, holstering), drives the arms Animator, fires the equipped weapon, toggles cursor lock. |
| `Movement` | Rigidbody + CapsuleCollider movement. Sets velocity directly from input; ground check via SphereCast; looping walk/run footstep audio. |
| `CameraLook` | Mouse/stick look. Yaw rotates the Rigidbody, pitch rotates the camera, clamped −60°…60°. Optional smoothing. |
| `Inventory` | Collects child `WeaponBehaviour`s, equips by index, cycles next/previous. |
| `CharacterKinematics` | IK for the hands on the weapon. |
| `CanvasSpawner` | Instantiates the HUD canvas prefab on Awake. |
| `TimeHandler` | Debug slow-motion/pause (keys 5/8/9). |

Tuning (base prefab): walk **4 m/s**, run **6.8 m/s**, look sensitivity **1×1**.

Rules in `Character`:
- Can't run while aiming, reloading, inspecting, firing with ammo, moving backward, or strafing purely sideways.
- Can't fire while holstered/holstering, reloading, or inspecting.
- Reload plays `Reload` or `Reload Empty` depending on remaining ammo; ammo is refilled through an animation event (`FillAmmunition`).
- All input is ignored while the cursor is unlocked (Esc).

**Not implemented:** jump (bound to Space, but no handler exists) and crouch. Player health, damage and death come from `PlayerHealth` (section 6b).

`Character.SetCursorLocked(bool)` (added for the game loop, also declared on `CharacterBehaviour`) locks or unlocks the cursor from code and clears held input when it unlocks.

## 4. Weapons

| Weapon | Prefab | Fire mode | RPM | Magazine | Damage | Projectile speed | Trace range |
|---|---|---|---|---|---|---|---|
| Assault Rifle (AR_01) | `P_LPSP_WEP_AR_01` | Automatic | 610 | 30 | 20 (40 headshot) | 400 | 500 m |
| Handgun (Handgun_03) | `P_LPSP_WEP_Handgun_03` | Semi-auto | 500 | 10 | 34 (68 headshot) | 400 | 500 m |

Damage is set by `Weapon.damage`. A negative value (the default) means 20 for automatic weapons and 34 for semi-auto. You can override it per weapon in the Inspector. The weapon passes it to each projectile it fires.

How firing works (`Weapon.Fire`):
1. Raycast from the camera centre (layer mask) to find the aim point.
2. Spawn `P_LPSP_PROJ_Bullet_01` at the muzzle socket, rotated toward that point, with velocity = forward × `projectileImpulse`.
3. Muzzle flash particles + flash light (`Muzzle`), fire sound, casing ejected via animation event (`P_LPSP_Casing_Big/Small`).
4. Ammo is infinite in reserve — only the magazine count exists (no total ammo pool).

Attachment system: `WeaponAttachmentManager` holds scope / muzzle / magazine behaviours (free sample has one muzzle and default scope/magazine).

Each weapon carries its own `AnimatorOverrideController` (`OC_LPSP_PCH_*` for arms, `OC_LPSP_WEP_*` for the gun) over the base controllers `AC_LPSP_PCH` / `AC_LPSP_WEP`.

## 5. Projectiles, Targets & Destructibles (`Code/Legacy`)

`Projectile` ignores the player's collider. On impact it first checks for an `IDamageable` (enemies, player) and applies its `damage`. Otherwise it reacts to the **tag** of whatever it hits:

| Tag | Result |
|---|---|
| `Blood`, `Metal`, `Dirt` | Spawns matching impact VFX |
| `Concrete` **or untagged** | Concrete impact VFX (so the military base now shows bullet hits) |
| `Target` | Knocks down a `TargetScript` pop-up target (pops back up after random delay) |
| `ExplosiveBarrel` | Triggers `ExplosiveBarrelScript` → explosion, physics force (radius 12.5), chain reactions, destroyed-barrel prefab |
| `GasTank` | Triggers `GasTankScript` |

Other legacy scripts: `ExplosionScript` (light flash + random sound, despawn), `CasingScript`, `DebrisScript`, `ImpactScript`, `PlayParticles`.

Targets, barrels and gas tanks only exist in the Infima overview scene, not in the main level.

## 6. HUD / UI

Prefab `P_LPSP_UI_Canvas` (spawned by `CanvasSpawner`). All elements derive from `Element` and read state from the player via the service locator:

- `Crosshair` — scales out while aiming/holstered.
- `TextAmmunitionCurrent` / `TextAmmunitionTotal` — magazine counter.
- `ImageWeapon` — weapon (+attachment) sprites.
- `TextMouseLock`, `TextTimescale`, `TextTutorial` (hold Tab for controls).

Extra: `Assets/UI/Cover Image.png` (game cover art). Fonts include TMP SDF assets (e.g. Anton SDF).

## 6b. Frontier Strike Game Loop (`Assets/Scripts/`)

All of this is created **at runtime**. No scene or prefab edits are needed. `GameBootstrap` adds a `GameManager` to every loaded scene that contains the player, except `S_Content_Overview`, which stays a sandbox.

| File | Role |
|---|---|
| `Core/GameBootstrap.cs` | Hooks `SceneManager.sceneLoaded`; spawns the Game Manager (also after restarts). |
| `Core/GameManager.cs` | State machine: **Briefing → Combat ⇄ Intermission → Victory / GameOver**, plus **Paused**. Handles spawning, scoring, best score (PlayerPrefs `FrontierStrike.BestScore`), restart and quit. Also turns off the military pack's fly-through camera. |
| `Core/RuntimeVisuals.cs` | Builds materials and primitives at runtime (enemy models, medkits). |
| `Combat/IDamageable.cs` | `IDamageable` + `DamageInfo` (amount, point, direction, hit collider, source). |
| `Combat/PlayerHealth.cs` | 100 HP. Regenerates 8 HP/s after 5 s without damage, up to 60% max. Raises `Damaged`/`Died` events. |
| `Combat/HealthPickup.cs` | Floating medkit, +30 HP. Dropped by enemies (18%, or 35% if you are under 50% HP). Lasts 25 s. |
| `AI/EnemySoldier.cs` | Enemy built from primitives (olive uniform, red visor). AI: **Advance → Attack** (strafe, fire bursts) **→ Dead** (falls over, sinks, removed). Hitscan shots with tracer, muzzle light and 3D gunshot (it borrows the player's weapon sound). Head collider takes ×2 damage. |
| `AI/RuntimeNavMesh.cs` | Bakes a NavMesh from the level's colliders at the briefing screen. Covers the terrain area, capped at 320 m. Non-readable meshes (e.g. fences) are replaced with oriented boxes so the bake also works in player builds. Without a NavMesh, enemies steer directly. |
| `UI/GameHUD.cs` | Code-built uGUI + TMP overlay: health bar, wave/hostiles counter, score, wave banners, hit marker (white / gold headshot / red kill), kill popups, directional damage indicators, damage flash, low-health pulse, and the Start / Pause / End screens. |

**Waves** (5 total): wave *n* has `4 + 3(n−1)` enemies (4, 7, 10, 13, 16). At most `min(3+n, 8)` are alive at once, one spawning every 1.2 s. They appear 22–48 m away, preferring spots you can't see that have a walkable path to you. Between waves there is an 8 s intermission.

**Enemy scaling per wave** (`EnemyStats.ForWave`): HP 100 (+15/wave), speed 3.2 (+0.2), damage 6 (+1) per hit, accuracy 40% (+5%, max 75%), which drops with distance and is ×0.6 while you sprint. Attack range 28 m, burst of 3+ shots.

**Scoring:** kill 100, headshot +50, wave clear 250 × wave number.

**Flow:** Start screen (DEPLOY button, enabled once the NavMesh is ready) → waves → "MISSION COMPLETE" after wave 5, or "MISSION FAILED" on death (with brief slow motion). Esc pauses, and Esc again or RESUME continues. The end screen shows score, waves, kills, headshots and time. QUIT is hidden in WebGL builds.

Tested with an automated PlayMode smoke test in Unity 6000.0.62f1 batchmode (not kept in the repo). It confirmed the NavMesh bake, enemy spawning, movement and shooting, player damage, kill scoring and the switch to the intermission. A hands-on playtest is still needed for feel and balance.

## 7. Controls (`IA_Player.inputactions`)

| Action | Keyboard / Mouse | Gamepad |
|---|---|---|
| Move | WASD | Left stick / D-pad |
| Look | Mouse | Right stick |
| Fire | LMB | RT |
| Aim (hold) | RMB | LT |
| Reload | R | X / West |
| Run | Left Shift | L3 |
| Inspect weapon | T | D-pad left |
| Holster | H | RB |
| Next weapon | Q / mouse wheel | Y / North |
| Jump | Space | A / South — *bound but not implemented* |
| Lock/unlock cursor | Esc | – |
| Show tutorial (hold) | Tab | Select |
| Time scale up / down / pause | 9 / 8 / 5 | D-pad up / down |

## 8. Architecture

- **Namespace:** `InfimaGames.LowPolyShooterPack` (legacy scripts are global namespace).
- **Abstract "Behaviour" pattern:** every system has an abstract base (`CharacterBehaviour`, `WeaponBehaviour`, `InventoryBehaviour`, `MovementBehaviour`, `MagazineBehaviour`, `MuzzleBehaviour`, `ScopeBehaviour`, `WeaponAttachmentManagerBehaviour`) and a concrete implementation, so systems can be swapped.
- **Service Locator:** `Bootstraper` runs `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` and registers:
  - `IGameModeService` → `GameModeService` (finds the player with `FindObjectOfType<CharacterBehaviour>()`, cached).
  - `IAudioManagerService` → `AudioManagerService` (DontDestroyOnLoad "Sound Manager"; spawns temporary AudioSources for one-shots, optional delay).
- **Animation events** drive gameplay timing: `CharacterAnimationEventHandler` / `WeaponAnimationEventHandler` call `EjectCasing`, `FillAmmunition`, `SetActiveMagazine`, `AnimationEndedReload/Inspect/Holster`. `PlaySoundBehaviour` StateMachineBehaviours play reload/holster sounds.
- **Input wiring:** `PlayerInput` component → Unity Events → `Character.On*` / `TimeHandler.On*` methods.

## 9. Folder Map

```
Assets/
├── Infima Games/
│   ├── Low Poly Shooter Pack - Free Sample/
│   │   ├── Animators/      Character + weapon controllers and override controllers
│   │   ├── Art/            Animations, Fonts, Materials, Meshes, Post Processing, Sprites
│   │   ├── Audio/          Weapon, footstep, explosion, target sounds (.wav)
│   │   ├── Code/           Animation, Camera, Character, Demo, Interface, Legacy, Services, Utilities, Weapons
│   │   ├── Input/          IA_Player.inputactions, IS_Default
│   │   ├── Lighting Settings/
│   │   ├── Prefabs/        Player (P_LPSP_FP_CH), weapons, casings, damageables, effects, UI canvas, demo grid
│   │   └── Scenes/         S_Content_Overview.unity
│   └── Tools/              Log, Looping utilities
├── Tiny Teacup Studio/Military Base Pack/
│   ├── Demo/               DemoScene.unity (MAIN LEVEL), terrain data, lightmaps, PP profile, camera fly-through anim
│   ├── Ground/ Materials/ Models/
│   └── Prefabs/            Buildings (Garage, Hangars, Tower), Ground (Blocks, Fence, Tile), Props (Barrels, Containers, Lamp, Toilet)
├── Prefabs/                P_LPSP_FP_CH Variant.prefab (game's player)
├── Scripts/                Frontier Strike game loop: AI/, Combat/, Core/, UI/ (see 6b)
├── Scenes/                 SampleScene.unity (unused)
├── TextMesh Pro/           TMP essentials + examples
├── UI/                     Cover Image.png
└── InputSystem_Actions.inputactions   Unity default actions (unused by the player)
```

Repository stats: ~1,566 tracked files; 93 C# scripts (55 gameplay, 38 TMP examples), 91 FBX, 60 prefabs, 60 materials, 37 WAV, 34 scenes.

## 10. Packages of Note

`inputsystem 1.14.2`, `postprocessing 3.5.0`, `ugui 2.0.0` (+TMP), `timeline 1.8.9`, `burst 1.8.25`, `ai` module (NavMesh available, unused), `visualscripting 1.9.7`, `test-framework 1.6.0`, `ide.rider`, `analytics 3.8.1`, `multiplayer.center`, `collab-proxy`.

## 11. Known Issues / Tech Debt

1. **Jump** is bound, but `Character` has no `OnTryJump` and `Movement` has no jump logic. `Movement` also sets the Y velocity to 0 every FixedUpdate, so gravity and jumping won't work until that changes.
2. Projectiles are created and destroyed on every shot (no pooling), which is costly for WebGL at 610 RPM.
3. `companyName` is still `DefaultCompany`. `SampleScene`, the TMP examples and the root `InputSystem_Actions.inputactions` are unused.
4. Action names don't match: the variant uses "Time Speed Stop", the asset uses "Time Speed Toggle". The debug time keys (5/8/9) still work during combat.
5. Enemy models are placeholder primitives with procedural animation, and they have no hit or death sounds of their own.

Fixed so far: the projectile impact-array bug, the deprecated `FindObjectOfType` in `GameModeService`, the fly-through camera (now turned off at runtime), and the missing bullet impacts on the untagged base.

## 12. What's Missing for a Full Game

Done: player health and death, enemy AI, the `IDamageable` damage system, the wave loop, start/pause/end menus, scoring and health pickups (section 6b).

Still open:

- Real enemy character models and animations (replacing the primitives), with enemy hit and death sounds.
- A reserve ammo pool and ammo pickups (magazines are still infinite).
- Difficulty tuning after hands-on playtests.
- Jump and crouch, plus more weapons (the attachment system is ready for them).
- A WebGL build profile, compression and a performance pass (projectile pooling).
