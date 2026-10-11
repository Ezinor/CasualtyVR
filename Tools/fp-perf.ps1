# First-person cost attribution: runs tools/vr-perf-matrix.ps1 once per variant, each with some [FirstPerson] /
# other config keys overridden in the dev copy's config, which is restored afterwards. "diorama" = first person
# not created (--cvr-dbg-nofp); "fp" = first person forced on (--cvr-dbg-fp) with the config as it is.
#
#   tools/fp-perf.ps1                         the default variant set
#   tools/fp-perf.ps1 -Variants fp,"nolights:FaceLights=false"
#   tools/fp-perf.ps1 -Extra "--cvr-dbg-modifier 8"   (a heavy layer: Infested)
[CmdletBinding()]
param([string]$Extra = "", [string[]]$Variants = @(
    "diorama", "fp",
    "nolights:FaceLights=false",
    "nobillboards:FaceCharacters=false",
    "noarms:Arms=false",
    "nobody:Body=false",
    "noface:FaceObjects=false",
    "void:Sides=Void",
    "bare:FaceLights=false,FaceCharacters=false,Arms=false,Body=false,FaceObjects=false,Sides=Void"))
$ErrorActionPreference = "Stop"
$cfg = Join-Path $PSScriptRoot "..\Working\game-dev\BepInEx\config\casualtyvr.core.cfg"
$saved = Get-Content $cfg -Raw
try {
    foreach ($v in $Variants) {
        $name, $edits = $v.Split(":", 2)
        $text = $saved
        if ($edits) {
            foreach ($e in $edits.Split(",")) {
                $k, $val = $e.Split("=", 2)
                # Only inside [FirstPerson] (several sections share key names such as Enabled).
                $text = [regex]::Replace($text, "(?s)(\[FirstPerson\].*?\n)$k = [^\r\n]*", "`${1}$k = $val")
            }
        }
        Set-Content -Path $cfg -Value $text -NoNewline -Encoding utf8
        $flag = if ($name -eq "diorama") { "--cvr-dbg-nofp" } else { "--cvr-dbg-fp" }
        if ($Extra) { $flag = "$flag $Extra" }
        $r = & (Join-Path $PSScriptRoot "vr-perf-matrix.ps1") -Configs @($flag) 2>&1 | Where-Object { $_ -is [string] -and $_ -match "median" }
        foreach ($line in $r) { "{0,-14} {1}" -f $name, (($line -replace '^\S+\s+', '') -replace '\| args.*', '') }
    }
}
finally { Set-Content -Path $cfg -Value $saved -NoNewline -Encoding utf8 }
