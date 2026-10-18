# VR lifecycle checks (docs/IMPROVEMENT_PLAN.md group A, receipt docs/LIFECYCLE_RECEIPT.md): drives xr-sim's session
# states, tracking validity and controls, and checks the mod's reaction in the log. Dev copy only (tools/launch-dev.ps1).
#
#   tools/vr-lifecycle.ps1            all scenarios; exit code = number of failures
#   tools/vr-lifecycle.ps1 -Only input
#
# Scenarios (each a fresh game, seed 1234, diorama):
#   input    held keys release when the frame does not render (head tracking invalid; not-focused with VDXR's
#            "no render" model), a control held across focus regain waits for a release, controller dropouts are
#            held for a moment then dropped, a runtime recenter re-anchors, a lost session stops VR cleanly
#   heading  the F9 recenter (--cvr-dbg-recenter) takes the head's heading; the runtime EXITING state stops VR
#   faults   an exception in one feature (--cvr-dbg-throw) turns that feature off and VR keeps submitting; instance
#            loss stops VR
#   imgui    a mod's OnGUI window (--cvr-dbg-imgui) is captured into the headset and takes laser hover and clicks, in
#            the diorama and in first person; a co-op-style name tag and icon (--cvr-dbg-clone) are found
[CmdletBinding()]
param([string[]]$Only = @())
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$d = "$env:LOCALAPPDATA\xr-sim\casualtyvr"
$t = Join-Path $root "Tools\xr-sim\tools"
$bepinexLog = Join-Path $root "Working\game-dev\BepInEx\LogOutput.log"
$log = $bepinexLog   # switched to the run's Unity player log by Start-Game
$bridgeLog = Join-Path $root "Working\game-dev\BepInEx\CasualtyVR-bridge.log"
$results = New-Object System.Collections.Generic.List[object]

function C([string[]]$c) { & "$t\xrsim-cmd.ps1" -Dir $d -Quiet @c | Out-Null }
function Has([string]$pattern) { (Test-Path $log) -and (Select-String -Path $log -Pattern $pattern -Quiet) }
function Count([string]$pattern) { if (Test-Path $log) { @(Select-String -Path $log -Pattern $pattern).Count } else { 0 } }
function Wait-For([string]$pattern, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) { if (Has $pattern) { return $true }; Start-Sleep -Milliseconds 500 }
    return $false
}
# The verbose input line comes every 2 s: wait for a fresh one and return its held keys.
function Held {
    Start-Sleep -Milliseconds 1200   # the command reaches the game on its next frames
    $n = Count "input: laser"
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and (Count "input: laser") -le $n) { Start-Sleep -Milliseconds 300 }
    $line = (Select-String -Path $log -Pattern "input: laser" | Select-Object -Last 1).Line
    return [regex]::Match($line, 'held=\[([^\]]*)\]').Groups[1].Value
}
function Game-Running { @(Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" }).Count -gt 0 }
function Stop-Game { Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force; Start-Sleep 2 }
function Start-Game([string[]]$extra) {
    Stop-Game
    # The new process truncates the log only once BepInEx starts: until then the last run's lines would match.
    Remove-Item $bepinexLog -ErrorAction SilentlyContinue
    $script:log = $bepinexLog
    Remove-Item $bridgeLog, ($bridgeLog -replace '\.log$', '.prev.log') -ErrorAction SilentlyContinue
    $args2 = @("--cvr-vr", "--cvr-autostart", "--cvr-seed", "1234") + $extra
    $started = Get-Date
    & (Join-Path $root "tools\launch-dev.ps1") -Windowed -XrSim -NoWait @args2 | Out-Null
    # Read the Unity player log (it carries the BepInEx lines too): BepInEx buffers LogOutput.log, so a "fresh"
    # line read from it can predate the command that was just sent.
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 300
        $newest = Get-ChildItem (Join-Path $root "Working\game-dev\CasualtyVR_Logs\Player-*.log") | Where-Object { $_.CreationTime -ge $started.AddSeconds(-2) } | Sort-Object CreationTime | Select-Object -Last 1
    } while ($null -eq $newest -and (Get-Date) -lt $deadline)
    if ($newest) { $script:log = $newest.FullName }
    # World generation runs at ~1 fps (xr-sim commands time out then): ready = the frame rate is back with 3D blocks.
    # Over 50 fps, not 80: whole runs of this machine sat at ~74 fps (2026-10-17), which is not what is tested here.
    $ready = Wait-For "perf: ([5-9]\d)\.\d fps.* voxels [1-9]" 180
    Start-Sleep 2
    C @("input clear")
    Start-Sleep 2
    return $ready
}
function Check([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Scenario = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Detail = $detail })
}
function Check-Stopped([string]$name) {
    $stopped = Wait-For "VR stopped \(the OpenXR runtime ended the session\)" 10
    Check "$name`: VR stops" $stopped "VR stopped (the OpenXR runtime ended the session)"
    Check "$name`: notice on the monitor" (Has "Notice: CasualtyVR: the VR session ended") "Notice: ... the VR session ended"
    Check "$name`: game pacing restored" (Has "Frame pacing: the game's own") "Frame pacing: the game's own (...)"
    $waitFails = Count "xrWaitFrame failed"
    Check "$name`: no per-frame error spam" ($waitFails -le 1) "$waitFails xrWaitFrame failure lines"
    Start-Sleep 3
    Check "$name`: game keeps running" (Game-Running) "process alive 3 s later"
}
function Check-NoExceptions([string]$name, [string]$except = "") {
    $lines = @(Select-String -Path $log -Pattern "Exception" | Where-Object { $except -eq "" -or $_.Line -notmatch $except })
    Check "$name`: no unexpected exceptions" ($lines.Count -eq 0) ("{0} lines" -f $lines.Count)
}
function Want([string]$s) { $Only.Count -eq 0 -or $Only -contains $s }

