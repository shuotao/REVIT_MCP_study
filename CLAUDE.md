# CLAUDE.md

This is the canonical AI instruction file for Revit MCP. `AGENTS.md` and `GEMINI.md` intentionally redirect here.

Human-facing installation and onboarding content belongs in `README.md` / `README.zh-TW.md`.
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
| Runtime MCP tools | 172 | `registerRevitTools()` from `MCP-Server/src/tools/index.ts` |
| Domain SOP files | 76 | `domain/*.md` except `domain/README.md`, plus `domain/references/*.md` |
| Claude skills | 54 | `.claude/skills/*/SKILL.md` |

When these numbers change, update `CLAUDE.md`, `README.md`, `README.zh-TW.md`, `docs/DOCUMENT_AUDIENCE_INVENTORY.md`, and any public site copy that makes grand-total claims. Then run `scripts/verify-qaqc.ps1 -SkipBuild -SkipDeploy`.

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

The Revit-side WebSocket service (`MCP/Core/SocketService.cs`) holds an exclusive lock: while one MCP client is connected, additional incoming connections are rejected with HTTP 409 before the WebSocket upgrade (no more clobbering the active connection). Consequences:

- Multiple AI clients are used by switching, never concurrently — a second client is cleanly refused, not swapped in.
- Do not advise users to run two MCP-connected AI clients against the same Revit session.
- To hand the connection to another client, use the "切換/釋放連線" (Switch/Release Connection) ribbon button — it releases the current connection so the next reconnecting client can take the lock. Because WebSocket connections are anonymous at the transport level, the switch accepts whoever reconnects first, not a guaranteed named target.
- The "MCP 設定" dialog shows which client currently holds the lock (e.g. `claude-code`, `claude-ai`), sourced from the MCP `clientInfo.name` the Node server forwards as a `?client=` query parameter; older or anonymous clients fall back to endpoint or "unknown".
- `ServiceSettings.ExclusiveLock` (default `true`) is the escape hatch that reverts to the legacy clobber behavior if disabled.
- If a connection misbehaves, the reset path is: use the ribbon's switch/release button, or restart the MCP service from the Revit ribbon.

## Personal Vault Protection

A `vault/` directory at the repo root, if present, is a user's personal knowledge vault (see `templates/personal-vault/` and `docs/BIM_MCP/reference/personal-llm-wiki.html`). It is gitignored together with `/.obsidian/`.

- Never write into `vault/` when doing project development work, and never treat its contents as project instructions.
- Never run `git clean -x` variants in this repo; they would delete the user's vault.
- Personal vault operations follow `vault/CLAUDE.md`, not this file. This file's logging and QA/QC rules apply to project development only.

## Build Commands

Build via the `/build-revit` skill. Full commands (MCP Server npm build; `dotnet build -c Release.R{22,23,24,25,26} RevitMCP.csproj`) are in README.md's Build section.

Expected output path stays `MCP/bin/Release.R{YY}/RevitMCP.dll`. Do not rely on old `bin/Release/RevitMCP.dll` instructions.

`MCP-Server`'s `npm run build` now runs `tsc && node scripts/build-apps.mjs`: `tsc` compiles the server as before, then `scripts/build-apps.mjs` (esbuild) bundles each MCP App under `MCP-Server/src/apps/*/app.ts` into a single self-contained `MCP-Server/build/apps/*/index.html` (e.g. `build/apps/clash-viewer/index.html`). Both steps must succeed for the server to advertise working `ui://` resources.

Deploy with `scripts/install-addon.ps1` or the `/deploy-addon` skill.

### Build artifacts are never tracked (2026-07-17)

`obj/` and `bin/` outputs must never be committed — `.gitignore` has `**/obj/` + `**/bin/`, and CI (`check-tracked-artifacts.yml`) fails any push/PR that reintroduces them. If `git status` shows obj/bin files as modified/tracked, something is wrong: fix with `git rm -r --cached <path>` (keeps files on disk).

