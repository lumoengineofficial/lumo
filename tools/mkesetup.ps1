<#
  mkesetup.ps1 - build dist\setuplumo.exe (Inno Setup installer) for the beta.

    powershell -ExecutionPolicy Bypass -File tools\mkesetup.ps1

  Steps: pack.ps1 -KeepStage (publish + zips, keep _editor/_game) -> ISCC setup.iss
  Requires: Inno Setup 6 (winget install JRSoftware.InnoSetup)
#>
param(
    [string]$Configuration = "Release",
    [string]$Out = "dist"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe not found - install Inno Setup 6 first" }

$version = "0.1.0"
$m = Select-String -Path (Join-Path $root "src\Lumo.Engine\Core\EngineConstants.cs") -Pattern 'Version = "([^"]+)"'
if ($m) { $version = $m.Matches[0].Groups[1].Value }

$stage = Join-Path $root $Out
$ed = Join-Path $stage "_editor"
$game = Join-Path $stage "_game"

Write-Host "== Packaging (stage kept) ==" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "pack.ps1") -Configuration $Configuration -Out $Out -KeepStage
if ($LASTEXITCODE -ne 0) { throw "pack.ps1 failed" }
if (-not (Test-Path (Join-Path $ed "Lumo.Editor.exe"))) { throw "staged editor missing: $ed" }

Write-Host "== Compiling setuplumo.exe ==" -ForegroundColor Cyan
$iss = Join-Path $PSScriptRoot "setup.iss"
& $iscc "/DStage=$stage" "/DVersion=$version" "/DOutDir=$stage" "/DRepoRoot=$root" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Remove-Item $ed -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $game -Recurse -Force -ErrorAction SilentlyContinue

$setup = Join-Path $stage "setuplumo.exe"
if (-not (Test-Path $setup)) { throw "setuplumo.exe not produced" }
Write-Host ("setuplumo.exe: {0:N1} MB" -f ((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
