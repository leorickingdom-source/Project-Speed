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

### Running a session

Host side, every time:

1. playit agent running.
2. Launch, click **Host**. The address and port boxes are ignored when hosting — the server
   binds `hostBindPort` (7770), which is what the tunnel forwards to.
3. Stay open. Your machine is the server; quitting drops everyone.

What friends enter once (remembered afterwards, including through a settings reset):

```
Address:  69.9.191.18        (Sydney region tunnel)
Port:     1103
```

Use the **raw IP**, not the `washington-tarantula.aus.at.playit.plus` hostname — the
`.playit.plus` domain was unreliable where the IP was not. Region matters a lot: the original
`gl` (global) tunnel landed on a relay in **Oregon** at 192ms from Penang; the `aus` region is
**Sydney** at 108ms. Region is only selectable when CREATING a tunnel, not when editing one, so
changing it means making a new tunnel.

### Two things that make a tunnel silently forward nothing

Both of these produce a tunnel that resolves, pings, and accepts packets — because you are
reaching *playit's* relay, not your machine. Nothing about the failure points at the real cause.

1. **No local address set.** The tunnel must point at `127.0.0.1:7770`. A newly created tunnel
   does not have this until you set it, and this cost an hour of misdiagnosis once already.
2. **Wrong protocol.** Must be **UDP** — Tugboat is LiteNetLib. A TCP tunnel looks correct in the
   dashboard and never connects.

If a client cannot connect, check those two before suspecting the game, the firewall, or the
friend's network. The fastest isolation test is to run the build **twice** on the host machine —
one hosting, one dialling the tunnel address. That separates "tunnel misconfigured" from
"friend's network" in about a minute, and they need completely different fixes.

### The host should be where the players are

Most of this group is in **Australia** while the host is in Malaysia, so every player's traffic
crosses to Penang and back. Even a perfect Sydney relay leaves them around 130ms.

**An Australian player hosting instead puts the majority at 10–40ms** and moves the distance cost
onto the single Malaysian player, which is the right trade. They need the same build, and either
their own `aus`-region tunnel or a direct port forward if their ISP is not CGNAT. No code changes
— any player can host.

This is worth more than any further relay tuning.

Gotchas worth knowing before someone reports a "bug":

- **Only the tunnel owner can host.** It points at one machine. A friend clicking Host starts a
  server nobody can reach.
- **Host first.** Joining before the host is up gives "Could not reach a host…", which is
  correct, not a failure.
- **Map, mode, bots and difficulty are the host's alone.** Clients can toggle them locally and
  it is ignored — MatchManager syncs the host's values.
- **IPv6.** The hostname resolves to both A and AAAA records. If someone cannot connect, having
  them use `147.185.221.225` forces IPv4 and is worth trying before assuming anything is broken.

Verified end to end: a client dialled the public address, traffic went out to the relay and back
into the local server, connection authenticated and a player spawned.

**Measuring latency: do not trust the editor.** FishNet's RTT is measured across ticks, so an
unfocused editor throttling its frame rate reports nonsense — a direct `127.0.0.1` loopback
measured 140ms under those conditions. The HUD ping readout exists for this; read it from a real
build.

Cost: the host's machine must be on, and traffic relays — which adds latency, and this game is
more sensitive to that than most since everything rests on movement precision.

Rejected: **Tailscale** (free tier caps at 3 users, and every friend needs an account plus an
approval — per-person admin forever). **Photon** (would mean replacing FishNet and rewriting the
prediction/reconcile layer, which is the hardest thing already working here, to solve a
connection-brokering problem).

### Port forwarding is impossible on this connection — do not retry it

The host is behind **carrier-grade NAT**. Traceroute hop 2 is `100.70.0.1`, inside the CGNAT
range `100.64.0.0/10`. Forwarding a port on the home router works, but the ISP's NAT above it
never forwards that port down, so inbound connections die before reaching the house.

