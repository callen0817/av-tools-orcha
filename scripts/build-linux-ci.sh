#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

echo "=== AV Orch Linux CI Build & Test ==="
dotnet build AV.Orch.sln -c Release --nologo
dotnet test tests/AV.Orch.UnitTests/AV.Orch.UnitTests.csproj -c Release --no-build --nologo
dotnet test tests/AV.Orch.IntegrationTests/AV.Orch.IntegrationTests.csproj -c Release --no-build --nologo

echo "=== Publishing win-x64 portable package (unsigned) ==="
mkdir -p build/publish/win-x64 release
dotnet publish src/AV.Orch.Desktop/AV.Orch.Desktop.csproj -c Release -r win-x64 --self-contained false -o build/publish/win-x64 --nologo

echo "=== Generating SHA-256 checksums for built binaries ==="
(cd build/publish/win-x64 && sha256sum AVOrch.exe AVOrch.dll AV.Orch.*.dll > "$REPO_ROOT/release/SHA256SUMS.txt")
cat release/SHA256SUMS.txt
