# AV Orch

**Universal local AI-to-Revit MCP connector by Artificial Vision, Inc.**

AV Orch connects your existing AI coding assistants — **OpenAI Codex CLI**, **Google Gemini CLI**, and **Anthropic Claude Code** — to a live **Autodesk Revit** session through **pyRevit 7.x+ MCP Agent Runtime**, without requiring a custom AI chat interface, cloud proxy, or separate AI subscription from Artificial Vision.

## V1.0 Capabilities

- **Windows 10/11 WPF Desktop Application** (`AVOrch.exe`) with Light and Dark themes (white, black, restrained gold).
- **Three Independent AI Provider Adapters** (`IAiProviderAdapter`):
  - **OpenAI Codex CLI** (`CodexAdapter`) — native `pyrevit mcp install codex` + surgical `~/.codex/config.toml` backup/merge.
  - **Google Gemini CLI** (`GeminiAdapter`) — dedicated local stdio MCP adapter for `~/.gemini/settings.json` with `trust: false` and surgical backup/merge.
  - **Anthropic Claude Code** (`ClaudeCodeAdapter`) — native `pyrevit mcp install claude` + surgical `~/.claude.json` backup/merge.
- **Multi-Provider Coexistence**: Enable one, two, or all three providers simultaneously without overwriting unrelated MCP servers or settings.
- **pyRevit 7.x+ MCP Integration**: Detects Revit (2021–2027) and pyRevit, manages `pyrevit configs agent`, enforces **Read-Only** by default, and supports **Ask Before Modifications** (native Revit Keep/Discard approval).
- **Reversible & Safe**: Timestamped SHA-256 configuration backups before every edit, automatic rollback on validation failure, secret redaction in local logs, zero telemetry by default, and clean uninstall (`--uninstall-cleanup`).

## Quick Start (Build & Test)

```bash
# Build Release & run unit + integration tests (Linux/macOS/Windows)
dotnet build AV.Orch.sln -c Release
dotnet test tests/AV.Orch.UnitTests/AV.Orch.UnitTests.csproj -c Release
dotnet test tests/AV.Orch.IntegrationTests/AV.Orch.IntegrationTests.csproj -c Release
```

On Windows 10/11 with WiX Toolset v5 SDK installed:

```powershell
.\scripts\build-release.ps1
```

## Documentation

- [Architecture](docs/architecture.md)
- [Provider Compatibility Matrix](docs/compatibility.md)
- [Installation Guide](docs/installation.md)
- [User Guide](docs/user-guide.md)
- [Security & Trust Boundaries](docs/security.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Release Checklist](docs/release-checklist.md)
- [Build & Verification Report](AV_ORCH_BUILD_REPORT.md)

## Disclaimer

AV Orch is a commercial product of **Artificial Vision, Inc.** It is not affiliated with, endorsed by, or sponsored by Autodesk, Inc., OpenAI, Google LLC, Anthropic PBC, or pyRevitLabs.
