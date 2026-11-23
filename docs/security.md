# Security, Privacy & Trust Boundaries

## Core Security Guarantees

- **No Provider Passwords or Tokens**: AV Orch never prompts for, collects, stores, or intercepts AI account passwords, cookies, or OAuth tokens. Authentication always happens inside the provider's official CLI/browser flow.
- **No Cloud Backend or Telemetry**: AV Orch makes zero outbound calls to Artificial Vision servers and collects zero telemetry by default.
- **Local-Only MCP**: Registered MCP servers execute strictly over local `stdio` with validated local executable paths. UNC/network paths (`\\server\share`) and shell metacharacters are blocked by `PathValidator`.
- **Redacted Local Logs**: `SecretRedactor` strips API keys (`sk-...`, `sk-ant-...`, `AIza...`), Bearer tokens, and credential assignments before writing to `%LOCALAPPDATA%\ArtificialVision\AVOrch\logs` or `audit.jsonl`.
- **Reversible Config Changes**: Every modification to a provider configuration file creates a timestamped SHA-256 backup first and restores automatically if post-write validation fails.

## Critical Trust Boundaries (Mandatory User Disclosures)

1. **Model Confidentiality**: Any data read from your Revit model (element names, parameter values, levels, schedules, query outputs) is sent directly from your PC to the AI provider you selected (OpenAI, Google, or Anthropic) under your account agreement.
2. **Local pyRevit Run Records**: pyRevit stores agent run scripts, requests, responses, and view captures in plaintext under `%APPDATA%\pyRevit\agent` for up to 14 days.
3. **Read-Only Is NOT an OS Sandbox**: pyRevit's `readonly` policy guarantees that Revit model transactions are rolled back, **not** that Python code is sandboxed at the operating-system level. Scripts run with your Windows user privileges. AV Orch's built-in Revit connection test uses pyRevit's restricted `pyrevit agent context` command rather than executing arbitrary Python.
4. **Revit Rollback Scope**: Revit transaction rollback (and pyRevit's Discard button) reverts Autodesk Revit model changes only. It does **not** undo external file edits, database writes, or network requests.
5. **No Autonomous Modification Mode in V1**: AV Orch only exposes `Read-Only` (default) and `Ask Before Modifications` (`ask`). pyRevit's `auto` and `reckless` modes are never enabled by AV Orch.
