# AV Orch v1.0.0 — Build & Verification Report

**Product:** AV Orch  
**Publisher:** Artificial Vision, Inc.  
**Version:** 1.0.0  
**Build Date:** 2026-11-23  
**Build Environment:** Ubuntu 24.04 x64, .NET SDK 10.0.115 / Target `net8.0` & `net8.0-windows`  

> **Production Readiness Status:** **NOT YET PRODUCTION-READY** — All core application code, provider adapters, WPF desktop UI, WiX v5 installer definitions, documentation, and automated unit/integration tests are implemented and passing, and an unsigned `win-x64` portable binary (`AVOrch.exe`) was published. However, **Windows MSI compilation, Authenticode signing, and real-system Autodesk Revit + pyRevit + live AI account acceptance tests have NOT been executed** because this build environment is Linux without Windows, WiX, or Autodesk Revit.

---

## 1. Status Matrix

### IMPLEMENTED & AUTOMATED TESTED
- **Core Architecture & Infrastructure** (`src/AV.Orch.Core`): Domain models, `IAiProviderAdapter`, safe `ArgumentList`-based `SystemProcessRunner`, atomic `LocalFileSystem`, JSON settings persistence (`TelemetryEnabled` always forced `false`), redacted structured logger, local JSONL audit history, and 10-category `DiagnosticsService`.
- **Security & Trust Boundaries** (`src/AV.Orch.Security`): `SecretRedactor` (OpenAI/Anthropic/Google keys, Bearer tokens, credential fields), `PathValidator` (blocks UNC paths & shell metacharacters, supports spaces & Unicode), `PermissionPolicyGuard` (`ReadOnly` default, explicit approval for `AskBeforeModifications`, flags unsupported `auto`), `ConfigurationBackupService` (timestamped SHA-256 backups & atomic restore), and `TrustBoundaryDisclosure`.
- **Revit & pyRevit MCP Connectivity** (`src/AV.Orch.Revit`): `RevitDiscoveryService` (Revit 2021–2027), `PyRevitService` (`pyrevit --version`, `configs agent`, `configs agent policy`, `agent status --json`, restricted read-only `agent context`), and `McpStdioProbe` (real JSON-RPC 2.0 `initialize` + `notifications/initialized` + `tools/list` over stdio).
- **Independent AI Provider Adapters** (`src/AV.Orch.Providers`):
  - `CodexAdapter`: Native `pyrevit mcp install codex` + surgical `~/.codex/config.toml` `[mcp_servers.pyrevit]` table manager preserving comments and other tables.
  - `GeminiAdapter`: Dedicated local stdio MCP adapter for `~/.gemini/settings.json` (`mcpServers.pyrevit`, `trust: false`) preserving all unrelated settings and MCP servers.
  - `ClaudeCodeAdapter`: Native `pyrevit mcp install claude` + surgical `~/.claude.json` `mcpServers.pyrevit` manager preserving all unrelated settings and MCP servers.
- **Installation, Repair & Clean Uninstall Services** (`src/AV.Orch.Installation`): `DependencyDiscoveryService`, approval-gated `DependencyInstallWorkflow`, `UninstallCleanupService` (removes ONLY AV Orch-owned MCP entries), and `AvOrchOrchestrator`.

### IMPLEMENTED (COMPILED, NOT GUI-EXECUTED ON WINDOWS)
- **WPF Desktop Application** (`src/AV.Orch.Desktop` -> `build/publish/win-x64/AVOrch.exe`):
  - 6 views: Home, Providers, Revit, Permissions, Diagnostics, Settings.
  - Light & Dark themes in white, black, and restrained gold.
  - `--uninstall-cleanup` CLI mode for WiX uninstaller integration.
  - Compiled with **0 Warnings, 0 Errors** in Release configuration and published for `win-x64`.
- **WiX v5 Per-User Windows MSI Installer** (`installer/AV.Orch.Installer.wixproj`, `installer/Package.wxs`):
  - Per-user scope (`Scope="perUser"`), stable `UpgradeCode`, `MajorUpgrade`, Start Menu shortcut, and optional `CleanupAvOrchMcpRegistrations` custom action.
- **Release & Acceptance Scripts** (`scripts/build-release.ps1`, `scripts/run-acceptance-tests.ps1`, `scripts/build-linux-ci.sh`).
- **Commercial Documentation & Legal** (`README.md`, `LICENSE`, `NOTICE.md`, `THIRD_PARTY_LICENSES.md`, `docs/*.md`).

### REAL-WORLD VERIFIED
- **Official Documentation Verification** (2026-11-23):
  - pyRevit 7.x+ Agent Runtime (`docs.pyrevitlabs.io/agent-runtime/`)
  - Anthropic Claude Code MCP (`code.claude.com/docs/en/mcp-quickstart`)
  - OpenAI Codex CLI MCP (`~/.codex/config.toml`, `codex mcp add`)
  - Google Gemini CLI MCP (`geminicli.com/docs/tools/mcp-server/`)
