# Release 1.0.1

First public GitHub release, including fixes following the local 1.0.0 milestone.

## What's changed

### Fixed desktop playback

- Paint the outgoing video frame in a reusable native GDI layer before retiring the decoder. Hold it until the replacement is ready.
- Reuse one video HWND; wait for old decoder exit before starting its replacement.
- Per-display desktop ownership across app copies and data directories. Detect the uncoordinated 1.0.0 renderer before touching Explorer.
- English/Vietnamese conflict notices and explicit retry instead of restarting a competing renderer.

### Updated interface

- Wallpaper-card mascot in the upper sidebar, laptop at the bottom, empty-box mascot for empty states.
- Transparent library mascot exterior with internal scenic artwork retained.
- Larger laptop artwork and consistent version labels.

### Distribution

- Per-user Setup.exe and self-contained Portable.zip.
- Source-only Git, build/installer scripts, docs and native interface screenshots.
- Requirements, MIT source license and separate media/tool notices.

## Features

Image/video preview, MP4/WebM, local/folder/drop/URL/YouTube import, progress/cancel/retry, collections/favorites/schedules/multi-monitor rotation, shuffle from selection, EN-VI, FPS options, hardware-decoding preference, Windows activity/power policies, backup, relinking, diagnostics and verified downloader updates.

## Validation

See [VALIDATION.md](../VALIDATION.md) and [developer context](DEVELOPMENT.md). Automated owned-window tests do not certify physical multi-monitor/mixed-DPI, real cold boot/sleep/lock/Explorer recovery or 8/24-hour soak tests; those remain open.

## Assets

- TD-WallpaperEngine-1.0.1-Setup.exe
- TD-WallpaperEngine-1.0.1-Portable.zip
- Full desktop demo: pending recording and review; the earlier window-only recording is excluded.
- SHA256SUMS.txt
- Source code ZIP/tar.gz generated automatically by GitHub for v1.0.1.
