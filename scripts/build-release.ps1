#Requires -Version 5.1
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "build\publish\$Runtime"
$ReleaseDir = Join-Path $RepoRoot "release"

Write-Host "=== AV Orch Release Build ($Configuration / $Runtime) ===" -ForegroundColor Yellow

New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

Write-Host "[1/5] Running unit and integration tests..."
dotnet test (Join-Path $RepoRoot "AV.Orch.sln") -c $Configuration --nologo

Write-Host "[2/5] Publishing AV.Orch.Desktop ($Runtime)..."
dotnet publish (Join-Path $RepoRoot "src\AV.Orch.Desktop\AV.Orch.Desktop.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $PublishDir `
    --nologo

$ZipPath = Join-Path $ReleaseDir "AVOrch-1.0.0-$Runtime-unsigned.zip"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath

if (-not $SkipInstaller) {
    Write-Host "[3/5] Building WiX MSI Installer..."
    dotnet build (Join-Path $RepoRoot "installer\AV.Orch.Installer.wixproj") -c $Configuration --nologo
    $MsiSource = Get-ChildItem -Path (Join-Path $RepoRoot "installer\bin") -Filter "*.msi" -Recurse | Select-Object -First 1
    if ($null -ne $MsiSource) {
        Copy-Item $MsiSource.FullName -Destination (Join-Path $ReleaseDir $MsiSource.Name) -Force
    }
}

Write-Host "[4/5] Generating SHA-256 checksums..."
$ChecksumFile = Join-Path $ReleaseDir "SHA256SUMS.txt"
Get-ChildItem -Path $ReleaseDir -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | ForEach-Object {
    $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
} | Set-Content -Path $ChecksumFile -Encoding UTF8

Write-Host "[5/5] Complete! Unsigned artifacts written to: $ReleaseDir" -ForegroundColor Green
Write-Host "NOTE: Sign AVOrch.exe and the MSI with Artificial Vision's EV Authenticode certificate before customer distribution."
