# Creative Replacement Checklist

Swapping placeholder art for real assets. Grounded in the current code so prep isn't wasted.

**Current placeholders**
- Player body — `X Bot` grey Mixamo mannequin, tinted per-player (`PlayerBody.cs`)
- FP weapon + hands — `Easy FPS` pack (`FPS_Character.fbx`)
- Weapon/player SFX — `Easy FPS` wavs
- Muzzle flash / crosshair / weapon icons — `Easy FPS` textures
- Dead weight to delete — old `Kevin Iglesias` dummy, `FishNet` demo art

---

## 0. Decide first (blocks everything)

- [ ] **Art direction** — realistic / stylized / low-poly? One decision, drives every asset below.
- [ ] **Make vs buy vs AI-gen** — Mixamo + Asset Store, commission, or generate?
- [ ] **Budgets** — verts per character (X Bot is ~28k), texture resolution, material count. Arena shooter = many bodies on screen, keep low.
- [ ] **Player identity model** — one shared body recoloured by tint (current design) or distinct per-player skins?

---

## Art direction — Painterly (LOCKED)

Hand-painted textures. Refs: **Team Fortress 2, Overwatch, Dota 2, World of Warcraft**. Light, form, and detail are painted into the albedo by hand rather than computed from PBR maps.

**Why it fits this project**
- Readability heritage — TF2/OW are built silhouette-first and read at range, the same goal as this arena shooter.
- Cheapest to run — diffuse-driven, minimal normal/roughness, simple shader. Lightest option for many bodies on URP.
- Hand-painted tiling materials drop straight onto the existing greybox box kit and hide the low geometry.
- No PBR/scan pipeline to stand up.

**Cost shape** — trades PBR authoring tech for painting skill and hours. Cheap in tech, expensive in artist time. AI-generated painterly textures are a viable substitute if not hand-painting.

**Texture / shader spec**
- Albedo-first. Bake light + AO + form into the diffuse by hand. Little or no normal/roughness; a soft normal only for large forms if needed.
- Shader: URP Lit at low smoothness, or a flat/ramp-lit stylized shader if you want to control lighting fully.
- Palette discipline (this is what preserves readability): environment painted in a muted mid-value range; reserve high saturation + high value contrast for players, pickups, and hazards. Painterly leans warm/rich — keep that on props, not on the whole map, so enemies still pop.

**Player identity — the one conflict to resolve**
`PlayerBody.Tint` currently multiplies the identity colour over the entire body `_BaseColor` and drives `_EmissionColor` (`PlayerBody.cs:358`). Multiplying a colour over a hand-painted skin turns it muddy — painterly games never full-body-tint, they zone it.

- [ ] Paint bodies in a neutral / desaturated base.
- [ ] Move identity to an **emissive trim/accent** (keeps the cross-map colour cue the code already relies on) plus a **tint-mask** so only jersey/armor panels take the `_BaseColor` multiply.
- [ ] Rewrite `Tint` to target the masked submaterial + emissive trim instead of every `Renderer`.
- [ ] Confirm this identity approach before painting any skin — it changes both the art and the `Tint` code.

**Environment**
- [ ] Hand-painted tiling trims / atlases applied to the box kit.
- [ ] Flatter lighting; let painted form carry it. `Global Volume` for mood, not realism.

---

## Hard constraints — new assets MUST meet these or the swap breaks

These come straight from `Assets/Scripts/Player/PlayerBody.cs`.

- [ ] **Rig = Humanoid avatar.** Code drives bones via `GetBoneTransform(HumanBodyBones.*)`. Import → Rig → Animation Type = **Humanoid**. Generic/none → no pose, no hitboxes (`PlayerBody.cs:308`).
- [ ] **Prefer Mixamo skeleton.** Native Mixamo rig = zero retarget, no foot-sink error. Any humanoid still retargets, but reintroduces the error the X Bot switch fixed (`PlayerBody.cs:28`).
- [ ] **Material = URP Lit with `_BaseColor` + `_EmissionColor`.** Tint sets those two props (`PlayerBody.cs:363`). Ideally **one material** for the whole body. Multi-material works but every submesh takes the tint.
- [ ] **Albedo neutral/desaturated, identity zoned.** Painterly skins can't take a full-body colour multiply — see the *Player identity* fix in the Art Direction section. Move identity to emissive trim + a tint mask.
- [ ] **Prefab in a `Resources/` folder, named `X Bot`** — or edit the `PrefabPath` const (`PlayerBody.cs:29`). Loaded via `Resources.Load`.
- [ ] **T-pose bind.** `armRestDown = 72` folds arms down from a T-pose. An A-pose model needs `armRestDown` retuned (`PlayerBody.cs:86`).

---

## 1. Character body

### Painterly character spec

**Rig / tech (hard, from code)**
- Humanoid avatar, **Mixamo skeleton** — native to the `PlayerLocomotion` clips, zero retarget (`PlayerBody.cs:28`).
- Authored in **T-pose** (`armRestDown` assumes it).
- Prefab in a `Resources/` folder, named `X Bot` — or update `PrefabPath` (`PlayerBody.cs:29`).
- Reference height ~1.81m at scale 1 (X Bot). Expect to set `modelScale` for a final ~2.35m and retune `PlayerMotor.standEyeHeight` with it.

