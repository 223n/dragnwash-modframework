# Copy the reference assemblies the framework compiles against from your own game
# install into src/DragNWash.ModFramework/libs. They are game and BepInEx files, so
# they are never committed.
#
#   pwsh tools/copy-libs.ps1
#   pwsh tools/copy-libs.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Drag'n Wash"
param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\Drag'n Wash"
)

$ErrorActionPreference = 'Stop'

$libs = Join-Path $PSScriptRoot '..\src\DragNWash.ModFramework\libs'
$managed = Join-Path $GamePath 'DragNWash_Data\Managed'
$core = Join-Path $GamePath 'BepInEx\core'

if (-not (Test-Path -LiteralPath $managed)) {
    throw "Game not found at '$GamePath'. Pass -GamePath."
}
if (-not (Test-Path -LiteralPath $core)) {
    throw "BepInEx is not installed in '$GamePath' (no BepInEx\core)."
}

$files = @(
    @{ From = $core;    Name = 'BepInEx.dll' },
    @{ From = $core;    Name = '0Harmony.dll' },
    @{ From = $managed; Name = 'UnityEngine.dll' },
    @{ From = $managed; Name = 'UnityEngine.CoreModule.dll' }
)

New-Item -ItemType Directory -Force -Path $libs | Out-Null
foreach ($f in $files) {
    Copy-Item -LiteralPath (Join-Path $f.From $f.Name) -Destination $libs -Force
    Write-Host "copied $($f.Name)"
}