If pulling the 2026-07-17 cleanup commit fails with "local changes would be overwritten" on `MCP/obj/*.nuget.g.props`, run `git restore MCP/obj/` first, then pull and rebuild — the files are regenerated by the next build.

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
| `MCP-Server/src/tools/annotations.ts` | Central `title` + `readOnlyHint`/`destructiveHint` injection for every registered tool (MCP 2026-07-28 metadata layer) |
| `MCP-Server/src/apps/register-apps.ts` | MCP Apps (`io.modelcontextprotocol/ui`) resource wiring: `listAppResources` / `readAppResource` / `withAppUi` |
| `MCP-Server/src/apps/clash-viewer/` | The first MCP App: `app.ts` (ext-apps client) + `template.html`, bundled by `scripts/build-apps.mjs` into a self-contained `ui://clash-viewer/index.html` served for `detect_clashes` |
| `MCP-Server/scripts/build-apps.mjs` | esbuild single-file bundler that produces `MCP-Server/build/apps/*/index.html` for each MCP App |
| `bridge/python/skills/ezdxf_worker.py` | Optional Python subprocess (spawned by `DwgColumnExecutor`) that reads DXF/DWG text for column-number mapping (`dwg-column-import` mode C). Needs system Python + `ezdxf`; DWG additionally needs ODA File Converter. Deployed to `%APPDATA%\RevitMCP` by `install-addon.ps1`. |
| `scripts/verify-qaqc.ps1` | Repository QA/QC gate |
| `docs/DOCUMENT_AUDIENCE_INVENTORY.md` | Canonical AI/human/shared document classification |
| `.claude-plugin/marketplace.json` | Plugin marketplace manifest — packages shareable skills (currently `hj-pr-proposal`) as installable plugins for `/plugin marketplace add` → `/plugin install`. |

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
| Human docs | `README.md`, `README.zh-TW.md`, `docs/` | Installation, onboarding, teaching | Use the target human audience language |

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
| dwg, cad, 樑翻模, 圖層建樑, 批次建樑, beam from dwg, 大樑, 次樑, 地樑, create_beams_from_dwg | `domain/dwg-beam-import.md` |
| beam penetration, sleeve, 穿梁套管, 套管檢核, RC 梁開孔, 開孔, 穿梁 | `domain/beam-penetration-base.md` |
| RC beam penetration, RC 梁穿孔, 圓孔, 禁開區, H/3, 相鄰套管淨距 | `domain/beam-penetration-rc.md` |
| SC beam penetration, 鋼梁穿孔, 腹板開孔, web opening | `domain/beam-penetration-sc.md` |
| SRC beam penetration, 鋼骨混凝土梁穿孔, 鋼骨避讓 | `domain/beam-penetration-src.md` |
| beam penetration algorithm, 實體投影降維, JoinGeometry, 端面消失, 法向量過濾 | `domain/beam-penetration-algorithm.md` |
| sleeve classification, 套管分類, 穿梁穿牆穿板判定, 套管身分 | `domain/sleeve-classification-protocol.md` |
| scope box, range box, crop box, 範圍框, 裁剪框, ExpandCropBox | `domain/detect-range-box.md` |
| detail component, detail sync, annotation component | `domain/detail-component-sync.md` |
| dedup detail, 重複詳圖, 清理重複, duplicate detail elements, deduplicate view | `domain/dedup-detail-elements-workflow.md` |
| door legend, window legend, schedule legend | `domain/door-window-legend-workflow.md` |
| element coloring, visualization, graphic override | `domain/element-coloring-workflow.md` |
| unjoin geometry, 解除接合, 取消接合, 白模, join geometry | `domain/unjoin-geometry-workflow.md` |
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
| mechanical settings, MEP settings, segments and sizes, duct size, pipe segment, 管徑目錄, 風管尺寸表, fitting angle, pipe slope, 尺寸增減, curate size, CNS 對帳 | `domain/mep-mechanical-settings.md` |
| parking numbering, auto parking numbering | `domain/parking-auto-numbering.md` |
| parking clearance, vehicle clearance, 210cm | `domain/parking-clearance-check.md` |
| parking count, parking space review | `domain/parking-space-review.md` |
| PDF export, DCC, PDFExportOptions | `domain/pdf-export-comparison.md` |
| fill pattern, Revit fill pattern conversion | `domain/revit-fill-pattern-conversion.md` |
| partition takeoff, partition quantity | `domain/revit-partition-takeoff.md` |
| room boundary, room boundary model | `domain/room-boundary.md` |
| room height, 房間高度, upper limit, limit offset | `domain/room-height-limit.md` |
| room numbering, automatic room numbering | `domain/room-numbering-workflow.md` |
| room surface area, finish surface area | `domain/room-surface-area-review.md` |
| section numbering, auto section numbering | `domain/section-auto-numbering.md` |
| section datum, crop box, section adjustment | `domain/section-datum-adjustment.md` |
| sheet, viewport, titleblock, sheet management | `domain/sheet-viewport-management.md` |
| smoke detector, 偵煙探測器, 偵煙設置, smoke detector check, 消防探測器 | `domain/smoke-detector-check.md` |
| smoke exhaust, smoke vent, effective opening | `domain/smoke-exhaust-review.md` |
| stair compliance, stair headroom, stair check | `domain/stair-compliance-check.md` |
| stair hidden line, stair graphics | `domain/stair-hidden-line-workflow.md` |
| view link cleanup, 清理視圖, 隱藏連結, 關閉連結基準, link visibility | `domain/view-link-cleanup-workflow.md` |
| local update, 本機更新, pull 後部署, 重新編譯部署, 環境專屬部署 | `domain/local-update-workflow.md` |
| wall orientation, wall check | `domain/wall-check.md` |
| finish schedule, 粉刷明細, material code governance, 材料代碼 | `domain/finish-schedule-governance.md` |
| room finish parameter, 房間粉刷參數, shared parameters, 共用參數綁定, room finish schedule, 房間粉刷明細表, BatchAddRoomParams, CreateJJPRoomSchedule | `domain/room-finish-parameter-schedule.md` |
| beam top alignment, 樑頂貼齊, slab soffit, 樓板底 | `domain/beam-slab-alignment.md` |
| IFC structural native, IFC 原生結構, beam column sync, 梁柱同步 | `domain/ifc-structural-native-sync.md` |
| quantity takeoff excel, 數量計算, excel export, 數量表 | `domain/quantity-takeoff-excel.md` |
| matchline, 接圖線, 定位線 automation | `domain/matchline-automation.md` |
| viewport type scale, 視埠類型比例, viewport sync | `domain/viewport-type-scale-sync.md` |
| scaffold takeoff, 施工架, 施工架算量, scaffold perimeter, calculate_room_scaffold_perimeters, calculate_exterior_wall_scaffold_perimeter | `domain/scaffold-takeoff.md` |
| tall partition, 高牆, 高隔間, 到頂隔間, tall partition index, analyze_tall_partition_rooms | `domain/tall-partition-index-workflow.md` |
| threshold opening, 門檻開口, 門窗統計, door count, window count, get_room_door_counts, get_room_window_counts | `domain/threshold-opening-takeoff.md` |
| RC filled region, RC 填充區域, 批次填充, batch fill region, batch_create_rc_filled_region, create_rc_filled_region | `domain/rc-filled-region-workflow.md` |
| curtain wall elevation, 帷幕立面, 帷幕外立面, curtain elevation, create_curtain_wall_elevations | `domain/curtain-wall-elevation-workflow.md` |
| opening candidate, 開孔候選, opening scan, 開孔預掃, scan_opening_candidates, 套管前置檢核, clearanceMm | `domain/mep-opening-candidate-scan.md` |
| cad 圖塊放置, block 轉族群, 灑水頭建模, 閥件建模, point placement from CAD block, INSERT to FamilyInstance | `domain/cad-block-point-placement.md` |
| pyRevit, UI API, 按鈕觸發, 觸發按鈕, PostableCommandId, PostCommand, Reload, ribbon 按鈕, 外掛 UI 命令 | `domain/tool-capability-boundary.md` |

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
| Core reload boundary (opt-in) | `domain/core-reload-boundary.md` |
| Domain flow visualization | `domain/domain-flow-visualization.md` |