The trap: the address reported by an external "what is my IP" service (`161.142.137.96`) is
routable and looks fine. That is the carrier's shared outer address, not the router's WAN IP.
Checking it proves nothing. **The traceroute is the test** — a private or `100.64.x` second hop
means CGNAT and no amount of router configuration will fix it.

Consequence: every workable option must be **outbound-initiated** from the host. That is why the
playit tunnel works at all — its agent dials out and traffic returns on that established flow, so
inbound firewall and inbound NAT never apply.

Asking the ISP for a real public IP would lift this restriction; some plans offer it on request.

If it outgrows the tunnel, in order of effort:

1. **ZeroTier** — free, 25 devices on one network you own, no card. Friends install once and
   join a network ID once; after that it behaves like a LAN. Hole-punches P2P, so a
   Malaysia-to-Malaysia game stays in-region rather than routing via Oregon, and it works
   through CGNAT because both ends dial out. Falls back to its own relay only if punching fails
   (symmetric NAT), which is the one case where latency stays bad.
2. **EOS** — free, no server to run, NAT punching with relay fallback, lobbies and join codes.
   Anonymous auth means nobody logs into anything. A transport swap, not a rewrite: FishNet and
   the prediction layer stay untouched. The "friends install nothing" option.
3. **Steam** — best UX ("Join Game" from the overlay) but only if shipping there.

**Oracle Cloud is not an option here** — signup rejection (card verification, capacity) blocked
it in practice. Other free tiers with a Singapore region exist but are 12-month trials rather
than always-free, so they solve this only temporarily.

### Bug: the dedicated Server path never loads a map

`ConnectUI`'s **Host** button subscribes to `OnServerState` and calls `LoadChosenMap` once the
socket is up. The **Server** button does neither — it starts the server and stops. So a
dedicated server never registers a global scene, and joining clients get no scene assignment.
It would appear to work if everyone happened to be in Arena and break confusingly otherwise.

Small fix, but load-bearing for any VPS route.

---

## 4b. Open: speed above 16 m/s buys nothing in a fight

Surfaced 2026-07-27 while adding Slipstream, from the question "Highground and Slipstream seem
the same?". They are not — but the answer exposed something worse than an overlap.

`MomentumDamage` ramps from `rampStartSpeed` 9 to `fullBonusSpeed` **16**, and the BASE air
ceiling is `groundSpeed * flowMax` = 9 × 1.8 = **16.2**. The damage bonus is therefore maxed out
by ordinary flow, and every metre per second past it is worth nothing:

| source | speed | damage bonus |
|---|---|---|
| slide | 16 | 0.25 (max) |
| bhop | 16.2 | 0.25 |
| Slipstream ceiling | 18.5 | 0.25 |
| grapple reel (jump held) | 23.8 | 0.25 |
| rocket-jump launch | ~27 vertical | 0.25 |

So the game's three signature verbs — grapple, rocket jump, slingshot release — are **damage
neutral**. "More movement -> more speed -> more power" holds only up to the speed a slide already
gives you. Everything above that buys position and time, which is real, but it is not the loop
the game says it runs on.

Three ways out, in the order I would consider them:

1. **Second tier.** Keep 0.25 at 16, then a smaller bonus continuing to ~28. Nothing existing is
   nerfed; the cost is a curve with two segments to explain.
2. **Leave it, and say so.** Declare 16 the "you are moving properly" line and let everything
   above it pay in position rather than damage. Slipstream is then honestly a mobility pick and
   Highground remains the damage-by-place option. Cheapest, and defensible.
3. **Extend the ramp** — `fullBonusSpeed` 16 → ~26. Avoid: it pays for the rare case by taxing
   the common one. At 16 the bonus would fall 0.25 → ~0.18, so sliding play gets worse to make
   flying play better.

Not urgent. It becomes urgent the moment someone asks why the grapple does not make them hit
harder, because the connect screen tells every player that speed is damage.

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
