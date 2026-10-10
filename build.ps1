param([switch]$Portable, [string]$OutputDirectory = "artifacts/release-1.0.3", [string]$StarterPackDirectory)
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    & (Join-Path $PSScriptRoot "prepare-player.ps1")
    & (Join-Path $PSScriptRoot "prepare-download-tools.ps1")
    & (Join-Path $PSScriptRoot "prepare-taskbar-tools.ps1")
    $privateSdk = Join-Path $PSScriptRoot "artifacts/toolchain/dotnet.exe"
    $sdk = if (Test-Path -LiteralPath $privateSdk) { $privateSdk } else { (Get-Command dotnet).Source }
    & $sdk build TienDangWallpaper.slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    & $sdk tests/TienDang.Tests/bin/Release/net10.0/TienDang.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw "Core tests failed." }
    if ($Portable) {
        & $sdk publish src/TienDang.App/TienDang.App.csproj -c Release -r win-x64 --self-contained true -o $OutputDirectory --nologo
        if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
        if ($StarterPackDirectory) {
            $starterTarget = Join-Path $OutputDirectory "StarterPack"
            New-Item -ItemType Directory -Force $starterTarget | Out-Null
            foreach ($sample in @("galaxy-eye.mp4","water-fantasy.mp4","forest-valley.jpg","fortress-city.png")) {
                Copy-Item -LiteralPath (Join-Path $StarterPackDirectory $sample) -Destination $starterTarget -Force
            }
        }
        foreach ($document in @("HUONG-DAN.txt","README.md","CHANGELOG.md","LICENSE","ASSET-LICENSES.md")) {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $OutputDirectory -Force
        }
        Write-Output ("Portable app: " + (Join-Path $OutputDirectory "TienDang.Wallpaper.exe"))
    }
}
finally { Pop-Location }
