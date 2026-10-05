$ErrorActionPreference = "Stop"
$playerDirectory = Join-Path $PSScriptRoot "src\TienDang.App\Player"
$executable = Join-Path $playerDirectory "mpv.exe"
$executableHash = "2924FF596AFD0352985B734B132F17F66564CC001E44D9E2E2CA7F5B5C16309B"
if (Test-Path -LiteralPath $executable) {
    if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $executableHash) { throw "Unexpected mpv.exe checksum." }
    return
}
$downloadDirectory = Join-Path $PSScriptRoot "artifacts\player-downloads"
New-Item -ItemType Directory -Force $playerDirectory, $downloadDirectory | Out-Null
$archive = Join-Path $downloadDirectory "mpv.7z"
$archiveHash = "60C30AAC468937448298B8FD97C81C4825E1C81EB6F56A1AB5CBFD2A9BC13668"
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261002/mpv-x86_64-20261002-git-3186d369f9.7z" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveHash) { throw "Downloaded mpv archive checksum mismatch." }
& tar -xf $archive -C $playerDirectory mpv.exe
if ($LASTEXITCODE -ne 0) { throw "Cannot extract mpv.exe. Windows tar with 7z support is required." }
if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $executableHash) { throw "Extracted mpv.exe checksum mismatch." }