# Builds leviathan.bundle next to this script from ..\leviathan.fbx and ..\textures. Waits while another Unity runs.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$src = (Resolve-Path "$here\..").Path
$root = (Resolve-Path "$here\..\..\..\..\..").Path
$proj = Join-Path $root 'work\kk\unity'
$u = 'C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe'
$log = Join-Path $here 'build.log'
while (Get-Process Unity -ErrorAction SilentlyContinue) { Start-Sleep 15 }
New-Item -ItemType Directory -Force "$proj\Assets\Editor" | Out-Null
Copy-Item "$here\KKLeviathanBuild.cs" "$proj\Assets\Editor\KKLeviathanBuild.cs" -Force
$a = @('-batchmode', '-force-d3d11', '-projectPath', "`"$proj`"", '-executeMethod', 'KKLeviathanBuild.Run', '-quit', '-logFile', "`"$log`"", '-kkSrc', "`"$src`"", '-kkBundleDir', "`"$here`"")
$pr = Start-Process $u -ArgumentList $a -PassThru -WindowStyle Hidden
if (-not $pr.WaitForExit(900000)) { $pr.Kill(); throw 'TIMEOUT' }
"unity exit $($pr.ExitCode)"
Select-String $log -Pattern 'error CS|KKLev\]' | ForEach-Object Line | Select-Object -First 60
