# AV Orch User Guide

## Views Overview

- **Home**: Displays AV Orch version, active Revit session and document, pyRevit MCP runtime status, overall health, last diagnostic result, and the model confidentiality notice.
- **Providers**: Manage **OpenAI Codex**, **Google Gemini CLI**, and **Anthropic Claude Code** independently. Each card supports **Sign In**, **Connect**, **Disconnect**, and **Reconnect / Repair**.
- **Revit**: Lists detected Revit installations, running Revit sessions (PID, year, active document, active policy), pyRevit runtime status, and the **Run Read-Only Revit Connection Test** button.
- **Permissions**:
  - **Read-Only (Default)**: Queries and dry runs only; Revit model changes are rolled back.
  - **Ask Before Modifications**: Allows AI assistants to propose model changes; every change prompts **Keep / Discard** inside Autodesk Revit with modified elements highlighted and isolated.
- **Diagnostics**: Surfaces missing prerequisites, elevated sessions, disabled pyRevit runtime, corrupt or conflicting MCP configs, and one-click safe repairs.
- **Settings**: Toggle Dark/Light theme, startup behavior, update checks, inspect redacted log and settings paths, view local audit history, restore defaults, or clean all AV Orch MCP registrations.

## Using Your Connected AI Provider with Revit

Once connected and verified:
- **Claude Code**: Open a terminal and run `claude`, then ask e.g. *"How many doors on Level 2 have no Mark?"*
- **OpenAI Codex**: Open a terminal and run `codex`, then ask about your open Revit model.
- **Google Gemini CLI**: Open a terminal and run `gemini`, type `/mcp` to confirm `pyrevit` is listed, then query your open Revit model.
