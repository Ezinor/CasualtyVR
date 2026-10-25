# First-person comfort, senses and movement checks (docs/BUILD_PLAN.md Phases 2 and 5, docs/FIRSTPERSON_REVIEW.md FP-8,
# FP-10, FP-12). Dev copy only; restores the
# dev config afterwards (presets and the remembered eye height change it).
#
#   tools/fp-comfort.ps1             all scenarios; exit code = number of failures
#   tools/fp-comfort.ps1 -Only wall
#
# Scenarios (seed 1234, first person in the open test tunnel, --cvr-dbg-cave), in two game sessions (starting the game
# is most of the time):
#   session 1, no remembered eye height:
#     scale    the run measures and remembers the character's eye height
#     wall     leaning 1 m sideways (xr-sim "head pos") puts the head past the tunnel's side: the view fades to black (an
#              xr-sim capture shows it), and comes back when you straighten up
#   session 2, the Gentle preset (snap turning, snap blink), the config session 1 left, with --cvr-dbg-sound,
#   --cvr-dbg-rope, --cvr-dbg-danger and --cvr-dbg-hurt:
#     scale    the run starts with the remembered eye height (no resize after spawn)
#     crouch   (Phase 5, FP 12a) the head 0.45 m down crouches the character, and the view is your head's height: the
#              standing eye height less 0.45 m, not lowered again by the character's crouch; standing up stands it up
#     sound    (FP 10a) the game's listener is off; a test sound ahead is behind you when you turn your head round, at
#              the flat game's loudness
#     rope     (FP 12b) the right stick up climbs on a rope next to you, down climbs off; the view's largest one-frame
#              move on and off is logged
#     haptics  (FP 10b) a dislocation (the game's own shake and pain) vibrates; the heartbeat beats at the heart rate
#     blink    a right-stick flick turns with a blink
#     walk     a half push of the left stick walks at about the walking speed, a full push runs (speeds from the
#              "fp path" lines)
[CmdletBinding()]
param([string[]]$Only = @())
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$d = "$env:LOCALAPPDATA\xr-sim\casualtyvr"
$t = Join-Path $root "tools\xr-sim\tools"
$bepinexLog = Join-Path $root "Working\game-dev\BepInEx\LogOutput.log"
$cfg = Join-Path $root "Working\game-dev\BepInEx\config\casualtyvr.core.cfg"
$cfgBackup = Join-Path $env:TEMP "casualtyvr.core.cfg.fp-comfort"
$log = $bepinexLog
$results = New-Object System.Collections.Generic.List[object]

function C([string[]]$c) { & "$t\xrsim-cmd.ps1" -Dir $d -Quiet @c | Out-Null }
function Has([string]$pattern) { (Test-Path $log) -and (Select-String -Path $log -Pattern $pattern -Quiet) }
function Wait-For([string]$pattern, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) { if (Has $pattern) { return $true }; Start-Sleep -Milliseconds 400 }
    return $false
}
function Last([string]$pattern) { $m = Select-String -Path $log -Pattern $pattern | Select-Object -Last 1; if ($m) { $m.Line } else { "" } }
function Stop-Game { Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force; Start-Sleep 2 }
function Start-Game([string[]]$extra, [switch]$KeepConfig) {
    Stop-Game
    if (-not $KeepConfig) { Copy-Item $cfgBackup $cfg -Force }
    Remove-Item $bepinexLog -ErrorAction SilentlyContinue
    $script:log = $bepinexLog
    $started = Get-Date
    & (Join-Path $root "tools\launch-dev.ps1") -Windowed -XrSim -NoWait (@("--cvr-vr", "--cvr-autostart", "--cvr-seed", "1234", "--cvr-dbg-fp", "--cvr-dbg-cave") + $extra) | Out-Null
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 300
        $newest = Get-ChildItem (Join-Path $root "Working\game-dev\CasualtyVR_Logs\Player-*.log") | Where-Object { $_.CreationTime -ge $started.AddSeconds(-2) } | Sort-Object CreationTime | Select-Object -Last 1
    } while ($null -eq $newest -and (Get-Date) -lt $deadline)
    if ($newest) { $script:log = $newest.FullName }
    $ready = Wait-For "Cave test: teleported" 180
    Start-Sleep 6
    C @("input clear", "head pose 0 1.6 0 0 0 0")
    Start-Sleep 1
    return $ready
}
function Check([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Scenario = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Detail = $detail })
}
function Check-NoExceptions([string]$name) {
    $lines = @(Select-String -Path $log -Pattern "Exception")
    Check "$name`: no exceptions" ($lines.Count -eq 0) ("{0} lines{1}" -f $lines.Count, $(if ($lines.Count) { ": " + $lines[0].Line } else { "" }))
}
function Want([string]$s) { $Only.Count -eq 0 -or $Only -contains $s }

