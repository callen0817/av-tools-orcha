# AV Orch v1.0.0 Release Notes

**Release Date:** 2026-11-23  
**Publisher:** Artificial Vision, Inc.

## Highlights
- Initial V1.0 commercial architecture and implementation of **AV Orch**, Artificial Vision's local AI-to-Revit MCP desktop connector.
- Independent provider adapters for **OpenAI Codex CLI**, **Google Gemini CLI**, and **Anthropic Claude Code** with full multi-provider coexistence.
- Native **pyRevit 7.x+ MCP Agent Runtime** integration with **Read-Only** default policy and **Ask Before Modifications** approval workflow.
- Surgical JSON/TOML configuration updates with timestamped SHA-256 backups, validation, automatic rollback, redacted local logs, and clean uninstall support.
- Per-user WiX v5 MSI installer definition and PowerShell release packaging pipeline.

## Known Limitations
- Unsigned build artifacts in V1.0 dev build; production distribution requires EV Authenticode signing of `AVOrch.exe` and `AVOrch-1.0.0-win-x64.msi`.
- Claude Desktop (`claude_desktop_config.json`) is deferred to V1.1.
- Real-system acceptance tests (`tests/AV.Orch.AcceptanceTests`) must be executed on a Windows 10/11 machine with Autodesk Revit, pyRevit 7.1+, and live provider accounts before declaring production readiness.
