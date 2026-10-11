# Builds the managed projects and copies them into the dev copy of the game.
# Never touches the user's Steam install.
[CmdletBinding()]
param(
    [string]$GameDir = (Join-Path $PSScriptRoot "..\Working\game-dev"),
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$GameDir = (Resolve-Path $GameDir).Path
if ($GameDir -like "*steamapps*") { throw "Refusing to deploy into a Steam install: $GameDir" }

dotnet build (Join-Path $root "src\CasualtyVR.sln") -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# Native OpenXR bridge (CMake; VS 2019 toolset - see CLAUDE.md for why not VS 2022).
$bridgeSrc = Join-Path $root "src\CasualtyVR.XrBridge"
$bridgeBuild = Join-Path $bridgeSrc "build"
if (-not (Test-Path (Join-Path $bridgeBuild "CMakeCache.txt"))) {
    cmake -S $bridgeSrc -B $bridgeBuild -G "Visual Studio 16 2019" -A x64 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Bridge configure failed" }
}
cmake --build $bridgeBuild --config Release -- /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Bridge build failed" }

$deploy = @(
    @{ From = "src\CasualtyVR.Sandbox.Runtime\bin\$Configuration\CasualtyVR.Sandbox.Runtime.dll"; To = "CasualtiesUnknown_Data\Managed" },
    @{ From = "src\CasualtyVR.Sandbox.Patcher\bin\$Configuration\CasualtyVR.Sandbox.Patcher.dll"; To = "BepInEx\patchers\CasualtyVR" },
    @{ From = "src\CasualtyVR.Probe\bin\$Configuration\CasualtyVR.Probe.dll"; To = "BepInEx\plugins\CasualtyVR" },
    @{ From = "src\CasualtyVR.Core\bin\$Configuration\CasualtyVR.Core.dll"; To = "BepInEx\plugins\CasualtyVR" },
    @{ From = "src\CasualtyVR.XrBridge\build\Release\CasualtyVR.XrBridge.dll"; To = "BepInEx\plugins\CasualtyVR\native" },
    @{ From = "src\CasualtyVR.XrBridge\build\Release\openxr_loader.dll"; To = "BepInEx\plugins\CasualtyVR\native" },
    @{ From = "src\CasualtyVR.Shaders\Bundles\casualtyvr_shaders"; To = "BepInEx\plugins\CasualtyVR" }
)
foreach ($d in $deploy) {
    $dest = Join-Path $GameDir $d.To
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $root $d.From) $dest -Force
    Write-Host "deployed $($d.From) -> $($d.To)"
}
