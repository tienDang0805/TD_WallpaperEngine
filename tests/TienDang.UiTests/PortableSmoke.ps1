param([string]$ReleaseDirectory="artifacts/release-1.0.1")
$ErrorActionPreference='Stop'
$taskRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskExe=Join-Path $taskRoot (Join-Path $ReleaseDirectory 'TienDang.Wallpaper.exe')
$taskBase=Join-Path ([IO.Path]::GetTempPath()) ('TD-v1-smoke-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskBase | Out-Null
$taskResults=[Collections.Generic.List[object]]::new()
function Run-Case([string]$name,[string[]]$flags,[object]$state,[int]$count,[switch]$BackupOnly) {
    $taskData=Join-Path $taskBase $name
    if($null -ne $state){
        New-Item -ItemType Directory -Path $taskData | Out-Null
        $taskStatePath=Join-Path $taskData $(if($BackupOnly){'library.json.bak'}else{'library.json'})
        [IO.File]::WriteAllText($taskStatePath,($state | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    }
    $taskStart=[Diagnostics.ProcessStartInfo]::new($taskExe)
    $taskStart.UseShellExecute=$false
    foreach($taskArgument in @('--smoke-test','--starter-smoke','--data-dir',$taskData)+$flags){$taskStart.ArgumentList.Add($taskArgument)}
    $taskClock=[Diagnostics.Stopwatch]::StartNew()
    $taskProcess=[Diagnostics.Process]::Start($taskStart)
    $taskFirstData=$null
    try {
        while(!$taskProcess.HasExited -and $taskClock.Elapsed.TotalSeconds -lt 30){
            if($null -eq $state -and $null -eq $taskFirstData -and (Test-Path -LiteralPath $taskData)){$taskFirstData=$taskClock.Elapsed.TotalMilliseconds}
            if($name -eq 'startup' -and $taskClock.Elapsed.TotalMilliseconds -lt 2500 -and (Test-Path -LiteralPath $taskData)){throw 'Startup read data too early.'}
            Start-Sleep -Milliseconds 25
            $taskProcess.Refresh()
        }
        if(!$taskProcess.HasExited){throw ('Smoke timeout: '+$name)}
        if($taskProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $taskData 'smoke-ok.txt'))){throw ('EXE smoke failed: '+$name)}
        $taskSaved=Get-Content (Join-Path $taskData 'library.json') -Raw | ConvertFrom-Json
        if($taskSaved.Version -ne 1 -or @($taskSaved.Items).Count -ne $count){throw ('Library seed/migration mismatch: '+$name)}
        if($null -ne $state -and $name -eq 'old-settings' -and ($taskSaved.Settings.Language -ne 'en' -or $taskSaved.Settings.FrameRateLimit -ne 15)){throw 'Old settings changed.'}
        if($name -eq 'startup' -and ($taskFirstData -lt 2900 -or $taskFirstData -gt 9000)){throw 'Startup delay budget exceeded.'}
        if($name -eq 'fresh-manual' -and $taskFirstData -ge 2900){throw 'Manual launch inherited startup delay.'}
        foreach($taskItem in $taskSaved.Items){if(!(Test-Path -LiteralPath $taskItem.Path)){throw 'Starter copied media missing.'}}
        $taskResults.Add([ordered]@{Case=$name;Passed=$true;Items=@($taskSaved.Items).Count;FirstDataMilliseconds=$taskFirstData;TotalMilliseconds=$taskClock.Elapsed.TotalMilliseconds;DataDirectory=$taskData})
        Write-Output ('PASS EXE '+$name+'; items='+$count+'; first data ms='+$taskFirstData)
    } finally {if(!$taskProcess.HasExited){$taskProcess.Kill($true);$taskProcess.WaitForExit()};$taskProcess.Dispose()}
}
$taskEmpty=[ordered]@{Version=1;Items=@();Playlists=@();Settings=@{Language='en';FrameRateLimit=15;WasRunning=$false}}
Run-Case 'fresh-manual' @() $null 4
Run-Case 'startup' @('--startup','--minimized') $null 4
Run-Case 'existing-empty' @() $taskEmpty 0
Run-Case 'old-settings' @() $taskEmpty 0
Run-Case 'backup-only' @() $taskEmpty 0 -BackupOnly
[IO.File]::WriteAllText((Join-Path $taskRoot 'artifacts/v101-smoke.json'),($taskResults | ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
Write-Output ('PASS Portable EXE 5/5; renders: '+$taskBase)
