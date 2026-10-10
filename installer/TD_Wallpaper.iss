#ifndef Version
  #define Version "1.0.4"
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\artifacts\release-1.0.4"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
[Setup]
AppId=TDWallpaperEngine
AppName=TD_Wallpaper
AppVersion={#Version}
AppPublisher=TD_Wallpaper contributors
AppPublisherURL=https://github.com/tienDang0805/TD_WallpaperEngine
AppSupportURL=https://github.com/tienDang0805/TD_WallpaperEngine/issues
AppUpdatesURL=https://github.com/tienDang0805/TD_WallpaperEngine/releases
DefaultDirName={localappdata}\Programs\TD_Wallpaper
DefaultGroupName=TD_Wallpaper
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=TD_Wallpaper-{#Version}-Setup
SetupIconFile=..\src\TD_Wallpaper.App\Assets\app.ico
UninstallDisplayIcon={app}\TD_Wallpaper.exe
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

[InstallDelete]
Type: files; Name: "{app}\TienDang.Wallpaper.exe"
Type: files; Name: "{app}\TienDang.Wallpaper.dll"
Type: files; Name: "{app}\TienDang.Wallpaper.deps.json"
Type: files; Name: "{app}\TienDang.Wallpaper.runtimeconfig.json"
Type: files; Name: "{app}\TienDang.Core.dll"
Type: files; Name: "{app}\TienDang.Wallpaper.pdb"
Type: files; Name: "{app}\TienDang.Core.pdb"
Type: files; Name: "{group}\TD-WallpaperEngine.lnk"
Type: files; Name: "{autodesktop}\TD-WallpaperEngine.lnk"

[Icons]
Name: "{group}\TD_Wallpaper"; Filename: "{app}\TD_Wallpaper.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\TD_Wallpaper"; Filename: "{app}\TD_Wallpaper.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TD_Wallpaper"; ValueData: """{app}\TD_Wallpaper.exe"" --startup --minimized"; Tasks: startup

[Run]
Filename: "{app}\TD_Wallpaper.exe"; Description: "Open TD_Wallpaper"; Flags: nowait postinstall skipifsilent

[Code]
var LegacyStartupCommand: String;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TienDangWallpaper', LegacyStartupCommand);
  if (FindWindowByWindowName('TD-WallpaperEngine · 1.0.0') <> 0) or
     (FindWindowByWindowName('TD-WallpaperEngine · 1.0.1') <> 0) or
     (FindWindowByWindowName('TD-WallpaperEngine · 1.0.2') <> 0) or
     (FindWindowByWindowName('TD-WallpaperEngine · 1.0.3') <> 0) or
     (FindWindowByWindowName('TD_Wallpaper · {#Version}') <> 0) then
    Result := 'Exit TD_Wallpaper from its system tray menu before installing. Your library will be preserved.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var OldExecutable: String;
begin
  OldExecutable := '"' + ExpandConstant('{app}\TienDang.Wallpaper.exe') + '"';
  if (CurStep = ssPostInstall) and (Pos(OldExecutable, LegacyStartupCommand) = 1) then
  begin
    StringChangeEx(LegacyStartupCommand, OldExecutable, '"' + ExpandConstant('{app}\TD_Wallpaper.exe') + '"', True);
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TD_Wallpaper', LegacyStartupCommand);
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TienDangWallpaper');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TD_Wallpaper', StartupCommand) then
      if Pos('"' + ExpandConstant('{app}\TD_Wallpaper.exe') + '"', StartupCommand) = 1 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TD_Wallpaper');
end;
