param([string]$Version='1.0.2', [string]$Compiler, [switch]$Force)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid version.'}
$taskSourceVersion=([xml](Get-Content (Join-Path $taskRoot 'src/TienDang.App/TienDang.App.csproj') -Raw)).Project.PropertyGroup.Version
if($taskSourceVersion -ne $Version){throw 'Source version mismatch; preserve historical releases.'}
$taskRelease=Join-Path $taskRoot ('artifacts/release-'+$Version)
$taskArtifacts=Join-Path $taskRoot 'artifacts'
$taskExe=Join-Path $taskRelease 'TienDang.Wallpaper.exe'
$taskArchive=Join-Path $taskArtifacts ("TD-WallpaperEngine-$Version-Portable.zip")
$taskSetup=Join-Path $taskArtifacts ("TD-WallpaperEngine-$Version-Setup.exe")
if(!(Test-Path -LiteralPath $taskExe) -or (Get-Item $taskExe).VersionInfo.ProductVersion -ne $Version){throw 'Publish a matching self-contained release first.'}
if(!$Force -and ((Test-Path $taskArchive) -or (Test-Path $taskSetup))){throw 'Artifacts exist; use -Force for this version only.'}
$taskDocuments=@('README.md','HUONG-DAN.txt','CHANGELOG.md','LICENSE','ASSET-LICENSES.md','VALIDATION.md')
foreach($document in $taskDocuments){Copy-Item -LiteralPath (Join-Path $taskRoot $document) -Destination $taskRelease -Force}
$taskDocsTarget=Join-Path $taskRelease 'docs'
New-Item -ItemType Directory -Force $taskDocsTarget | Out-Null
foreach($document in @('DEVELOPMENT.md','RELEASE-1.0.2.md')) { Copy-Item -LiteralPath (Join-Path $taskRoot ('docs/'+$document)) -Destination $taskDocsTarget -Force }
$taskImagesTarget=Join-Path $taskDocsTarget 'images'
New-Item -ItemType Directory -Force $taskImagesTarget | Out-Null
foreach($image in @('library.png','settings.png')) { Copy-Item -LiteralPath (Join-Path $taskRoot ('docs/images/'+$image)) -Destination $taskImagesTarget -Force }
$taskRequired=@('TienDang.Wallpaper.dll','TienDang.Core.dll','Player/mpv.exe','Player/THIRD-PARTY.txt','Player/LICENSE.GPL.txt','Player/LICENSE.LGPL.txt','DownloadTools/THIRD-PARTY.txt','DownloadTools/yt-dlp-LICENSE.txt','DownloadTools/Deno-LICENSE.txt','DownloadTools/FFmpeg-GPLv3.txt','StarterPack/galaxy-eye.mp4','StarterPack/water-fantasy.mp4','StarterPack/forest-valley.jpg','StarterPack/fortress-city.png','StarterPack/CREDITS.md')
foreach($file in $taskRequired){if(!(Test-Path -LiteralPath (Join-Path $taskRelease $file))){throw ('Missing distribution file: '+$file)}}
$taskPins=[ordered]@{
'Player/mpv.exe'='2924FF596AFD0352985B734B132F17F66564CC001E44D9E2E2CA7F5B5C16309B'
'DownloadTools/yt-dlp.exe'='66674953FE251B89F4D08C5F0E35E0728679BD67AB3D7D05C0562AF101DD3E7A'
'DownloadTools/deno.exe'='E020F3E232BD16E33768DEE528E5983349C962952051CED0A5D58AD42F5D9B33'
'DownloadTools/ffmpeg.exe'='F39B47B36F100DA0F393DD9A6173B1C67BB351E51858B28F69293955D0A384F3'
}
foreach($file in $taskPins.Keys){if((Get-FileHash -LiteralPath (Join-Path $taskRelease $file)).Hash -ne $taskPins[$file]){throw ('Tool checksum mismatch: '+$file)}}
$taskFiles=@(Get-ChildItem -LiteralPath $taskRelease -Recurse -File | Where-Object {$_.Extension -ne '.pdb' -and $_.Name -ne 'RELEASE-MANIFEST.json'})
if($taskFiles.FullName -match '[\\/](media|thumbnails|ValidationLogs|SourceContext)[\\/]|[\\/]library[^\\/]*\.json$'){throw 'Private runtime/source-context data cannot be packaged.'}
$taskRecords=@($taskFiles | Sort-Object FullName | ForEach-Object {[ordered]@{Path=[IO.Path]::GetRelativePath($taskRelease,$_.FullName).Replace('\','/');Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$taskUtf8=[Text.UTF8Encoding]::new($false)
$taskCommit=& git -c safe.directory=$taskRoot rev-parse HEAD 2>$null
if($LASTEXITCODE -ne 0){$taskCommit=$null}
$taskManifest=[ordered]@{Product='TD-WallpaperEngine';Version=$Version;Platform='win-x64';SelfContained=$true;Commit=$taskCommit;CreatedUtc=[DateTime]::UtcNow.ToString('O');Tools=$taskPins;Files=$taskRecords}
[IO.File]::WriteAllText((Join-Path $taskRelease 'RELEASE-MANIFEST.json'),($taskManifest | ConvertTo-Json -Depth 8),$taskUtf8)
$taskFiles+=Get-Item (Join-Path $taskRelease 'RELEASE-MANIFEST.json')
if(Test-Path $taskArchive){[IO.File]::Delete($taskArchive)}
$taskZip=[IO.Compression.ZipFile]::Open($taskArchive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in $taskFiles | Sort-Object FullName){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip,$file.FullName,[IO.Path]::GetRelativePath($taskRelease,$file.FullName).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal) | Out-Null}}finally{$taskZip.Dispose()}
$taskVerify=[IO.Compression.ZipFile]::OpenRead($taskArchive)
try{
if($taskVerify.Entries.Count -ne $taskFiles.Count){throw 'Archive entry count mismatch.'}
foreach($record in $taskRecords){
$entry=$taskVerify.GetEntry($record.Path)
if(!$entry -or $entry.Length -ne $record.Bytes){throw ('Archive file mismatch: '+$record.Path)}
$stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
try{$hash=[Convert]::ToHexString($sha.ComputeHash($stream))}finally{$stream.Dispose();$sha.Dispose()}
if($hash -ne $record.SHA256){throw ('Archive checksum mismatch: '+$record.Path)}
}
}finally{$taskVerify.Dispose()}
if(!$Compiler){$Compiler=(Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source}
if(!$Compiler -or !(Test-Path -LiteralPath $Compiler)){throw 'Supply an Inno Setup 6 ISCC.exe compiler with -Compiler.'}
& $Compiler ('/DVersion='+$Version) ('/DReleaseDir='+$taskRelease) ('/DOutputDir='+$taskArtifacts) (Join-Path $taskRoot 'installer/TD-WallpaperEngine.iss')
if($LASTEXITCODE -ne 0 -or !(Test-Path $taskSetup)){throw 'Installer compilation failed.'}
$taskUploads=@($taskSetup,$taskArchive)
foreach($demo in @('TD-WallpaperEngine-Demo.mp4','TD-WallpaperEngine-Demo.gif')){
$demoPath=Join-Path $taskArtifacts ('demo-'+$Version+'/'+$demo)
if(Test-Path $demoPath){$taskUploads+=$demoPath}
}
$taskSums=@($taskUploads | ForEach-Object {(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_)})
[IO.File]::WriteAllLines((Join-Path $taskArtifacts 'SHA256SUMS.txt'),$taskSums,$taskUtf8)
$taskResult=[ordered]@{Version=$Version;Commit=$taskCommit;Portable=$taskArchive;Setup=$taskSetup;VerifiedFileCount=$taskFiles.Count;ToolPinsVerified=$true;ZipContentVerified=$true;Files=@($taskUploads | ForEach-Object {@{Name=[IO.Path]::GetFileName($_);Bytes=(Get-Item $_).Length;SHA256=(Get-FileHash -LiteralPath $_).Hash}})}
[IO.File]::WriteAllText((Join-Path $taskArtifacts ('release-'+$Version+'-package-results.json')),($taskResult | ConvertTo-Json -Depth 6),$taskUtf8)
$taskResult | ConvertTo-Json -Depth 6
