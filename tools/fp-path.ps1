# First-person eye path (docs/BUILD_PLAN.md Phase 0; IP 9 judder, FP 8d vertical steadiness). In an open tunnel
# (--cvr-dbg-cave) the character stands, walks, runs back and jumps, driven by xr-sim; the verbose "fp path" lines
# (FirstPerson.PathStats, per 2 s) are collected per segment. Dev copy only.
#
#   tools/fp-path.ps1
#
# Columns: steady-speed = frames at over 80 % of top speed; speed CV = how uneven the per-frame speed is there
# (0 = perfectly even; physics at 50 Hz under a 90 Hz view gives uneven steps); stalled = frames where the head limb
# did not move although the eye did; vertical: largest single-frame step, total up+down travel, net change.
[CmdletBinding()]
param()
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$d = "$env:LOCALAPPDATA\xr-sim\casualtyvr"
$t = Join-Path $root "tools\xr-sim\tools"
function C([string[]]$c) { & "$t\xrsim-cmd.ps1" -Dir $d -Quiet @c | Out-Null }

Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force; Start-Sleep 2
$started = Get-Date
& (Join-Path $root "tools\launch-dev.ps1") -Windowed -XrSim -NoWait --cvr-vr --cvr-autostart --cvr-seed 1234 --cvr-dbg-fp --cvr-dbg-cave | Out-Null
Start-Sleep 4
$log = (Get-ChildItem (Join-Path $root "Working\game-dev\CasualtyVR_Logs\Player-*.log") | Where-Object { $_.CreationTime -ge $started.AddSeconds(-2) } | Sort-Object CreationTime | Select-Object -Last 1).FullName
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline -and -not (Select-String -Path $log -Pattern "Cave test: teleported" -Quiet)) { Start-Sleep 2 }
Start-Sleep 6
C @("head rot 0 0 0", "input clear")

function Segment([string]$name, [string[]]$cmds, [double]$seconds) {
    $n = @(Select-String -Path $log -Pattern "fp path:").Count
    foreach ($c in $cmds) { C @($c) }
    Start-Sleep -Milliseconds ([int]($seconds * 1000))
    C @("stick l center")
    Start-Sleep 2.5
    $lines = @(Select-String -Path $log -Pattern "fp path: (.*)" | Select-Object -Skip $n)
    $perf = @(Select-String -Path $log -Pattern "frame p50 ([\d.]+) p99 ([\d.]+) ms, over 1.5x period (\d+)/(\d+)" | Select-Object -Last 2)
    [pscustomobject]@{
        Segment = $name
        Path = ($lines | ForEach-Object { $_.Matches[0].Groups[1].Value }) -join " || "
        Frames = ($perf | ForEach-Object { "p99 {0} ms, missed {1}/{2}" -f $_.Matches[0].Groups[2].Value, $_.Matches[0].Groups[3].Value, $_.Matches[0].Groups[4].Value }) -join "; "
    }
}

$rows = @()
$rows += Segment "stand" @() 4
# The test tunnel is about 15 blocks long: run to the wall, flick round, run back, several times.
$n0 = @(Select-String -Path $log -Pattern "fp path:").Count
foreach ($i in 1..4) { C @("stick l 0 1"); Start-Sleep -Milliseconds 1100; C @("stick l center", "stick r 1 0"); Start-Sleep -Milliseconds 300; C @("stick r 0 0") }
Start-Sleep 2.5
$rows += [pscustomobject]@{
    Segment = "runs (4 lengths)"
    Path = (@(Select-String -Path $log -Pattern "fp path: (.*)" | Select-Object -Skip $n0) | ForEach-Object { $_.Matches[0].Groups[1].Value }) -join " || "
    Frames = "-"
}
$rows += Segment "jumps" @("btn a press 120") 1.5
$rows += Segment "jumps 2" @("btn a press 120") 1.5
Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force
$rows | Format-List | Out-String -Width 300 | Write-Host
Write-Host "Log: $log"
