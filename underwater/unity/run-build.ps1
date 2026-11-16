# Builds bloop.bundle from bloop.fbx. Waits while another Unity runs.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = (Resolve-Path "$here\..\..\..\..").Path
$proj = Join-Path $root 'work\kk\unity'
$u = 'C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe'
$log = Join-Path $here 'build.log'
while (Get-Process Unity -ErrorAction SilentlyContinue) { Start-Sleep 15 }
New-Item -ItemType Directory -Force "$proj\Assets\Editor" | Out-Null
Copy-Item "$here\KKBloopBuild.cs" "$proj\Assets\Editor\KKBloopBuild.cs" -Force
$a = @('-batchmode', '-force-d3d11', '-projectPath', "`"$proj`"", '-executeMethod', 'KKBloopBuild.Run', '-quit', '-logFile', "`"$log`"", '-kkFbx', "`"$root\work\kk\blender\bloop.fbx`"", '-kkBundleDir', "`"$root\work\kk\rig\out`"")
$pr = Start-Process $u -ArgumentList $a -PassThru -WindowStyle Hidden
if (-not $pr.WaitForExit(900000)) { $pr.Kill(); throw 'TIMEOUT' }
"unity exit $($pr.ExitCode)"
Select-String $log -Pattern 'error CS|KKBloop\]' | ForEach-Object Line | Select-Object -First 40