**Budgets (arena shooter — many bodies on screen)**
- Verts: **≤ 28k** (X Bot parity ceiling); target **10–18k**. Painterly hides low poly well.
- Materials: **2** — `Body` + `Trim` (see identity contract). Keeps draw calls low.
- Textures: 1 hand-painted albedo per material, **2048** body / 1024 trim. Optional soft normal for big forms only. **No** roughness/metallic map — flat smoothness in the shader.

**Identity material contract (this locks the `Tint` rewrite)**
- **`Body` submesh/material** — neutral / desaturated painted albedo. **Not** colour-multiplied; stays as painted.
- **`Trim` submesh/material** — the identity zone (panels/stripes on large, high-contrast areas: torso, shoulders). Takes `_BaseColor` = identity colour **and** `_EmissionColor` = identity × strength. This is the cross-map "who is that" cue the emission system already exists for.
- **Recommendation: two submeshes**, not a mask texture. A submesh split needs no custom shader — the C# `Tint` loop just checks which renderer is the trim. A mask-channel approach would require authoring a shader that multiplies by the mask.

**Readability**
- Distinct silhouette, front/back readable (the reason the capsule was replaced).
- Trim on large readable zones so identity reads at range, not just up close.

### Build steps

- [ ] Model + rig to the spec above (Humanoid, Mixamo skeleton, T-pose, 2 submeshes `Body`/`Trim`).
- [ ] Hand-paint `Body` albedo neutral; author `Trim` as its own submesh/material.
- [ ] Place prefab in `Resources/`, name `X Bot` (or update `PrefabPath`).
- [ ] Retune size: `modelScale` (X Bot = 1.81m at scale 1, currently 1.3) **and** `PlayerMotor.standEyeHeight` (1.8) together, eye capped under the 2m capsule (`PlayerBody.cs:41`).
- [ ] Retune hitbox radii to new proportions: `headRadius`, `torsoRadius`, `armRadius`, `legRadius` (`PlayerBody.cs:66`).
- [ ] Verify `PlayerLocomotion` clips retarget cleanly (feet planted, hips at right height).
- [ ] Rewrite `Tint` to the contract: identity on `Trim` (`_BaseColor` + `_EmissionColor`), `Body` left as painted (`PlayerBody.cs:358`). Backward-compatible fallback to full-body tint when no `Trim` submesh exists, so the X Bot keeps working until the new model lands.
- [ ] In-game check: facing readable from silhouette, identity reads at range, crouch/slide keeps head inside capsule.

## 2. FP weapon + hands (viewmodel)

- [ ] Decide weapon set (matches art direction).
- [ ] Separate viewmodel arms mesh, authored for FP FOV / near-clip — not the world body.
- [ ] Fire / reload / pull animations.
- [ ] Timing lines up with weapon SFX below.

## 3. Audio

- [ ] Weapon: fire, reload, pull/equip, hit-marker.
- [ ] Player: footstep (walk/run — note movement audio is derived procedurally in `PlayerAudio`), jump, land, death.
- [ ] Format: WAV or OGG, mono for positional SFX.

## 4. VFX

- [ ] Muzzle flash (replace `Easy FPS` textures or go procedural).
- [ ] Decide: keep procedural `BlastFx` / `CorpseFx` or replace with authored effects.
- [ ] Impact / blood / hit spark.

## 5. UI / HUD

- [ ] Crosshair (replace `Easy FPS` crosshair textures).
- [ ] Hit-marker.
- [ ] Weapon icons.

## 6. Map / Arena

**Confirmed greybox.** `Assets/Scenes/Arena.unity` is built from Unity primitives — 44 cubes, 2 capsules, 1 sphere, zero real meshes. Layout + gameplay markers are complete and named (`Cover_*`, `Wall_*`, `Floor_*`, `Beam_*`, `Deck_*`, `Pylon_*`, `Ramp_NE`, `Tunnel_*`, `MapBounds`, `SpawnPoint_1-6`, `RocketSpawn`/`FlagSpawn`/`OddballSpawn`, `Pickup_*`). Only the visuals are placeholder. Own milestone.

- [ ] Choose approach: model each structural piece to match the blockout, **or** apply a modular environment kit + real materials/lighting onto the existing transforms.
- [ ] Preserve the current layout — transforms, spawns, and pickup markers are the tuned gameplay; don't lose them in the art pass.
- [ ] Replace prototype materials on the primitives with real surfaces.
- [ ] Lighting + `Global Volume` post pass (scene already has a Directional Light + Global Volume).

---

## Swap execution — code touchpoints

Where the swap actually lands:

- `PrefabPath` const — prefab name / location (`PlayerBody.cs:29`)
- `modelScale` + `PlayerMotor.standEyeHeight` — size retune
- `headRadius` / `torsoRadius` / `armRadius` / `legRadius` — hitbox retune
- `armRestDown` — only if new model is A-pose
- `ControllerPath` = `PlayerLocomotion` — the Mixamo AnimatorController

## Cleanup

- [ ] Delete `Assets/Kevin Iglesias` (old dummy, unused).
- [ ] Strip unused `Assets/FishNet/Demos` art.
- [ ] Remove leftover `Easy FPS` assets once replaced.
- [ ] Remove the TEMP `[BodyAnim]` diagnostic log (`PlayerBody.cs:451`).

---

## Suggested order

1. Art direction (decision)
2. Character body — biggest visual read
3. FP viewmodel
4. Audio
5. VFX / UI
6. Map art pass
