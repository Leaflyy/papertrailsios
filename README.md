# PaperTrails

A Unity 6, 3D, Red vs. Blue territory-game prototype. All project source, tests,
build outputs, and work logs live in this directory.

Current verification and device-test limits: [Docs/VALIDATION.md](Docs/VALIDATION.md).

## Play

Open `Builds/Windows/PaperTrails.exe`, then choose **Practice with CPUs**. Pick a
team, skin, and arena vote, press **Ready**, then **Start match**. Practice keeps
the two human slots, with the absent partner under CPU control. Standard LAN
matches require both humans to connect and ready up.

Movement is continuous. Drag/swipe anywhere to change direction. On desktop,
WASD and arrow keys also steer. Release input to keep the current heading.
Leave friendly turf, draw a loop, and return to friendly turf to capture it.
Cut enemy trails, avoid your own, and freely cross friendly trails.

For online play, select **Online** and **Host game**. Share the six-character
code displayed in the lobby. The other player selects **Online**, enters that
code, and presses **Join game**. Unity Relay carries encrypted traffic; the
hosting player's own device runs the authoritative simulation, bots, scoring,
and match timer. No router port forwarding is required. The project is linked
to the PaperTrails Unity Services project and Relay is enabled.

For LAN play, select **LAN**, host on one device and enter that device's IPv4 address on the
other. Both devices must be on the same reachable LAN. TCP port 27851 is used.
The menu displays one detected address; PCs with VPN/virtual interfaces may
need the physical Wi-Fi adapter's address instead.

To invite someone, tap **Share join link** in the host lobby and send them the
copied text. Tapping a `papertrails://join` link opens the app and joins that
lobby directly. If a joiner loses connection mid-match, the game banks their
coins and spends a few seconds reconnecting before falling back to the lobby.

## Unity

Open this folder as a project with Unity **6000.5.6f1**. Open
`Assets/PaperTrails/Scenes/PaperTrails.unity` and press Play. The empty startup
scene is bootstrapped by `PaperTrailsApp`; visuals are generated at runtime.

Editor menu commands:

- `PaperTrails/Create startup scene`
- `PaperTrails/Build Windows`
- `PaperTrails/Build Android APK`

Android builds require Unity's Android Build Support, SDK, NDK, and OpenJDK
modules. The build targets ARM64 / IL2CPP, Android API 26 minimum, with automatic
portrait/landscape rotation and safe-area-aware layouts.
iOS needs the iOS support module and a Mac with Xcode/signing to produce and
install an iPhone build. The iOS app has not been built or tested here.

## Implemented Foundation

- Separate, engine-independent C# simulation running at 25 Hz.
- All four human team combinations with five roster slots per team.
- Team-owned grid turf, independent overlapping trails, enclosure flood fill,
  stealing, enemy cuts, own-trail death, and permanent spawn hubs.
- Positional death penalty, 2-second respawn, 2.5-second self-trail respawn.
- Tunable 1-10 minute lobby duration, five-minute default; exact cell-count
  scoring, neutral turf in the denominator, blocked cells excluded, unfinished
  regulation trails discarded, first new capture wins tied overtime.
- Three baseline bot personalities, host-controlled pathfinding and expansion.
- Ten procedural arena masks, all ten participants' votes, weighted selection,
  and an animated vote display.
- Smoothed shared contour vertices without visible tiles; the grid stays internal.
- Nonlethal walls slide the character along the same smoothed arena boundary.
- Chunked 16x16 territory meshes, interpolated characters, continuous trails, close follow camera,
  minimal HUD, team hubs, and skin-based human minimap markers.
- Netcode for GameObjects / Unity Transport / Relay join-code adapter plus direct
  LAN adapter, both with device-host authority and input-only guest commands.
- Compressed 10 Hz snapshots,
  keepalives, bounded packets/queues, CPU takeover, original-player reconnect.
- Private human coin pickups, local currency, duplicate-free 25-coin capsule
  unlocks, skin selection, and per-match reward records to avoid replay payouts.
- 34 procedural cosmetic prototypes, a rendered capsule machine, and original
  synthesized sound effects. Cosmetics never enter simulation calculations.
- Results display for turf, captures, largest capture, cuts, deaths, and coins.

## Verification

Run the engine-independent rule and real-socket tests:

```powershell
dotnet run --project Tests/PaperTrails.Tests.csproj
```

The suite covers each arena, all team compositions, trail overlap rules,
respawning, capture/stealing, timer expiry/overtime, full bot simulations,
snapshot round trips, disconnects, and reconnects.

The Windows player's development test flags are `-paperSmoke`,
`-paperHostSmoke`, and `-paperClientSmoke`. The visual smoke run cycles through
gameplay and menus, writes PNGs under `Builds/Screenshots`, then exits. Use a
visible window and `-force-d3d11` on this PC; a hidden/minimized window may
produce black frames. The network flags exercise two separate game instances
on localhost and report success/failure in their player logs. The equivalent
Relay flags exercise real cloud allocations and join codes with separate
anonymous test profiles.

```powershell
./Tests/VerifyPlayer.ps1 -Mode Visual
./Tests/VerifyPlayer.ps1 -Mode LAN
./Tests/VerifyPlayer.ps1 -Mode Relay
```

The visual script checks gameplay and five menu screens in desktop, portrait,
and landscape windows, including nonblank pixel checks. Screenshots need human
inspection as well; pixel checks alone do not establish correct layout.

## Remaining Production Work

This is a playable foundation, not the finished launch game in the design brief.

- The Relay path uses anonymous Unity Authentication and encrypted DTLS traffic.
  Client prediction, bandwidth-efficient deltas, adverse-network testing,
  authentication hardening, and reconnect UX polish remain.
- Arena masks and skins are original procedural blockouts. They need authored
  launch-quality meshes, animation, and art review.
  Some roster members still share much of their construction.
- Bots need tactical tuning, contested-area awareness, and mobile profiling.
  They currently use grid paths and simple expansion/attack priorities.
- Audio and capsule feedback are prototypes. Full event coverage, particle
  effects, mix/music, reveal choreography, and accessibility settings remain.
- IMGUI provides the current responsive menu/HUD. A production mobile UI,
  localization, and device touch usability testing are still needed.
- Device thermal/battery/performance tests and Android/iOS cross-device play
  have not been verified. No host migration is implemented, as specified.

## Source Layout

`Core/Arena.cs` owns playable masks and hub placement. `Core/GameSimulation.cs`
owns gameplay and bot decisions. `Runtime/LanSession.cs` contains only the
socket transport; `RelaySession.cs` implements the NGO/Relay adapter.
`WireState.cs` contains snapshot data and `PacketCodec.cs` compresses it.
`PaperTrailsApp.cs`
orchestrates lobby/match flow and progression. `GameView.cs`, `SkinFactory.cs`,
`PreviewStudio.cs`, and `SoundBank.cs` implement presentation.

Coin rate/cost, tick rate, movement speed, arena grid dimensions, and the small
hub-protection region are initial playtest values. The entire protected hub
is permanently inaccessible to opponents; it is not a timed invulnerability
power-up. Multiple enemy trails in one cell all have their owners eliminated.

All art and audio in this prototype are generated from original code. No assets
from Paper.io, Splatoon, or other commercial games are included.
