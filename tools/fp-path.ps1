# First-person eye path (docs/BUILD_PLAN.md Phase 0 and 2; IP 9 judder, FP 8a walking, FP 8d vertical steadiness). In
# an open tunnel (--cvr-dbg-cave) the character stands, runs back and forth, walks on a half push and jumps, driven by
# xr-sim; the verbose "fp path" lines (FirstPerson.PathStats, per 2 s) are collected per segment. Dev copy only;
# restores the dev config afterwards (presets change it).
#
#   tools/fp-path.ps1                          the dev config's comfort settings
#   tools/fp-path.ps1 -Preset Moderate         a comfort preset (--cvr-dbg-preset)
#   tools/fp-path.ps1 -Extra --cvr-dbg-nofilter   without the velocity-led horizontal filter (Phase 0 behaviour)
#
# Columns: moving = frames with the body moving, and the eye's mean speed; steady-speed = frames with the body at over
# 80 % of its top speed; speed CV = how uneven the per-frame speed is there (0 = perfectly even) for the raw target
# (the game's own position) and the eye (after the velocity-led filter and smoothing); raw stalled = frames where the target did not
# moved the raw target; vertical: largest single-frame step, total up+down travel, net change.
[CmdletBinding()]
param([string]$Preset = "", [string[]]$Extra = @())
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$d = "$env:LOCALAPPDATA\xr-sim\casualtyvr"
$t = Join-Path $root "tools\xr-sim\tools"
$cfg = Join-Path $root "Working\game-dev\BepInEx\config\casualtyvr.core.cfg"
$cfgBackup = Join-Path $env:TEMP "casualtyvr.core.cfg.fp-path"
function C([string[]]$c) { & "$t\xrsim-cmd.ps1" -Dir $d -Quiet @c | Out-Null }

Copy-Item $cfg $cfgBackup -Force
try {
    Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force; Start-Sleep 2
    $started = Get-Date
    $gameArgs = @("--cvr-vr", "--cvr-autostart", "--cvr-seed", "1234", "--cvr-dbg-fp", "--cvr-dbg-cave") + $(if ($Preset) { @("--cvr-dbg-preset", $Preset) } else { @() }) + $Extra
    & (Join-Path $root "tools\launch-dev.ps1") -Windowed -XrSim -NoWait @gameArgs | Out-Null
    Start-Sleep 4
    $log = (Get-ChildItem (Join-Path $root "Working\game-dev\CasualtyVR_Logs\Player-*.log") | Where-Object { $_.CreationTime -ge $started.AddSeconds(-2) } | Sort-Object CreationTime | Select-Object -Last 1).FullName
    $deadline = (Get-Date).AddSeconds(180)
    while ((Get-Date) -lt $deadline -and -not (Select-String -Path $log -Pattern "Cave test: teleported" -Quiet)) { Start-Sleep 2 }
    Start-Sleep 6
    C @("head rot 0 0 0", "input clear")

    function Lines([int]$skip) { @(Select-String -Path $log -Pattern "fp path: (.*)" | Select-Object -Skip $skip | ForEach-Object { $_.Matches[0].Groups[1].Value }) }
    function Segment([string]$name, [scriptblock]$drive) {
        $n = @(Select-String -Path $log -Pattern "fp path:").Count
        & $drive
        C @("stick l center")
        Start-Sleep 2.5
        [pscustomobject]@{ Segment = $name; Path = (Lines $n) -join " || " }
    }

    $rows = @()
    $rows += Segment "stand" { Start-Sleep 4 }
    # The test tunnel is about 15 blocks long: run to the wall, turn round, run back, several times. Turning with the
    # stick flick works with snap turning; with smooth turning it turns about 90 degrees, then walking still follows.
    $rows += Segment "run (4 lengths)" { foreach ($i in 1..4) { C @("stick l 0 1"); Start-Sleep -Milliseconds 1100; C @("stick l center", "stick r 1 0"); Start-Sleep -Milliseconds 300; C @("stick r 0 0") } }
    $rows += Segment "walk, half push (2 lengths)" { foreach ($i in 1..2) { C @("stick l 0 0.5"); Start-Sleep -Milliseconds 3200; C @("stick l center", "stick r 1 0"); Start-Sleep -Milliseconds 300; C @("stick r 0 0") } }
    $rows += Segment "jumps" { C @("btn a press 120"); Start-Sleep 1.5 }
    $rows += Segment "jumps 2" { C @("btn a press 120"); Start-Sleep 1.5 }
    $on = (Select-String -Path $log -Pattern "First person: on \((.*)\)" | Select-Object -Last 1)
    Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force
    if ($on) { Write-Host ("Settings: " + $on.Matches[0].Groups[1].Value) }
    $rows | Format-List | Out-String -Width 400 | Write-Host
    Write-Host "Log: $log"
}
finally {
    Start-Sleep 1
    Copy-Item $cfgBackup $cfg -Force
}
