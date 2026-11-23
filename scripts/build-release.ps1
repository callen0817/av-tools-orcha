#Requires -Version 5.1
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "build\publish\$Runtime"
$ReleaseDir = Join-Path $RepoRoot "release"
$SelfContained = if ($FrameworkDependent) { "false" } else { "true" }

function Invoke-CheckedDotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Write-Host "=== AV Orch Release Build ($Configuration / $Runtime / SelfContained=$SelfContained) ===" -ForegroundColor Yellow

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

Write-Host "[1/5] Running unit and integration tests..."
Invoke-CheckedDotNet @("test", (Join-Path $RepoRoot "tests\AV.Orch.UnitTests\AV.Orch.UnitTests.csproj"), "-c", $Configuration, "--nologo")
Invoke-CheckedDotNet @("test", (Join-Path $RepoRoot "tests\AV.Orch.IntegrationTests\AV.Orch.IntegrationTests.csproj"), "-c", $Configuration, "--nologo")

Write-Host "[2/5] Publishing AV.Orch.Desktop ($Runtime, self-contained=$SelfContained)..."
Invoke-CheckedDotNet @(
    "publish",
    (Join-Path $RepoRoot "src\AV.Orch.Desktop\AV.Orch.Desktop.csproj"),
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $SelfContained,
    "-o", $PublishDir,
    "--nologo"
)

$ZipPath = Join-Path $ReleaseDir "AVOrch-1.0.0-$Runtime-unsigned.zip"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath

if (-not $SkipInstaller) {
    Write-Host "[3/5] Building WiX MSI Installer..."
    Invoke-CheckedDotNet @("build", (Join-Path $RepoRoot "installer\AV.Orch.Installer.wixproj"), "-c", $Configuration, "--nologo")
    $MsiSource = Get-ChildItem -Path (Join-Path $RepoRoot "installer\bin") -Filter "*.msi" -Recurse | Select-Object -First 1
    if ($null -eq $MsiSource) {
        throw "WiX build succeeded but no .msi file was found under installer\bin."
    }
    Copy-Item $MsiSource.FullName -Destination (Join-Path $ReleaseDir $MsiSource.Name) -Force
}

Write-Host "[4/5] Generating SHA-256 checksums..."
$ChecksumFile = Join-Path $ReleaseDir "SHA256SUMS.txt"
Get-ChildItem -Path $ReleaseDir -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | ForEach-Object {
    $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
} | Set-Content -Path $ChecksumFile -Encoding UTF8

Write-Host "[5/5] Complete! Unsigned artifacts written to: $ReleaseDir" -ForegroundColor Green
Write-Host "NOTE: Sign AVOrch.exe and the MSI with Artificial Vision's EV Authenticode certificate before customer distribution."