if (Want "input") {
    $ready = Start-Game @()
    Check "input: game ready" $ready "verbose input line with a body"
    Check "input: frame pacing by the runtime" (Has "Frame pacing: the OpenXR runtime") "Frame pacing: the OpenXR runtime"

    # 1. Head tracking invalid (no rendered frame) while a stick is held, then released: the key must release.
    C @("stick l 1 0"); $h1 = Held
    C @("head valid off"); Start-Sleep 1
    C @("stick l center"); Start-Sleep 1; $h2 = Held
    C @("head valid on"); Start-Sleep 1
    Check "input: stick walks" ($h1 -ne "") "held=[$h1]"
    Check "input: release reaches the game while not rendering" ($h2 -eq "") "held=[$h2] after release with head tracking off"

    # 2. VDXR model: not focused = no rendering. Focus loss releases; a control held across regain waits for release.
    C @("focus norender on", "stick l 1 0"); $h3 = Held
    C @("focus lose"); Start-Sleep 1; $h4 = Held
    C @("focus regain"); Start-Sleep 1; $h5 = Held
    Check "input: focus loss releases (no rendering)" ($h3 -ne "" -and $h4 -eq "") "held=[$h3] -> [$h4]"
    Check "input: held across regain is ignored" ($h5 -eq "" -and (Has "focus regained with \[.*L\.stickRight")) "held=[$h5]"
    C @("stick l center"); Start-Sleep 1
    C @("stick l 1 0"); $h6 = Held
    Check "input: works again after a release" ($h6 -ne "") "held=[$h6]"
    C @("stick l center", "focus norender off")

    # 3. Controller dropout: held for 0.5 s, then dropped; back when tracked.
    C @("hand r valid off"); Start-Sleep 2
    C @("hand r valid on"); Start-Sleep 2
    Check "input: dropout held, then dropped, then back" ((Has "controller right: tracking lost, holding the last pose") -and (Has "controller right: pose lost") -and (Has "controller right: tracked again")) "[bridge] controller right: ..."

    # 4. Runtime recenter (xr-sim moves LOCAL onto the head and sends REFERENCE_SPACE_CHANGE_PENDING).
    C @("head pos 0.4 1.5 0.3", "head rot 50 0 0"); Start-Sleep 1
    C @("recenter")
    Check "input: runtime recenter re-anchors" (Wait-For "Recentered \(runtime\)" 5) "Recentered (runtime): ..."
    C @("head rot 0 0 0")

    Check "input: bridge log file" ((Test-Path $bridgeLog) -and (Select-String -Path $bridgeLog -Pattern "init ok" -Quiet) -and (Select-String -Path $bridgeLog -Pattern "Reference space change pending" -Quiet)) $bridgeLog

    # 5. Session lost.
    C @("state lost")
    Check-Stopped "input: session lost"
    Check-NoExceptions "input"
}

