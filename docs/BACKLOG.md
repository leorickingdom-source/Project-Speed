# Backlog

Deferred work, with the reasoning behind each decision so it does not get re-litigated.
Written after the armour / bots / names / feedback pass on `feat/movement-tech`.

---

## 1. Scaling past 6 players

Currently comfortable at **6**. Nothing crashes past that — it degrades.

### Cheap and mechanical (an afternoon, no design risk)

| item | current | what to do |
|---|---|---|
| Spawn points | 6 per map | Place more `SpawnPoint_*` objects. `SpawnPointBinder` binds by name prefix, so this is **zero code** — just objects in the scene. |
| Colour palette | 6, then repeats | Extend `PlayerColors.Palette` and `NameFor`. |

Spawn points bite first: past 6, `PickSpawn` hands the same point to two people at once and
its anti-camp rule (furthest from the nearest living opponent) stops meaning anything.

The palette realistically caps around 8–10 before hues stop being separable against a
desaturated arena *and* stay distinguishable for colour-blind players — the original
constraint that put red and blue first. Custom names cushion this: two people sharing a tint
is survivable when both typed a real name.

Scoreboard layout holds to roughly 12–16 without changes.

### Wasteful around 12+, not before

`PlayerScore.OnGUI` calls `FindObjectsByType<PlayerScore>` on **every OnGUI pass**, and OnGUI
fires 2–3 times per frame. That is a full scene scan several times a frame. Invisible at 6,
genuinely wasteful at 16. `MatchManager` and `SimpleBot` do similar scans. `SpeedHud`'s
nameplate scan is already throttled to 2Hz.

All of these are cache-the-list fixes, not redesigns. Do them together when the count justifies it.

Bandwidth is O(n²) — every player's transform to every client. Fine to ~10, needs FishNet
observer / interest management past that. There is a HashGrid condition in the FishNet demos
that does exactly this.

### The real wall — not a player count

**Hit detection trusts the shooter.** The client decides it hit you and tells the server how
much damage to apply (`PlayerNetwork.ReportHit`). The code comment already flags this: swap it
out before anything competitive.

With close friends this is a non-issue. With strangers it is not "cheatable with effort", it is
*trivially* cheatable. Fixing it means server-side raycasts with lag compensation, which is a
bigger job than everything else in this document combined.

**Decision rule: more friends → spawn points and colours. Strangers → authority rewrite first,
regardless of count.**

---

## 2. Shipped but never looked at

All verified programmatically or by arithmetic, never by eye. Highest-value thing to clear.

- **Damage vignette** intensity and the low-health pulse rate.
- **Damage direction wedges** with several attackers at once — the merge-by-proximity rule has
  never been seen firing with a real crossfire.
- **Death camera framing** on Stacks specifically: the centre pillar is the case the wall-clip
  raycast exists for, and it has never been tested there.
- **Kill feed** rendering. Verified through a scripted host session, never seen on screen mid-fight.
- **Practice-tier bots** (`BotChoice.Practice = 0.35`). Estimated, never felt. One constant to change.
- **HUD font sizes.** Raised ~a third, but guessed at the target resolution. Derived layouts were
  checked to fit from 1440p down to 720p; below ~700p the pause panel overflows.

---

## 3. Considered and deliberately not built

Keep the reasoning — these looked attractive and were rejected for specific reasons.

### Chosen secondary weapons — no

Letting players pick a secondary makes everyone carry **long gun + short gun**, so nobody has a
weakness. The falloff system (inverted sniper curve, shotgun cliff at 20m, SMG dying at range)
exists to make distance a real constraint; a range-covering secondary is a solvent for it.
Twenty combinations collapse to about four once people find the pairings.

It also collides with passives, which already occupy the "second axis that shapes how I play" slot.

If ever revisited: the pool must be **all short-range sidearms** that differ in how they fight up
close, so the sniper's 10m hole stays a hole whichever you take.

### Movement-based spread — no

Would penalise accuracy while moving, in a game built on bhop, slide, dash and grapple. Fights
the core mechanic head-on.

### Bloom spread — only if needed

Spread that grows while holding fire and resets when you stop. The *one* spread variant worth
having: rewards burst discipline, is learnable, and separates the automatics from the semi-auto
weapons without touching a damage number. Only build it if the automatics turn out to need a
skill layer.

### Armour absorption rate — settled

0.6 confirmed fine in playtest; automatics felt right. Revisit only if that changes. Note that
lowering it reopens the sniper headshot threshold (below).

### Weapon slot keys 1–5 — parked correctly

