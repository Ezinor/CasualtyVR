# First-person comfort checks (docs/BUILD_PLAN.md Phase 2, docs/FIRSTPERSON_REVIEW.md FP-8). Dev copy only; restores the
# dev config afterwards (presets and the remembered eye height change it).
#
#   tools/fp-comfort.ps1             all scenarios; exit code = number of failures
#   tools/fp-comfort.ps1 -Only wall
#
# Scenarios (each a fresh game, seed 1234, first person in the open test tunnel, --cvr-dbg-cave):
#   wall     leaning 1 m sideways (xr-sim "head pos") puts the head past the tunnel's side: the view fades to black (an
#            xr-sim capture shows it), and comes back when you straighten up
#   blink    the Gentle preset (snap turning, snap blink): a right-stick flick turns with a blink
#   scale    the first run measures and remembers the character's eye height; the next run starts with it (no
#            resize after spawn)
#   walk     a half push of the left stick walks at about the walking speed, a full push runs (speeds from the
#            "fp path" lines)
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

Copy-Item $cfg $cfgBackup -Force
try {
    if (Want "wall") {
        $ready = Start-Game @()
        Check "wall: game ready" $ready ""
        Check "wall: fade on by default" (Has "First person: on .*wall fade True") (Last "First person: on")
        C @("head pos 1.0 1.6 0"); Start-Sleep 1.5
        Check "wall: leaning out of the tunnel fades the view" (Wait-For "First person: head inside a wall" 5) (Last "First person: head inside a wall")
        & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\comfort-phase2\wall") -Quiet | Out-Null
        C @("head pos 0 1.6 0"); Start-Sleep 1.5
        Check "wall: straightening up brings it back" (Wait-For "First person: head back in the open" 5) (Last "First person: head back in the open")
        & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\comfort-phase2\open") -Quiet | Out-Null
        Check-NoExceptions "wall"
    }

    if (Want "blink") {
        $ready = Start-Game @("--cvr-dbg-preset", "gentle")
        Check "blink: game ready" $ready ""
        Check "blink: Gentle preset applied" (Has "First person: on .*comfort Gentle") (Last "First person: on")
        C @("stick r 1 0"); Start-Sleep -Milliseconds 400; C @("stick r 0 0"); Start-Sleep 1
        Check "blink: a snap turn blinks" (Wait-For "First person: snap turn with a blink" 5) (Last "snap turn with a blink")
        Check-NoExceptions "blink"
    }

    if (Want "scale") {
        # First run from a config with no remembered eye height: the scale is a guess until it is measured.
        $c = Get-Content $cfgBackup -Raw
        $fresh = [regex]::Replace($c, "(?m)^MeasuredEyeHeight = .*$", "MeasuredEyeHeight = 0")
        Stop-Game
        [System.IO.File]::WriteAllText($cfg, $fresh)
        $ready = Start-Game @() -KeepConfig
        Check "scale: game ready" $ready ""
        Check "scale: first run measures the eye height" (Wait-For "First person: character eye height" 20) (Last "character eye height")
        Start-Sleep 2
        Stop-Game
        $saved = (Select-String -Path $cfg -Pattern "^MeasuredEyeHeight = (.*)$" | Select-Object -Last 1)
        Check "scale: the eye height is remembered" ($saved -and [double]$saved.Matches[0].Groups[1].Value -gt 0.5) $(if ($saved) { $saved.Line } else { "no line" })
        $ready = Start-Game @() -KeepConfig
        Check "scale: next run starts with it" (Has "First person: on .*remembered eye height") (Last "First person: on")
        $m = Select-String -Path $log -Pattern "character eye height .* -> ([\d.]+) units/m \(was ([\d.]+)\)" | Select-Object -Last 1
        $same = $m -and [math]::Abs([double]$m.Matches[0].Groups[1].Value - [double]$m.Matches[0].Groups[2].Value) -lt 0.05
        Check "scale: no resize when it is measured again" $same $(if ($m) { $m.Line } else { "not measured again yet" })
        Check-NoExceptions "scale"
    }

    if (Want "walk") {
        # Gentle: snap turning, so the flick between the walk and the run turns round (the dev config turns smoothly).
        $ready = Start-Game @("--cvr-dbg-preset", "gentle")
        Check "walk: game ready" $ready ""
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
        Check-NoExceptions "walk"
    }
}
finally {
    Stop-Game
    Copy-Item $cfgBackup $cfg -Force
}
$results | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
$fails = @($results | Where-Object { $_.Result -eq "FAIL" }).Count
Write-Host "$fails failure(s). Log: $log"
exit $fails
