#Requires -Version 5.1
Write-Host "=== AV Orch Real-Revit Acceptance Test Runner ===" -ForegroundColor Yellow
Write-Host "PREREQUISITES:"
Write-Host "  1. Windows 10/11 (standard non-elevated user)"
Write-Host "  2. Autodesk Revit (2021-2027) open with a NON-PRODUCTION sample model"
Write-Host "  3. pyRevit 7.1+ installed with 'pyrevit configs agent enable'"
Write-Host "  4. Codex, Gemini CLI, and/or Claude Code installed and signed in"
Write-Host ""

$env:AV_ORCH_REAL_REVIT = "1"
$RepoRoot = Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $RepoRoot "tests\AV.Orch.AcceptanceTests\AV.Orch.AcceptanceTests.csproj") -c Release --nologo --verbosity normal
