param([string]$Version='1.0.2')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskBase=Join-Path ([IO.Path]::GetTempPath()) ('TD-installer-check-'+[Guid]::NewGuid().ToString('N'))
$taskInstall=Join-Path $taskBase 'app'
$taskData=Join-Path $taskBase 'library'
New-Item -ItemType Directory -Path $taskBase | Out-Null
$taskPersonal=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TienDangWallpaper/library.json'
$taskBefore=if(Test-Path $taskPersonal){(Get-FileHash -LiteralPath $taskPersonal).Hash}else{$null}
$taskRegistry='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$taskStartupBefore=(Get-ItemProperty -LiteralPath $taskRegistry -Name TienDangWallpaper -ErrorAction SilentlyContinue).TienDangWallpaper
function Run-Tool([string]$exe,[string[]]$arguments,[int]$timeoutSeconds=90){
    $start=[Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute=$false
    $start.CreateNoWindow=$true
    foreach($argument in $arguments){$start.ArgumentList.Add($argument)}
    $child=[Diagnostics.Process]::Start($start)
    try {
        if(!$child.WaitForExit($timeoutSeconds*1000)){$child.Kill($true);throw ('Test timeout: '+[IO.Path]::GetFileName($exe))}
        if($child.ExitCode -ne 0){throw ('Test exit '+$child.ExitCode+': '+[IO.Path]::GetFileName($exe))}
    } finally {$child.Dispose()}
}
$taskSetup=Join-Path $taskRoot ("artifacts/TD-WallpaperEngine-$Version-Setup.exe")
Run-Tool $taskSetup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR='+$taskInstall),('/LOG='+$taskBase+'/install.log'))
$taskApp=Join-Path $taskInstall 'TienDang.Wallpaper.exe'
if((Get-Item -LiteralPath $taskApp).VersionInfo.ProductVersion -ne $Version){throw 'Installed version mismatch.'}
Run-Tool $taskApp @('--smoke-test','--starter-smoke','--data-dir',$taskData) 30
$taskState=Join-Path $taskData 'library.json'
$taskSaved=Get-Content -LiteralPath $taskState -Raw | ConvertFrom-Json
if(@($taskSaved.Items).Count -ne 4 -or !(Test-Path -LiteralPath (Join-Path $taskData 'smoke-ok.txt'))){throw 'Installed EXE starter/UI smoke failed.'}
foreach($item in $taskSaved.Items){if(!(Test-Path -LiteralPath $item.Path)){throw 'Installed starter media missing.'}}
$taskDataHash=(Get-FileHash -LiteralPath $taskState).Hash
Run-Tool (Join-Path $taskInstall 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG='+$taskBase+'/uninstall.log'))
$taskClock=[Diagnostics.Stopwatch]::StartNew()
while((Test-Path -LiteralPath $taskApp) -and $taskClock.Elapsed.TotalSeconds -lt 30){Start-Sleep -Milliseconds 100}
if(Test-Path -LiteralPath $taskApp){throw 'Uninstaller left installed app EXE.'}
if((Get-FileHash -LiteralPath $taskState).Hash -ne $taskDataHash){throw 'Uninstall modified external library.'}
$taskAfter=if(Test-Path $taskPersonal){(Get-FileHash -LiteralPath $taskPersonal).Hash}else{$null}
$taskStartupAfter=(Get-ItemProperty -LiteralPath $taskRegistry -Name TienDangWallpaper -ErrorAction SilentlyContinue).TienDangWallpaper
if($taskBefore -ne $taskAfter -or $taskStartupBefore -ne $taskStartupAfter){throw 'Installation test changed personal library/startup.'}
$result=[ordered]@{Version=$Version;InstallPassed=$true;InstalledExeSmokePassed=$true;StarterItems=4;UninstallPassed=$true;ExternalLibraryPreserved=$true;PersonalLibraryPreserved=$true;StartupUnchanged=$true;Artifacts=$taskBase}
[IO.File]::WriteAllText((Join-Path $taskRoot ("artifacts/installer-smoke-$Version.json")),($result|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$result|ConvertTo-Json
