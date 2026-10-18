# First-person smoke test (docs/FIRSTPERSON_PLAN.md, FP-6): drives xr-sim's head and hands through the
# first-person interactions and checks the game's own results in the log. Dev copy only (tools/launch-dev.ps1).
#
#   tools/fp-smoke.ps1            all scenarios; exit code = number of failures
#
# Scenarios (each a fresh game, seed 1234, first person forced with --cvr-dbg-fp):
#   grab-throw   right hand down to a spawned water bottle, grip -> picked up; release while moving -> thrown
#   chest-store  holding a bottle: hand to the chest, press/release -> stored in an inventory slot; empty hand at
#                the chest, grip -> inventory radial on the panel
#   mouth        holding a bottle at the mouth -> used (drink), with a vibration pulse accepted by the runtime
#   touch        keypad minigame: the hand on the panel moves the mouse (centre, right)
#   swing        fast right-hand swings in a cave -> swing attacks
#   lockpick     lockpicking by hand: the fingertip is the click point; touching the ring + trigger engages the pick
[CmdletBinding()]
param([string[]]$Only = @())
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$d = "$env:LOCALAPPDATA\xr-sim\casualtyvr"
$t = Join-Path $root "tools\xr-sim\tools"
$log = Join-Path $root "Working\game-dev\BepInEx\LogOutput.log"
$results = New-Object System.Collections.Generic.List[object]

function C([string[]]$c) { & "$t\xrsim-cmd.ps1" -Dir $d -Quiet @c | Out-Null }
function Has([string]$pattern) { (Test-Path $log) -and (Select-String -Path $log -Pattern $pattern -Quiet) }
function Stop-Game { Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force; Start-Sleep 2 }
function Start-Game([string[]]$extra, [string]$readyPattern) {
    Stop-Game
    $args2 = @("--cvr-vr", "--cvr-autostart", "--cvr-seed", "1234", "--cvr-dbg-fp") + $extra
    & (Join-Path $root "tools\launch-dev.ps1") -Windowed -XrSim -NoWait @args2 | Out-Null
    $deadline = (Get-Date).AddSeconds(150)
    while ((Get-Date) -lt $deadline) { Start-Sleep 3; if (Has $readyPattern) { break } }
    Start-Sleep 4
    C @("head rot 0 0 0", "hand r offset 0.3 0.2 -0.3", "hand l offset 0.3 -0.2 -0.3", "grip r 0", "grip l 0")
    Start-Sleep 1
}
function Check([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Scenario = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Detail = $detail })
}
function Want([string]$s) { $Only.Count -eq 0 -or $Only -contains $s }

if (Want "grab-throw") {
    Start-Game @("--cvr-dbg-spawn", "waterbottle") "Spawn: waterbottle"
    $held = $false
    foreach ($f in 0.6, 0.75, 0.9) { foreach ($dn in -1.3, -1.45, -1.6) {
        C @("hand r offset $f 0 $dn"); Start-Sleep -Milliseconds 300
        C @("grip r 1"); Start-Sleep -Milliseconds 400
        if (Has "picked up .*: True") { $held = $true; break }
        C @("grip r 0"); Start-Sleep -Milliseconds 300
    } if ($held) { break } }
    Check "grab-throw: pick up" $held "Grab: picked up ...: True"
    C @("hand r offset 0.2 0 -0.4"); Start-Sleep -Milliseconds 500
    C @("hand r offset 0.7 0 -0.1", "grip r 0"); Start-Sleep -Milliseconds 800
    Check "grab-throw: throw" (Has "let go of .* -> throw") "Grab: let go of ... -> throw"
}

if (Want "chest-store") {
    Start-Game @("--cvr-dbg-spawn", "waterbottle", "--cvr-dbg-hold") "put in hand slot"
    C @("hand r offset 0.1 0 -0.38"); Start-Sleep -Milliseconds 400
    C @("grip r 1"); Start-Sleep -Milliseconds 400
    C @("grip r 0"); Start-Sleep -Milliseconds 600
    Check "chest-store: store" (Has "Grab: stored .* in slot") "Grab: stored ... in slot"
    Start-Sleep 1
    C @("grip r 1"); Start-Sleep -Milliseconds 800
    C @("grip r 0"); Start-Sleep 3
    Check "chest-store: radial" ((Has "chest -> inventory radial") -and (Has "UI mode: dashboard")) "radial on the dashboard"
}

if (Want "mouth") {
    Start-Game @("--cvr-dbg-spawn", "waterbottle", "--cvr-dbg-hold") "put in hand slot"
    C @("hand r offset 0.06 0 -0.08"); Start-Sleep 2.5
    C @("hand r offset 0.4 0.1 -0.3"); Start-Sleep 2.5
    Check "mouth: drink" (Has "to the mouth -> use") "Grab: ... to the mouth -> use"
    Check "mouth: vibration" (Has "haptic: xrApplyHapticFeedback.*XR_SUCCESS") "[bridge] haptic ... XR_SUCCESS"
}

if (Want "touch") {
    Start-Game @("--cvr-dbg-spawn", "dropcapsule", "--cvr-dbg-keypad") "UI mode: dashboard"
    $mice = @()
    foreach ($o in "0.42 0 -0.32", "0.42 0.12 -0.32") {
        C @("hand r offset $o", "hand r point 0 -30"); Start-Sleep 4.5   # the input log line comes every 2 s
        $line = (Select-String -Path $log -Pattern "input: laser" | Select-Object -Last 1).Line
        $mice += [regex]::Match($line, 'mouse=\(([-\d.]+),').Groups[1].Value
    }
    $ok = $mice.Count -eq 2 -and [double]$mice[1] -gt [double]$mice[0] + 100
    Check "touch: hand moves the mouse" $ok ("mouse x " + ($mice -join " -> "))
}

if (Want "swing") {
    Start-Game @("--cvr-dbg-cave") "Cave test: teleported"
    foreach ($i in 1..3) { C @("hand r offset 0.2 0.2 0.0"); Start-Sleep -Milliseconds 700; C @("hand r offset 0.6 0.0 -0.3"); Start-Sleep -Milliseconds 700 }
    Start-Sleep 1
    Check "swing: attack" (Has "Grab: swing attack") "Grab: swing attack ..."
}

if (Want "lockpick") {
    # Lockpicking by hand on the touch panel: the click point is the fingertip in VR (VrHands), and touching the
    # top of the lock's ring (195-247 px from its centre) then pulling the trigger engages the pick.
    Start-Game @("--cvr-dbg-spawn", "dropcapsule", "--cvr-dbg-lockpick") "UI mode: dashboard"
    C @("hand r offset 0.42 0 -0.231", "hand r point 0 -30"); Start-Sleep 2.5
    C @("trigger r 1"); Start-Sleep 2.2
    C @("trigger r 0"); Start-Sleep 0.5
    Check "lockpick: tip is the click point" (Has "Minigame hand: LockpingMinigame Point pivot \(0\.25, 0\.91\)") "pivot at the fingertip"
    Check "lockpick: pick engages on the ring" (Has "Lockpick: .* inside True") "Lockpick: ... inside True"
}
Stop-Game
$exc = Has "Exception"
Check "no exceptions (last run)" (-not $exc) "LogOutput.log"
$results | Format-Table -AutoSize | Out-String | Write-Host
$fails = @($results | Where-Object { $_.Result -eq "FAIL" }).Count
Write-Host "$($results.Count - $fails)/$($results.Count) passed"
exit $fails
