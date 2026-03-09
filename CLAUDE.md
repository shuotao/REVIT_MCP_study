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

> **Note:** `RevitMCP.2024.csproj` is a legacy file (hardcoded Revit 2024 DLL paths). Use the unified `RevitMCP.csproj` for all versions.

After building, close Revit, then deploy DLL:
```powershell
# Example for Revit 2024:
Copy-Item "bin/Release/RevitMCP.dll" "$env:APPDATA\Autodesk\Revit\Addins\2024\RevitMCP\" -Force
# Example for Revit 2025:
Copy-Item "bin/Release/RevitMCP.dll" "$env:APPDATA\Autodesk\Revit\Addins\2025\RevitMCP\" -Force
```
Or use `scripts/install-addon.ps1` for automated install.

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

## Version-Specific Build Matrix

All versions now use the unified `RevitMCP.csproj` with `Nice3point.Revit.Sdk`.

| Revit | Build Configuration | Output Dir | Notes |
|-------|-------------------|------------|-------|
| 2022 | `Release.R22` | `bin\Release\` | .NET Framework 4.8 |
| 2023 | `Release.R23` | `bin\Release\` | .NET Framework 4.8 |
| 2024 | `Release.R24` | `bin\Release\` | .NET Framework 4.8 |
| 2025 | `Release.R25` | `bin\Release\` | .NET 8, ElementId=long |
| 2026 | `Release.R26` | `bin\Release\` | .NET 8, ElementId=long |

> **Cross-version compatibility:** `MCP/Core/RevitCompatibility.cs` provides `GetIdValue()` and `ToElementId()` extension methods.
> The SDK auto-defines preprocessor symbols like `REVIT2025_OR_GREATER` for conditional compilation.
> Legacy `RevitMCP.2024.csproj` is kept for backward reference but should not be used for new builds.

## Domain Knowledge & Workflow Files

The `domain/` directory contains BIM compliance workflows that AI must consult before executing related tasks:

| Trigger Keywords | File |
|-----------------|------|
| fire rating, fireproofing | `domain/fire-rating-check.md` |
| corridor, escape route | `domain/corridor-analysis-protocol.md` |
| floor area, FAR | `domain/floor-area-review.md` |
| element coloring, visualization | `domain/element-coloring-workflow.md` |
| exterior wall openings | `domain/exterior-wall-opening-check.md` |
| daylight area | `domain/daylight-area-check.md` |
| QA, verification | `domain/qa-checklist.md` |

## Script Organization

- `MCP-Server/scripts/` — Stable, reusable workflow scripts (e.g., `fire_rating_full.js`)
- `MCP-Server/scratch/` — Temporary debug/one-off scripts
- `scripts/` — Installation & deployment PowerShell scripts

## CODEOWNERS

- `MCP/`, `MCP-Server/src/`, `scripts/` — Core code, owner-reviewed only
- `domain/`, `GEMINI.md` — Knowledge contributions accepted via PR

## Development Workflow

1. After any C# change: close Revit → build → deploy DLL → restart Revit
2. After TypeScript changes: `npm run build` in MCP-Server (no Revit restart needed)
3. Config/addin file changes: restart may be needed depending on scope
4. Always read `GEMINI.md` for AI collaboration rules and the `/lessons`, `/domain`, `/review` directive system
5. Before writing new scripts, check `domain/`, `scripts/`, and `MCP-Server/scripts/` for existing workflows — avoid duplicating logic
