# TD_Wallpaper

Bring your Windows desktop to life with video wallpapers, or build a collection of images that changes throughout the day. TD_Wallpaper lets you preview, organize and apply your wallpapers from one place, with controls for playback, rotation and resource usage.

The app supports English and Vietnamese and runs independently of Steam and Wallpaper Engine.

[Download](#download) · [Demo](#demo) · [Features](#main-features) · [Report an issue](https://github.com/tienDang0805/TD_WallpaperEngine/issues)

## Download

Get the latest version from the [Releases page](https://github.com/tienDang0805/TD_WallpaperEngine/releases/latest).

| Download | Description |
| --- | --- |
| [Windows installer](https://github.com/tienDang0805/TD_WallpaperEngine/releases/download/v1.0.4/TD_Wallpaper-1.0.4-Setup.exe) | Installs the app for your Windows account and includes shortcuts and an uninstaller. |
| [Portable ZIP](https://github.com/tienDang0805/TD_WallpaperEngine/releases/download/v1.0.4/TD_Wallpaper-1.0.4-Portable.zip) | Extract the folder, then run `TD_Wallpaper.exe`. Keep the included files and folders together. |
| Source code | Available as ZIP and tar.gz on each release page. See [Build from source](#build-from-source) for instructions. |
| [SHA256 checksums](https://github.com/tienDang0805/TD_WallpaperEngine/releases/download/v1.0.4/SHA256SUMS.txt) | Verify the downloaded release files. |

A new library includes two video wallpapers and two fantasy images to try. Updating an existing installation keeps your library and settings.

## Requirements

- Windows 10 or Windows 11, 64-bit.
- A GPU and driver that support Direct3D 11. Hardware video decoding depends on the GPU and video format.
- Enough disk space for the app and your wallpaper collection.
- An internet connection to download wallpapers from URLs or YouTube.

The installer and portable package include the required runtime and media tools. You do not need to install .NET, mpv, yt-dlp, Deno or FFmpeg separately.

## Demo

See the app in use on a Windows desktop, including a wallpaper change through **Set as wallpaper**.

![Changing a wallpaper on the desktop](https://github.com/tienDang0805/TD_WallpaperEngine/releases/download/v1.0.2/TD-WallpaperEngine-Demo.gif)

[Watch the demo in 1080p](https://github.com/tienDang0805/TD_WallpaperEngine/releases/download/v1.0.2/TD-WallpaperEngine-Demo.mp4).

![Wallpaper library and video preview](docs/images/library.png)

## Main features

- **Image and video wallpapers.** Preview your collection before applying a wallpaper, with support for MP4 and WebM videos.
- **Easy imports.** Add files or folders, drag and drop media into the app, or download from a direct media URL or YouTube link. Downloads show progress and can be cancelled.
- **Collections and favorites.** Keep wallpapers organized and find the ones you use most.
- **Automatic rotation.** Choose an interval, schedule and target monitor. Shuffle starts with your selected wallpaper and avoids repeats until the round is complete.
- **Smoother video changes.** Keep the previous frame on screen while the next video loads. Preview shows a thumbnail until playback is ready.
- **Transparent taskbar.** Turn it on from the library header to show more of your wallpaper while keeping taskbar icons visible. Uncheck it to restore the taskbar background.
- **Playback controls.** A video set as wallpaper repeats automatically. Choose the display fit, volume and frame rate: original, 15, 30 or 60 FPS.
- **Resource management.** Pause playback for fullscreen or maximized apps, while on battery, or through application rules. An optional memory-saving mode releases the player while paused.
- **Library maintenance.** Locate missing files, reconnect moved folders, back up media and undo cleanup operations. Playback diagnostics help investigate problems.

![Playback and performance settings](docs/images/settings.png)

## Quick start

1. Install the app, or extract the portable ZIP and run `TD_Wallpaper.exe`.
2. Open the library and try a bundled wallpaper, or use **Add wallpaper** to import your own files or a URL.
3. Select a wallpaper to preview it, then choose **Set as wallpaper**.
4. To cycle through a collection, enable automatic rotation and choose the interval, shuffle order and target monitor.
5. Adjust playback and performance settings to suit your desktop.

Closing the main window sends the app to the system tray and keeps the wallpaper running. Use **Exit** from the tray menu to stop the app completely. Exit the previous version before installing an update.

New libraries are stored in `%LOCALAPPDATA%\TD_Wallpaper`. Existing installations continue using `%LOCALAPPDATA%\TienDangWallpaper`, so their media paths, settings and backups stay intact during updates and uninstall. Files imported without copying still use their original locations; enable library copying if you want to move or delete the originals later.

## Changelog

See the [full changelog](CHANGELOG.md) for version history and the [1.0.4 release notes](docs/RELEASE-1.0.4.md) for details of the latest changes and testing.

## Build from source

You will need Windows x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/tienDang0805/TD_WallpaperEngine.git
cd TD_WallpaperEngine
./build.ps1 -Portable -OutputDirectory artifacts/release-1.0.4
```

The build scripts download pinned media tools, verify their SHA256 checksums, run the core tests and publish the app. Source builds let you import your own wallpapers; bundled sample media is distributed with releases rather than stored in Git.

To include the sample wallpapers and produce an installer, provide the four-file starter pack and install [Inno Setup 6](https://jrsoftware.org/isdl.php):

```powershell
./build.ps1 -Portable -StarterPackDirectory "D:/StarterPack"
./package-release.ps1 -Version 1.0.4 -Compiler "C:/Path/To/Inno Setup 6/ISCC.exe"
```

The repository contains application source, tests, build scripts, UI assets and documentation. Build outputs, external executables, wallpaper libraries and recordings are excluded. See [DEVELOPMENT.md](docs/DEVELOPMENT.md) for the project structure, testing and release process.

## License

Application source and documentation are available under the [MIT License](LICENSE).

Artwork, bundled wallpapers and third-party tools retain their own licenses. See [ASSET-LICENSES.md](ASSET-LICENSES.md) and the [starter wallpaper credits](src/TD_Wallpaper.App/StarterPack/CREDITS.md) for attribution. Release packages include the applicable third-party license notices.
