# CLAUDE.md

This is the canonical AI instruction file for Revit MCP. `AGENTS.md` and `GEMINI.md` intentionally redirect here.

Human-facing installation and onboarding content belongs in `README.md` / `README.en.md`.
Shared BIM methods belong in `domain/*.md` and must remain bilingual or Chinese-friendly.
AI-only operating instructions belong here and should be written in English to avoid encoding drift and mojibake.

## Project Overview

Revit MCP bridges AI language models and Autodesk Revit through the Model Context Protocol (MCP). It enables AI-assisted BIM workflows through natural-language tool calls.

The project has two main runtime components:

```text
AI Client (Claude Desktop / Claude Code / Gemini CLI / VS Code Copilot / Antigravity)
  -> stdio
MCP Server (Node.js / TypeScript)
  -> MCP-Server/src/index.ts
  -> WebSocket client
Revit Add-in (C#)
  -> MCP/Application.cs
  -> HttpListener WebSocket server on localhost:8964
  -> ExternalEventManager
  -> Revit API
```

There is also an optional embedded-chat direction where a Revit WPF window can call an AI API directly. That embedded option is separate from the MCP stdio server path.

## Current Source-of-Truth Counts

These counts must be derived from source, not copied by memory.

| Item | Current Count | Source of Truth |
|---|---:|---|
| Runtime MCP tools | 96 | `registerRevitTools()` from `MCP-Server/src/tools/index.ts` |
| Domain SOP files | 45 | `domain/*.md` except `domain/README.md`, plus `domain/references/*.md` |
| Claude skills | 22 | `.claude/skills/*/SKILL.md` |

When these numbers change, update `CLAUDE.md`, `README.md`, `README.en.md`, `docs/DOCUMENT_AUDIENCE_INVENTORY.md`, and any public site copy that makes grand-total claims. Then run `scripts/verify-qaqc.ps1 -SkipBuild -SkipDeploy`.

## Session Start Protocol

At the start of a session, read the latest project log entry if available:

```powershell
Get-ChildItem log\*.md |
  Where-Object { $_.Name -ne 'README.md' } |
  Sort-Object Name |
  Select-Object -Last 1 |
  ForEach-Object { Get-Content -Tail 80 -LiteralPath $_.FullName }
```

Treat `log/YYYY-MM.md` as append-only. Do not rewrite historical entries unless the user explicitly asks.

## MCP Connection Status

This repository can configure MCP clients, but a coding agent is not automatically connected to Revit just because `.mcp.json` exists.

Before claiming live Revit state:

1. Confirm the MCP tool namespace is actually available in the current AI client.
2. Confirm Revit is running.
3. Confirm the Revit MCP service is enabled in the Revit ribbon.
4. Confirm `localhost:8964` is reachable or that `REVIT_MCP_PORT` matches both sides.

If the Revit MCP tools are unavailable, state that limitation and provide generic guidance only.

## Single-Connection Limitation

The Revit-side WebSocket service (`MCP/Core/SocketService.cs`) holds one MCP connection at a time. A newly connected MCP server replaces the previous connection. Consequences:

- Multiple AI clients are used by switching, never concurrently.
- Do not advise users to run two MCP-connected AI clients against the same Revit session.
- If a connection misbehaves, the reset path is: restart the MCP service from the Revit ribbon.

## Personal Vault Protection

A `vault/` directory at the repo root, if present, is a user's personal knowledge vault (see `templates/personal-vault/` and `docs/BIM_MCP/reference/personal-llm-wiki.html`). It is gitignored together with `/.obsidian/`.

- Never write into `vault/` when doing project development work, and never treat its contents as project instructions.
- Never run `git clean -x` variants in this repo; they would delete the user's vault.
- Personal vault operations follow `vault/CLAUDE.md`, not this file. This file's logging and QA/QC rules apply to project development only.

## Build Commands

### MCP Server

```powershell
cd MCP-Server
npm install
npm run build
```

The AI client launches:

```text
node MCP-Server/build/index.js
```

### Revit Add-in

