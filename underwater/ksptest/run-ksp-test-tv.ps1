# Unattended in-game test with the full visual mod stack: True Volumetrics, EVE, Scatterer, Deferred. The gold standard for the underwater work.
# Runs in its own copy C:\KSP_kktv. The mods come from the Steam install (only read), the game files from the stock copy C:\KSP_kktest.
# The mod list is the one of C:\KSP_rvperf, known to load; the copy takes the Steam DLLs, not the experimental ones of that copy.
# Refuses to run while any KSP is open. Same flag and results as run-ksp-test.ps1.
param([int]$TimeoutMin = 40, [switch]$PrepareOnly, [string]$Dll, [string]$Scenes = 'uw_light')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..\..\..\..").Path   # this copy sits in project\KrakensKreations\underwater\ksptest
$results = Join-Path $root 'work\kk\ksptest\results'
$steam = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program'
$base = 'C:\KSP_kktest'
$ksp = 'C:\KSP_kktv'
$dst = "$ksp\GameData\UnkerbalThings"
$flag = Join-Path $dst 'kktest.flag'
$mods = '000_Harmony', '000_KSPBurst', '000_NiakoUtils', '001_DuckweedUtils', 'BetterDres', 'BetterKerbol', 'CTTP', 'EnvironmentalVisualEnhancements',
    'KSPCommunityFixes', 'KSPTextureLoader', 'Kopernicus', 'KopernicusExpansion', 'MPE', 'ModularFlightIntegrator', 'OPM', 'ScaledDecorator', 'Scatterer',
    'ScattererAtmosphereCache', 'Shabby', 'SigmaDimensions', 'Squad', 'SquadExpansion', 'StockScattererConfigs', 'StockVolumetricClouds', 'zzz_Deferred'

if (-not (Test-Path "$ksp\KSP_x64.exe")) {
    "making the copy in $ksp"
    & robocopy $base $ksp /E /NFL /NDL /NJH /NJS /NP /XD "$base\GameData" "$base\saves" "$base\Logs" /XF KSP.log PartDatabase.cfg | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed: $LASTEXITCODE" }
    foreach ($d in $mods) {
        & robocopy "$steam\GameData\$d" "$ksp\GameData\$d" /E /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed on ${d}: $LASTEXITCODE" }
    }
    Copy-Item "$steam\GameData\ModuleManager.4.2.3.dll" "$ksp\GameData\" -Force
    Copy-Item "$steam\settings.cfg" "$ksp\settings.cfg" -Force
    New-Item -ItemType Directory -Force "$ksp\saves" | Out-Null
}
if ($PrepareOnly -and -not $Dll) { "prepared: $ksp"; exit 0 }
if (Get-Process KSP_x64 -ErrorAction SilentlyContinue) { throw 'KSP is running. Close it first.' }
$pkg = Join-Path $root "work\kk\package\GameData\UnkerbalThings"
& robocopy $pkg $dst /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed: $LASTEXITCODE" }
if ($Dll) { Copy-Item $Dll "$dst\UnkerbalThings.dll" -Force }
if ($env:KK_TEST_SETTINGS) { Copy-Item $env:KK_TEST_SETTINGS "$dst\settings.cfg" -Force }
if ($PrepareOnly) { "prepared: $ksp"; exit 0 }
$Dir = Join-Path $results ('tv-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $Dir | Out-Null
@"
outDir = $Dir
craft = Ships/VAB/Kerbal 1.craft
scenes = $Scenes
"@ | Set-Content $flag -Encoding ascii
$start = Get-Date
try {
    $proc = Start-Process "$ksp\KSP_x64.exe" -WorkingDirectory $ksp -PassThru
    "launched KSP at $start, results $Dir"
    while ($true) {
        Start-Sleep 10
        if ((Test-Path "$Dir\DONE") -or (Test-Path "$Dir\FAILED")) {
            for ($i = 0; $i -lt 40 -and -not $proc.HasExited; $i++) { Start-Sleep 3 }
            break
        }
        if ($proc.HasExited) { 'KSP exited before the test finished (closed or crashed)'; break }
        if (((Get-Date) - $start).TotalMinutes -gt $TimeoutMin) {
            'timeout: stopping the KSP this script started'
            $proc | Stop-Process -Force
            break
        }
    }
} finally {
    Remove-Item $flag -ErrorAction SilentlyContinue
}
Copy-Item "$ksp\KSP.log" "$Dir\KSP.log" -ErrorAction SilentlyContinue
"elapsed: $([int]((Get-Date) - $start).TotalMinutes) min, results $Dir"
if (Test-Path "$Dir\FAILED") { "FAILED: $(Get-Content "$Dir\FAILED" -Raw)"; exit 1 }
