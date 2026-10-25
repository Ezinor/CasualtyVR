# First-person smoke test (docs/FIRSTPERSON_PLAN.md, FP-6): drives xr-sim's head and hands through the
# first-person interactions and checks the game's own results in the log. Dev copy only (tools/launch-dev.ps1).
#
#   tools/fp-smoke.ps1                    all scenarios (5 game sessions, about 5 minutes); exit code = number of failures
#   tools/fp-smoke.ps1 -Quick             the hands and the swings only (2 sessions, about 2 minutes)
#   tools/fp-smoke.ps1 -Only pull,mouth   only these scenarios (the sessions they need)
#
# Scenarios share game sessions (seed 1234, first person forced with --cvr-dbg-fp); starting the game is most of the
# time, so each session runs several in a row, each checking only the log lines written since its own step began:
#   hands session (three water bottles, 2.5, 4 and 5.5 blocks ahead):
#     poses        both empty hands held out in front: open, fist, pointing, pressing (captures, Working/hands-phase4b)
#     affordance   the open hand near a bottle ticks and half-closes; the grip takes exactly that bottle
#     grab-throw   it was picked up; released while moving -> thrown from the hand at its peak speed; an empty grip is a fist
#     pull         point at a bottle further off (selected), grip (locked), flick towards you (pulled into the hand)
#     mouth        the held bottle at the mouth -> used (drink), with a vibration pulse accepted by the runtime
#     chest-store  hand to the chest, let go -> stored in an inventory slot; empty hand at the chest, grip -> inventory radial
#   cave session, machete in hand:
#     swing-gate   (Phase 4a) the grip pose is located; swings with a machete attack
#     swing        the machete stored at the chest, bare-hand swings attack
#   cave session, pistol in hand (not in -Quick):
#     swing-gate   swings with a pistol in hand do not fire it
#   touch          keypad minigame: the hand on the panel moves the mouse (centre, right) (not in -Quick)
#   lockpick       lockpicking by hand: the fingertip is the click point; ring + trigger engages the pick (not in -Quick)
[CmdletBinding()]
param([string[]]$Only = @(), [switch]$Quick)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$d = "$env:LOCALAPPDATA\xr-sim\casualtyvr"
$t = Join-Path $root "tools\xr-sim\tools"
$log = Join-Path $root "Working\game-dev\BepInEx\LogOutput.log"
$results = New-Object System.Collections.Generic.List[object]
$script:mark = 0
$script:excRuns = @()
$script:inSession = $false
$started = Get-Date
if ($Quick -and $Only.Count -eq 0) { $Only = @("poses", "affordance", "grab-throw", "pull", "mouth", "chest-store", "swing", "swing-gate") }

