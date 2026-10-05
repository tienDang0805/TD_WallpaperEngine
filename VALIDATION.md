# Validation — 1.0.2

Checked on Windows x64, 2026-10-06, .NET SDK 10.0.300. This records completed checks, not certification of every Windows/GPU configuration. Unchanged ownership/phase-4 behavior retains the earlier 1.0.1 results below.

## Passed

- Release build and self-contained publish: zero warnings/errors.
- Core suite: 23/23 groups, including persistence, shuffle, streaming download validation, cancellation, YouTube routing and argument isolation.
- Native transitions: first-frame commit, same-PID video replacement, image renderer release, retained layered frame on failed replacement, rapid loads, EOF and disposal.
- Native preview: renderer reuse, first-frame readiness, real playback/seek, rapid selection, image/failure/hide cleanup and paused restore.
- 48 video replacements: one renderer; hardware decoding retained, app warm-to-end private-memory median delta -2.79 MiB and handles -6; renderer memory/handle growth within short-run limits.
- 20 mixed-codec replacements: H.264/HEVC/VP9/AV1, correct active media and codec, first frame/hardware output, one PID, exclusive access to each outgoing file and final process cleanup.
- Real desktop handoff inspected at 30 fps: no default-wallpaper gap in the captured switch. Two 4K media first-frame decode measurements approximately 57/67 ms, excluding held-frame preparation. Public demo contains the actual app and desktop; raw recordings/window-only clips are excluded.
- Desktop ownership: cross-process exclusion, independent displays, abandoned-lease recovery, legacy-conflict rejection and release without leaks. A running 1.0.0 desktop worker was detected on the physical display.
- Controller: a desktop conflict produces one failed attempt and an actionable notice; timer ticks do not restart the competing renderer; explicit retry can recover.
- Native phase-4 checks: four owned starter assets, deduplication, distinct mascots, transparent library-logo exterior, English/Vietnamese labels, serial download/import, retry/cancel cleanup and verified toolset staging/rollback.
- Memory-saving profile: the actual owned worker and decoder exited; decoder private memory was approximately 228 MiB before release and zero after that process exited. D3D11VA was observed on this machine; resume restored the same item. This is an owned-process measurement, not total system/GPU memory.
- Published EXE 1.0.2: 5/5 isolated cases (new library, startup, existing empty library, preserved old settings, backup-only recovery). Manual first data: approximately 914 ms; startup: approximately 3,206 ms. Existing empty libraries remained empty. These are isolated launches, not a cold-boot benchmark.
- Installer: silent per-user installation into an isolated temporary directory, installed EXE/UI smoke, four playable starter files, uninstall, external library retention and unchanged personal library/startup.
- README images: captured from the current native UI after real thumbnails finished.

## Not completed

- Physical cold boot, game/fullscreen load, mixed-DPI multi-monitor/hotplug, real sleep/lock/Explorer recovery, and 8/24-hour soak.
- Broad GPU/codec compatibility; hardware decoding remains dependent on the actual GPU/codec.
- Live YouTube availability for every URL; automated process/download safety tests do not guarantee third-party service availability.

## Reproduce

See [developer context](docs/DEVELOPMENT.md) for preparation, test commands and release procedure. Portable smoke uses fresh temporary libraries and does not modify the personal library. Installer smoke accepts -Version and consumes the matching installer in artifacts.

Private logs, local media and recordings are kept outside Git.
