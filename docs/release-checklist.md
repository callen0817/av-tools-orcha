# AV Orch V1.0 Release Checklist

## Status Legend
- **PASS**: Verified in this build environment.
- **FAIL**: Failed verification.
- **BLOCKED**: Requires Windows 10/11 + WiX / Autodesk Revit / live provider accounts unavailable in Linux build VM.
- **NOT TESTED**: Not yet executed on target hardware.

## Installation
- [BLOCKED] Windows MSI installer produced (`installer/AV.Orch.Installer.wixproj` authored; requires Windows + WiX Toolset v5 build host)
- [BLOCKED] Application launches on Windows 10/11 (WPF `AVOrch.exe` + `AVOrch.dll` compiled & published for `win-x64`; GUI launch requires Windows)
- [PASS] Revit detected correctly (automated unit + integration tests)
- [PASS] pyRevit detected correctly (automated unit + integration tests)
- [PASS] Missing dependencies handled (automated unit + diagnostics tests)
- [BLOCKED] Installer repair/uninstall verified on Windows (WiX `MajorUpgrade` + `--uninstall-cleanup` implemented & unit-tested; MSI execution requires Windows)

## Providers
- [PASS] Codex adapter implemented (`CodexAdapter`)
- [PASS] Gemini adapter implemented (`GeminiAdapter`)
- [PASS] Claude Code adapter implemented (`ClaudeCodeAdapter`)
- [PASS] Multiple providers can coexist (integration-tested on real disk)
- [PASS] Existing MCP configuration preserved (unit + integration tested)
- [PASS] Authentication handoff implemented (unit tested)
- [PASS] Registrations are reversible with SHA-256 backups (unit + integration tested)

## Revit
- [PASS] MCP process validation implemented (`McpStdioProbe` integration-tested against live JSON-RPC stdio subprocess)
- [PASS] Active session handling implemented (`pyrevit agent status --json`)
- [PASS] Active document detection implemented (`pyrevit agent context`)
- [PASS] Read-only query test implemented (restricted `agent context` command, no raw Python)
- [PASS] Approval-based modification workflow integrated (`readonly` default, `ask` with explicit approval)
- [PASS] Failure states communicated (timeouts, missing doc, elevated Revit, disabled runtime)

## Security
- [PASS] No provider password collection
- [PASS] No mandatory external backend
- [PASS] No automatic upload to AV servers (zero telemetry)
- [PASS] Configuration backups & rollback implemented
- [PASS] Restricted diagnostic operations
- [PASS] User approval before modifications
- [PASS] Untrusted code execution risks documented (`docs/security.md` + UI notices)

## Quality
- [PASS] Unit tests pass
- [PASS] Integration tests pass
- [BLOCKED] Installer build passes (requires Windows WiX v5 runner)
- [PASS] Static analysis passes (0 errors, 0 warnings in Release build)
- [PASS] Core workflows documented
- [PASS] No unexplained critical warnings
- [PASS] No placeholder production functionality

## Real System Validation (Windows + Real Revit + Live Accounts)
- [NOT TESTED] Codex tested with real Revit
- [NOT TESTED] Gemini tested with real Revit
- [NOT TESTED] Claude tested with real Revit
- [NOT TESTED] Model read operation verified
- [NOT TESTED] Approved modification verified
- [NOT TESTED] Rejected modification verified
- [NOT TESTED] Clean uninstall verified