function C([string[]]$c) { & "$t\xrsim-cmd.ps1" -Dir $d -Quiet @c | Out-Null }
# Log lines written since the current step began (Mark).
function Since([string]$pattern) {
    if (-not (Test-Path $log)) { return @() }
    @(Select-String -Path $log -Pattern $pattern | Where-Object { $_.LineNumber -gt $script:mark })
}
function Has([string]$pattern) { (Since $pattern).Count -gt 0 }
function HasAny([string]$pattern) { (Test-Path $log) -and (Select-String -Path $log -Pattern $pattern -Quiet) }
function LastLine([string]$pattern) { $m = Since $pattern; if ($m.Count) { $m[-1].Line } else { "" } }
function Mark { $script:mark = if (Test-Path $log) { @(Get-Content $log).Count } else { 0 } }
function Stop-Game {
    if ($script:inSession -and (HasAny "Exception")) { $script:excRuns += (Split-Path $log -Leaf) }
    $script:inSession = $false
    Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force; Start-Sleep 2
}
function Start-Game([string[]]$extra, [string]$readyPattern) {
    Stop-Game
    $args2 = @("--cvr-vr", "--cvr-autostart", "--cvr-seed", "1234", "--cvr-dbg-fp") + $extra
    $launched = Get-Date
    & (Join-Path $root "tools\launch-dev.ps1") -Windowed -XrSim -NoWait @args2 | Out-Null
    # Read this run's Unity player log (it carries the BepInEx lines too): LogOutput.log is buffered, so a line can be
    # missing when a check reads it (chest-store failed that way, 2026-10-23), and it holds the last run until BepInEx starts.
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 300
        $newest = Get-ChildItem (Join-Path $root "Working\game-dev\CasualtyVR_Logs\Player-*.log") | Where-Object { $_.CreationTime -ge $launched.AddSeconds(-2) } | Sort-Object CreationTime | Select-Object -Last 1
    } while ($null -eq $newest -and (Get-Date) -lt $deadline)
    if ($newest) { $script:log = $newest.FullName; $script:inSession = $true }
    $script:mark = 0
    $deadline = (Get-Date).AddSeconds(150)
    while ((Get-Date) -lt $deadline) { Start-Sleep 1; if (Has $readyPattern) { break } }
    Start-Sleep 3
    C @("head rot 0 0 0", "hand r offset 0.3 0.2 -0.3", "hand l offset 0.3 -0.2 -0.3", "grip r 0", "grip l 0")
    Start-Sleep 1
}
function Want([string]$s) { $Only.Count -eq 0 -or $Only -contains $s }
function WantAny([string[]]$s) { foreach ($x in $s) { if (Want $x) { return $true } }; $false }
# A result counts only for wanted scenarios (a session also runs the steps its later scenarios need).
function Check([string]$name, [bool]$ok, [string]$detail) {
    $scenario = ($name -split ":")[0]
    if ($scenario -notlike "no exceptions*" -and -not (Want $scenario)) { return }
    $results.Add([pscustomobject]@{ Scenario = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Detail = $detail })
}
function Shot([string]$name) { & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\hands-phase4b\$name") -Quiet | Out-Null }
# The right hand reaches down and forward until it is within reach of the nearest bottle (or picks it up, with -Grip).
function Reach-Bottle([string]$pattern, [switch]$Grip) {
    foreach ($f in 0.6, 0.75, 0.9) { foreach ($dn in -1.3, -1.45, -1.6) {
        C @("hand r offset $f 0 $dn"); Start-Sleep -Milliseconds 400
        if ($Grip) { C @("grip r 1"); Start-Sleep -Milliseconds 400 }
        if (Has $pattern) { return $true }
        if ($Grip) { C @("grip r 0"); Start-Sleep -Milliseconds 300 }
    } }
    $false
}
# Point the right laser at the nearest loose bottle the game would let you pick up (--cvr-dbg-pull-aim logs the angles).
function Aim-Pull {
    Start-Sleep 2.5
    $m = Since "Grab test: pull aim '([^']+)': point (\S+) (\S+)" | Select-Object -Last 1
    if ($m) { C @("hand r point $($m.Matches[0].Groups[2].Value) $($m.Matches[0].Groups[3].Value)"); Start-Sleep 1.5 }
    $m
}

# ---- hands session ------------------------------------------------------------------------------------------------
if (WantAny @("poses", "affordance", "grab-throw", "pull", "mouth", "chest-store")) {
    Start-Game @("--cvr-dbg-spawn", "waterbottle,waterbottle,waterbottle", "--cvr-dbg-pull-aim") "Spawn: waterbottle"

    if (Want "poses") {
        # Phase 4b (FP 9b): both empty hands held out in front, one capture per pose, from behind and turned across the view.
        Mark
        C @("hand r offset 0.42 0.14 0.02", "hand l offset 0.42 -0.14 0.02", "hand r point -10 15", "hand l point 10 15"); Start-Sleep 1
        foreach ($p in @(@("open", @("grip r 0", "grip l 0", "trigger r 0", "trigger l 0"), "Open"),
                         @("fist", @("grip r 1", "grip l 1"), "Fist"),
                         @("point", @("grip r 0", "grip l 0", "trigger r 0.7", "trigger l 0.7"), "Point"),
                         @("press", @("trigger r 1", "trigger l 1"), "PointPress"))) {
            C $p[1]; Start-Sleep 1
            # The pose now (the last change logged, which may be before this step: the hands start open).
            $now = @(Select-String -Path $log -Pattern "Arms: right hand pose (\w+)") | Select-Object -Last 1
            $near = LastLine "Grab: hand 1 (within reach of|pointing at)"
            Check "poses: $($p[0])" ($now -and $now.Matches[0].Groups[1].Value -eq $p[2]) $(if ($now) { $now.Line + $(if ($near) { " (hand near: $near)" }) } else { "no pose line" })
            Shot "pose-$($p[0])"
            C @("hand r point 60 15", "hand l point -60 15"); Start-Sleep 0.6
            Shot "pose-$($p[0])-side"
            C @("hand r point -10 15", "hand l point 10 15"); Start-Sleep 0.4
        }
        C @("trigger r 0", "trigger l 0", "grip r 0", "grip l 0", "hand r point 0 0", "hand l point 0 0", "hand r offset 0.3 0.2 -0.3", "hand l offset 0.3 -0.2 -0.3"); Start-Sleep 1
    }

    # Phase 4b (FP 9b, 9c): the open hand near a bottle ticks and half-closes; the grip takes exactly that bottle.
    Mark
    $near = Reach-Bottle "Grab: hand 1 within reach of waterbottle"
    Check "affordance: within reach is shown" $near "Grab: hand 1 within reach of waterbottle"
    Start-Sleep 1
    Check "affordance: the hand half-closes" (Has "Arms: right hand pose Half") "Arms: right hand pose Half"
    if (Want "affordance") { Shot "affordance" }
    C @("grip r 1"); Start-Sleep 1
    Check "affordance: the grip takes the one shown" (Has "Grab: hand 1 .* item waterbottle.* \(the one shown\)") (LastLine "Grab: hand 1 slot")
    if (-not (Has "picked up .*: True")) { C @("grip r 0"); Start-Sleep 0.3; Reach-Bottle "picked up .*: True" -Grip | Out-Null }
    Check "grab-throw: pick up" (Has "picked up .*: True") "Grab: picked up ...: True"

    # Thrown: the hand winds back, then moves fast while the grip lets go.
    Mark
    C @("hand r offset 0.2 0 -0.4"); Start-Sleep -Milliseconds 500
    C @("hand r offset 0.7 0 -0.1", "grip r 0"); Start-Sleep -Milliseconds 800
    Check "grab-throw: throw" (Has "let go of .* -> throw") "Grab: let go of ... -> throw"
    Check "grab-throw: thrown from the hand at its peak speed" (Has "Grab: threw .* \(peak over 150 ms\)") (LastLine "Grab: threw")
    Mark
    C @("hand r offset 0.4 0.2 -0.3", "grip r 1"); Start-Sleep 1
    Check "grab-throw: an empty grip makes a fist" (Has "Arms: right hand pose (Fist|Own)") (LastLine "Arms: right hand pose")
    C @("grip r 0"); Start-Sleep 0.5

    # Phase 4b (FP 9g, D4): point at a bottle further off (it is selected), grip to lock it, flick towards you to pull it.
    Mark
    $m = Aim-Pull
    Check "pull: pointing selects the item" (Has "Grab: hand 1 pointing at waterbottle") $(if ($m) { $m.Line.Substring($m.Line.IndexOf("Grab")) } else { "no aim line" })
    if (Want "pull") { Shot "pull-selected" }
    C @("grip r 1"); Start-Sleep 0.8
    Check "pull: the grip locks it" (Has "Grab: hand 1 locked onto waterbottle") (LastLine "locked onto")
    C @("hand r offset 0.05 0.05 -0.1"); Start-Sleep 1
    Check "pull: a flick pulls it into the hand" (Has "Grab: pulled waterbottle.*: in hand") (LastLine "Grab: pulled")
    # The grip stays held: the pulled bottle is in the hand.

    if (WantAny @("mouth", "chest-store")) {
        Mark
        C @("hand r point 0 0", "hand r offset 0.06 0 -0.08"); Start-Sleep 2.5
        C @("hand r offset 0.4 0.1 -0.3"); Start-Sleep 2.5
        Check "mouth: drink" (Has "to the mouth -> use") "Grab: ... to the mouth -> use"
        Check "mouth: vibration" (HasAny "haptic: xrApplyHapticFeedback.*XR_SUCCESS") "[bridge] haptic ... XR_SUCCESS"

        Mark
        C @("hand r offset 0.1 0 -0.38"); Start-Sleep -Milliseconds 400
        C @("grip r 0"); Start-Sleep -Milliseconds 600
        Check "chest-store: store" (Has "Grab: stored .* in slot") (LastLine "Grab: (stored|let go)")
        Start-Sleep 1
        # 30 cm ahead of the eyes is in front of the chest, not at it (headset, test13: it opened too far out).
        Mark
        C @("hand r offset 0.3 0 -0.38"); Start-Sleep -Milliseconds 400
        C @("grip r 1"); Start-Sleep -Milliseconds 600
        C @("grip r 0"); Start-Sleep -Milliseconds 600
        Check "chest-store: a hand 30 cm out does not open the inventory" (-not (Has "chest -> inventory radial")) $(if (Has "chest -> inventory radial") { LastLine "chest -> inventory radial" } else { "no radial" })
        C @("hand r offset 0.1 0 -0.38"); Start-Sleep -Milliseconds 400
        C @("grip r 1"); Start-Sleep -Milliseconds 800
        C @("grip r 0"); Start-Sleep 3
        Check "chest-store: radial" ((Has "chest -> inventory radial") -and (Has "UI mode: dashboard")) "radial on the dashboard"
    }
}

# ---- cave session, machete --------------------------------------------------------------------------------------------
function Swings { foreach ($i in 1..3) { C @("hand r offset 0.2 0.2 0.0"); Start-Sleep -Milliseconds 700; C @("hand r offset 0.6 0.0 -0.3"); Start-Sleep -Milliseconds 700 }; Start-Sleep 1 }
if (WantAny @("swing", "swing-gate")) {
    # Phase 4a (FAIL-HAND-006): with a melee tool in hand a swing attacks; bare hands too. The grip pose is located.
    Start-Game @("--cvr-dbg-cave", "--cvr-dbg-spawn", "machete", "--cvr-dbg-hold") "put in hand slot"
    Check "swing-gate: grip pose located" (Has "controller right: grip pose located") "[bridge] controller right: grip pose located"
    Mark
    Swings
    Check "swing-gate: a machete swings" (Has "Grab: swing attack") (LastLine "Grab: swing attack")
    Check "swing-gate: a miss does not count as a hit (Phase 5)" (-not (Has "Haptics: hit landed")) "no 'hit landed' for swings at the air"
    # Phase 5 (FP 10b): chops down at the floor land (the machete reaches about 5 blocks from the torso).
    Mark
    foreach ($i in 1..3) { C @("hand r offset 0.35 0.1 0.1"); Start-Sleep -Milliseconds 700; C @("hand r offset 0.5 0.1 -0.7"); Start-Sleep -Milliseconds 700 }
    Start-Sleep 1
    Check "swing-gate: a hit that lands vibrates (Phase 5)" (Has "Haptics: hit landed") $(if (Has "Haptics: hit landed") { LastLine "Haptics: hit landed" } else { "swings: " + (Since "Grab: swing attack").Count })
    # Store the machete at the chest, then swing bare-handed.
    C @("hand r offset 0.1 0 -0.38"); Start-Sleep -Milliseconds 400
    C @("grip r 1"); Start-Sleep -Milliseconds 400
    C @("grip r 0"); Start-Sleep -Milliseconds 800
    Mark
    Swings
    Check "swing: bare hands attack" ((Has "Grab: swing attack") -and (HasAny "Grab: stored .*machete")) "$(LastLine 'Grab: swing attack') (machete stored: $(HasAny 'Grab: stored .*machete'))"
}

# ---- cave session, pistol -------------------------------------------------------------------------------------------
if (-not $Quick -and (Want "swing-gate")) {
    Start-Game @("--cvr-dbg-cave", "--cvr-dbg-spawn", "pistol", "--cvr-dbg-hold") "put in hand slot"
    Mark
    Swings
    Check "swing-gate: a gun is not swung" ((Has "swing not used with pistol") -and -not (Has "Grab: swing attack")) "Grab: swing not used with pistol..., no swing attack"
}

# ---- minigames ------------------------------------------------------------------------------------------------------
if (Want "touch") {
    Start-Game @("--cvr-dbg-spawn", "dropcapsule", "--cvr-dbg-keypad") "UI mode: dashboard"
    $mice = @()
    foreach ($o in "0.42 0 -0.32", "0.42 0.12 -0.32") {
        C @("hand r offset $o", "hand r point 0 -30"); Start-Sleep 4.5   # the input log line comes every 2 s
        $line = LastLine "input: laser"
        $mice += [regex]::Match($line, 'mouse=\(([-\d.]+),').Groups[1].Value
    }
    $ok = $mice.Count -eq 2 -and [double]$mice[1] -gt [double]$mice[0] + 100
    Check "touch: hand moves the mouse" $ok ("mouse x " + ($mice -join " -> "))
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
Check "no exceptions (all sessions)" ($script:excRuns.Count -eq 0) $(if ($script:excRuns.Count) { $script:excRuns -join ", " } else { "player logs" })
$results | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
$fails = @($results | Where-Object { $_.Result -eq "FAIL" }).Count
Write-Host "$($results.Count - $fails)/$($results.Count) passed in $([int]((Get-Date) - $started).TotalSeconds) s"
exit $fails
