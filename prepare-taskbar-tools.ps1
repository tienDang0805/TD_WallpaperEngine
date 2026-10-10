$ErrorActionPreference='Stop'
$taskVersion='2026.2'
$taskSha='0DBE8E0255C20E131CDE536DCD0AE490D45989A7D360E26D0150DFF1922AC420'
$taskCache=Join-Path $PSScriptRoot 'artifacts/taskbar-tools'
$taskTarget=Join-Path $PSScriptRoot 'src/TD_Wallpaper.App/TaskbarTools/TranslucentTB'
New-Item -ItemType Directory -Path $taskCache,$taskTarget -Force | Out-Null
$taskZip=Join-Path $taskCache 'TranslucentTB-2026.2-x64.zip'
if(!(Test-Path -LiteralPath $taskZip) -or (Get-FileHash -LiteralPath $taskZip).Hash -ne $taskSha) {
 Invoke-WebRequest "https://github.com/TranslucentTB/TranslucentTB/releases/download/$taskVersion/TranslucentTB-portable-x64.zip" -OutFile $taskZip
}
if((Get-FileHash -LiteralPath $taskZip).Hash -ne $taskSha){throw 'TranslucentTB archive checksum mismatch.'}
Expand-Archive -LiteralPath $taskZip -DestinationPath $taskTarget -Force
if((Get-FileHash (Join-Path $taskTarget 'TranslucentTB.exe')).Hash -ne 'F933F5BF70405E13FEADBCC53883F2676B5A8A1DAE214B52C0FE0971C583A0FF'){throw 'TranslucentTB executable mismatch.'}
Invoke-WebRequest "https://raw.githubusercontent.com/TranslucentTB/TranslucentTB/$taskVersion/LICENSE.md" -OutFile (Join-Path $taskTarget 'LICENSE.md')
$taskFiles=@(Get-ChildItem -LiteralPath $taskTarget -File -Recurse | Where-Object { $_.Name -notin @('FILE-HASHES.json','settings.json','THIRD-PARTY.txt') } | ForEach-Object { [ordered]@{Path=[IO.Path]::GetRelativePath($taskTarget,$_.FullName).Replace('\','/');SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$taskFiles | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskTarget 'FILE-HASHES.json') -Encoding utf8
@"
TranslucentTB $taskVersion (unmodified x64 portable release)
Copyright TranslucentTB contributors. GPL-3.0; see LICENSE.md.
Project: https://github.com/TranslucentTB/TranslucentTB
Corresponding source: https://github.com/TranslucentTB/TranslucentTB/archive/refs/tags/$taskVersion.zip
Archive SHA256: $taskSha
Runs as a separate, app-owned helper on Windows 11. No separate installation or startup registration.
"@ | Set-Content (Join-Path $taskTarget 'THIRD-PARTY.txt') -Encoding utf8
Write-Output "Taskbar helper prepared: TranslucentTB $taskVersion"