$startedAt = Get-Date
Copy-Item $cfg $cfgBackup -Force
try {
    # ---- session 1: no remembered eye height (scale, wall) ----
    $session2KeepsConfig = $false
    if ((Want "scale") -or (Want "wall")) {
        $c = Get-Content $cfgBackup -Raw
        $fresh = [regex]::Replace($c, "(?m)^MeasuredEyeHeight = .*$", "MeasuredEyeHeight = 0")
        Stop-Game
        [System.IO.File]::WriteAllText($cfg, $fresh)
        $ready = Start-Game @() -KeepConfig
        Check "wall: game ready" $ready ""
        if (Want "scale") { Check "scale: first run measures the eye height" (Wait-For "First person: character eye height" 20) (Last "character eye height") }
        if (Want "wall") {
            Check "wall: fade on by default" (Has "First person: on .*wall fade True") (Last "First person: on")
            C @("head pos 1.0 1.6 0"); Start-Sleep 1.5
            Check "wall: leaning out of the tunnel fades the view" (Wait-For "First person: head inside a wall" 5) (Last "First person: head inside a wall")
            & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\comfort-phase2\wall") -Quiet | Out-Null
            C @("head pos 0 1.6 0"); Start-Sleep 1.5
            Check "wall: straightening up brings it back" (Wait-For "First person: head back in the open" 5) (Last "First person: head back in the open")
            & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\comfort-phase2\open") -Quiet | Out-Null
        }
        if (Want "scale") { Wait-For "First person: character eye height" 20 | Out-Null; Start-Sleep 2 }
        Check-NoExceptions "session 1"
        Stop-Game
        if (Want "scale") {
            $saved = (Select-String -Path $cfg -Pattern "^MeasuredEyeHeight = (.*)$" | Select-Object -Last 1)
            Check "scale: the eye height is remembered" ($saved -and [double]$saved.Matches[0].Groups[1].Value -gt 0.5) $(if ($saved) { $saved.Line } else { "no line" })
            $session2KeepsConfig = $true
        }
    }

    # ---- session 2: Gentle, from the config session 1 left (scale, blink, walk) ----
    if ((Want "scale") -or (Want "blink") -or (Want "walk") -or (Want "crouch") -or (Want "sound") -or (Want "rope") -or (Want "haptics")) {
        $ready = Start-Game @("--cvr-dbg-preset", "gentle", "--cvr-dbg-sound", "--cvr-dbg-rope", "--cvr-dbg-danger", "--cvr-dbg-hurt", "80") -KeepConfig:$session2KeepsConfig
        Check "blink: game ready" $ready ""
        if (Want "scale") {
            Check "scale: next run starts with it" (Has "First person: on .*remembered eye height") (Last "First person: on")
            $m = Select-String -Path $log -Pattern "character eye height .* -> ([\d.]+) units/m \(was ([\d.]+)\)" | Select-Object -Last 1
            $same = $m -and [math]::Abs([double]$m.Matches[0].Groups[1].Value - [double]$m.Matches[0].Groups[2].Value) -lt 0.05
            Check "scale: no resize when it is measured again" $same $(if ($m) { $m.Line } else { "not measured again yet" })
        }
        if (Want "crouch") {
            # Real crouch: xr-sim stands the head at 1.6 m; 0.45 m lower is past the 0.3 m threshold.
            C @("head pos 0 1.15 0")
            Check "crouch: the head down crouches" (Wait-For "Crouch: real on" 5) (Last "Crouch: real")
            Start-Sleep 2
            $m = [regex]::Match((Last "First person: real crouch"), "view ([\d.]+) m above the feet \(standing eye ([\d.]+) m, your head ([\d.]+) m down, character crouch ([\d.]+)")
            $ok = $m.Success -and [math]::Abs([double]$m.Groups[1].Value - ([double]$m.Groups[2].Value - [double]$m.Groups[3].Value)) -lt 0.08 -and [double]$m.Groups[4].Value -gt 0.5
            Check "crouch: the view is your head's height (no double drop)" $ok $(if ($m.Success) { $m.Value } else { "no real crouch line" })
            & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\senses-phase5\crouch") -Quiet | Out-Null
            C @("head pos 0 1.6 0")
            Check "crouch: standing up stands" (Wait-For "Crouch: real off" 5) (Last "Crouch: real")
            Start-Sleep 1
        }
        if (Want "sound") {
            Check "sound: the game's listener is off in first person" (Has "Ears: the game's listener on") (Last "Ears: the game's listener")
            function Ears-Line { $n = @(Select-String -Path $log -Pattern "Ears test:").Count; $deadline = (Get-Date).AddSeconds(5); while ((Get-Date) -lt $deadline -and @(Select-String -Path $log -Pattern "Ears test:").Count -le $n) { Start-Sleep -Milliseconds 300 }; Last "Ears test:" }
            C @("head rot 0 0 0"); Start-Sleep 1; $a = Ears-Line
            C @("head rot 180 0 0"); Start-Sleep 1; $b = Ears-Line
            C @("head rot 0 0 0")
            $pa = [regex]::Match($a, "ears-local \(([-\d.]+), ([-\d.]+), ([-\d.]+)\).* gain ([\d.]+) \(flat game ([\d.]+)\)")
            $pb = [regex]::Match($b, "ears-local \(([-\d.]+), ([-\d.]+), ([-\d.]+)\).* gain ([\d.]+) \(flat game ([\d.]+)\)")
            $turned = $pa.Success -and $pb.Success -and [double]$pa.Groups[3].Value -gt 0.5 -and [double]$pb.Groups[3].Value -lt -0.5
            Check "sound: ahead, then behind when you turn your head" $turned "ahead z $($pa.Groups[3].Value) -> after turning z $($pb.Groups[3].Value)"
            $same = $pa.Success -and [math]::Abs([double]$pa.Groups[4].Value - [double]$pa.Groups[5].Value) -le 0.1 * [double]$pa.Groups[5].Value + 0.002
            Check "sound: the flat game's loudness" $same "gain $($pa.Groups[4].Value), flat game $($pa.Groups[5].Value)"
        }
        if (Want "rope") {
            Check "rope: a rope hangs beside you" (Has "Rope test: hung from") (Last "Rope test:")
            C @("stick r 0 1"); Start-Sleep 1.5; C @("stick r 0 0")
            Check "rope: the stick climbs on" (Wait-For "First person: climbing on" 4) (Last "First person: climbing on")
            C @("stick r 0 -1"); $off = Wait-For "First person: climbing off" 8; C @("stick r 0 0")
            if (-not $off) { C @("btn a press"); $off = Wait-For "First person: climbing off" 4 }
            Check "rope: down climbs off" $off (Last "First person: climbing off")
        }
        if ((Want "blink") -or (Want "walk")) {
            Check "blink: Gentle preset applied" (Has "First person: on .*comfort Gentle") (Last "First person: on")
            C @("stick r 1 0"); Start-Sleep -Milliseconds 400; C @("stick r 0 0"); Start-Sleep 1
            Check "blink: a snap turn blinks" (Wait-For "First person: snap turn with a blink" 5) (Last "snap turn with a blink")
        }
        if (Want "walk") {
            # Snap turning (Gentle), so the flick between the walk and the run turns round (the dev config turns smoothly).
            function Speed([string]$stick, [int]$ms) {
                $n = @(Select-String -Path $log -Pattern "fp path:").Count
                C @("stick l $stick"); Start-Sleep -Milliseconds $ms; C @("stick l center"); Start-Sleep 2.5
                $speeds = @(Select-String -Path $log -Pattern "fp path:" | Select-Object -Skip $n | ForEach-Object { [regex]::Match($_.Line, "moving (\d+) frames at ([\d.]+) m/s") } | Where-Object { $_.Success } | ForEach-Object { [pscustomobject]@{ N = [int]$_.Groups[1].Value; V = [double]$_.Groups[2].Value } })
                $best = $speeds | Sort-Object N -Descending | Select-Object -First 1
                if ($best) { $best.V } else { 0 }
            }
            $walk = Speed "0 0.5" 3000
            C @("stick r 1 0"); Start-Sleep -Milliseconds 300; C @("stick r 0 0"); Start-Sleep 1
            $run = Speed "0 1" 1000
            Check "walk: a half push walks (about 1.4 m/s)" ($walk -gt 0.9 -and $walk -lt 1.9) ("{0:F2} m/s" -f $walk)
            Check "walk: a full push runs (faster than 3 m/s; Gentle runs at about 4)" ($run -gt 3) ("{0:F2} m/s" -f $run)
        }
        if (Want "haptics") {
            Check "haptics: the heartbeat beats at the heart rate" (Wait-For "Haptics: heartbeat at" 5) (Last "Haptics: heartbeat")
            $hurt = Wait-For "Hurt test: dislocated" 60
            Check "haptics: being hurt vibrates" ($hurt -and (Wait-For "Haptics: damage" 3)) (Last "Haptics: damage")
        }
        Check-NoExceptions "session 2"
    }
}
finally {
    Stop-Game
    Copy-Item $cfgBackup $cfg -Force
}
$results | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
$fails = @($results | Where-Object { $_.Result -eq "FAIL" }).Count
Write-Host "$fails failure(s) in $([int]((Get-Date) - $startedAt).TotalSeconds) s. Log: $log"
exit $fails
