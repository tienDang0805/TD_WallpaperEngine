# Validation — 1.0.4

## 1.0.4 naming regression

- Renamed solution/project/namespace/resource paths build and publish with zero warnings/errors; core tests pass 23/23 groups.
- Naming checks pass: fresh TD_Wallpaper data folder, existing legacy library retained byte-for-byte, explicit --data-dir, old/new startup ownership, unrelated argument rejection, renamed assemblies and embedded English labels.
- Controller, cross-process desktop lease, real MPV transitions and four native video loops/pause/resume/cleanup pass.
- Taskbar preference/UI regression passes. Published TD_Wallpaper.exe passes all five isolated fresh/startup/existing/recovery cases; fresh manual data about 1,612 ms, startup about 3,267 ms under concurrent QA load.
- The personal installed app/library was not upgraded or modified. Inno compilation and archive content/hash checks validate packaging; physical legacy installer/startup migration remains open.

## Previous 1.0.3 validation

Checked on Windows 11 25H2 x64 (build 26200), 2026-10-10, .NET SDK 10.0.300.

## 1.0.3 checks

- Build: zero warnings/errors. Core suite: 23/23 groups, including the new taskbar preference in detached snapshots.
- Controller regression: pinned video looping with EOF rotation enabled, unpin policy updates, interval rotation, single-item looping, plus existing recovery/pause/ownership checks.
- Real MPV/presenter playback: four consecutive loops of a 1.2-second H.264 fixture, same media/renderer, no EOF rotation event, pause/resume, disabling native looping and process cleanup.
- Native transition suite: all covered handoff, invalid target, renderer reuse, EOF rotation, rapid selection and cleanup cases pass with the new loop policy.
- Taskbar preference: opt-in default, main-window checkbox, persisted snapshots, enable/disable, failure cleanup and duplicate-enable serialization. The rendered checkbox fits at the minimum 1040×690 window size.
- Real Windows 11 TranslucentTB helper: starts with the app-owned clear configuration, a conflicting second owner is rejected without stopping the first, and disable exits the owned helper. No unrelated TranslucentTB or Explorer process is terminated.
- Taskbar startup runs off the UI thread; disposing during a delayed backend startup cancels it rather than waiting the full startup timeout.
- Published EXE 1.0.3: 5/5 isolated library/startup/recovery cases, with four starter items for fresh libraries. Manual first data approximately 776 ms; startup approximately 3,304 ms. These are launch checks, not cold-boot benchmarks.

The 1.0.3 installer uses the previously tested install/uninstall flow with updated version guards and bundled helper files. Its full install/uninstall smoke was not repeated while the owner's installed 1.0.2 app was running; the installer intentionally asks for the current app to exit first. Portable content and helper pins are verified during packaging.

Physical Windows 10/classic taskbar, secondary-monitor taskbar, customized Explorer shells and taskbar recovery after a real Explorer restart remain unverified. The Computer Use window binding was stale during this run, so UI layout was checked through the native application's rendered test window rather than claiming a fresh physical-desktop screenshot.

## Previous 1.0.2 validation

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
