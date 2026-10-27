param(
    [ValidateSet("Smoke", "Clouds", "Cobbles", "Setts", "Dirt", "WetDirt", "Pipes", "All")]
    [string]$Make = "All",
    # Where to write; the project's own textures by default (their .meta files are kept, so
    # nothing that uses them needs relinking). Give a folder to try changes outside the project.
    [string]$Out = ""
)
# Rebuilds the project's own textures from Tools/TextureGen. Windows PowerShell 5.1 (uses .NET's
# System.Drawing; Unity doesn't compile this folder). Unity reimports on focus.
#   .\TextureGen.ps1 -Make Cobbles
#   .\TextureGen.ps1 -Make Smoke -Out C:\Temp\try
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot)
$tex = Join-Path $root "Assets\Art\Textures"
Add-Type -AssemblyName System.Drawing
foreach ($f in "SmokeSheet", "CloudSky", "Cobbles", "PolyHavenGround", "PipesPainted") {
    Add-Type -TypeDefinition (Get-Content (Join-Path $PSScriptRoot "$f.cs") -Raw) -ReferencedAssemblies System.Drawing
}

function Target([string]$projectDir) {
    if ($Out) { New-Item -ItemType Directory -Force $Out | Out-Null; return $Out }
    return $projectDir
}

# A Poly Haven set's 2k PNG maps, downloaded once into Tools/TextureGen/Downloads (git-ignored).
function Fetch([string]$id) {
    $dir = Join-Path $PSScriptRoot "Downloads\$id"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $files = Invoke-RestMethod "https://api.polyhaven.com/files/$id" -UserAgent "TeaLeaf-TextureGen"
    $maps = @{ diff = "Diffuse"; nor_gl = "nor_gl"; disp = "Displacement"; rough = "Rough"; ao = "AO" }
    foreach ($m in $maps.Keys) {
        $file = Join-Path $dir "$m.png"
        if (-not (Test-Path $file)) {
            Invoke-WebRequest $files.($maps[$m]).'2k'.png.url -OutFile $file -UserAgent "TeaLeaf-TextureGen"
        }
    }
    return $dir
}

if ($Make -in "Smoke", "All") {
    $dir = Target (Join-Path $tex "")
    [TextureGen.SmokeSheet]::Run((Join-Path $dir "SmokeFlipbook.png")); "Smoke flipbook written"
}
if ($Make -in "Clouds", "All") {
    $dir = Target (Join-Path $tex "Sky")
    [TextureGen.CloudSky]::Run((Join-Path $dir "SkyClouds.png"), 2048); "Cloud cubemap written"
}
if ($Make -in "Cobbles", "All") {
    $dir = Target (Join-Path $tex "Ground")
    [TextureGen.Cobbles]::Run($dir, "Cobbles"); "Cobbles written"
}
# Last of the cobble layouts: UseSetts() changes the shared fields.
if ($Make -in "Setts", "All") {
    $dir = Target (Join-Path $tex "Ground")
    [TextureGen.Cobbles]::UseSetts()
    [TextureGen.Cobbles]::Run($dir, "Setts"); "Setts written"
}
if ($Make -in "Dirt", "All") {
    $dir = Target (Join-Path $tex "Ground")
    [TextureGen.PolyHavenGround]::Run((Fetch "rock_ground"), $dir, "Dirt", [TextureGen.PolyHavenGround]::Dirt); "Dirt written"
}
# Painted from the pack's pipe atlas (Assets/ASTROFISH_GAMES, re-imported on a fresh clone).
if ($Make -in "Pipes", "All") {
    $dir = Target (Join-Path $root "Assets\Art\Materials\Environment\Pipes")
    New-Item -ItemType Directory -Force $dir | Out-Null
    [TextureGen.PipesPainted]::Run((Join-Path $root "Assets\ASTROFISH_GAMES\STYLZ_MVillage\_Assets\Pipes\Mats"), $dir); "Pipes written"
}
if ($Make -in "WetDirt", "All") {
    $dir = Target (Join-Path $tex "Ground")
    [TextureGen.PolyHavenGround]::Run((Fetch "mud_forest"), $dir, "WetDirt", [TextureGen.PolyHavenGround]::WetDirt); "Wet dirt written"
}
