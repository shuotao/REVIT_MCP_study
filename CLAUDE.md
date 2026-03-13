# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Revit MCP is a bridge between AI language models and Autodesk Revit via the Model Context Protocol (MCP). It enables AI-driven BIM workflows through natural language commands. The project has two main components that communicate over WebSocket on `localhost:8964`.

## Architecture (4+1 Pattern)

```
AI Client (Claude Desktop / Gemini CLI / VS Code Copilot / Antigravity)
  ↓ stdio
MCP Server (Node.js/TypeScript) — MCP-Server/src/index.ts
  ↓ WebSocket (ws://localhost:8964)
Revit Add-in (C# .NET 4.8) — MCP/Application.cs
  ↓ ExternalEventManager (UI thread)
CommandExecutor → Revit API
```

A 5th "embedded" option bypasses the MCP Server entirely — a WPF chat window inside the Revit Add-in calls the Gemini API directly.

## Build Commands

### C# Revit Add-in (Unified Build via Nice3point.Revit.Sdk)

The project uses `Nice3point.Revit.Sdk/6.1.0` for unified multi-version builds.
A single `RevitMCP.csproj` supports Revit 2022–2026 via configuration suffixes.

```powershell
cd MCP

# Revit 2022
dotnet build -c Release.R22 RevitMCP.csproj

# Revit 2023
dotnet build -c Release.R23 RevitMCP.csproj

# Revit 2024
dotnet build -c Release.R24 RevitMCP.csproj

# Revit 2025
dotnet build -c Release.R25 RevitMCP.csproj

# Revit 2026
dotnet build -c Release.R26 RevitMCP.csproj
```

> **Note:** Only `RevitMCP.csproj` exists. Legacy version-specific files (`RevitMCP.2024.csproj`, `RevitMCP.2024.addin`) have been removed. See Deployment Rules below.

After building, close Revit, then deploy DLL:
```powershell
# Replace {version} with 2022 / 2023 / 2024 / 2025 / 2026:
Copy-Item "bin/Release/RevitMCP.dll" "$env:APPDATA\Autodesk\Revit\Addins\{version}\RevitMCP\" -Force
```
Or use `scripts/install-addon.ps1` for automated install.
Or use Claude Code skills: `/build-revit` and `/deploy-addon`

### MCP Server (Node.js)
```bash
cd MCP-Server
npm install
npm run build    # tsc && node build/index.js
npm run watch    # tsc --watch (development)
```

## Key Source Files

| File | Role |
|------|------|
| `MCP/Application.cs` | Revit IExternalApplication entry point, creates ribbon panel |
| `MCP/Core/CommandExecutor.cs` | Central command dispatcher (40+ commands), largest file |
| `MCP/Core/SocketService.cs` | HttpListener-based WebSocket server in Revit |
| `MCP/Core/RevitCompatibility.cs` | Cross-version compatibility layer (ElementId int→long for 2025+) |
| `MCP/Core/ExternalEventManager.cs` | Ensures commands execute on Revit UI thread |
| `MCP-Server/src/index.ts` | MCP Server entry (StdioServerTransport) |
| `MCP-Server/src/socket.ts` | RevitSocketClient — WebSocket client to Revit |
| `MCP-Server/src/tools/revit-tools.ts` | Tool definitions (50+ tools exposed to AI) |

## Code Conventions

- **C# namespace**: `RevitMCP` — all classes use this namespace
- **Revit API safety**: All Revit operations MUST use `Transaction` and be reversible. Commands run through `ExternalEventManager` to ensure UI thread execution.
- **Command pattern**: Commands in `CommandExecutor.cs` follow a `case "command_name":` switch pattern, each returning data objects wrapped in `RevitCommandResponse`.
- **Singletons**: `ConfigManager`, `ExternalEventManager`, `Logger` are all singletons
- **Config storage**: `%AppData%\RevitMCP\config.json` (default port 8964)
- **Logs**: `%AppData%\RevitMCP\Logs\RevitMCP_YYYYMMDD.log`

## Claude Code Skills

| Skill | Usage | Description |
|-------|-------|-------------|
| `/build-revit` | `/build-revit`, `--version 2024`, `--all` | Build for one or all Revit versions |
| `/deploy-addon` | `/deploy-addon`, `--version 2024` | Deploy DLL to correct AppData path (Windows only) |

> **Cross-version compatibility:** `MCP/Core/RevitCompatibility.cs` provides `GetIdValue()` and `ToElementId()` extension methods.
> Revit 2025+ uses `ElementId` as `long`; 2022-2024 uses `int`. Use `REVIT2025_OR_GREATER` preprocessor symbol for conditional compilation.

## AI Client Setup

All AI clients connect to the MCP Server via the same config format. Replace `{absolute-path}` with your actual project path.

```json
{
  "mcpServers": {
    "revit-mcp": {
      "command": "node",
      "args": ["{absolute-path}/MCP-Server/build/index.js"]
    }
  }
}
```

| AI Client | Config File Location | Notes |
|-----------|---------------------|-------|
| Claude Desktop | `%APPDATA%\Claude\config.json` (Windows) | Restart app after edit |
| Gemini CLI | `~/.gemini/settings.json` | No restart needed |
| VS Code Copilot | `.vscode/mcp.json` (project root) | Can use `${workspaceFolder}` instead of absolute path |

