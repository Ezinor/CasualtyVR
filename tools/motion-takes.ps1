# Motion takes (docs/BUILD_PLAN.md Phase 4a, FIRSTPERSON_REVIEW FP 9f, playbook TEST-027): replay the swing and throw
# recipes (CasualtyVR.HandMotion) over hand motion, headless, and check what they find. Dev copy only; VR is not
# started (--cvr-dbg-replay runs at plugin start).
#
#   tools/motion-takes.ps1                    generated takes (swings, ordinary moves, throws, drops)
#   tools/motion-takes.ps1 -Takes <folder>    takes recorded in the headset (VR settings > Hands & body > More settings >
#                                             Record a motion take), e.g. the game's BepInEx\CasualtyVR\takes (read only)
#
# Expected per take label: Swings = about 5 swings and no throws; Throws = about 5 throws and no swings; Other = no
# swings and no throws. Generated: exactly 5 swings / 0 / 4 throws with 4 swings cancelled / 3 drops.
[CmdletBinding()]
param([string]$Takes = "")
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$results = New-Object System.Collections.Generic.List[object]
function Check([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Take = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Detail = $detail })
}

$what = "synthetic"
if ($Takes) {
    # Copy them next to the dev copy, so the replay never reads outside it.
    $dest = Join-Path $root "Working\motion-takes"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $Takes "*.csv") $dest -Force
    $what = $dest
}
Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force; Start-Sleep 2
$started = Get-Date
& (Join-Path $root "tools\launch-dev.ps1") -Windowed -NoWait --cvr-dbg-replay $what | Out-Null
$log = $null
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $log = (Get-ChildItem (Join-Path $root "Working\game-dev\CasualtyVR_Logs\Player-*.log") | Where-Object { $_.CreationTime -ge $started.AddSeconds(-2) } | Sort-Object CreationTime | Select-Object -Last 1).FullName
    if ($log -and (Select-String -Path $log -Pattern "Motion replay: done|Motion replay failed|no takes in" -Quiet)) { break }
}
Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force

$lines = @(Select-String -Path $log -Pattern "Motion replay: '([^']+)' \(([^,]+), (\d+) samples\): (\d+) swings, (\d+) throws \(peak ([\d.]+) m/s\), (\d+) drops, (\d+) swings cancelled")
Check "replay ran" ($lines.Count -gt 0) ((Select-String -Path $log -Pattern "Motion replay" | Select-Object -Last 1).Line)
foreach ($l in $lines) {
    $g = $l.Matches[0].Groups
    $name = $g[1].Value; $label = $g[2].Value
    $swings = [int]$g[4].Value; $throws = [int]$g[5].Value; $drops = [int]$g[7].Value; $cancel = [int]$g[8].Value
    $detail = "$swings swings, $throws throws (peak $($g[6].Value) m/s), $drops drops, $cancel cancelled; $($g[3].Value) samples"
    switch -Regex ($name) {
        "^synthetic-swings" { Check $name ($swings -eq 5 -and $throws -eq 0) $detail }
        "^synthetic-other" { Check $name ($swings -eq 0 -and $throws -eq 0) $detail }
        "^synthetic-throws" { Check $name ($throws -eq 4 -and $swings -eq 0 -and $cancel -eq 4) $detail }
        "^synthetic-drops" { Check $name ($drops -eq 3 -and $throws -eq 0) $detail }
        default {
            switch ($label) {
                "swings" { Check $name ($swings -ge 4 -and $swings -le 6 -and $throws -eq 0) $detail }
                "throws" { Check $name ($throws -ge 4 -and $swings -eq 0) $detail }
                default { Check $name ($swings -eq 0 -and $throws -eq 0) $detail }
            }
        }
    }
}
$results | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Host
$fails = @($results | Where-Object { $_.Result -eq "FAIL" }).Count
Write-Host "$fails failure(s). Log: $log"
exit $fails
