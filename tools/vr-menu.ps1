# VR settings menu checks (docs/BUILD_PLAN.md Phase 1, docs/VRMENU_REVIEW.md §6). Dev copy only (tools/launch-dev.ps1);
# restores the dev config afterwards (the laser clicks change settings).
#
#   tools/vr-menu.ps1                 all scenarios; exit code = number of failures
#   tools/vr-menu.ps1 -Only laser
#
# Scenarios (each a fresh game, seed 1234, diorama):
#   layout   every tab, then every tab with "More settings": each control's angular size at the default panel
#            (VR settings check lines: >= 2.5 degrees) and the smallest text; an xr-sim capture per tab
#            (Working/menu-phase1/)
#   laser    the right hand's laser (xr-sim "hand r point", angles from --cvr-dbg-menu-aim) clicks a switch row, a
#            segment, drags a slider, opens More settings, scrolls with the stick and changes tab
#   fp       the same panel opens in first person, in front of the head; the sticks do not walk meanwhile
[CmdletBinding()]
param([string[]]$Only = @())
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$d = "$env:LOCALAPPDATA\xr-sim\casualtyvr"
$t = Join-Path $root "tools\xr-sim\tools"
$bepinexLog = Join-Path $root "Working\game-dev\BepInEx\LogOutput.log"
$cfg = Join-Path $root "Working\game-dev\BepInEx\config\casualtyvr.core.cfg"
$cfgBackup = Join-Path $env:TEMP "casualtyvr.core.cfg.vr-menu"
$log = $bepinexLog
$results = New-Object System.Collections.Generic.List[object]

