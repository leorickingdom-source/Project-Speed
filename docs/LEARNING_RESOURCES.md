# Learning Resources

Curated to match **this project's** actual stack, so each resource ties to code you already have. Read alongside [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md).

Tags: 🆓 free · 💲 paid · 📺 video · 📖 docs/article · 🧩 code to study · ⚠️ dated (still useful, but check versions).

---

## 1. The language & engine (start here if any of this is new)

- 🆓📖 **Microsoft Learn — C# first steps / fundamentals** — https://learn.microsoft.com/training/paths/csharp-first-steps/ · Learn C# *as a language* before Unity, so you don't confuse "C#-isms" with "Unity-isms". Everything in `Assets/Scripts` is C#.
- 🆓📺 **Unity Learn — Create with Code** — https://learn.unity.com/course/create-with-code · Project-based intro to Unity scripting. The best hands-on on-ramp.
- 🆓 **Unity Learn — Essentials Pathway** — https://learn.unity.com/pathway/unity-essentials · Editor, GameObjects, prefabs, physics, build process — the things you do every day in this project.
- 🆓📖 **Unity 6 resources hub** — https://unity.com/campaign/unity-6-resources · Official Unity 6 docs, sample projects, best-practice guides. (Your Editor is Unity 6.5.)
- 🆓📺 **Code Monkey (YouTube)** — https://www.youtube.com/@CodeMonkeyUnity · Clear, current Unity/C# tutorials; good for specific "how do I do X" questions.
- 💲📖 **Learning C# by Developing Games with Unity 6 (Packt)** — https://www.packtpub.com/en-us/product/learning-c-by-developing-games-with-unity-6-9781805808701 · If you want one structured book that matches your exact Unity version.

> ⚠️ **Brackeys** is still widely recommended, but the channel stopped posting new Unity videos around 2020 — great fundamentals, just verify anything version-specific.

## 2. URP (how this project renders)

- 🆓📺 **Unity Learn — Unity 6: Universal Render Pipeline** — https://learn.unity.com/course/unity-6-universal-render-pipeline · Short course on URP in Unity 6. Explains why materials/shaders behave the way they do here (remember the magenta-material issue — that was a built-in-shader-under-URP mismatch).
- 🆓📖 **Introduction to URP e-book (Unity 6 edition)** — https://unity.com/resources/introduction-to-urp-advanced-creators-unity-6 · Deeper dive once the basics click.

## 3. FishNet — the netcode this game runs on (most project-specific)

This is the single most important area to study, because the host/client logic in `Net/`, `Player/PlayerNetwork.cs`, and the `SyncVar`/`[ObserversRpc]` calls throughout are all FishNet.

- 🆓📖 **FishNet official docs — Getting Started** — https://fish-networking.gitbook.io/docs/guides/getting-started · Installing, connecting, preparing & moving a player, spawning, SyncVars. Do the tutorials in order.
- 🆓📖 **FishNet docs home** — https://fish-networking.gitbook.io/docs · Reference for RPCs, SyncTypes, prediction, observers — look up the exact feature you're touching.
- 🆓📖 **FishNet on DeepWiki** — https://deepwiki.com/FirstGearGames/FishNet · Auto-generated, searchable overview of the FishNet codebase/architecture.
- 🆓 **FishNet Unity Discussions thread** — https://discussions.unity.com/t/fish-networking-unity-networking-evolved-free/885382 · Community Q&A; the author is active. (FishNet also has a busy Discord linked from the docs.)

## 4. Movement tech — the soul of this game (study with `Player/PlayerMotor.cs`)

Your bunny-hop / air-strafe / speed-as-damage design comes straight from Quake and Source. Learn the math once and `PlayerMotor` will read like a book.

- 🆓📖 **"Bunnyhopping from the Programmer's Perspective" — Adrian Biagioli (Flafla2)** — https://flafla2.github.io/2015/02/14/bunnyhop.html · *The* canonical explanation of air acceleration and why strafing gains speed. Read this first.
- 🆓📖 **"Bunnyhopping for Noobs"** — https://2nafish117.github.io/2021/07/09/bunnyhopping-for-noobs.html · A friendlier walkthrough of the same idea.
- 🆓📖 **Valve Developer Wiki — Bunnyhopping** — https://developer.valvesoftware.com/wiki/Bunnyhopping · The original-source reference for the mechanic and its history.
- 🧩 **WiggleWizard/quake3-movement-unity3d** — https://github.com/WiggleWizard/quake3-movement-unity3d · Quake III strafe-jump ported to Unity C#. The closest reference implementation to study next to your motor.
- 🧩 **atil/fpscontroller** — https://github.com/atil/fpscontroller · A clean Unity FPS controller citing the Quake 3 / Half-Life SDK sources.
- 🧩 **ccaven/fps-parkour** — https://github.com/ccaven/fps-parkour · Source-style movement in Unity, includes the strafe computation.
- 📖 **Replicating Source Engine Air Strafing in Unity (thesis)** — https://www.theseus.fi/handle/10024/507593 · If you want the full derivation and a faithful Unity recreation written up.

> When you read these, check their licenses before copying code. Study the *approach*; your `PlayerMotor` already has its own (well-commented) version.

## 5. FPS combat & making hits fair over the network

Relevant because your hits are currently **client-authoritative** (see DEVELOPER_GUIDE §6). If you ever make this competitive, you'll need server-side validation / lag compensation — these explain how.

- 🆓📖 **"The Art of Hit Registration"** — https://danieljimenezmorales.github.io/2023-10-29-the-art-of-hit-registration · What hitscan is, and why/when you need to "rewind" the world to register hits fairly.
- 🆓📖 **Embedded FPS — Health, Shooting, Lag Compensation** — https://lukestampfli.github.io/EmbeddedFPSExample/guide/health-shooting-lag-Compensation.html · A concrete Unity walkthrough of server-authoritative shooting. ⚠️ Older Unity, concepts still current.
- 🆓📖 **Unity — Server Rewind (Netcode for Entities)** — https://docs.unity3d.com/Packages/com.unity.netcode@1.7/manual/server-rewind.html · Unity's official take on lag compensation. (Different netcode than FishNet, but the *concepts* — render latency, rewind-and-check — are universal.)

## 6. The art/animation packs you're using

- **Easy FPS** (your animated gun viewmodel + hands, in `Assets/Easy FPS`) — search "Easy FPS" on the Unity Asset Store for its page/docs.
- **Mixamo** — https://www.mixamo.com · Free rigged characters and animations (your body animations come from here). Good to know for adding new character anims.
- **Low Poly Guns** (your optional static gun meshes) — Asset Store pack; see DEVELOPER_GUIDE §7 for how it's wired in.

---

## Suggested order

1. **If Unity/C# is new:** §1 (Microsoft C# → Create with Code → Essentials).
2. **Understand your own game's feel:** §4 — read the Flafla2 article, then open `PlayerMotor.cs` and map the concepts to the code.
3. **Understand multiplayer:** §3 — the FishNet getting-started tutorials, then trace one feature (e.g. a kill) through `KillFeed.cs` → `[ObserversRpc]`.
4. **Go deeper when needed:** §2 (URP) when you touch materials/shaders; §5 (lag comp) only if you take this competitive.

Everything in §1–§5 marked 🆓 is free. Start small, change one number in `WeaponController.DefaultLoadout()`, press Play, and build from there.
