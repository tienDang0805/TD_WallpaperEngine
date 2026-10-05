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

WallpaperPresenter captures at most 2,073,600 outgoing pixels into a temporary PNG, decodes a detached bitmap and synchronously paints a native GDI child before old MPV exit. The same video HWND is reused for the replacement, without overlapping decoders. A failed replacement retains the held frame. Buffers are cleared after commit and the native frame window is destroyed on disposal. Still-image candidates retain their existing WPF path.

Tests verify ownership, native paint, window ordering and process-exit-before-start. Full desktop recording remains a separate visual check; its current status is in VALIDATION.md.

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
./tests/TienDang.UiTests/PortableSmoke.ps1 -ReleaseDirectory artifacts/release-1.0.1
```

Phase4 needs all four starter files locally. Transition EOF checks need a valid MP4 shorter than 12 seconds. Use isolated data and owned native test windows.

For demos: exit the active app first, launch a single packaged app, record the whole desktop, restore the personal library/app afterward and verify one worker. Inspect the entire recording before upload for unrelated window content.

## Release procedure

1. Increment versions; preserve older artifacts and complete targeted checks.
2. Publish with the credited starter folder outside Git.
3. Prepare public README/screenshots/demo/notes; review the source index for personal data.
4. Build Setup/Portable/checksums; test install/uninstall and library retention.
5. Commit source and tag the exact version; push main and tag to the owner repository.
6. Create a GitHub Release and upload Setup, Portable, demo and checksums. GitHub generates source ZIP/tar.gz.
7. Verify remote commit/tag and asset checksums.
8. Open the default-library app once to refresh already-enabled startup; verify one desktop worker.

## Next QA

- [ ] Real cold boot and startup resource contention.
- [ ] Games/fullscreen/maximize under GPU load.
- [ ] Two physical monitors, mixed DPI, hotplug.
- [ ] Real sleep/lock/remote desktop/Explorer restart.
- [ ] 8-hour and 24-hour soak and resource/handle growth.
- [ ] More GPU/codec combinations.
