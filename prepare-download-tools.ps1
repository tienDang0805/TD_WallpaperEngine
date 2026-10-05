$ErrorActionPreference = 'Stop'
$toolDirectory = Join-Path $PSScriptRoot 'src\TienDang.App\DownloadTools'
$downloadDirectory = Join-Path $PSScriptRoot 'artifacts\download-tools'
New-Item -ItemType Directory -Force $toolDirectory, $downloadDirectory | Out-Null
function Get-VerifiedFile([string]$Uri, [string]$Destination, [string]$Hash) {
    if (-not (Test-Path -LiteralPath $Destination)) { Invoke-WebRequest -Uri $Uri -OutFile $Destination }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $Hash) { throw ('Checksum mismatch: ' + $Destination) }
}
Get-VerifiedFile 'https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe' (Join-Path $toolDirectory 'yt-dlp.exe') '66674953FE251B89F4D08C5F0E35E0728679BD67AB3D7D05C0562AF101DD3E7A'
$denoArchive = Join-Path $downloadDirectory 'deno-2.9.7.zip'
Get-VerifiedFile 'https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-pc-windows-msvc.zip' $denoArchive 'A0C3101B4158D1DFB7D6A78A7BF0F3DE80C96BB423C152BEEC8BEB22786F2238'
if (-not (Test-Path -LiteralPath (Join-Path $toolDirectory 'deno.exe'))) {
    $zip = [IO.Compression.ZipFile]::OpenRead($denoArchive)
    try { [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry('deno.exe'), (Join-Path $toolDirectory 'deno.exe'), $false) }
    finally { $zip.Dispose() }
}
if ((Get-FileHash -LiteralPath (Join-Path $toolDirectory 'deno.exe')).Hash -ne 'E020F3E232BD16E33768DEE528E5983349C962952051CED0A5D58AD42F5D9B33') { throw 'Unexpected deno.exe checksum.' }
$ffmpegArchive = Join-Path $PSScriptRoot 'artifacts\player-downloads\ffmpeg.7z'
Get-VerifiedFile 'https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261002/ffmpeg-x86_64-git-460cb0521.7z' $ffmpegArchive 'CFA365650B8EF7BD3802F0C69E5904F227D8A7B58445253E89BBF1890E7F4887'
if (-not (Test-Path -LiteralPath (Join-Path $toolDirectory 'ffmpeg.exe'))) {
    & tar -xf $ffmpegArchive -C $toolDirectory ffmpeg.exe
    if ($LASTEXITCODE -ne 0) { throw 'Cannot extract ffmpeg.exe.' }
}
if ((Get-FileHash -LiteralPath (Join-Path $toolDirectory 'ffmpeg.exe')).Hash -ne 'F39B47B36F100DA0F393DD9A6173B1C67BB351E51858B28F69293955D0A384F3') { throw 'Unexpected ffmpeg.exe checksum.' }
Write-Output 'YouTube download tools ready.'