if (Want "heading") {
    $ready = Start-Game @("--cvr-dbg-recenter", "60")
    Check "heading: game ready" $ready ""
    C @("head rot 60 0 0")
    $re = Wait-For "Recentered \(key\).*heading (60|300)\.0 deg" 60
    Check "heading: F9 takes the heading" $re "Recentered (key): ... heading 300.0 deg (60 to the left)"
    # Head forward in world after the recenter: straight into the diorama (+z), although the head is turned 60 degrees.
    $n = Count "perf: "
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and (Count "perf: ") -le $n) { Start-Sleep -Milliseconds 300 }
    $line = (Select-String -Path $log -Pattern "perf: " | Select-Object -Last 1).Line
    $m = [regex]::Match($line, 'fwd \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)')
    $ok = $m.Success -and [math]::Abs([double]$m.Groups[1].Value) -lt 0.05 -and [double]$m.Groups[3].Value -gt 0.99
    Check "heading: diorama is in front of the turned head" $ok ("world forward " + $m.Value)
    & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\lifecycle-heading.png") -Quiet | Out-Null
    C @("state exiting")
    Check-Stopped "heading: session exiting"
    Check-NoExceptions "heading"
}

if (Want "faults") {
    $ready = Start-Game @("--cvr-dbg-throw", "28")
    Check "faults: game ready" $ready ""
    $failed = Wait-For "VR feature 'debug hooks' failed" 40
    Check "faults: the failing feature is turned off" $failed "VR feature 'debug hooks' failed and is off ..."
    Check "faults: reported once" ((Count "VR feature 'debug hooks' failed") -eq 1) "one error line"
    $s1 = [int64][regex]::Match((Select-String -Path $log -Pattern "perf: .*submitted" | Select-Object -Last 1).Line, 'submitted (\d+)').Groups[1].Value
    Start-Sleep 5
    $s2 = [int64][regex]::Match((Select-String -Path $log -Pattern "perf: .*submitted" | Select-Object -Last 1).Line, 'submitted (\d+)').Groups[1].Value
    Check "faults: VR keeps submitting" ($s2 -gt $s1 + 200) "submitted $s1 -> $s2"
    C @("instanceloss")
    Check-Stopped "faults: instance loss"
    Check-NoExceptions "faults" "cvr-dbg-throw test exception|VR feature 'debug hooks' failed|at CasualtyVR\.|at \(wrapper"
}
if (Want "imgui") {
    # Mods' OnGUI windows (ImguiCapture): a test window from the probe (--cvr-dbg-imgui) in the middle of the screen.
    $ready = Start-Game @("--cvr-dbg-imgui")
    Check "imgui: game ready" $ready ""
    Check "imgui: windows repaint into the capture" (Wait-For "IMGUI test: repaints into 'CasualtyVR.ImguiTexture'" 30) "IMGUI test: repaints into the capture texture"
    C @("hand r point 15 22"); Start-Sleep 2
    Check "imgui: laser hovers the window (diorama)" (Wait-For "IMGUI test: pointer inside the window" 5) "IMGUI test: pointer inside the window"
    C @("trigger r pull 300"); Start-Sleep 1.5
    C @("trigger r pull 300"); Start-Sleep 1.5
    Check "imgui: laser clicks reach the window (diorama)" (Wait-For "IMGUI test: button clicked \(2\)" 5) "IMGUI test: button clicked (2)"
    # The pointer messages move Unity's own mouse: that must not count as the player taking the real mouse.
    $last = Held | Out-Null; $line = (Select-String -Path $log -Pattern "input: laser" | Select-Object -Last 1).Line
    Check "imgui: the laser keeps the pointer" ($line -match "laser=on") ([regex]::Match($line, 'laser=\S+').Value)
    & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\lifecycle-imgui") -Quiet | Out-Null
    Check-NoExceptions "imgui"

    # First person: the window is on the HUD panel in front of you; pointing at it takes the laser off the world.
    $ready = Start-Game @("--cvr-dbg-imgui", "--cvr-dbg-fp", "--cvr-dbg-clone")
    Check "imgui fp: game ready" $ready ""
    C @("hand r point 10 10"); Start-Sleep 1
    $n = Count "input: laser"
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and (Count "input: laser") -le $n + 1) { Start-Sleep -Milliseconds 300 }
    Check "imgui fp: pointing at the window uses it" (((Select-String -Path $log -Pattern "input: laser" | Select-Object -Last 1).Line) -match "modWindow=True") "modWindow=True"
    C @("trigger r pull 300"); Start-Sleep 1.5
    Check "imgui fp: click reaches the window" (Wait-For "IMGUI test: button clicked \(1\)" 5) "IMGUI test: button clicked (1)"
    # Co-op-style name tag and status icon on a copy of the character (--cvr-dbg-clone): found for first person.
    Check "imgui fp: name tag and icon found" (Wait-For "WorldText: 1 name tags, 1 status icons" 10) "WorldText: 1 name tags, 1 status icons on other characters"
    Check-NoExceptions "imgui fp"
}
Stop-Game
$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$fails = @($results | Where-Object { $_.Result -eq "FAIL" }).Count
Write-Host "$($results.Count - $fails)/$($results.Count) passed"
exit $fails