- **Live Subprocess JSON-RPC 2.0 MCP Stdio Protocol**: `McpStdioProbe` verified end-to-end against a real local stdio child process.
- **Live Autodesk Revit / pyRevit / Provider Accounts**: **NONE** (requires Windows test workstation).

### BLOCKED (ENVIRONMENTAL)
- Building `AVOrch-1.0.0-win-x64.msi` via WiX Toolset v5 (requires Windows build host).
- Authenticode code signing of `AVOrch.exe` and MSI (requires Artificial Vision's EV signing certificate).
- Launching the WPF GUI (`AVOrch.exe`) and verifying DPI/accessibility on Windows 10/11.
- Real-system acceptance tests (`tests/AV.Orch.AcceptanceTests/RealRevitAcceptanceTests.cs`) against Autodesk Revit + pyRevit 7.1+ + live Codex/Gemini/Claude accounts.

### NOT IMPLEMENTED (INTENTIONALLY OUT OF V1 SCOPE)
- Claude Desktop (`claude_desktop_config.json`) — deferred to V1.1.
- Cursor / VS Code adapters — deferred to future release.
- Online licensing, payment processing, cloud backend, custom AI chat UI, or BIM automation skills.

---

## 2. Automated Test Execution Report

Command: `./scripts/build-linux-ci.sh` (`Release` configuration, .NET 8.0)

| Suite | Passed | Failed | Skipped | Duration | Notes |
|---|---|---|---|---|---|
| `AV.Orch.UnitTests` | **20** | 0 | 0 | 342 ms | Covers security, redaction, path validation, permissions, backups, settings, audit logs, all 3 provider adapters, Revit discovery, pyRevit service, diagnostics, approval workflow |
| `AV.Orch.IntegrationTests` | **2** | 0 | 0 | 681 ms | Covers real-disk 3-provider coexistence + clean uninstall AND real JSON-RPC 2.0 stdio subprocess `McpStdioProbe` |
| `AV.Orch.AcceptanceTests` | 0 (gated) | 0 | 0 | — | Strictly gated by `AV_ORCH_REAL_REVIT=1` on Windows; not counted as real-world verified |

**Static Analysis / Compiler Diagnostics:** **0 Warnings, 0 Errors** across all 9 projects.

---

## 3. Built Artifacts & SHA-256 Checksums (Unsigned)

Published to `build/publish/win-x64/` (checksums in `release/SHA256SUMS.txt`):
- `AVOrch.exe`: `d900730514162a1e2bc9c1f0e408ad5c7d1830fd53ee7c0472144acbe05340bc`
- `AVOrch.dll`: `80813363bba8d5fb1cfbe07357f90a046602e568f8df408b090f7833a15f8781`
- `AV.Orch.Core.dll`: `ffd760dff3813ba2ec2a82105ca9368b83adeec5a332d4a53ac9df03446c116a`
- `AV.Orch.Installation.dll`: `2195e5918d8cf152896be4ea0e0b797deb705f5815f4b525725ce3dadc5c2993`
- `AV.Orch.Providers.dll`: `130de913d15d17b30042e6dbb5931daccbbe13947a1afc57f50a73228eb0b99a`
- `AV.Orch.Revit.dll`: `4ee6638fe706d4fefd79c410c417a1ae0cfb6b6b1d2594da19882c601080d1fe`
- `AV.Orch.Security.dll`: `662cda645689a6fdf0dff1a5f221a3e73c24ab50d639a67d07c516ac54ec29a6`

---

## 4. Exact Remaining Actions for Production Deployment

1. **Build MSI on Windows 10/11**:
   ```powershell
   .\scripts\build-release.ps1
   ```
2. **Code-Sign Binaries & MSI**: Sign `build\publish\win-x64\AVOrch.exe` and `release\AVOrch-1.0.0-win-x64.msi` using Artificial Vision's EV Authenticode certificate (`signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 ...`) and regenerate `release\SHA256SUMS.txt`.
3. **Run Real-Revit Acceptance Tests** (using a non-production sample Revit model, standard non-elevated Windows user):
   ```powershell
   .\scripts\run-acceptance-tests.ps1
   ```
4. **Manual End-to-End Sign-Off Checklist** (`docs/release-checklist.md`):
   - Verify Codex, Gemini CLI, and Claude Code each query the live sample Revit model.
   - Verify an approved modification commits as one undo entry (`Agent: <title>`).
   - Verify a rejected modification rolls back cleanly.
   - Verify MSI install, repair, and clean uninstall preserve unrelated MCP servers, Revit, pyRevit, and customer models.
