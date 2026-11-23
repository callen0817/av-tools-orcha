# Provider & Platform Compatibility Matrix

*Verified against official documentation on 2026-11-23.*

## AI Provider Matrix (V1.0)

| Provider | CLI Command | Supported Auth Modes | Config File (User Scope) | Registration Method | Known Limitations | Verification Status |
|---|---|---|---|---|---|---|
| **Anthropic Claude Code** | `claude` | Claude account OAuth (`claude auth login`) or `ANTHROPIC_API_KEY` | `%USERPROFILE%\.claude.json` (or `%CLAUDE_CONFIG_DIR%\.claude.json`) | Primary: `pyrevit mcp install claude`; Fallback: surgical JSON update under `mcpServers.pyrevit` | Claude Desktop (`claude_desktop_config.json`) is separate and deferred to V1.1+. Elevated execution skips `claude` CLI in pyRevit. Raise `MCP_TOOL_TIMEOUT` for long Revit approvals. | **Doc-verified & Automated-tested** (Real Revit: NOT TESTED) |
| **OpenAI Codex CLI** | `codex` | ChatGPT subscription OAuth (`codex login`) or `OPENAI_API_KEY` | `%USERPROFILE%\.codex\config.toml` (or `%CODEX_HOME%\config.toml`) | Primary: `pyrevit mcp install codex`; Fallback: surgical TOML table `[mcp_servers.pyrevit]` | User-scope only (project `.codex/config.toml` requires trusted project). Elevated execution skips `codex` CLI in pyRevit. | **Doc-verified & Automated-tested** (Real Revit: NOT TESTED) |
| **Google Gemini CLI** | `gemini` | Google account OAuth (`gemini`) or `GEMINI_API_KEY` / `GOOGLE_API_KEY` | `%USERPROFILE%\.gemini\settings.json` | Dedicated AV Orch surgical JSON adapter (`mcpServers.pyrevit` with `trust: false`) | pyRevit 7.x has no built-in `pyrevit mcp install gemini` command; AV Orch provides the adapter. Unpaid/Google One users should check Google's latest CLI entitlement notes. | **Doc-verified & Automated-tested** (Real Revit: NOT TESTED) |

> **Important**: Web chat subscriptions (ChatGPT web, Claude.ai web, Gemini web) do not automatically guarantee CLI entitlement; customers use their own provider accounts subject to each vendor's terms.

## Autodesk Revit & pyRevit Compatibility

| Component | Supported Versions | Notes |
|---|---|---|
| **Operating System** | Windows 10 (1903+) / Windows 11 (x64) | Must run as a standard (non-elevated) user for pyRevit agent host. |
| **Autodesk Revit** | **2023 forward** (2023, 2024, 2025, 2026, 2027+) | Revit must not be run as Administrator. |
| **AV Tools Revit Ribbon** | Shared `AV Tools` tab (`%APPDATA%\pyRevit\Extensions\AV Tools.extension\AV Tools.tab\AI.panel\AV Orch.pushbutton`) | Automatically shares the single `AV Tools` tab with NX Xplode (`av-tools-xnf`) and other AV Tools without creating a duplicate ribbon. |
| **pyRevit** | 7.0.0+ (7.1.0+ strongly recommended) | Agent runtime is off by default; AV Orch enables via `pyrevit configs agent enable`. |
