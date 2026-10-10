# Development context

## Structure

- src/TienDang.Core: schema-1 library, persistence, rotation/schedules, media validation, backup and download utilities.
- src/TienDang.App: WPF/localization, previews, native desktop attachment, controller, separate wallpaper worker/mpv, Windows policies and download queue.
- tests/TienDang.Tests: core/persistence/security regressions.
- tests/TienDang.UiTests: native UI/controller/transport/transition/ownership and portable smoke checks.
- tests/TienDang.VideoTests: GPU/codec diagnostics.
- prepare-player.ps1 and prepare-download-tools.ps1: pinned tools and integrity checks.
- build.ps1: build/core validation/self-contained publish.
- installer/TD-WallpaperEngine.iss and package-release.ps1: distribution packaging.
- .github/workflows/build.yml: code validation/artifacts, without personal or starter media.

## Lifecycle changes in 1.0.1

Data-directory mutexes protect each library. DesktopRenderLease separately protects each display across app copies/libraries. Workers acquire it before Explorer attachment. An abandoned mutex is recoverable; close releases it on the worker dispatcher thread. Native workers inside owned test windows bypass the actual desktop lease.

NativeDesktop.OtherRenderer detects an earlier visible wallpaper worker by title, Explorer-root ancestry and display bounds. This covers 1.0.0, which predates the mutex. Unrelated mpv instances and owned native test windows are excluded.

WallpaperWindow reports desktop-busy; PlayerHost throws a typed error. The controller suspends retries for that session, gives an actionable message and permits explicit retry after the other instance exits. It never kills a competing app.

The original 1.0.1 handoff used a GDI child and restarted MPV. A real desktop recording subsequently showed a remaining default-wallpaper gap; owned-window ordering/paint tests had not caught it.

## Renderer reuse in 1.0.2

The owner explicitly approved keeping the renderer and releasing its old decoder. WallpaperPresenter transfers the owned MpvPlayer to a new media generation, with one native video HWND and one subscription set per renderer. MPV `loadfile replace` unloads the prior media/decoder; image changes, release profiles, failures and shutdown still dispose the owned process. Do not restore the old kill-on-every-switch policy without reconsidering the transition regression.

MpvPlayer serializes media loads and waits for start-file -> file-loaded -> playback-restart, rather than the IPC acknowledgement. Playlist entry IDs filter end-file failures, per-media versions route events to their owner, and per-media load timings reset. EOF handlers recheck pending ownership before seeking, because rotation can start a replacement synchronously.

The outgoing screenshot is decoded on a reusable STA worker to at most 2,073,600 pixels. NativeFrameLayer uploads a display-sized DIB (64 MiB cap) to a layered DWM child and frees the staging DIB/DC immediately. The bridge remains until first-frame commit, including on a failed replacement. Managed exceptions must not cross its native window procedure.

Preview serializes selections, reuses renderer/surface between videos, retains a selected thumbnail until the decoded frame and asynchronously exits MPV before destroying HwndHost. Images decode to a finite budget while original dimensions remain metadata. Hide/dispose releases preview; restoring it starts paused. Late events from old selections must not change controls or release the current player.

Tests cover actual file-handle release across mixed codecs, same-PID replacements, latest selection, invalid media, EOF, shutdown and short-run resource bounds. Real desktop recording is a separate visual check: ordinary child-window visibility alone cannot establish physical Explorer continuity. Current validation and remaining coverage are in VALIDATION.md.

## Compatibility

Public name: TD-WallpaperEngine. Internal EXE/assembly: TienDang.Wallpaper. Data: %LOCALAPPDATA%/TienDangWallpaper. Startup value: TienDangWallpaper. Schema: 1.

Custom libraries never replace the personal startup entry. Only new libraries are seeded. Empty/backup-only libraries are preserved. Samples ship in distributions and are excluded from Git.

