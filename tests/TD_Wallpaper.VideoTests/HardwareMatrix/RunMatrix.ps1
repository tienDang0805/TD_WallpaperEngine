param([string]$FixtureFile,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if(!$FixtureFile){$FixtureFile=Join-Path $root 'artifacts/stage3-hardware-fixtures.json'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $env:TEMP ('TD_Wallpaper-hardware-run-'+[Guid]::NewGuid().ToString('N'))}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$taskOutput=Join-Path $OutputDirectory 'matrix.json';$taskGpu=Join-Path $OutputDirectory 'gpu.jsonl';$taskMarker=Join-Path $OutputDirectory 'stage3-hardware-active.json'
if((Test-Path $taskOutput) -or (Test-Path $taskGpu)){throw 'Choose an unused output directory to retain previous evidence.'}
$taskStop=Join-Path $env:TEMP ('TD_Wallpaper-gpu-stop-'+[Guid]::NewGuid().ToString('N'))
$sdk=Join-Path $root 'artifacts/toolchain/dotnet.exe';if(!(Test-Path $sdk)){$sdk=(Get-Command dotnet).Source}
$taskPwsh=(Get-Process -Id $PID).Path
$taskScript=Join-Path $PSScriptRoot 'MeasureOwnedGpu.ps1'
$taskMonitor=Start-Process -FilePath $taskPwsh -ArgumentList @('-NoProfile','-File',('"'+$taskScript+'"'),'-Marker',('"'+$taskMarker+'"'),'-Output',('"'+$taskGpu+'"'),'-StopFile',('"'+$taskStop+'"')) -WindowStyle Hidden -PassThru
try{
    & $sdk (Join-Path $root 'tests/TD_Wallpaper.VideoTests/bin/Release/net10.0-windows/TD_Wallpaper.VideoTests.dll') --hardware-matrix $FixtureFile $taskOutput | Tee-Object -FilePath (Join-Path $OutputDirectory 'matrix.txt')
    $taskExit=$LASTEXITCODE
}finally{
    [IO.File]::WriteAllText($taskStop,'stop')
    if(!$taskMonitor.WaitForExit(12000)){$taskMonitor.Kill();$taskMonitor.WaitForExit()}
    $taskMonitor.Dispose()
}
if($taskExit -ne 0){throw "Matrix failed: $taskExit. Raw evidence: $OutputDirectory"}
"Hardware matrix evidence: $OutputDirectory"