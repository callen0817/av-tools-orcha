# Third-Party Licenses & Attribution

AV Orch separates its proprietary source code from all external GPL-licensed or
vendor-owned components.

## Build & Test Dependencies

| Component | License | Usage in AV Orch |
|---|---|---|
| .NET 8 Runtime / WPF (`Microsoft.NET.Sdk`) | MIT | Application framework |
| xUnit (`xunit`, `xunit.runner.visualstudio`) | Apache-2.0 | Automated unit/integration tests (not shipped) |
| Microsoft.NET.Test.Sdk | MIT | Test host (not shipped) |
| WiX Toolset v5 (`WixToolset.Sdk`) | MS-RL / Reciprocal | MSI installer build tooling |

## External User-Installed Software (Not Bundled / Not Redistributed)

| Software | License / Terms | How AV Orch Interacts |
|---|---|---|
| Autodesk Revit | Proprietary (Autodesk) | Discovered locally; interacted with exclusively via pyRevit MCP |
| pyRevit 7.x+ | GPL-3.0 | Detected locally; invoked as separate CLI subprocess (`pyrevit.exe`); no GPL code linked or copied |
| OpenAI Codex CLI | OpenAI Terms / Apache-2.0 | Detected locally; user-scope `~/.codex/config.toml` or `pyrevit mcp install codex` |
| Google Gemini CLI | Apache-2.0 | Detected locally; user-scope `~/.gemini/settings.json` |
| Anthropic Claude Code | Anthropic Commercial Terms | Detected locally; user-scope `~/.claude.json` or `pyrevit mcp install claude` |
