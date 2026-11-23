# AV Orch Installation Guide

## Prerequisites

- **Windows 10 (1903+) or Windows 11 (x64)** — run as a **standard (non-administrator)** user.
- **.NET 8 Desktop Runtime (x64)** (required for framework-dependent MSI/ZIP builds).
- **Autodesk Revit 2023 or newer** (2023 forward).
- **pyRevit 7.0+** (7.1+ recommended) installed from official releases: https://github.com/pyrevitlabs/pyRevit/releases.
- One or more supported AI CLIs with your own account:
  - **OpenAI Codex CLI** (`codex`)
  - **Google Gemini CLI** (`gemini`)
  - **Anthropic Claude Code** (`claude`)

## Step-by-Step First-Run Experience

1. Download and launch `AVOrch-1.0.0-win-x64.msi` (per-user installer; no administrator rights required). By default, the installer adds **AV Orch** to the shared **`AV Tools`** Revit ribbon (`AI` panel). If **NX Xplode** (`av-tools-xnf`) or another AV Tool is already installed, AV Orch attaches to the same `AV Tools` tab without creating a duplicate ribbon; if no AV Tools are installed, it creates the `AV Tools` tab and adds AV Orch.
2. Open **AV Orch** from the Start Menu or from **Revit -> AV Tools -> AI -> AV Orch**.
3. Review the **Home** dashboard: AV Orch automatically scans Autodesk Revit, pyRevit, Codex, Gemini, and Claude Code.
4. Go to **Providers** and click **Sign In** on any provider not yet authenticated (opens the provider's official login flow; never enter passwords into AV Orch).
5. Click **Connect** on one, two, or all three providers. Confirm the backup & confidentiality notice. AV Orch enables `pyrevit configs agent enable`, enforces **Read-Only** by default, backs up your provider config, and registers the local pyRevit MCP server.
6. Launch **Autodesk Revit** (as a normal user) and open a project.
7. Click **Test Revit (Read-Only)** in AV Orch to verify live session and active document discovery.

## Repair & Clean Uninstall

- **Repair**: Run `AVOrch-1.0.0-win-x64.msi` and choose **Repair**, or use **Diagnostics -> Run Safe Repair** inside AV Orch.
- **Uninstall**: Uninstall **AV Orch** from Windows Settings -> Apps. By default (`AV_ORCH_CLEAN_MCP_ON_UNINSTALL=1`), the uninstaller invokes `AVOrch.exe --uninstall-cleanup` to remove ONLY AV Orch-owned MCP registrations while leaving Revit, pyRevit, Codex, Gemini, Claude, unrelated MCP servers, and all customer Revit models untouched.
