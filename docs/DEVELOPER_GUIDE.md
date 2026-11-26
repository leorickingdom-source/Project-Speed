# Developer Guide

A practical map of this project so you can edit any part of it on your own. Written to be read top-to-bottom once, then used as a reference ("where do I change X?").

---

## 1. What this project is

A fast first-person arena shooter built around **movement tech** (bunny-hopping, slides, wall jumps, double jumps, a grapple hook, and rocket jumps). Single-player-first: you can play solo against bots, and a friend can join over the network.

- **Engine:** Unity **6.5** (`6000.5.0f1`), **URP** (Universal Render Pipeline).
- **Networking:** **FishNet** (in `Assets/FishNet`). Host runs as server **and** client at the same time.
- **Core identity:** "Speed IS damage" — moving fast increases your damage. Weapons and passives are balanced around mobility, not raw DPS.
- All gameplay scripts live in `Assets/Scripts`, compiled into one assembly (`Game.Runtime`, defined by `Assets/Scripts/Game.Runtime.asmdef`).

Third-party packages you normally won't touch: `Assets/FishNet` (netcode), `Assets/Easy FPS` (the animated gun viewmodel + hands), `Assets/Low Poly Guns` (optional static gun meshes, see §7), `Assets/Mixamo` and `Assets/Kevin Iglesias` (character animations), `Assets/HFDownloader`, `Assets/ParrelSync` (test multiple clients locally).

---

## 2. Open, run, and edit

### Open the project
1. Open **Unity Hub** → the project is `C:\Users\User\My project (2)`.
2. Open the scene **`Assets/Scenes/Arena.unity`** (the main map). Other maps: `Stacks`, `Expanse`, `Vault`.

### Play it
- Press **Play** in the Editor.
- On the connect screen (drawn by `Net/ConnectUI.cs`): pick a **Map**, a **Weapon**, and a **Passive**, then click **Host** (play + let others join), **Client** (join a host), or **Server** (headless). Default port is **7770**.
- Controls (also shown in-game on **F1**): WASD move, Space jump / hold to bunny-hop, Ctrl or C crouch/slide, Mouse look, **LMB** fire, **R** reload, **RMB** scope (sniper), **V / Mouse5** melee, **Q** grapple hook.

### The edit → test loop (no Claude needed)
1. Edit a `.cs` file in your editor (VS Code, Rider, Visual Studio).
2. **Alt-Tab back to the Unity Editor.** Unity auto-recompiles when it regains focus.
3. Watch the **Console** window for red errors. Unity 6.5 also writes a full log to **`Logs/Editor.log`** inside the project (not the old `%LOCALAPPDATA%\Unity` one).
4. Press **Play** to test.

> **Tip:** Most tunable numbers have `[Tooltip("…")]` and `[Header("…")]` attributes. The Inspector shows those tooltips on hover — so the Inspector and the field comments *are* the documentation for anything placed in a scene. **Weapon stats are the exception:** they're defined in code (see §5), not the Inspector, because the weapon list is built at runtime.

---

## 3. Project map

