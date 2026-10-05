#ifndef Version
  #define Version "1.0.1"
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\artifacts\release-1.0.1"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
[Setup]
AppId=TDWallpaperEngine
AppName=TD-WallpaperEngine
AppVersion={#Version}
AppPublisher=TD-WallpaperEngine contributors
AppPublisherURL=https://github.com/tienDang0805/TD_WallpaperEngine
AppSupportURL=https://github.com/tienDang0805/TD_WallpaperEngine/issues
AppUpdatesURL=https://github.com/tienDang0805/TD_WallpaperEngine/releases
DefaultDirName={localappdata}\Programs\TD-WallpaperEngine
DefaultGroupName=TD-WallpaperEngine
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=TD-WallpaperEngine-{#Version}-Setup
SetupIconFile=..\src\TienDang.App\Assets\app.ico
UninstallDisplayIcon={app}\TienDang.Wallpaper.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
DisableDirPage=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
Name: startup; Description: "Start wallpaper with Windows"; Flags: unchecked

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\TD-WallpaperEngine"; Filename: "{app}\TienDang.Wallpaper.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\TD-WallpaperEngine"; Filename: "{app}\TienDang.Wallpaper.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TienDangWallpaper"; ValueData: """{app}\TienDang.Wallpaper.exe"" --startup --minimized"; Tasks: startup

[Run]
Filename: "{app}\TienDang.Wallpaper.exe"; Description: "Open TD-WallpaperEngine"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if (FindWindowByWindowName('TD-WallpaperEngine · 1.0.0') <> 0) or
     (FindWindowByWindowName('TD-WallpaperEngine · 1.0.1') <> 0) then
    Result := 'Exit TD-WallpaperEngine from its system tray menu before installing. Your library will be preserved.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TienDangWallpaper', StartupCommand) then
      if Pos('"' + ExpandConstant('{app}\TienDang.Wallpaper.exe') + '"', StartupCommand) = 1 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TienDangWallpaper');
end;