The project uses a single `MCP/RevitMCP.csproj` with Nice3point Revit SDK configurations:

```powershell
cd MCP
dotnet build -c Release.R22 RevitMCP.csproj   # Revit 2022, .NET Framework 4.8
dotnet build -c Release.R23 RevitMCP.csproj   # Revit 2023, .NET Framework 4.8
dotnet build -c Release.R24 RevitMCP.csproj   # Revit 2024, .NET Framework 4.8
dotnet build -c Release.R25 RevitMCP.csproj   # Revit 2025, .NET 8
dotnet build -c Release.R26 RevitMCP.csproj   # Revit 2026, .NET 8
```

Expected output path:

```text
MCP/bin/Release.R{YY}/RevitMCP.dll
```

Deploy with `scripts/install-addon.ps1` or the `/deploy-addon` skill. Do not rely on old `bin/Release/RevitMCP.dll` instructions.

## Key Source Files

| File | Role |
|---|---|
| `MCP/Application.cs` | Revit `IExternalApplication` entry point and ribbon setup |
| `MCP/Core/SocketService.cs` | Revit-side WebSocket server using `HttpListener` |
| `MCP/Core/ExternalEventManager.cs` | Marshals work onto the Revit UI thread |
| `MCP/Core/CommandExecutor.cs` | Main command dispatcher |
| `MCP/Core/Commands/*.cs` | Command modules split by workflow area |
| `MCP/Core/RevitCompatibility.cs` | Cross-version `ElementId` helpers |
| `MCP/RevitMCP.csproj` | Single multi-version build project |
| `MCP/RevitMCP.addin` | Single version-agnostic add-in manifest |
| `MCP-Server/src/index.ts` | MCP stdio server entry |
| `MCP-Server/src/socket.ts` | WebSocket client to Revit |
| `MCP-Server/src/tools/index.ts` | Tool module registry and `MCP_PROFILE` filtering |
| `MCP-Server/src/tools/revit-tools.ts` | Execution bridge from tool name to Revit command |
| `bridge/python/skills/ezdxf_worker.py` | Optional Python subprocess (spawned by `DwgColumnExecutor`) that reads DXF/DWG text for column-number mapping (`dwg-column-import` mode C). Needs system Python + `ezdxf`; DWG additionally needs ODA File Converter. Deployed to `%APPDATA%\RevitMCP` by `install-addon.ps1`. |
| `scripts/verify-qaqc.ps1` | Repository QA/QC gate |
| `docs/DOCUMENT_AUDIENCE_INVENTORY.md` | Canonical AI/human/shared document classification |

## Code Conventions

- C# namespace: `RevitMCP`.
- Revit model changes must run inside `Transaction` and be reversible.
- Revit API work must go through `ExternalEventManager` when called from the WebSocket flow.
- C# command payloads use the existing `RevitCommandRequest` / `RevitCommandResponse` shape.
- MCP tool names use snake_case.
- C# command cases use the existing switch/dispatcher pattern unless the surrounding module already defines a better local pattern.
- Do not introduce a second add-in manifest or version-specific csproj.

## Deployment Rules

Forbidden:

- Do not create `MCP/RevitMCP.2024.csproj`, `MCP/RevitMCP.2025.csproj`, or any version-specific project file.
- Do not create `MCP/RevitMCP.2024.addin`, `MCP/RevitMCP.2025.addin`, or any version-specific add-in file.
- Do not create nested `MCP/MCP/` directories.
- Do not hardcode absolute DLL paths in `.addin` files.
- Do not change `<AddInId>` unless explicitly requested and coordinated.
- Do not set `<DeployAddin>true</DeployAddin>` in `MCP/RevitMCP.csproj`.
- Do not change port `8964` without updating every config template and documentation reference.

Required:

- One csproj: `MCP/RevitMCP.csproj`.
- One add-in manifest: `MCP/RevitMCP.addin`.
- One primary installer: `scripts/install-addon.ps1`.
- Build configs: `Release.R22`, `Release.R23`, `Release.R24`, `Release.R25`, `Release.R26`.
- Add-in assembly path remains relative: `RevitMCP\RevitMCP.dll`.