| Folder (`Assets/Scripts/…`) | What's in it | Start here |
|---|---|---|
| **Combat/** (21 files) | Weapons, damage, explosions, hitboxes, ragdolls, tracers | `WeaponController.cs` |
| **Player/** (14 files) | Movement, camera, grapple, body model, networking glue | `PlayerMotor.cs` |
| **Net/** (8 files) | Connect screen, match rules/modes, loadout & map pickers | `MatchManager.cs`, `ConnectUI.cs` |
| **Passives/** (6 files) | The passive perks and the scripts that apply them | `PassiveType.cs` |
| **UI/** (6 files) | HUD, kill feed, menus, settings | `SpeedHud.cs` |
| **Core/** (4 files) | Settings, keybinds, tick, network-presence helpers | `GameSettings.cs`, `Keybinds.cs` |
| **Replay/** (3 files) | "Play of the Game" highlight recorder + playback | `MatchRecorder.cs` |
| **World/** (4 files) | Jump pads, pickups, map bounds, bots | `JumpPad.cs`, `SimpleBot.cs` |
| **Audio/** (2 files) | Procedural footsteps/gunfire/etc. | `PlayerAudio.cs` |

Files are deliberately small and single-purpose. If you're hunting for something, the filename almost always says what it does.

---

## 4. The most common edits

### Tune how a weapon feels or hits
All weapons are defined in one method: **`WeaponController.DefaultLoadout()`** in `Assets/Scripts/Combat/WeaponController.cs`. Each weapon is a `new Weapon { … }` block. Change the numbers, Alt-Tab, Play.

Fields on a `Weapon` (all documented in the `Weapon` class at the top of the same file):

| Field | Meaning |
|---|---|
| `damage` | Per-hit (per-pellet for the shotgun). |
| `cycle` | Seconds between shots (lower = faster). |
| `automatic` | `true` = hold to fire; `false` = click per shot. |
| `pellets` | >1 = a shotgun-style spread shot. |
| `spreadDegrees` | Base cone. A tapped/first shot uses this. |
| `spreadMaxDegrees` / `spreadBloomPerShot` / `spreadRecoverPerSec` | **Spread bloom** (CS/Valorant-style). Hold fire and the cone grows per shot up to the max, then recovers when you stop. Leave `spreadMaxDegrees = 0` for a fixed cone. **Gotcha:** `spreadBloomPerShot` must be bigger than `spreadRecoverPerSec × cycle` or the recovery outpaces the bloom and the cone never opens. |
| `nearDistance`/`nearMultiplier`/`farDistance`/`farMultiplier` | **Damage falloff.** Two points on a distance→damage line. Set `farDistance ≤ nearDistance` to disable. Works both ways (e.g. the sniper is *weaker* up close). |
| `scopeFov` | Zoom FOV while aiming (RMB). `0` = no scope. `≤ 45` counts as a "true scope" → gets the banded overlay and hides the gun while aimed. |
| `magSize` / `reloadTime` | Ammo and reload length. |
| `headMultiplierOverride` | Per-weapon headshot multiplier (`0` = use the shared default). |
| `lowPolyModel` + `lowPoly*` | Optional static viewmodel. See §7. Normally `null`. |

### Add/remove a weapon from the connect-screen menu
Two files:
1. **`WeaponController.DefaultLoadout()`** — the weapon must exist in the `weapons[]` array (it has a fixed slot index).
2. **`Net/LoadoutChoice.cs`** — controls which weapons are *selectable*. Add the display name to `Names`, the matching `weapons[]` slot to `SlotIndices` (keep the two arrays parallel), and a line in `Describe()`. Weapons left out of these arrays are "shelved" — still in the game, just off the menu. (This is exactly how the Rifle, SMG, and Knife were shelved/restored.)

### Change a passive perk
- **`Passives/PassiveType.cs`** — the enum of all perks, each with a one-line comment saying who owns it. **⚠ Never delete or reorder entries.** They're written as integers into scenes, prefabs, and the loadout SyncVar, so renumbering silently turns a saved pick into a different passive. Add new perks at the **end** only. (`Momentum`, `Dash`, `WallJump` are baseline mechanics, not menu picks — they stay in the enum purely to keep those integers stable.)
- **`Net/PassiveChoice.cs`** — which passives appear on the menu + their descriptions.
- **`Passives/PassiveLoadout.cs`** — what a player currently owns (`Has(PassiveType.X)`).
- Each perk's *effect* lives in whichever script "owns" it: `MomentumDamage`, `CamperDamage`, `HighgroundDamage` (Passives/), or inside `PlayerMotor` (Slipstream air ceiling), `WeaponController` (Executioner melee), `Explosion` (KineticPlating self-damage), `GrappleHook` (Hookweaver), `PlayerHealth` (Vitality/Bloodrush). Search the enum name to find the code.

### Change movement feel
Everything is in **`Player/PlayerMotor.cs`** — the biggest and most important script. Jump height, air control, slide, bunny-hop, speed caps, jump pads, and knockback all live here, each with a tooltip. `AddImpulse()` is the single entry point for external knockback (explosions, rocket jumps).

### Change a map
- Maps are scenes: `Arena`, `Stacks`, `Expanse`, `Vault`. Edit geometry visually in the Scene view.
- `Net/MapChoice.cs` + `Net/SpawnPointBinder.cs` wire the connect-screen map pick to the scene and spawn points.
- `World/JumpPad.cs`, `World/Pickup.cs`, `World/MapBounds.cs` are the interactive map pieces. Drop the prefabs into a scene and tune via the Inspector.

### Change match rules / game modes
**`Net/MatchManager.cs`** owns scoring and win conditions for every mode (deathmatch, oddball, flashpoint, CTF, flag capture). `Net/GameModeChoice.cs` picks the mode. The match-over signal is the `winnerId` SyncVar; `MatchManager.MatchOver` is the boolean everything else reads.

### Change the HUD / kill feed / menus
`UI/SpeedHud.cs` (speed, health, crosshair, scope overlay), `UI/KillFeed.cs` (the "X killed Y" corner), `UI/HitFeedback.cs`, `UI/GameMenu.cs`, `UI/SettingsUI.cs`, `UI/KeybindsUI.cs`. These draw with Unity's immediate-mode `OnGUI`.

---

## 5. How the gun viewmodel works (`Combat/WeaponView.cs`)

The gun you see in first person is built **at runtime**, not placed in a scene — so you can't configure it in the Inspector, only in code.

- The default model is the **Easy FPS** animated viewmodel (`NewGun_auto` / `NewGun_semi` prefabs in `Assets/Easy FPS/Resources`). It has hands, an Animator (reload/walk/aim/melee/takeout clips via the `GunAnimator` controller), and a `muzzelSpawn` bone.
- `WeaponView` drives it: recoil kick, aim pose, reload speed, the muzzle flash, and hiding the gun in third person or under a true scope.
- The **tracer** (the visible shot line) starts at the muzzle bone and is drawn by `Combat/TracerRenderer.cs`.
- Recoil is purely cosmetic (`recoilKick`, `recoilRecover`, and `adsRecoilScale` to damp it while aiming). It never moves your actual aim — by design, this game has no aim-moving recoil.

---

## 6. Networking model (important before you touch Combat/Net)

- **Host = server + client.** When you click Host you are authoritative *and* a player. A friend who clicks Client is a pure client.
- **Hits are client-authoritative.** The shooter's machine decides what it hit and reports the damage (see the comment at the top of `Net/LoadoutChoice.cs`). This is fine for a friendly/co-op game but means a cheating client could claim any hit — if you ever make this competitive, hit validation must move to the server.
- **Synced state** uses FishNet `SyncVar`s (e.g. `MatchManager.winnerId`) and `[ObserversRpc]` calls (e.g. `KillFeed.BroadcastKill`). The clientHost receives its own ObserversRpcs.
- **The replay/kill-feed system records locally on every machine** from data each machine already has (motor state, kills, tracers). It costs no bandwidth but means two players can see slightly different highlights. See `Replay/MatchRecorder.cs`.
- **Explosions apply once per entity.** `Combat/Explosion.cs` groups a player's many hitbox colliders so a blast shoves and damages each player exactly once. If you add any radial/area effect, copy that dedup or it will stack per-collider.

### Hosting a friend
From project notes: friends join via **playit.gg** or **Tailscale** (both forward to port **7770**). If a friend can't connect, have them **disconnect Surfshark/any VPN** first, and make sure host and friend are running the **same build**.

---

## 7. The optional low-poly viewmodel system

`Assets/Low Poly Guns` is a pack of simpler, textured gun meshes (pistol/assault/sniper/shotgun). There's a built-in way to use them **instead of** the animated Easy FPS gun, but it's **off by default** and currently unused.

- To turn it on for a weapon: give that weapon a `lowPolyModel = "Viewmodels/<name>/<name>"` path in `DefaultLoadout()`, where the mesh lives in a folder named **`Resources`** (runtime loading needs that). Four meshes are already staged in `Assets/Low Poly Guns/Resources/Viewmodels/` (assault1, pistol1, sniper1, shotgun1).
- The `lowPolyScale / lowPolyEuler / lowPolyPos / lowPolyMuzzle` fields position and size it (tune by eye in Play mode).
- **Trade-off (why it's off):** these meshes are static — you get the gun shape but **no reload/walk/aim animations and no hands**. That's why the guns currently use the animated Easy FPS model. The code path is kept (`WeaponView.EquipLowPoly` / `LoadLowPoly`) in case you revisit it after sorting out animations.

If you decide you'll never use it, you can delete `WeaponView.EquipLowPoly`/`LoadLowPoly`, the `lowPoly*` fields on `Weapon`, and the low-poly branch in `WeaponController.SyncGunView`. Nothing else depends on it.

---

## 8. Known issues & gotchas

- **Unity's log is at `Logs/Editor.log`** inside the project (Unity 6.5 moved it). Check it for compile errors and runtime exceptions.
- **Wait for the recompile.** After editing a script, give Unity a second to recompile (watch the spinner / Console) before pressing Play, or you'll run a stale build.
- **Play of the Game** used to show nothing / spam `NullReferenceException` every frame. Cause: a duplicate `MatchRecorder` whose ring buffer was never allocated. Fixed in `Replay/MatchRecorder.cs` (a null-ring guard + disabling the duplicate). If POTG still misbehaves, remember it records *locally*, so a machine that scored no kills has no highlight to show.
- **Scope FOV vs the user FOV slider:** the scope zoom (`SpeedFeel.cs`) and the Field-of-View setting share the camera. Scope zoom targets the weapon's `scopeFov`; the slider sets the hip-fire FOV.
- **The sniper hides its gun when scoped** (`WeaponView.SetScopeHidden`), triggered for any weapon with `scopeFov ≤ 45`.

---

## 9. Conventions used throughout

- **Runtime-built, `Ensure()`-created singletons:** several systems (`MatchRecorder`, `ReplayPlayer`, `PlayOfTheGame`, `ImpactFx`) create themselves on demand so no scene has to place them. Look for a static `Ensure()` / `Instance`.
- **Net-free local FX:** visual/audio effects (tracers, blood, impact marks, the kill feed) are drawn from data each client already has, rather than sent over the network. Keep that pattern for new cosmetics.
- **Small files, clear names.** Keep new scripts focused; the existing tree groups by domain (Combat/Player/Net/…), not by type.
- **Heavy, honest comments.** Most non-obvious decisions are explained in a comment right above the code. When you change balance or behavior, update the comment too — it's the main design record.

---

## 10. Where things are (quick index)

- Weapon stats → `Combat/WeaponController.cs` (`DefaultLoadout`)
- Weapon menu → `Net/LoadoutChoice.cs`
- Passives → `Passives/PassiveType.cs` + `Net/PassiveChoice.cs` + owner scripts
- Movement → `Player/PlayerMotor.cs`
- Grapple → `Player/GrappleHook.cs`
- Camera/FOV/speed-feel → `Player/SpeedFeel.cs`, `Player/MouseLook.cs`
- Health/armour/death → `Combat/PlayerHealth.cs`, `Combat/PlayerArmour.cs`, `Combat/Ragdoll.cs`
- Explosions/rockets → `Combat/Explosion.cs`, `Combat/Rocket.cs`
- Hitboxes/headshots → `Player/PlayerBody.cs`, `Combat/Hitbox.cs`, `Combat/Headshot.cs`
- Match rules/modes → `Net/MatchManager.cs`
- Connect screen → `Net/ConnectUI.cs`
- HUD → `UI/SpeedHud.cs`; kill feed → `UI/KillFeed.cs`
- Replay/POTG → `Replay/MatchRecorder.cs`, `ReplayPlayer.cs`, `PlayOfTheGame.cs`
- Bots → `World/SimpleBot.cs`
- Settings/keybinds → `Core/GameSettings.cs`, `Core/Keybinds.cs`, `UI/SettingsUI.cs`
- Gun viewmodel → `Combat/WeaponView.cs`

---

_Keep this file next to the code. When you add a system, add a row to §3 and §10 so the map stays true._