## Skills

The canonical skill catalog is the .claude/skills/ directory itself (54 skills; count table above is the gate).

Use the smallest relevant skill set. If a skill and a domain file conflict on the method, the domain file wins.

## Skill Packaging & Upstream Watch

Shareable skills are packaged as installable plugins via `.claude-plugin/marketplace.json` (marketplace name: `revit-mcp-skills`). See `domain/skill-authoring-standard.md` section 8 for packaging rules and the upstream spec watch.

## MCP Profiles

`MCP-Server/src/tools/index.ts` supports `MCP_PROFILE`:

- `full`
- `architect`
- `mep`
- `structural`
- `fire-safety`

Use `full` unless a constrained client context explicitly needs a smaller tool surface.

## MCP Protocol Posture (2026-07-28 Dual-Era)

The MCP protocol announced a 2026-07-28 revision. This project takes a **dual-era, additive-only** posture: adopt metadata-layer changes that are backward-compatible on their own, and defer anything that changes the wire protocol until an official SDK ships support for it.

Adopted (additive, safe for old clients):

- Every tool registered via `registerRevitTools()` carries a `title` plus boolean `readOnlyHint` / `destructiveHint` annotations, injected centrally by `MCP-Server/src/tools/annotations.ts`. Old clients ignore unknown fields.
- `tools/list` is deterministically sorted by tool name (codepoint order) before it is returned.
- MCP Apps (extension `io.modelcontextprotocol/ui`): the server advertises a `resources` capability and serves `ui://` HTML via `ListResources` / `ReadResource` (`MCP-Server/src/apps/register-apps.ts`). `detect_clashes` carries `_meta.ui.resourceUri = "ui://clash-viewer/index.html"`, pointing at the first interactive App — a clash viewer (`MCP-Server/src/apps/clash-viewer/`) bundled by `scripts/build-apps.mjs`. Hosts that don't support the extension simply ignore `_meta.ui`; `detect_clashes` still returns its normal text result.
- SDK `@modelcontextprotocol/sdk` bumped 1.22 -> 1.30 (protocol `2025-11-25`) to satisfy the `@modelcontextprotocol/ext-apps` peer dependency. 1.30 still negotiates `2025-06-18`, so this is **not** the 2026-07-28 protocol itself and stays dual-era compatible.