## AI Guard Rails

### Do Not Bypass MCP

Do not write ad hoc WebSocket scripts that directly send JSON to `ws://localhost:8964`.
Do not bypass `MCP-Server/src/tools/*.ts` and the Revit command dispatcher.
Do not invent raw `CommandName` / `Parameters` / `RequestId` payloads outside the established bridge.

If a tool is missing, create or modify the proper MCP tool definition and matching Revit command implementation.

### Tool Call Data Honesty

Every concrete datum in an answer must trace to a tool response in the current turn:

- IDs, GUIDs, element names, room names, view names.
- Lists of entities.
- Counts, areas, lengths, percentages, coordinates, measurements.
- Native external-system type names.

Do not fill these from memory or from a previous turn.

Before output:

1. If the draft contains a six-or-more digit number, it must appear in a tool response from this turn.
2. If the draft lists two or more named entities of the same kind, each must appear in a tool response from this turn.
3. If the draft states a count, area, length, or percentage, it must be derivable from tool output.
4. If the draft names a Revit-native type or class in a project-specific way, it must come from a tool response.

If tools are unavailable, say so and switch to generic guidance.

### Domain Method Compliance

When a task involves code compliance, regulation checks, engineering analysis, BIM quantity calculations, or a workflow covered by `domain/*.md`, the domain file defines the method.

The model's general knowledge does not define the method.

Before computing:

1. Identify whether the request matches a domain trigger.
2. Read the relevant domain file.
3. Follow its formulas, exclusions, deductions, multipliers, and edge cases.
4. If tool output lacks required fields, stop and fetch the missing fields or state that the analysis is under-specified.

Output should cite the domain file used, for example:

```text
Per domain/daylight-area-check.md, step N: ...
```

### Active State Re-Anchoring

Any claim or action depending on active Revit context must be anchored in this turn.

Re-anchor before using:

- current document
- active view
- active level
- current selection
- view ID
- level name
- side-effecting view overrides or model edits

Use `get_active_view` before the dependent operation; if it is unavailable, call `get_all_views` and identify the active view from its result. Do not reuse a view ID or level name from an earlier turn.

If the anchor tool times out, retry once. If it still fails, stop and report the limitation.

## Domain vs Skill

Domain files and skills have different responsibilities:

| Layer | Location | Purpose | Language Policy |
|---|---|---|---|
| Domain | `domain/*.md` | Shared BIM SOP, regulations, formulas, review methods | Must remain readable by both humans and AI; do not convert to English-only |
| Skill | `.claude/skills/*/SKILL.md` | AI workflow orchestration and tool sequence guidance | Prefer English; preserve exact local terms where needed |
| Command | `.claude/commands/*.md` | Slash-command behavior | English preferred |
| AI constitution | `CLAUDE.md` | Global AI rules and project map | English only |
| Human docs | `README.md`, `README.en.md`, `docs/` | Installation, onboarding, teaching | Use the target human audience language |

## Domain Knowledge and Workflow Files

Read the matching file before applying a workflow or calculation.

