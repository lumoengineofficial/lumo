<#
  pack.ps1 — build distributable Windows zips for the beta.

    powershell -ExecutionPolicy Bypass -File tools\pack.ps1
    powershell -ExecutionPolicy Bypass -File tools\pack.ps1 -SkipEditor    # game zip only
    powershell -ExecutionPolicy Bypass -File tools\pack.ps1 -SkipGame      # editor zip only

  Output (in dist/):
    LumoEngine-<version>-win-x64.zip   self-contained editor (no .NET install needed)
    Wildlands-<version>-win-x64.zip    self-contained game player + project
#>
param(
    [string]$Configuration = "Release",
    [string]$Rid = "win-x64",
    [string]$Out = "dist",
    [string]$GameProject = (Join-Path $env:USERPROFILE "Documents\LumoProjects\Wildlands"),
    [switch]$SkipEditor,
    [switch]$SkipGame,
    [switch]$KeepStage   # keep _editor/_game staging dirs (for setup.iss)
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$dotnet = if ($env:DOTNET_HOST_PATH -and (Test-Path $env:DOTNET_HOST_PATH)) { $env:DOTNET_HOST_PATH }
          elseif (Test-Path "C:\Program Files\dotnet\dotnet.exe") { "C:\Program Files\dotnet\dotnet.exe" }
          else { "dotnet" }

$version = "0.1.0"
$m = Select-String -Path (Join-Path $root "src\Lumo.Engine\Core\EngineConstants.cs") -Pattern 'Version = "([^"]+)"'
if ($m) { $version = $m.Matches[0].Groups[1].Value }

$stage = Join-Path $root $Out
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Publish-App([string]$project, [string]$dest) {
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    & $dotnet publish $project -c $Configuration -r $Rid --self-contained true `
        -o $dest --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $project" }
}

function Copy-Tree([string]$src, [string]$dst) {
    if (-not (Test-Path $src)) { return }
    if (Test-Path $src -PathType Leaf) {
        $dir = Split-Path -Parent $dst
        if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Copy-Item $src $dst -Force
        return
    }
    robocopy $src $dst /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed: $src" }
    $global:LASTEXITCODE = 0
}

# All three plugins deploy themselves to bin/<cfg>/<tfm>/Plugins as a side
# effect of building. Publish only builds referenced projects, so build the
# whole solution first — otherwise Lumo.GraphicsPlugin never lands in the zip.
Write-Host "== Building solution ($Configuration) ==" -ForegroundColor Cyan
& $dotnet build (Join-Path $root "LumoEngine.sln") -c $Configuration -m:1 --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "solution build failed" }

# ---------------------------------------------------------------- editor zip
if (-not $SkipEditor) {
    Write-Host "== Publishing editor ($Rid, $Configuration) ==" -ForegroundColor Cyan
    $ed = Join-Path $stage "_editor"
    Publish-App (Join-Path $root "src\Lumo.Editor\Lumo.Editor.csproj") $ed

    # Plugin DLLs deploy next to the build output, not into publish output.
    $tfm = "net10.0"
    $binPlugins = Join-Path $root "src\Lumo.Editor\bin\$Configuration\$tfm\Plugins"
    Copy-Tree $binPlugins (Join-Path $ed "Plugins")

    $readme = @"
Lumo Engine $version (beta) — Windows x64
=========================================

Run Lumo.Editor.exe to open the editor. No .NET installation required.

Open the bundled Wildlands project:
  1. On the home screen open the settings gear (top right)
  2. "Projects folder" > Browse > pick the "Projects" folder next to this exe
     (alternatively copy Projects\Wildlands into Documents\LumoProjects)
  3. Select Wildlands > Edit (or double-click), then press Play

Controls in play mode: WASD move, mouse look, Shift sprint, Space jump,
Esc release mouse. Collect all 25 apples to win.

Docs & issue tracker: https://github.com/lumoengineofficial/lumo
"@
    Set-Content -Path (Join-Path $ed "README.txt") -Value $readme -Encoding UTF8

    Copy-Tree (Join-Path $root "KNOWN_BUGS.md") (Join-Path $ed "KNOWN_BUGS.md")
    if (Test-Path (Join-Path $GameProject "Project.json")) {
        Copy-Tree $GameProject (Join-Path $ed "Projects\Wildlands")
        # Exports/Builds are huge local build artifacts — keep them out of the zip.
        $built = Join-Path $ed "Projects\Wildlands\Builds"
        if (Test-Path $built) { Remove-Item $built -Recurse -Force }
    }

    $zip = Join-Path $stage "LumoEngine-$version-$Rid.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Write-Host "== Zipping editor -> $zip ==" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $ed "*") -DestinationPath $zip -CompressionLevel Optimal
    if (-not $KeepStage) { Remove-Item $ed -Recurse -Force } else { Write-Host "stage kept: $ed" -ForegroundColor DarkGray }
    Write-Host ("editor zip: {0:N1} MB" -f ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
}

# ----------------------------------------------------------------- game zip
if (-not $SkipGame) {
    if (-not (Test-Path (Join-Path $GameProject "Project.json"))) {
        Write-Warning "Game project not found: $GameProject (skipping game zip)"
    } else {
        Write-Host "== Publishing game ($Rid, $Configuration) ==" -ForegroundColor Cyan
        $game = Join-Path $stage "_game"
        Publish-App (Join-Path $root "src\Lumo.Runtime\Lumo.Runtime.csproj") $game

        $tfm = "net10.0"
        $binPlugins = Join-Path $root "src\Lumo.Runtime\bin\$Configuration\$tfm\Plugins"
        Copy-Tree $binPlugins (Join-Path $game "Plugins")

        foreach ($d in "Scenes", "Graphs", "Scripts", "Assets", "Plugins") {
            Copy-Tree (Join-Path $GameProject $d) (Join-Path $game $d)
        }
        Copy-Item (Join-Path $GameProject "Project.json") (Join-Path $game "Project.json") -Force

        $readme = @"
Wildlands — a Lumo Engine beta game (Windows x64)
==================================================

Run Lumo.Runtime.exe. No .NET installation required.

Collect all 25 apples. WASD move, mouse look (click the window),
Shift sprint, Space jump, Esc release mouse.

Made with Lumo Engine: https://github.com/lumoengineofficial/lumo
"@
        Set-Content -Path (Join-Path $game "README.txt") -Value $readme -Encoding UTF8
        Copy-Tree (Join-Path $root "KNOWN_BUGS.md") (Join-Path $game "KNOWN_BUGS.md")

        $zip = Join-Path $stage "Wildlands-$version-$Rid.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Write-Host "== Zipping game -> $zip ==" -ForegroundColor Cyan
        Compress-Archive -Path (Join-Path $game "*") -DestinationPath $zip -CompressionLevel Optimal
        if (-not $KeepStage) { Remove-Item $game -Recurse -Force } else { Write-Host "stage kept: $game" -ForegroundColor DarkGray }
        Write-Host ("game zip: {0:N1} MB" -f ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
    }
}

Write-Host "Done. Zips in $stage" -ForegroundColor Green
Get-ChildItem $stage -Filter *.zip | ForEach-Object { Write-Host ("  {0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