The only unbindable inputs. `allowWeaponSwitching` is `false`, so they currently do nothing at
all — there is no binding to miss. Becomes live the moment switching turns on, which is the same
change that would need a swap key anyway. One task, not two.

---

## 4. Getting friends connected

Direct IP only — Tugboat has no discovery and no NAT traversal. Bundled transports are
Tugboat, Synapse and Multipass; no relay, no lobbies, no friend lists.

**Current approach: `playit.gg` free tunnel.** Host runs the agent alongside the game, which
gives a permanent public address with no port forwarding and works behind CGNAT. Shared once,
never changes. The connect screen now remembers the last address and port, so it is typed once
per player, ever.

Cost: the host's machine must be on, and traffic relays — which adds latency, and this game is
more sensitive to that than most since everything rests on movement precision.

Rejected: **Tailscale** (free tier caps at 3 users, and every friend needs an account plus an
approval — per-person admin forever). **Photon** (would mean replacing FishNet and rewriting the
prediction/reconcile layer, which is the hardest thing already working here, to solve a
connection-brokering problem).

If it outgrows the tunnel, in order of effort:

1. **Free VPS** (Oracle Cloud Always Free) running a Linux headless server. Fixed address,
   always up, nobody's PC needed, no relay hop. **Blocked on the bug below.**
2. **EOS** — free, no server to run, NAT punching with relay fallback, lobbies and join codes.
   Anonymous auth means nobody logs into anything. A transport swap, not a rewrite: FishNet and
   the prediction layer stay untouched.
3. **Steam** — best UX ("Join Game" from the overlay) but only if shipping there.

### Bug: the dedicated Server path never loads a map

`ConnectUI`'s **Host** button subscribes to `OnServerState` and calls `LoadChosenMap` once the
socket is up. The **Server** button does neither — it starts the server and stops. So a
dedicated server never registers a global scene, and joining clients get no scene assignment.
It would appear to work if everyone happened to be in Arena and break confusingly otherwise.

Small fix, but load-bearing for any VPS route.

---

## 5. Placeholders and small gaps

- **Armour pickup is a primitive cube.** Reads as clearly *not a sphere*, which was the point,
  but looks like programmer art. Mesh/material job.
- **Bots do not score.** They carry `Health`, not `PlayerScore`, so killing one credits nothing —
  hitmarker and a kill feed row, then nothing. Fine for a dummy, odd for a networked opponent.
- **`SampleScene.unity` is unused** — not in `MapChoice.Names`. Dead weight; delete or adopt.
- **`.plastic/` working-tree files** show as permanently modified in git. Plastic's own state,
  not ours. Consider gitignoring.

---

## 6. Balance reference

Baseline as of the revolver / armour pass, so future tuning has something to measure against.

Bare = 150 HP. Armoured = 150 HP + 100 armour at 0.6 absorption (~250 effective).

| weapon | dmg | cycle | mag | STK bare | STK armour | TTK bare | TTK armour |
|---|---|---|---|---|---|---|---|
| Revolver | 65 | 0.55 | 6 | 3 | 4 | 1.10s | 1.65s |
| Rifle | 14 | 0.11 | 30 | 11 | 18 | 1.10s | 1.87s |
| Sniper | 100 | 1.20 | 5 | 2 | 3 | 1.20s | 2.40s |
| SMG | 9 | 0.07 | 45 | 17 | 28 | 1.12s | 1.89s |
| Shotgun | 13×8 | 0.70 | 6 | 2 | 3 | 0.70s | 1.40s |

**Sniper headshot is 3× (300), not the shared 2×.** Armour moved the one-shot threshold to 250
damage — armour caps its soak at 100, so anything past 166 dumps the remainder into health and a
one-shot needs `dmg - 100 >= 150`. 300 clears it by 50, the same margin a 200 headshot had over
150 health before armour existed. **Anything that changes armour absorption or max armour
invalidates this number.**

**Spread matters more than falloff for range identity.** Perfect-aim hit chance against a
standing player (1m × 2m):

| | 10m | 20m | 30m | 50m | 90m |
|---|---|---|---|---|---|
| Revolver / Sniper (0°) | 100% | 100% | 100% | 100% | 100% |
| Rifle (1.5°) | 100% | 99% | 75% | 37% | 12% |
| SMG (3.5°) | 91% | 43% | 19% | 7% | 2% |

The SMG's falloff only takes it to 0.4× at 45m, but spread takes it to a 19% hit rate at 30m —
it runs dry before it can kill at that range. Crouching roughly halves these, and sliding is a
core move, so it is a live combat mechanic that is currently invisible to the player.
