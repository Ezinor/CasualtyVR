# Launches the dev copy of the game. Extra arguments go to the game
# (e.g. --cvr-autostart --cvr-seed 1234 --cvr-quit).
#
# Isolation from the user's real install has two independent layers:
#  1. Engine identity: the dev copy's globalgamemanagers has productName "CasualtyVRDevCopy"
#     (same-length byte patch, original kept as globalgamemanagers.orig). Unity therefore uses
#     LocalLow\Orsoniks\CasualtyVRDevCopy and HKCU\Software\Orsoniks\CasualtyVRDevCopy even if
#     BepInEx fails to load. This script refuses to launch without it.
#  2. Game sandbox: CasualtyVR.Sandbox.Patcher redirects the game's own save/PlayerPrefs calls
#     into <dev copy>\CasualtyVR_UserData.
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$GameDir = (Join-Path $PSScriptRoot "..\Working\game-dev"),
    [switch]$Windowed,
    [switch]$RenderDoc,
    # Headless VR: run against xr-sim (per-process runtime, Quest 3 + VDXR geometry by default)
    [switch]$XrSim,
    [string]$XrSimHeadset = (Join-Path $PSScriptRoot "..\tools\xr-sim\headsets\prey-quest3-vdxr.json"),
    # Record what the mod submits with the xr-tape API layer, then check the trace after exit
    [switch]$XrTape,
    [switch]$NoWait,
    [int]$TimeoutSeconds = 3600,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$GameArgs
)
$ErrorActionPreference = "Stop"
$GameDir = (Resolve-Path $GameDir).Path
if ($GameDir -like "*steamapps*") { throw "Refusing to launch the Steam install as the dev copy: $GameDir" }

$ggm = [IO.File]::ReadAllBytes((Join-Path $GameDir "CasualtiesUnknown_Data\globalgamemanagers"))
if ([Text.Encoding]::ASCII.GetString($ggm).IndexOf("CasualtyVRDevCopy") -lt 0) {
    throw "Dev copy still has the retail product name, so it would share the user's saves. Patch globalgamemanagers first (see docs/BASELINE_RECEIPT.md)."
}
foreach ($required in @("CasualtiesUnknown_Data\Managed\CasualtyVR.Sandbox.Runtime.dll",
                        "BepInEx\patchers\CasualtyVR\CasualtyVR.Sandbox.Patcher.dll")) {
    if (-not (Test-Path (Join-Path $GameDir $required))) { throw "Sandbox missing ($required); run tools\deploy-dev.ps1 first" }
}

$logDir = Join-Path $GameDir "CasualtyVR_Logs"
New-Item -ItemType Directory -Force $logDir | Out-Null
$playerLog = Join-Path $logDir ("Player-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

$argList = @("-logFile", "`"$playerLog`"")
if ($Windowed) { $argList += @("-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720") }
if ($GameArgs) { $argList += @($GameArgs | Where-Object { $_ }) }

$exe = Join-Path $GameDir "CasualtiesUnknown.exe"
if ($RenderDoc) {
    # Do not launch through renderdoccmd: it stops Doorstop from bootstrapping BepInEx. The
    # RenderDocPreload patcher loads renderdoc.dll before the D3D11 device exists, and the probe
    # triggers the capture through the in-app API.
    if ($argList -notcontains "--cvr-renderdoc") { $argList += "--cvr-renderdoc" }
}
$tools = Join-Path $PSScriptRoot "..\Tools"
$envNames = @("XR_RUNTIME_JSON", "XRSIM_DIR", "XRSIM_HEADSET_CONFIG", "XR_API_LAYER_PATH", "XR_ENABLE_API_LAYERS", "XRTAPE_DIR")
$savedEnv = @{}
foreach ($n in $envNames) { $savedEnv[$n] = [Environment]::GetEnvironmentVariable($n, "Process") }
$traceDir = $null
$traceStart = Get-Date
try {
    if ($XrSim) {
        $install = & (Join-Path $tools "xr-sim\tools\install-runtime.ps1") -Architecture x64
        $env:XR_RUNTIME_JSON = $install.Manifest
        $env:XRSIM_DIR = Join-Path $env:LOCALAPPDATA "xr-sim\casualtyvr"
        if ($XrSimHeadset) { $env:XRSIM_HEADSET_CONFIG = (Resolve-Path -LiteralPath $XrSimHeadset).Path }
        Write-Host "xr-sim: runtime $($install.Manifest), state $env:XRSIM_DIR, headset $env:XRSIM_HEADSET_CONFIG"
    }
    if ($XrTape) {
        $layerDir = Join-Path $env:LOCALAPPDATA "xr-tape\layer-x64"
        if (-not (Test-Path (Join-Path $layerDir "xrtape_api_layer.json"))) { throw "xr-tape not installed: run tools\xr-tape\tools\Install-XrTape.ps1 -Architecture x64" }
        $traceDir = Join-Path $GameDir ("CasualtyVR_Logs\xrtape-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
        New-Item -ItemType Directory -Force $traceDir | Out-Null
        $env:XR_API_LAYER_PATH = $layerDir
        $env:XR_ENABLE_API_LAYERS = "XR_APILAYER_XRTAPE_recorder"
        $env:XRTAPE_DIR = $traceDir
        Write-Host "xr-tape: traces -> $traceDir"
    }
    Write-Host "Launching $exe $($argList -join ' ')"
    $proc = Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory $GameDir -PassThru
} finally {
    foreach ($n in $envNames) { [Environment]::SetEnvironmentVariable($n, $savedEnv[$n], "Process") }
}
Write-Host "PID $($proc.Id); Player log: $playerLog"
if ($NoWait) { return }
if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
    Write-Warning "Still running after $TimeoutSeconds s (PID $($proc.Id))"
    exit 2
}
Write-Host "Exited with code $($proc.ExitCode)"

if ($XrTape) {
    # A run that recorded nothing proves nothing: require a new trace with a header record.
    $trace = Get-ChildItem $traceDir -Recurse -File -Filter *.ndjson -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $traceStart } | Sort-Object Length -Descending | Select-Object -First 1
    if (-not $trace) { throw "xr-tape wrote NO trace in ${traceDir}: the layer did not load (elevated shell, or the app never created an OpenXR instance)" }
    $header = Get-Content -LiteralPath $trace.FullName -TotalCount 1 | ConvertFrom-Json
    if ($header.r -ne "header") { throw "trace $($trace.FullName) has no header record" }
    Write-Host "xr-tape trace: $($trace.FullName) ($([math]::Round($trace.Length / 1KB)) KB)"
    $checkArgs = @((Join-Path $tools "xr-tape\tools\xrtape_check.py"), $trace.FullName)
    if ($XrSim) { $checkArgs += @("--expect-runtime", "xr-sim") }
    & python @checkArgs
    $checkExit = $LASTEXITCODE
    Write-Host "xrtape_check exit $checkExit"
    exit $checkExit
}