The serial download queue streams to disk and reuses import transactions. External tools use argument arrays and private kill-on-close jobs. Do not reintroduce shell-concatenated URLs or whole-file RAM buffers.

## Targeted tests

After preparing tools and building with .NET SDK 10 on Windows:

```powershell
dotnet tests/TienDang.Tests/bin/Release/net10.0/TienDang.Tests.dll
dotnet tests/TienDang.UiTests/bin/Release/net10.0-windows/TienDang.UiTests.dll $PWD --desktop-lease
dotnet tests/TienDang.UiTests/bin/Release/net10.0-windows/TienDang.UiTests.dll $PWD --controller
dotnet tests/TienDang.UiTests/bin/Release/net10.0-windows/TienDang.UiTests.dll $PWD --phase4
dotnet tests/TienDang.UiTests/bin/Release/net10.0-windows/TienDang.UiTests.dll $PWD "D:/Fixtures/short-h264.mp4" --transitions
dotnet tests/TienDang.UiTests/bin/Release/net10.0-windows/TienDang.UiTests.dll $PWD --renderer-reuse "D:/Fixtures/hardware-fixtures.json"
./tests/TienDang.UiTests/PortableSmoke.ps1 -ReleaseDirectory artifacts/release-1.0.2
```

Phase4 needs all four starter files locally. Transition EOF checks need a valid MP4 shorter than 12 seconds. Use isolated data and owned native test windows.

For demos: exit the active app first, launch a single packaged app, record the whole desktop, restore the personal library/app afterward and verify one worker. Inspect the entire recording before upload for unrelated window content.

## Release procedure

1. Increment versions; preserve older artifacts and complete targeted checks.
2. Publish with the credited starter folder outside Git.
3. Prepare public README/screenshots/demo/notes; review the source index for personal data.
4. Commit the validated source, then build Setup/Portable/checksums so the manifest points to that commit.
5. Test install/uninstall and library retention. Only after checks pass, tag the exact version and push main/tag. If source changes after packaging, rebuild packages against the new commit.
6. Create a GitHub Release and upload Setup, Portable, demo and checksums. GitHub generates source ZIP/tar.gz.
7. Verify remote commit/tag and asset checksums.
8. Open the default-library app once to refresh already-enabled startup; verify one desktop worker.

## Next QA

### 1.0.3 continuation

- `PlayerMessage.LoopVideo` is computed from pin state, EOF rotation and usable collection size. Set `loop-file=inf` before unpausing the committed media. Keep EOF events for multi-item EOF rotation; do not change shuffle order to implement looping.
- `TaskbarTransparency` serializes changes, performs helper verification/copy/start off the WPF thread and cancels warm-up on disposal. `MainWindow.Taskbar.cs` owns preference transactions and the main-screen checkbox. Closing to tray keeps the effect; Exit/disable restores it.
- Windows 11 uses the pinned, unmodified GPL-3.0 TranslucentTB 2026.2 helper in a private job with a per-session ownership lease. Config/files live in the selected library's toolsets folder. Never close/reconfigure an unrelated TranslucentTB instance. Windows 10 uses a separate classic composition backend and still needs physical QA.
- Prepare with `prepare-taskbar-tools.ps1`. Run UI checks with `--controller`, `--loops artifacts/loop-regression.mp4`, `--taskbar`; `--taskbar --taskbar-live` explicitly modifies/restores the physical taskbar briefly. The loop fixture must be short enough for four loops within twelve seconds.
- The user's installed 1.0.2 app/library/startup remain in place during this development run. Do not assume an upgrade has been installed just because new artifacts were published.


- [ ] Real cold boot and startup resource contention.
- [ ] Games/fullscreen/maximize under GPU load.
- [ ] Two physical monitors, mixed DPI, hotplug.
- [ ] Real sleep/lock/remote desktop/Explorer restart.
- [ ] 8-hour and 24-hour soak and resource/handle growth.
- [ ] More GPU/codec combinations.
