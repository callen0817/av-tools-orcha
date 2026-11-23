# AV Orch Architecture

## Design Principles

1. **Zero Custom Revit Execution Engine**: AV Orch delegates all Revit API execution and modification approvals to **pyRevit 7.x+ Agent Runtime (MCP)**.
2. **Strict License Separation**: AV Orch's proprietary C#/.NET codebase never links against, embeds, or redistributes GPL-3.0 pyRevit source or binaries. All interaction occurs via official `pyrevit.exe` CLI subprocesses and standard JSON-RPC 2.0 MCP over stdio.
3. **Provider Neutrality**: Every AI assistant implements `IAiProviderAdapter` independently (`CodexAdapter`, `GeminiAdapter`, `ClaudeCodeAdapter`). Customers can enable one, two, or all three simultaneously.
4. **Surgical & Reversible Configuration**: Before modifying any provider configuration file (`~/.claude.json`, `~/.codex/config.toml`, `~/.gemini/settings.json`), AV Orch creates a timestamped SHA-256 backup under `%LOCALAPPDATA%\ArtificialVision\AVOrch\backups`, applies the smallest surgical update, validates the result, and rolls back automatically on failure.
5. **Local-Only, Zero Telemetry**: No cloud backend, no AI proxy, no password/token storage, and no telemetry.

## Layer Overview

| Project | Target | Responsibility |
|---|---|---|
| `AV.Orch.Core` | `net8.0` | Domain models, `IAiProviderAdapter`, process runner, atomic filesystem, JSON settings, structured redacted logger, audit log, diagnostics |
| `AV.Orch.Security` | `net8.0` | `SecretRedactor`, `PathValidator`, `PermissionPolicyGuard`, `ConfigurationBackupService`, `TrustBoundaryDisclosure` |
| `AV.Orch.Revit` | `net8.0` | `RevitDiscoveryService`, `PyRevitService`, `McpStdioProbe` (real JSON-RPC 2.0 `initialize` + `tools/list` probe) |
| `AV.Orch.Providers` | `net8.0` | `CodexAdapter`, `GeminiAdapter`, `ClaudeCodeAdapter`, `JsonMcpConfigHelper`, `TomlMcpConfigHelper` |
| `AV.Orch.Installation` | `net8.0` | `DependencyDiscoveryService`, `DependencyInstallWorkflow`, `UninstallCleanupService`, `AvOrchOrchestrator` |
| `AV.Orch.Desktop` | `net8.0-windows` | WPF desktop UI (Home, Providers, Revit, Permissions, Diagnostics, Settings), Light/Dark themes, `--uninstall-cleanup` switch |
| `installer/` | WiX v5 | Per-user Windows MSI installer with stable `UpgradeCode`, repair, upgrade, and optional MCP cleanup on uninstall |