function C([string[]]$c) { & "$t\xrsim-cmd.ps1" -Dir $d -Quiet @c | Out-Null }
function Has([string]$pattern) { (Test-Path $log) -and (Select-String -Path $log -Pattern $pattern -Quiet) }
function Wait-For([string]$pattern, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) { if (Has $pattern) { return $true }; Start-Sleep -Milliseconds 400 }
    return $false
}
function Stop-Game { Get-Process CasualtiesUnknown -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*game-dev*" } | Stop-Process -Force; Start-Sleep 2 }
function Start-Game([string[]]$extra) {
    Stop-Game
    Copy-Item $cfgBackup $cfg -Force   # each scenario starts from the dev config
    Remove-Item $bepinexLog -ErrorAction SilentlyContinue
    $script:log = $bepinexLog
    $started = Get-Date
    & (Join-Path $root "tools\launch-dev.ps1") -Windowed -XrSim -NoWait (@("--cvr-vr", "--cvr-autostart", "--cvr-seed", "1234") + $extra) | Out-Null
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 300
        $newest = Get-ChildItem (Join-Path $root "Working\game-dev\CasualtyVR_Logs\Player-*.log") | Where-Object { $_.CreationTime -ge $started.AddSeconds(-2) } | Sort-Object CreationTime | Select-Object -Last 1
    } while ($null -eq $newest -and (Get-Date) -lt $deadline)
    if ($newest) { $script:log = $newest.FullName }
    $ready = Wait-For "perf: ([5-9]\d)\.\d fps.* voxels [1-9]" 180
    C @("input clear", "head rot 0 0 0")
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
function Aim([string]$name) {
    $m = Select-String -Path $log -Pattern ("VR settings aim '" + [regex]::Escape($name) + "': point (\S+) (\S+)") | Select-Object -Last 1
    if (-not $m) { return $null }
    return "hand r point {0} {1}" -f $m.Matches[0].Groups[1].Value, $m.Matches[0].Groups[2].Value
}
function Click([string]$name) {
    $a = Aim $name
    if (-not $a) { return $false }
    C @($a); Start-Sleep -Milliseconds 700
    C @("trigger r 1"); Start-Sleep -Milliseconds 300
    C @("trigger r 0"); Start-Sleep -Milliseconds 700
    return $true
}

Copy-Item $cfg $cfgBackup -Force
try {
    if (Want "layout") {
        $out = Join-Path $root "Working\menu-phase1"
        New-Item -ItemType Directory -Force $out | Out-Null
        foreach ($more in @($false, $true)) {
            $extra = @("--cvr-dbg-settings", "all") + $(if ($more) { @("--cvr-dbg-settings-more") } else { @() })
            $ready = Start-Game $extra
            Check "layout$(if ($more) { ' more' }): game ready" $ready ""
            # The menu opens 8 s into the level and shows each of the 8 tabs for 3 s, round and round: capture each tab
            # about a second after it appears (a line already there when this loop starts may be nearly 3 s old).
            $seen = @{}
            $count = @(Select-String -Path $log -Pattern "VR settings: tab '([^']+)'").Count
            $deadline = (Get-Date).AddSeconds(75)
            while ((Get-Date) -lt $deadline -and $seen.Count -lt 8) {
                $lines = @(Select-String -Path $log -Pattern "VR settings: tab '([^']+)'")
                if ($lines.Count -gt $count) {
                    $count = $lines.Count
                    $tab = $lines[-1].Matches[0].Groups[1].Value
                    if (-not $seen.ContainsKey($tab)) {
                        Start-Sleep -Milliseconds 1000
                        $name = ($tab -replace '[^A-Za-z0-9]', '') + $(if ($more) { "-more" } else { "" })
                        & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $out $name) -Quiet | Out-Null
                        $seen[$tab] = $true
                    }
                }
                Start-Sleep -Milliseconds 200
            }
            Start-Sleep 2
            $checks = @(Select-String -Path $log -Pattern "VR settings check: (.*)" | ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
            Check "layout$(if ($more) { ' more' }): all tabs checked" ($checks.Count -ge 8) ("{0} check lines" -f $checks.Count)
            foreach ($c in $checks) { Check ("layout: " + ($c -replace ':.*', '')) ($c -notmatch "FAIL") $c }
            Check-NoExceptions "layout$(if ($more) { ' more' })"
        }
    }

    if (Want "laser") {
        $ready = Start-Game @("--cvr-dbg-settings", "comfort", "--cvr-dbg-menu-aim")
        Check "laser: game ready" $ready ""
        Check "laser: aim angles logged" (Wait-For "VR settings aim 'more'" 30) (Aim "switch Blackout")
        Check "laser: panel cropped to the menu" (Has "UI mode: dashboard") "UI mode: dashboard"
        [void](Click "switch Blackout")
        Check "laser: a click on a switch row toggles it" (Wait-For "VR settings: FirstPerson\.Blackout = False" 5) "FirstPerson.Blackout = False"
        [void](Click "segment Snap")
        Check "laser: a segment click chooses it" (Wait-For "VR settings: FirstPerson\.Turning = Snap" 5) "FirstPerson.Turning = Snap (the dev config has Smooth)"
        # Drag the fall-vignette slider from 20 % to 80 % with the trigger held.
        C @((Aim "slider Fall vignette 0.2")); Start-Sleep -Milliseconds 700
        C @("trigger r 1"); Start-Sleep -Milliseconds 500
        $v1 = Wait-For "VR settings: FirstPerson\.FallVignette = 0\.2" 3
        C @((Aim "slider Fall vignette 0.8")); Start-Sleep -Milliseconds 800
        C @("trigger r 0"); Start-Sleep -Milliseconds 500
        $v2 = Wait-For "VR settings: FirstPerson\.FallVignette = 0\.8" 3
        Check "laser: slider press sets the value under the pointer" $v1 "FallVignette = 0.2"
        Check "laser: slider drag follows the pointer" $v2 "FallVignette = 0.8"
        [void](Click "segment Moderate")
        Check "laser: the comfort preset sets its settings" ((Wait-For "Comfort preset: Moderate" 5) -and (Wait-For "VR settings: FirstPerson\.SnapBlink = True" 3)) "Comfort preset: Moderate; FirstPerson.SnapBlink = True"
        [void](Click "more")
        Check "laser: More settings shows the tuning rows" (Wait-For "VR settings: tab 'Comfort' \(more\)" 5) "tab 'Comfort' (more)"
        [void](Click "tab View")
        Check "laser: a tab click changes the page" (Wait-For "VR settings: tab 'View'" 5) "tab 'View'"
        # View has more rows than fit once More settings is on: the stick scrolls.
        C @("stick r 0 -1"); Start-Sleep -Milliseconds 900; C @("stick r 0 0"); Start-Sleep -Milliseconds 500
        Check "laser: the stick scrolls the list" (Wait-For "VR settings: scrolled to row [1-9]" 3) ((Select-String -Path $log -Pattern "VR settings: scrolled to row \d+" | Select-Object -Last 1).Line)
        & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\menu-phase1\laser") -Quiet | Out-Null
        Check-NoExceptions "laser"
    }

    if (Want "fp") {
        $ready = Start-Game @("--cvr-dbg-fp", "--cvr-dbg-cave", "--cvr-dbg-settings", "comfort", "--cvr-dbg-menu-aim")
        Check "fp: game ready" $ready ""
        Check "fp: menu opens on its own panel" (Wait-For "VR settings aim 'more'" 40) (Aim "tab View")
        [void](Click "switch Blackout")
        Check "fp: the laser clicks the panel" (Wait-For "VR settings: FirstPerson\.Blackout = False" 5) "FirstPerson.Blackout = False"
        # The verbose input line (every 2 s) lists the keys the game sees: pushing the left stick must add none.
        C @("stick l 0 1"); Start-Sleep 4.5
        $line = (Select-String -Path $log -Pattern "input: laser" | Select-Object -Last 1).Line
        C @("stick l 0 0")
        $held = [regex]::Match($line, 'held=\[([^\]]*)\]').Groups[1].Value
        Check "fp: the left stick does not walk while the menu is open" ($held -eq "") "held=[$held]"
        & "$t\xrsim-shot.ps1" -Dir $d -Out (Join-Path $root "Working\menu-phase1\fp") -Quiet | Out-Null
        Check-NoExceptions "fp"
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
