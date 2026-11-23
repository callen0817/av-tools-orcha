# AV Orch Troubleshooting Guide

| Symptom | Likely Cause | Recovery Steps |
|---|---|---|
| **No Active Revit Session** | Revit is closed, running as Administrator, or pyRevit agent host is disabled. | Close elevated Revit; enable agent runtime via Diagnostics -> Safe Repair (`pyrevit configs agent enable`); relaunch Revit as a normal user. |
| **pyRevit Upgrade Required** | Installed pyRevit is older than 7.0 (or an older `pyrevit.exe` appears earlier on `PATH`). | Run `where.exe pyrevit` to find conflicting installs; upgrade to pyRevit 7.1+. |
| **Corrupt Configuration File** | Existing `~/.claude.json`, `~/.gemini/settings.json`, or `~/.codex/config.toml` has invalid JSON/TOML. | Click **Reconnect / Repair** on the provider card. AV Orch backs up the broken file before creating a clean config. |
| **Conflicting MCP Registration** | An existing `pyrevit` server entry points to a non-pyRevit executable. | AV Orch refuses to overwrite or delete unowned entries automatically. Inspect the config file manually or rename the conflicting entry. |
| **Approval Prompt Times Out** | Long-running Keep/Discard review inside Revit exceeded client MCP timeout. | Increase client MCP timeout (e.g., set `MCP_TOOL_TIMEOUT=300000` for Claude Code). |
| **Provider Shows Not Authenticated** | CLI session expired or never signed in. | Click **Sign In** on the provider card to launch the vendor's official login command. |
