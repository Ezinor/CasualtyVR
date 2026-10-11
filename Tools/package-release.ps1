# Builds the player release: dist\CasualtyVR-<version>.zip, laid out to extract straight into the game folder.
# Contains only what players need: CasualtyVR.Core.dll and the native OpenXR bridge + loader, with the README,
# changelog and licence notices. Not included (developer tooling): the probe plugin, the sandbox patcher/runtime.
[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

$version = (Select-String -Path (Join-Path $root "src\CasualtyVR.Core\Plugin.cs") -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "deploy-dev.ps1") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

$stage = Join-Path $root "dist\CasualtyVR-$version"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$plugin = Join-Path $stage "BepInEx\plugins\CasualtyVR"
New-Item -ItemType Directory -Force (Join-Path $plugin "native") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $stage "CasualtyVR-docs\licenses") | Out-Null

Copy-Item (Join-Path $root "src\CasualtyVR.Core\bin\Release\CasualtyVR.Core.dll") $plugin
Copy-Item (Join-Path $root "src\CasualtyVR.XrBridge\build\Release\CasualtyVR.XrBridge.dll") (Join-Path $plugin "native")
Copy-Item (Join-Path $root "src\CasualtyVR.XrBridge\build\Release\openxr_loader.dll") (Join-Path $plugin "native")
Copy-Item (Join-Path $root "src\CasualtyVR.Shaders\Bundles\casualtyvr_shaders") $plugin
foreach ($doc in "README.md", "CHANGELOG.md", "THIRD_PARTY_NOTICES.md", "LICENSE.txt") {
    Copy-Item (Join-Path $root "release\$doc") (Join-Path $stage "CasualtyVR-docs")
}
Copy-Item (Join-Path $root "third_party\openxr-1.1.63\share\doc\openxr\LICENSE") (Join-Path $stage "CasualtyVR-docs\licenses\OpenXR-LICENSE.txt")

$zip = Join-Path $root "dist\CasualtyVR-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Set-Content -Path "$zip.sha256" -Value "$hash  CasualtyVR-$version.zip" -Encoding ascii
Get-ChildItem -Recurse $stage | Where-Object { -not $_.PSIsContainer } | ForEach-Object { $_.FullName.Substring($stage.Length + 1) + "  " + $_.Length }
"zip: $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)  sha256 $hash"
