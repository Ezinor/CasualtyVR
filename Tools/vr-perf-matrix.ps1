# Runs the dev copy under xr-sim once per configuration and reports steady-state fps after the world is
# ready. Used to attribute VR frame cost (Phase 1). "novr" is the control: same launch, VR never started.
[CmdletBinding()]
param([string[]]$Configs = @("novr", "baseline", "--cvr-dbg-noeyes"))
$ErrorActionPreference = "Stop"
foreach ($cfg in $Configs) {
    $gameArgs = [System.Collections.Generic.List[string]]::new()
    if ($cfg -ne "novr") { $gameArgs.Add("--cvr-vr") }
    foreach ($a in @("--cvr-autostart", "--cvr-seed", "1234", "--cvr-probe-delay", "10", "--cvr-quit")) { $gameArgs.Add($a) }
    if ($cfg -ne "novr" -and $cfg -ne "baseline") { foreach ($a in ($cfg -split " ")) { $gameArgs.Add($a) } }

    & (Join-Path $PSScriptRoot "launch-dev.ps1") -Windowed -XrSim -TimeoutSeconds 300 @($gameArgs.ToArray()) | Out-Null

    $log = Get-ChildItem (Join-Path $PSScriptRoot "..\Working\game-dev\CasualtyVR_Logs\Player-*.log") | Sort-Object LastWriteTime | Select-Object -Last 1
    $lines = Get-Content $log.FullName
    $probe = ($lines | Select-String "Report written: (.*probe-04-gameplay.txt)" | Select-Object -Last 1)
    $probeFps = if ($probe) { (Get-Content $probe.Matches[0].Groups[1].Value | Select-String "frameStats: .*= ([\d.]+) fps avg, worst frame ([\d.]+) ms").Matches[0].Value } else { "no probe report" }
    $ready = ($lines | Select-String "World ready" | Select-Object -First 1).LineNumber
    $fps = @(if ($ready) { $lines[$ready..($lines.Count - 1)] | Select-String "perf: ([\d.]+) fps" | Select-Object -Skip 1 | ForEach-Object { [double]$_.Matches[0].Groups[1].Value } })
    $median = if ($fps.Count) { ($fps | Sort-Object)[[int]($fps.Count / 2)] } else { "n/a" }
    "{0,-18} rig median {1,6} fps ({2} samples) | probe {3} | args: {4}" -f $cfg, $median, $fps.Count, $probeFps, ($gameArgs -join " ")
}