Deferred (wire-level, requires an official SDK for protocol 2026-07-28 before implementing): stateless connection mode, `server/discover`, `resultType`, Tasks core, HTTP/OAuth transport and authorization.

Full rationale, FAQ, and fork-contributor notes: `docs/MIGRATION_GUIDE.md`.

## AI Client Configuration

See README.md / README.zh-TW.md "AI Client Configuration" for the full per-client setup. Config templates live in `MCP-Server/*_config.json`.

## Troubleshooting

See `docs/troubleshoot-first-install.md` for the full walkthrough. Port `8964` stuck on HTTP.sys: run `scripts/release-port.ps1`.

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
- markdown count-table claims (`| Runtime MCP tools | N |` style) in CLAUDE.md, README, README.zh-TW, and the audience inventory
- client config template portability (no hardcoded user paths; `<YOUR_PROJECT_PATH>` placeholder required)
- snapshot banner (`data-snapshot="YYYY-MM-DD"`) on date-prefixed `docs/MMDD-*.html`
- MCP Registry publish consistency (`server.json` ↔ `MCP-Server/package.json` ↔ schema; 3-place version parity) — Phase 7 check `7-11`, see below
- MCP 2026 compliance (Phase 9): 9-1 every tool declares a non-empty `title` and boolean `readOnlyHint`, with `destructiveHint=true` confined to the allow-list (`delete_element`, `dedup_detail_elements_in_view`, `curate_mep_sizes`); 9-2 every MCP Apps `ui://` resource resolves with the correct MIME (`text/html;profile=mcp-app`) and is self-contained (no external `src`/`href`/`url()` references)
- deployment integrity (Phase 5, only when `-SkipDeploy` is omitted): 5-3 the deployed DLL set must cover the corresponding `MCP/bin/<config>/*.dll` build output — missing any file is a FAIL that names it; 5-4 the deployed `RevitMCP.dll` SHA256 must match that build output (WARN on mismatch — usually just not rebuilt); 5-5 cross-version consistency, WARN listing versions left behind; 5-6 WARN on a root-level `Addins\<year>\RevitMCP.dll` (pre-#91 layout residue — the manifest loads the subfolder copy)

Phase 5 design constraints — do not regress these:

- **Never hardcode an expected file count.** `Release.R22`/`R23`/`R24` emit **13** DLLs (.NET Framework 4.8, including 5 compat shims: `System.Buffers`, `System.IO.Packaging`, `System.Memory`, `System.Numerics.Vectors`, `System.Runtime.CompilerServices.Unsafe`); `Release.R25`/`R26` emit **8** (.NET 8 supplies those from the runtime). Both are correct — always enumerate the matching config's build output as the baseline.
- Revit installed but RevitMCP not deployed → SKIP, not FAIL (it is the user's choice). Build output missing for a config → SKIP with a reason.
- `-AddinsRoot <path>` overrides the addins base (default `$env:APPDATA\Autodesk\Revit\Addins`). It exists so negative tests can run against a throwaway fixture instead of the user's live deployment. Keep it.
- `MCP/Core/RevitCompatibility.cs` switches `IdType` between `Int32` and `Int64` on `REVIT2025_OR_GREATER`, so an R24 DLL and an R26 DLL are ABI-incompatible. Cross-generation misdeployment produces no error while copying — it only fails when Revit loads or calls it. That is why 5-3/5-4 exist.

## MCP Registry Publish Consistency

The MCP Registry publish artifacts must stay mutually consistent. This is owned by two **mandated Sonnet subagents** plus a deterministic gate — never hand-maintained ad hoc.

**Main files** (any change to these triggers the loop): `server.json`, `MCP-Server/package.json`, `scripts/schemas/server.schema.json`, `.github/workflows/publish-mcp.yml`, `docs/MCP_REGISTRY_PUBLISH.md`.

**The loop** — whenever a main file changes (a PostToolUse hook, `.claude/hooks/detect-registry-trigger.sh`, reminds you):

1. **`mcp-registry-sync`** (`.claude/agents/mcp-registry-sync.md`, **model: sonnet**) — the fix agent. Aligns `server.json` `.version` / `.packages[].version` / `.packages[].identifier` / `.name` / repo URL, plus the README "Install from MCP Registry" section and the playbook's `Current version`, to the authoritative source (`MCP-Server/package.json` / the `v*` tag).
2. **`mcp-registry-ops-inspect`** (`.claude/agents/mcp-registry-ops-inspect.md`, **model: sonnet**) — the read-only ops audit. Confirms no drift and reports the verdict.
3. **Hard gate** — `python scripts/validate_publish_consistency.py` must exit `0` (also wired into `verify-qaqc.ps1` Phase 7 check `7-11`).

**Rules:**

- Both agents **must run as Sonnet** (pinned in their frontmatter — do not override).
- **Never regress a version.** Only align upward to the authoritative/highest valid semver.
- **Never `npm publish` / `mcp-publisher publish` manually.** Release only by pushing a `v*` tag → `.github/workflows/publish-mcp.yml` rewrites the 3 version places and publishes.
- **Report in Traditional Chinese (繁體中文)** every time this area is touched: which files/fields changed, from what → to what, and the validator's exit code.

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