| Trigger Keywords | File |
|---|---|
| building code, code compliance, FAR, floor area, fire compartment, egress, stair width, corridor width | `domain/references/building-code-tw.md` |
| auto dimension, ray cast, dimension workflow | `domain/auto-dimension-workflow.md` |
| corridor, escape route, egress route, corridor analysis | `domain/corridor-analysis-protocol.md` |
| curtain wall, panel pattern, curtain panel | `domain/curtain-wall-pattern.md` |
| daylight, daylight area, natural lighting | `domain/daylight-area-check.md` |
| dependent view, crop, grid crop, view split | `domain/dependent-view-crop-workflow.md` |
| dwg, cad, 柱匯入, 圖層建柱, 批次建柱, column from dwg, 柱號對應, 柱名稱對應, textLayerName | `domain/dwg-column-import.md` |
| detail component, detail sync, annotation component | `domain/detail-component-sync.md` |
| door legend, window legend, schedule legend | `domain/door-window-legend-workflow.md` |
| element coloring, visualization, graphic override | `domain/element-coloring-workflow.md` |
| family inventory, type inventory, unused type, duplicate type, purge type, merge type, 族群整理, 類型盤點, 未使用類型, 重複類型 | `domain/family-inventory-cleanup.md` |
| element query, filter, category fields | `domain/element-query-workflow.md` |
| exterior wall opening, facade opening | `domain/exterior-wall-opening-check.md` |
| facade generation, AI facade design | `domain/facade-generation.md` |
| finish legend, room finish legend | `domain/finish-legend-creation.md` |
| fire rating, fireproofing | `domain/fire-rating-check.md` |
| floor area, FAR review, gross floor area | `domain/floor-area-review.md` |
| floor slope, drainage slope, slab slope, 樓板坡度, 排水坡度, 洩水 | `domain/floor-slope-analysis.md` |
| IFC, structural sync, imported structural framing | `domain/ifc-structural-sync.md` |
| mechanical part, assembly, BIP, mechanical documentation | `domain/mechanical-part-doc.md` |
| MEP clash, CSA clash, penetration, beam penetration | `domain/mep-csa-clash-detection.md` |
| MEP extension, pyRevit MEP guide | `domain/mep-extension-guide.md` |
| parking numbering, auto parking numbering | `domain/parking-auto-numbering.md` |
| parking clearance, vehicle clearance, 210cm | `domain/parking-clearance-check.md` |
| parking count, parking space review | `domain/parking-space-review.md` |
| PDF export, DCC, PDFExportOptions | `domain/pdf-export-comparison.md` |
| fill pattern, Revit fill pattern conversion | `domain/revit-fill-pattern-conversion.md` |
| partition takeoff, partition quantity | `domain/revit-partition-takeoff.md` |
| room boundary, room boundary model | `domain/room-boundary.md` |
| room numbering, automatic room numbering | `domain/room-numbering-workflow.md` |
| room surface area, finish surface area | `domain/room-surface-area-review.md` |
| section numbering, auto section numbering | `domain/section-auto-numbering.md` |
| section datum, crop box, section adjustment | `domain/section-datum-adjustment.md` |
| sheet, viewport, titleblock, sheet management | `domain/sheet-viewport-management.md` |
| smoke exhaust, smoke vent, effective opening | `domain/smoke-exhaust-review.md` |
| stair compliance, stair headroom, stair check | `domain/stair-compliance-check.md` |
| stair hidden line, stair graphics | `domain/stair-hidden-line-workflow.md` |
| wall orientation, wall check | `domain/wall-check.md` |

Meta and governance domain files:

| Purpose | File |
|---|---|
| Domain catalog | `domain/README.md` |
| QA/QC checklist | `domain/qa-checklist.md` |
| Lessons learned | `domain/lessons.md` |
| Frontmatter standard | `domain/frontmatter-standard.md` |
| Path maintenance QA | `domain/path-maintenance-qa.md` |
| Session context guard | `domain/session-context-guard.md` |
| Skill authoring standard | `domain/skill-authoring-standard.md` |
| Tool capability boundary | `domain/tool-capability-boundary.md` |

## Skills

Available Claude skills:

- `/auto-dimension`
- `/building-compliance`
- `/build-revit`
- `/claude-md-sync`
- `/curtain-wall`
- `/dependent-view-crop`
- `/deploy-addon`
- `/detail-component-sync`
- `/detect-clashes`
- `/dwg-column-import`
- `/element-coloring`
- `/element-query`
- `/facade-generation`
- `/family-inventory-cleanup`
- `/fire-safety-check`
- `/hj-pr-proposal`
- `/parking-check`
- `/qa-review`
- `/sheet-management`
- `/smoke-exhaust`
- `/stair-hidden-line`
- `/wall-orientation-check`

Use the smallest relevant skill set. If a skill and a domain file conflict on the method, the domain file wins.

