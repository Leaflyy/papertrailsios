# Validation - 2026-09-14

## Build Outputs

- `Builds/Windows/PaperTrails.exe` and its adjacent data/runtime folders.
- `Builds/Android/PaperTrails.apk` (28,305,911 bytes at verification).
- Unity 6000.5.6f1 reports successful builds for both targets.
- Android APK Signature Scheme v2 verification passes.
- Android package: `com.papertrails.game`, version `0.1.0`, ARM64,
  minimum API 26, target API 36, internet permission, automatic orientation.

## Passed Checks

- 96 engine-independent rule, geometry, compression, and socket checks.
- Nonlethal wall sliding follows the smoothed arena boundary.
- Friendly trail overlap, all overlapping enemy cuts, own-trail death,
  2.0/2.5-second respawn, stealing, permanent hubs, and 5v5 team compositions.
- Friendly trails remain harmless for every human/CPU combination. Human steering
  uses a sharp bounded turn rate and a touch dead zone; smooth trails use rounded,
  translucent rendering, overlap the departure turf, and reconnect from the
  authoritative trail geometry rather than grid-cell coincidence.
- The Collection screen uses a 20-coin capsule cost and exposes a checksum-protected
  recovery code containing the local coin, equipped-skin, and unlock progress.
- Player names are locally persisted, capped at 16 characters, shown above human
  players, and carried through lobby and authoritative network snapshots.
- Regulation expiry, unfinished-trail discard, exact ties, fresh overtime
  captures, and frozen final results. An unfinished regulation excursion
  cannot be reused to win overtime.
- Bot simulation on all ten arena masks.
- Two separate Windows players complete LAN host/join, lobby voting,
  roulette, and authoritative match replication.
- Two separate Windows players complete a real Unity Relay allocation and
  join-code connection, lobby voting, and authoritative match replication.
  This test uses the actual Relay service, not a local mock.
- 21 nonblank screenshot checks: gameplay, curved-trail gameplay, main menu, lobby, collection,
  roulette, and results at 1280x800, 430x900, and 932x430.
- Visual inspection of the smoothed turf, closer camera, minimap, portrait
  menu/gallery, and landscape gameplay. PNGs are in `Builds/Screenshots`.

## Online Configuration

The user's PaperTrails Unity Services project was created and Relay enabled:

`ee6f361f-8676-4b2e-a668-87487892d59c`

The host device owns the simulation. Relay carries DTLS-encrypted traffic;
it does not run bots, territory calculations, or the match timer. Players use
the lobby's short code instead of configuring port forwarding. LAN remains
available independently of Relay.

## Not Verified

- Physical Android installation/gameplay: the connected phone still reports
  `unauthorized` to ADB. The phone must accept the USB debugging prompt before
  automated installation and device testing can proceed.
- Android/iPhone cross-device gameplay, thermal/battery use, touch feel, and
  network impairment behavior.
- iOS builds/signing. These require the additional iOS toolchain and a Mac
  with Xcode for the final device build.

This is a functional prototype with procedural art and initial tuning. The
remaining production work is listed in the root README.