> Run `npm run build` in `MCP-Server/` before first use. Verify port 8964 is free.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| 56 warnings on build (Revit 2024) | Normal — project uses 2022-compatible syntax | Ignore, does not affect functionality |
| `RevitMCP.dll` not found after build | Wrong build config | Use `dotnet build -c Release.RXX RevitMCP.csproj` where XX = 22/23/24/25/26 |
| MCP Server connection failed | Wrong path or not built | Check absolute path in config, re-run `npm run build`, verify port 8964 free |
| Commands not responding in Revit | Revit UI thread issue | Ensure `ExternalEventManager` is used; check `%AppData%\RevitMCP\Logs\` |

## Domain Knowledge & Workflow Files

The `domain/` directory contains BIM compliance workflows that AI must consult before executing related tasks:

| Trigger Keywords | File |
|-----------------|------|
| fire rating, fireproofing, 防火, 耐燃 | `domain/fire-rating-check.md` |
| corridor, escape route, 走廊, 逃生, 通道寬度 | `domain/corridor-analysis-protocol.md` |
| floor area, FAR, 容積, 樓地板面積, 送審 | `domain/floor-area-review.md` |
| element coloring, visualization, 上色, 顏色標示 | `domain/element-coloring-workflow.md` |
| exterior wall openings, 外牆開口, 鄰地距離 | `domain/exterior-wall-opening-check.md` |
| daylight area, 採光 | `domain/daylight-area-check.md` |
| QA, verification, 檢查, 驗證, 一致性 | `domain/qa-checklist.md` |
| room boundary, 房間邊界 | `domain/room-boundary.md` |
| lessons learned, 開發經驗, 避坑 | `domain/lessons.md` |

## Deployment Rules (DO NOT VIOLATE)

These rules ensure unified multi-version deployment. **Any AI assistant or code reviewer MUST follow them.**

### Forbidden Actions
- **DO NOT** create version-specific `.csproj` files (e.g., `RevitMCP.2024.csproj`, `RevitMCP.2025.csproj`)
- **DO NOT** create version-specific `.addin` files (e.g., `RevitMCP.2024.addin`)
- **DO NOT** create nested `MCP/MCP/` directories
- **DO NOT** hardcode absolute DLL paths in `.addin` files (use relative `RevitMCP.dll` only)
- **DO NOT** modify `<AddInId>` in `RevitMCP.addin` — duplicates cause Revit to load twice

### Required Architecture
- **ONE** `.csproj`: `MCP/RevitMCP.csproj` (Nice3point.Revit.Sdk, supports 2022-2026)
- **ONE** `.addin`: `MCP/RevitMCP.addin` (version-agnostic, relative assembly path)
- **ONE** install script: `scripts/install-addon.ps1` (primary, all versions)
- Build config format: `Release.R{YY}` where YY = 22/23/24/25/26
- `<DeployAddin>true</DeployAddin>` in csproj auto-deploys to correct Addins folder

### Multi-Version Build
```
dotnet build -c Release.R22 → Revit 2022 (.NET Framework 4.8)
dotnet build -c Release.R23 → Revit 2023 (.NET Framework 4.8)
dotnet build -c Release.R24 → Revit 2024 (.NET Framework 4.8)
dotnet build -c Release.R25 → Revit 2025 (.NET 8, ElementId=long)
dotnet build -c Release.R26 → Revit 2026 (.NET 8, ElementId=long)
```
All output to `bin\Release\RevitMCP.dll`. Each build overwrites the previous. Deploy immediately after building for the target version.

### Adding New Tools/Commands Safely
When adding new `IExternalCommand` in `Commands/` folder:
1. Add ribbon button in `Application.OnStartup()` — isolated, won't break existing buttons
2. Add case in `CommandExecutor.cs` switch block — existing cases unaffected
3. Run `scripts/verify-installation.ps1` to validate no deployment issues
4. Do NOT modify singleton initialization (`ConfigManager`, `ExternalEventManager`, `Logger`)
5. Do NOT change WebSocket port (8964) without updating all config templates

## Script Organization

- `MCP-Server/scripts/` — Stable, reusable workflow scripts (e.g., `fire_rating_full.js`)
- `MCP-Server/scratch/` — Temporary debug/one-off scripts
- `scripts/` — Installation & deployment PowerShell scripts

## CODEOWNERS

- `MCP/`, `MCP-Server/src/`, `scripts/` — Core code, owner-reviewed only
- `domain/`, `.claude/commands/` — Knowledge contributions accepted via PR

## Development Workflow

1. After any C# change: close Revit → `/build-revit` → `/deploy-addon` → restart Revit
   (or manually: `dotnet build -c Release.R{YY}` then copy DLL)
2. After TypeScript changes: `npm run build` in MCP-Server (no Revit restart needed)
3. Config/addin file changes: restart may be needed depending on scope
4. Use `/lessons` to capture new rules, `/domain` to convert workflows to SOP, `/review` to audit CLAUDE.md size
5. Before writing new scripts, check `domain/`, `scripts/`, and `MCP-Server/scripts/` for existing workflows — avoid duplicating logic