## MCP Profiles

`MCP-Server/src/tools/index.ts` supports `MCP_PROFILE`:

- `full`
- `architect`
- `mep`
- `structural`
- `fire-safety`

Use `full` unless a constrained client context explicitly needs a smaller tool surface.

## AI Client Configuration

Project-level Claude Code config:

```json
{
  "mcpServers": {
    "revit-mcp": {
      "type": "stdio",
      "command": "node",
      "args": ["./MCP-Server/build/index.js"],
      "env": {}
    }
  }
}
```

VS Code config:

```json
{
  "servers": {
    "revit-mcp": {
      "type": "stdio",
      "command": "node",
      "args": ["${workspaceFolder}/MCP-Server/build/index.js"],
      "env": {}
    }
  }
}
```

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---|---|---|
| AI cannot find Revit tools | MCP server not configured or build output missing | Run `npm run build` in `MCP-Server`, then restart the AI client |
| MCP server cannot connect to Revit | Revit is not running or MCP service is off | Start Revit and click MCP service on/off in the ribbon |
| Port `8964` is unavailable | Existing listener or orphaned HTTP.sys queue | Run `scripts/release-port.ps1` as needed |
| Add-in not visible in Revit | Add-in manifest or DLL missing | Re-run `scripts/install-addon.ps1` |
| Build succeeds but docs mention old DLL path | Stale documentation | Use `MCP/bin/Release.R{YY}/RevitMCP.dll` |

## QA/QC

Before completing changes that affect docs, tools, skills, domain files, build config, or deployment:

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```

For release or deployment validation, run without skip flags on Windows with the required SDKs installed:

```powershell
.\scripts\verify-qaqc.ps1 -Version 2024
```

QA/QC must cover:

- forbidden legacy files
- required file structure
- stale path references
- build config consistency
- add-in manifest safety
- runtime tool count alignment
- domain and skill count alignment
- domain table forward/reverse link checks
- local markdown link rot
- AI/human/shared document audience classification
- mojibake risk in AI-only and human-facing canonical docs
- markdown count-table claims (`| Runtime MCP tools | N |` style) in CLAUDE.md, README, README.en, and the audience inventory
- client config template portability (no hardcoded user paths; `<YOUR_PROJECT_PATH>` placeholder required)
- snapshot banner (`data-snapshot="YYYY-MM-DD"`) on date-prefixed `docs/MMDD-*.html`

## Logging Protocol

Append meaningful AI-driven changes to the current monthly log:

```markdown
## [YYYY-MM-DD HH:MM] {event-type} | {short-description}
- actor: {model-id} (via {client-name})
- files: {comma-separated list}
- trigger: {git-hook | claude-hook | manual}
- summary: {one-liner}
```

Do not log secrets, API keys, or large tool outputs.

## Documentation Writing Policy

Use `docs/DOCUMENT_AUDIENCE_INVENTORY.md` as the classification source.

Rules:

1. AI-only documents should be English.
2. Human-facing Traditional Chinese documents may be Chinese, but must be valid UTF-8 and readable.
3. English human-facing documents should not contain mojibake.
4. Domain files are shared by humans and AI. They must not become English-only.
5. Domain files may use bilingual headings and terminology when useful.
6. Any new domain file must include frontmatter consistent with `domain/frontmatter-standard.md`.
7. Any new AI instruction file must declare whether it is canonical, redirect, command, skill, or local-only.
8. Date-prefixed `docs/MMDD-*.html` files are immutable event snapshots: they must carry a `data-snapshot="YYYY-MM-DD"` banner, their numbers are never re-synced, and QA/QC count checks intentionally skip them.

## Final Pre-Response Checklist

Before answering with project-specific facts:

1. Did I read the latest relevant files in this turn?
2. If live Revit state is involved, did I call the relevant MCP tool in this turn?
3. If a domain method applies, did I read and follow the domain file?
4. If active view/level/selection matters, did I re-anchor in this turn?
5. If I changed docs or counts, did I run QA/QC or state why I could not?
