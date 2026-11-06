# CLAUDE.md

This is the canonical AI instruction file for Revit MCP. `AGENTS.md` and `GEMINI.md` intentionally redirect here.

Human-facing installation and onboarding content belongs in `README.md` / `README.zh-TW.md`.
Shared BIM methods belong in `domain/*.md` and must remain bilingual or Chinese-friendly.
AI-only operating instructions belong here and should be written in English to avoid encoding drift and mojibake.

## Project Overview

## Abby column creation workflow

For CAD column creation requests, follow `docs/ABBY-COLUMN-WORKFLOW.md` (user-defined workflow, 2026-10-24). Ask together for the outline layer, base level and top level unless already supplied. The user's response authorizes the complete batch: read CAD outlines and numbers, match/create size types, place columns, fill Text-group number and Floor, then verify. Do not omit metadata or revert to per-element confirmation loops. Confirm deployed tool capability before starting; avoid repeated compilation for routine modeling. Keep protected skills/config untouched.

## Project architecture

For elevation CAD steel beams, follow `docs/ABBY-ELEVATION-STEEL.md`. Use the existing `create_beams_from_dwg` tool's `elevationSteel` mode: CAD top lines provide Z, the named plan grid provides the center plane, and exact schedule dimensions determine the type. Write both Constraints/編號 and Text/編號. BOX thickness uses 牆標稱厚度. Preview before apply; do not treat a build as live validation.

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

The add-in also ships one in-Revit WPF surface of its own: the **camera-manager dockable pane** (`MCP/UI/`, registered in `OnStartup` under a fixed GUID). It is driven by a ribbon button and Revit events, calls the Revit API directly on the UI thread, and never goes through the WebSocket bridge — so a session with no MCP client connected still has a working pane.

Separately, there is an optional embedded-chat *direction* where a Revit WPF window would call an AI API directly. That one is still hypothetical — nothing in the repo implements it — and is unrelated to both the MCP stdio path and the pane above.

A second, unrelated stdio MCP server ships in the repo: `scripts/monstrare_mcp_server.py` exposes the Monstrare kanban cards (`tools/kanban/cards/*.json`) as MCP tools. It does not talk to Revit and is not part of the bridge above. See "Monstrare" below.

## Current Source-of-Truth Counts

These counts must be derived from source, not copied by memory.

| Item | Current Count | Source of Truth |
|---|---:|---|
| Runtime MCP tools | 253 | `registerRevitTools()` from `MCP-Server/src/tools/index.ts` |
| Domain SOP files | 94 | `domain/*.md` except `domain/README.md`, plus `domain/references/*.md` |
| Claude skills | 61 | `.claude/skills/*/SKILL.md` |

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

- Any handshake carrying an `Origin` header is rejected with HTTP 403 **before** the exclusive-lock check and before the upgrade (issue #125). WebSocket handshakes are exempt from the same-origin policy and do no CORS preflight, so without this a malicious page open in the user's browser could drive the add-in. The node MCP bridge (`ws`) sends no `Origin`; browsers always do per RFC 6455, which separates the two at zero cost to the bridge. It sits before the lock so an untrusted handshake can neither learn the lock state nor squat on it. This gate has **no settings opt-out** — unlike `ExclusiveLock` below.
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
| `MCP/UI/CameraManagerPaneProvider.cs` | `IDockablePaneProvider` for the camera-manager pane. The pane's `FrameworkElement` must be constructed in `OnStartup` — Revit never rebuilds that instance after `RegisterDockablePane` |
| `MCP/UI/CameraManagerPanelControl.cs` | The pane itself: WPF built in code, no XAML. Styling comes only from `MCP/UI/CameraManagerTheme.cs` (every container needs an explicit `Background`; the pane host paints black behind a transparent one) |
| `MCP/Core/CameraManagerEventQueue.cs` | FIFO `ExternalEvent` queue used **only** by the pane. Deliberately separate from `ExternalEventManager`, whose single `_action` field would drop queued pane work |
| `MCP/Core/CameraKeyPlanService.*.cs` | Key Plan generation, split across eight `partial` files by concern (Overview / Sheet / MarkerA / EyeRoom / LiveUpdate / Versions / ViewHygiene / core) |
| `MCP/Resources/icon-32-{on,off}.png` | Ribbon toggle-button state icons, compiled as WPF `<Resource>` into `RevitMCP.g.resources`. Load them only through an explicit-assembly pack URI (`pack://application:,,,/RevitMCP;component/...`) — the assembly-less relative form needs `Application.ResourceAssembly`, which an add-in inside Revit.exe cannot rely on |
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
| `scripts/monstrare_mcp_server.py` | Optional second stdio MCP server exposing the Monstrare kanban (`tools/kanban/cards/*.json`). Independent of the Revit bridge; protocol `2024-11-05`, not covered by the dual-era posture below |
| `.agents/AGENTS.md` | Monstrare entry point. Carries a governance-precedence banner stating `CLAUDE.md` wins on any conflict. **Not** the root `AGENTS.md`, which is a redirect to this file |
| `scripts/verify-qaqc.ps1` | Repository QA/QC gate |
| `docs/BIM_MCP/reference/mep-playbook.html` | Hub page for the MEP design playbook (`docs/mep-design-playbook-ch1..ch3` + the model guide). The chapters are teaching material derived from an Autodesk certification course model — **not a client project**; the page states that provenance up front. Method definition stays in `domain/mep-space-demand-matrix.md` |
| `docs/BIM_MCP/reference/tools-index.html` | Generated index of every runtime tool (one card each, badge = `readOnlyHint`/`destructiveHint`). Regenerate with `python3 scripts/gen-tools-index.py`; `7-14` fails on hand-edits that drift |
| `docs/BIM_MCP/reference/skills-index.html` | Generated index of every skill (one card + one quick-table row each). Regenerate with `python3 scripts/gen-skills-index.py`; its only curated input is `scripts/skills-index-map.json` (group A–G, Chinese description, trigger tags, Domain links). `7-18` fails on hand-edits that drift |
| `docs/DOCUMENT_AUDIENCE_INVENTORY.md` | Canonical AI/human/shared document classification |
| `.claude-plugin/marketplace.json` | Plugin marketplace manifest — packages shareable skills (currently `hj-pr-proposal`) as installable plugins for `/plugin marketplace add` → `/plugin install`. |

## Code Conventions

- C# namespace: `RevitMCP`.
- Revit model changes must run inside `Transaction` and be reversible.
- Revit API work must go through `ExternalEventManager` when called from the WebSocket flow.
- Revit API work initiated by the dockable pane goes through `CameraManagerEventQueue`, not `ExternalEventManager`. Do not merge the two: the pane can have several operations in flight, and `ExternalEventManager` holds one `_action` at a time.
- Work triggered by `DocumentChanged` / `ViewActivated` must only set a dirty flag; do the actual reading and redrawing on the next `Idling`. Those two events fire while a document is still opening, and heavy WPF or `ExportImage` work there crashed Revit (`0xe0434352`) — see `docs/camera-manager/adr.zh-TW.md` D-008.
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

### Subagent Delegation Boundaries

**A constraint you are operating under does not propagate to a subagent unless you write it into the delegation message.** The subagent cannot see your system prompt, this file, or the earlier conversation. It sees only what you send.

Before spawning or messaging a subagent, restate every constraint that applies to the work you are handing over. At minimum:

- **What must not be written**, and to which files. Name the confidential source explicitly (e.g. "client documents under `<path>` are confidential; this repo is public — pass methods only, never client names, project codes, document titles, or client-specific figures").
- **Which files the subagent may write**, and which it must not touch.
- **Which tools it must not call** (git operations, Revit MCP, network).
- **What to do when your instruction conflicts with what it observes** — it should trust the repository and say so.

Two failure modes to guard against:

1. **You quote the confidential material while describing the task.** A delegation message that says "record what we found in the client's unit inventory" is safe; one that names the client and the document is not. **The subagent will faithfully record whatever you put in front of it — that is its job.** If confidential content reaches it, the leak is already yours.
2. **You assume a rule stated once in the session still governs.** It governs *you*, not the agent you spawn ten turns later.

**Applies to every delegation path**: `Agent`, `SendMessage` to a resumed agent, `Workflow` scripts, and skills that run in a subagent.

> **Incident (2026-08-12)**: A background recorder agent wrote client identifiers into a repo-tracked file across three commits, which were pushed to a public repository and remained reachable for roughly 21 hours. The confidentiality rule had been stated repeatedly in the session and was already written in `log/2026-08.md` — but it was never included in the messages sent to the agent, and the messages themselves named the client. The agent behaved correctly throughout; it later detected the violation itself and redacted the working file. Remediation required deleting the remote branch and filing a GitHub Support ticket for cache purging, neither of which restores the exposure window.
>
> **Rule that would have prevented it**: constraints travel with the work, not with the person who knows them.

## Domain vs Skill

Domain files and skills have different responsibilities:

| Layer | Location | Purpose | Language Policy |
|---|---|---|---|
| Domain | `domain/*.md` | Shared BIM SOP, regulations, formulas, review methods | Must remain readable by both humans and AI; do not convert to English-only |
| Skill | `.claude/skills/*/SKILL.md` | AI workflow orchestration and tool sequence guidance | Prefer English; preserve exact local terms where needed |
| Command | `.claude/commands/*.md` | Slash-command behavior | English preferred |
| AI constitution | `CLAUDE.md` | Global AI rules and project map | English only |
| Human docs | `README.md`, `README.zh-TW.md`, `docs/` | Installation, onboarding, teaching | Use the target human audience language |
| Teaching companion | `docs/mep-design-playbook-*.md` | A worked walkthrough of one domain method, for people meeting it for the first time. **Not a fourth layer** — the domain file still defines the method, and wins on any conflict | Traditional Chinese |

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
| space demand matrix, 空間需求矩陣, MEP tag, 容量包絡, 機房面積反算, 前期容量, 基本設計容量, programming, SMP, concept design, Space 明細表, 逐空間通風檢核, FUM | `domain/mep-space-demand-matrix.md` |
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
| green building material, green material, 綠建材, 綠建材標章, TABC, 綠建材採購指南, 健康綠建材, 高性能綠建材, 再生綠建材, 生態綠建材 | `domain/GM_catalog.md` |
| green material parameter, 綠建材參數, GreenMaterial_ 共享參數, 綠建材標註, 六槽位, Mat1, layerComposition, materialSlotAssignment | `domain/GM_parameter-schema.md` |
| green material search, 綠建材關鍵字, 關鍵字檢索, 同義擴充, DATA Engine, GBM 證號查詢 | `domain/GM_keyword-search.md` |
| RFA family injection, 門窗綠建材, 獨立元件, RFA 導入, loadable family, 防音門窗, Low-E玻璃, 遮陽係數, 隔音Rw | `domain/GM_rfa-family-injection.md` |
| opening candidate, 開孔候選, opening scan, 開孔預掃, scan_opening_candidates, 套管前置檢核, clearanceMm | `domain/mep-opening-candidate-scan.md` |
| cad 圖塊放置, block 轉族群, 灑水頭建模, 閥件建模, point placement from CAD block, INSERT to FamilyInstance, get_dwg_block_instances, preview_family_instances_from_dwg_blocks, create_family_instances_from_dwg_blocks | `domain/cad-block-point-placement.md` |
| space centroid, 空間中心點, 代表點, centroid, 批次放置, 逐室放置, 風口放置, air terminal, place_family_instances, get_space_centroid, IsPointInSpace | `domain/space-centroid-placement.md` |
| 機電教戰手冊, MEP playbook, 教戰手冊, 五種量, 量從哪裡來, 決定的量, 教學假設, 放風口, 系統流量 vs 端點需求, 旗標誠實但問錯問題 | `domain/mep-space-demand-matrix.md`（方法） + `docs/BIM_MCP/reference/mep-playbook.html`（教學導覽） |
| pyRevit, UI API, 按鈕觸發, 觸發按鈕, PostableCommandId, PostCommand, Reload, ribbon 按鈕, 外掛 UI 命令 | `domain/tool-capability-boundary.md` |
| view template comparison, 視圖樣板比對, 樣板差異, 僅顯示差異, 樣板稽核, QC audit | `domain/view-template-comparison.md` |
| archicad adapter, 轉譯邊界, archicad backend, 能力降級 | `domain/archicad-adapter-boundary.md` |
| archicad 名詞對照, terminology map, GUID, Story, Zone, Layout, Tapir | `domain/archicad-terminology-map.md` |
| archicad 可攜性, portability matrix, A/B/C 分級, Wave 驗證狀態 | `domain/archicad-portability-matrix.md` |
| archicad 結構 dwg, archicad 逐層校準, archicad 版次比對, Property 標記 | `domain/archicad-structural-dwg-sync.md` |
| 批次注入共用參數, 門窗共參, shared parameter injection, FamilyManager.AddParameter, CategoryBinding, 模型檢查器 | `domain/revit-batch-family-shared-param-injection-spec.md` |
| 材料影像, 貼圖遺失, 重連貼圖, relink material images, Appearance Asset, unifiedbitmap | `domain/material-image-relinking.md` |
| 送照面積計算式, 樓板面積算式, 面積計算式, floor area formula, create_floor_area_formula | `domain/permit-floor-area-formula.md` |
| 順序編號, 編號插入, 圖面編號, 跳4, sequence numbering, create_sequence_numbers | `domain/sequence-numbering.md` |
| 停車格重新編號, renumber_parking_spaces | `domain/parking-auto-numbering.md` |
| 梁下淨高, 樑下淨高, 淨高檢討, 天花內管線空間, clear height, check_beam_clear_height | `domain/beam-clear-height-check.md` |
| 管線間距, 管線淨距, 管線預留, 維修空間, 保溫間距, MEP spacing, check_mep_spacing | `domain/mep-spacing-clearance-check.md` |

Meta and governance domain files:

| Purpose | File |
|---|---|
| Domain catalog | `domain/README.md` |
| QA/QC checklist | `domain/qa-checklist.md` |
| Lessons learned | `domain/lessons.md` |
| Anti-lessons (negative examples) | `domain/anti-lessons.md` |
| Frontmatter standard | `domain/frontmatter-standard.md` |
| Path maintenance QA | `domain/path-maintenance-qa.md` |
| Session context guard | `domain/session-context-guard.md` |
| Skill authoring standard | `domain/skill-authoring-standard.md` |
| Tool capability boundary | `domain/tool-capability-boundary.md` |
| Core reload boundary (opt-in) | `domain/core-reload-boundary.md` |
| Domain flow visualization | `domain/domain-flow-visualization.md` |

## Skills

The canonical skill catalog is the .claude/skills/ directory itself (61 skills; count table above is the gate).

Use the smallest relevant skill set. If a skill and a domain file conflict on the method, the domain file wins.

## Monstrare (Opt-In Process Layer)

`ai/` (35 files), `tools/kanban/` (25 files), `.agents/AGENTS.md`, and `scripts/monstrare_mcp_server.py` are **Monstrare** — a gate-based spec/plan/verify/review workflow contributed with PR #116 (@CWLin0518) and harvested 2026-08-31.

- **It is opt-in, not the project default.** Nothing requires you to follow it unless explicitly asked.
- **`CLAUDE.md` wins on any conflict.** `.agents/AGENTS.md` carries this precedence banner explicitly.
- **`ai/skills/*.md` are not Claude Code skills.** They do not live in `.claude/skills/`, are not counted in the 61, and are not bound by `domain/skill-authoring-standard.md`.
- An audit against this file's AI Guard Rails, Deployment Rules, and Logging Protocol found no contradicting rule — only unstated precedence, now stated.

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
- claim-site scan scope (Phase 7 checks `7-1`/`7-2`/`7-3`): `$scanPaths` walks `docs\BIM_MCP\**\*.html` recursively. Exclusions live in `$skipPatterns` and nowhere else — never narrow coverage by editing a glob, because a file that is never read produces the same green report as a file that passes
- three-layer enumeration parity (Phase 7 checks `7-9`/`7-10`/`7-14`): every Domain file, every Skill, and every runtime tool must have exactly one card on its BIM_MCP index page, checked in both directions. `7-1`/`7-2`/`7-3` only verify that the stated *numbers* are right — a correct count is fully compatible with zero items being documented, which is how the Tool layer stayed unlisted while every count claim passed. `7-14` additionally checks that `tools-index.html`'s own derived tallies (badge counts, per-category counts) agree with the cards it actually contains, **and that every card's `tool-desc` text equals that tool's runtime `description`** — compared after HTML-unescaping and whitespace collapse, with raw markup inside a card rejected outright rather than stripped, and a mismatch reported as the first divergence offset plus a window from both strings so a tail edit in a 4000-character description stays locatable. A *present* card is exactly as compatible with a *wrong* card as a correct count is with zero cards: the description comparison's first run found **45** stale cards sitting behind a green `7-14`, including three the same session had just caused. That page is generated by `scripts/gen-tools-index.py` from `registerRevitTools()` (build `MCP-Server` first) — regenerate, never hand-edit; the generator did not exist until 2026-10-23, which is why the prose drifted. Its only curated input is the module → category map, and it refuses to render when a registered tool's module is unclassified or a listed module has no tools. `--check` exits 1 when the page differs from what the registry would produce. And because an unreadable registry is not a clean one, `7-14` SKIPs only when `MCP-Server/build/tools/index.js` is absent and FAILs when it exists but cannot be evaluated (same posture as `9-1`/`9-3`)
- claim-pattern liveness (Phase 7 check `7-13`): every pattern in `$claimSites` must still match at least one live site. A pattern that matches nothing reports PASS exactly like one that matches N correct sites, so reworded pages silently lose their guard. When a claim site legitimately disappears, mark that entry `Dormant = $true` — an explicit, reviewable decision rather than a silent zero
- hardcoded user account name scan (Phase 3 check `3-4`): scans every tracked text file (not just client config templates) for a literal account name in a `Users\`/`Users/` path; acceptable placeholder forms are enumerated in `$allowedUsers` (e.g. `<YOUR_USERNAME>`, `<YOUR_PROJECT_PATH>`, `<CONTRIBUTOR_USERNAME>`, `<OTHER_MACHINE_USER>`); any exclusion in `$pathScanSkip` must be listed by exact file path, never by prefix — a self-check FAILs if the hit count drifts from the list length; if `git ls-files` returns an empty scan universe (not a git repo, or run outside one), the check SKIPs rather than reporting a false PASS on zero files scanned
- registered-tool ↔ dispatcher reconciliation (Phase 7 check `7-15`): every tool `registerRevitTools()` declares must resolve to a `case "..."` label somewhere in `MCP/Core/CommandExecutor.cs` or a globbed `MCP/Core/Commands/*.cs` (never a hand-typed file list), after the rename map in `MCP-Server/src/tools/revit-tools.ts` is applied and comments are stripped from the C# source. Forward direction only — case labels also serve internal non-tool switches (filter operators, etc.), so a reverse check would be pure noise. An orphan (declared but unimplemented) is a FAIL; `$knownUnimplemented` is a named, reasoned quarantine list that still WARNs on every run rather than silently filtering the tool out, and FAILs if a quarantined name gains a dispatcher case without being removed from the list (drift self-check). This exists because "TS registers a tool, C# has no case for it" reaches callers as `NotImplementedException` while staying invisible to QA/QC — it has recurred more than once
- kanban-card index parity (Phase 7 check `7-16`): `docs/BIM_MCP/reference/kanban-index.html` is generated by `scripts/gen-kanban-index.py` from `tools/kanban/cards/*.json` — regenerate, never hand-edit. The check requires exactly one row per card in both directions, every GitHub blob link resolving on disk, the "cards are historical records, not current method" statement, and a superseded marker on every card listed in the generator's `SUPERSEDED` dict (parsed from the generator, never restated in the gate)
- snapshot banner (`data-snapshot="YYYY-MM-DD"`) on date-prefixed `docs/MMDD-*.html`
- event-page snapshot banner (Phase 7 check `7-8`, two extra lines): every `docs/BIM_MCP/YYYY-MM/*.html` (folder month `01`–`12`) must carry the canonical banner `<div data-snapshot="YYYY-MM-DD" …>…活動快照 snapshot…` immediately after its single `<body>`, with the banner month equal to the folder month; meta-refresh redirect stubs with no real content are exempt from the banner but stay count-scanned. A page that passes this guard is dropped from the `7-1`/`7-2`/`7-3` claim scan (its numbers are frozen at the event date); the token `data-snapshot` anywhere else in a living `docs/BIM_MCP` page is a FAIL ("banner outside snapshot locations"), so a living page can never buy itself out of the count scan. All matching is linear-time (comments/script/style/template/textarea stripped first, `[^<>]` classes, `\G`-anchored banner match) — a 2 MB hostile page adds well under a second. Known accepted limits are documented in the script comments
- MCP Registry publish consistency (`server.json` ↔ `MCP-Server/package.json` ↔ schema; 3-place version parity) — Phase 7 check `7-11`, see below
- meetup signup freshness (Phase 7 check `7-17`): `docs/BIM_MCP/shared.js` holds one `var MEETUP_SIGNUP = { url: "…", eventDate: "YYYY-MM-DD" }` line (plain quoted strings) plus a `window.__meetupSignupEffective()` getter. The check runs shared.js under node (stdin-fed, `vm` with 1000/500 ms timeouts, process killed after 5 s, fail closed if node is missing or anything throws), judges the *effective* config, and FAILs when a non-empty url is not `https://`, the date is missing/invalid, or `eventDate` is before today (Asia/Taipei) — so a stale signup link turns QA red the day after the meetup until the maintainer sets the next link+date or clears `url`. It also FAILs on a second definition, a string decoy, a literal that does not match the effective value, and any static `href` on a `[data-meetup-link]` element in a living `docs/BIM_MCP` page (linear quote-aware scan, 512 KB / 20000-tag guards). `-QaToday YYYY-MM-DD` overrides "today" for negative tests. The runtime disables the buttons by the same date rule, so visitors never get a stale open link; vm-detection/stateful getters/deferred mutations are deliberately hostile and out of scope
- skills-index generation parity (Phase 7 check `7-18`): `docs/BIM_MCP/reference/skills-index.html` must equal what `scripts/gen-skills-index.py` renders from `.claude/skills/*/SKILL.md` + `scripts/skills-index-map.json` (`--check`, byte-for-byte). The generator refuses to render when a skill directory has no map entry or a map entry has no `SKILL.md`, when a Domain link has no matching id in `domain-index.html`, or when a group is empty. SKIP only when no Python is found (reuses 7-11's `$pyCmd`); FAIL when the generator or map is missing. It is the live skills-page guard on macOS, where `7-10` cannot read the page (backslash path) and its reverse regex cannot see the `GM_*` cards
- MCP 2026 compliance (Phase 9): 9-1 every tool declares a non-empty `title` and boolean `readOnlyHint`, with `destructiveHint=true` confined to the allow-list (`delete_element`, `dedup_detail_elements_in_view`, `curate_mep_sizes`, `import_excel_to_drafting_views`); 9-2 every MCP Apps `ui://` resource resolves with the correct MIME (`text/html;profile=mcp-app`) and is self-contained (no external `src`/`href`/`url()` references); 9-3 **readOnlyHint honesty** — a tool declaring `readOnlyHint: true` must not have a dispatcher handler that commits a transaction or deletes elements. This exists because `annotations.ts` derives the hint from the tool-name *prefix* and never reads the C#, while 9-1 only checks that the hint is a *boolean* — so declaring `readOnlyHint: true` was the easiest way to pass every gate in the file, and seven tools did exactly that (issue #137). The check cannot be satisfied by a declaration, because the declaration is its **input**: `readOnlyHint: true` is what puts a tool into the scanned set, and the evidence is the C# the dispatcher reaches. It reuses 7-15's tool↔handler mapping rather than keeping a second copy. Exemptions are named, carry a written reason, WARN on every run, and FAIL the moment their machine-checked `Condition` stops holding (currently two rollback-only curtain-wall probes)
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
8. Date-prefixed `docs/MMDD-*.html` files and event pages `docs/BIM_MCP/YYYY-MM/*.html` are immutable event snapshots: they must carry a `data-snapshot="YYYY-MM-DD"` banner (event pages: the canonical banner right after `<body>`, month matching the folder), their numbers are never re-synced, and QA/QC count checks intentionally skip them. Never write the token `data-snapshot` on a living page.

## Final Pre-Response Checklist

Before answering with project-specific facts:

1. Did I read the latest relevant files in this turn?
2. If live Revit state is involved, did I call the relevant MCP tool in this turn?
3. If a domain method applies, did I read and follow the domain file?
4. If active view/level/selection matters, did I re-anchor in this turn?
5. If I changed docs or counts, did I run QA/QC or state why I could not?

## Abby wall-floor join preference
For parameter filling across multiple elements, always use a real batch tool with one apply call and one reversible transaction. Never loop over single-element write calls or request approval per element. For copying full type names to an instance text field, use modify_element_parameter with copyTypeNameBatch=true, explicit elementIds, the requested parameterName, and value="". Preview, apply once, and verify. If the loaded add-in lacks batch support, prepare the update and explain the restart requirement instead of reverting to individual writes.

When the user requests wall-floor joining, use join_wall_tops with floorCutsWall=true. Floors must cut walls. Default scope is the whole current project unless the user narrows it. Preview first; apply only with dryRun=false and apply=true, then verify. Never substitute legacy mixed-category joining. This preference does not change beam-wall rules.


The framing-region floor creation tool automatically joins each newly created floor to intersecting walls, enforcing floors cut walls. Report WallJoinFailed separately from floor creation failures. This automation is limited to floors created by that tool, not manual Revit floor creation.


For beam-wall joining, use join_wall_tops with beamCutsWall=true: beams cut walls. Preview, apply and verify final committed state. Do not enable floorCutsWall simultaneously. Keep this rule alongside floors-cut-walls; report unresolved pairs rather than claiming complete success.


Join priority is beams > floors > walls. For beam-floor inspection use join_wall_tops with auditBeamFloor=true, dryRun=true, apply=false (read-only). Do not infer cutting order from bounding boxes. This mode reports existing join order and intersecting unjoined pairs; it does not apply beam-floor joins.


To apply the authorized beam > floor rule, use join_wall_tops with beamCutsFloor=true,dryRun=false,apply=true after preview. auditBeamFloor remains read-only. Report Verification (post-commit) as authoritative; failed pairs stay reported.



For custom tool fixes, identify the user's active Revit version and compile/deploy/test only that version first. Validate on the actual model (preview, authorized apply, readback) before compiling other Revit versions. Distinguish compilation from live validation. Current active version confirmed 2024 on 2026-10-07.


For selected RC-wall structural enabling, use modify_element_parameter with rcWallsStructuralBatch=true and explicit captured elementIds, parameterName=結構,value=1. Preview first; one batch apply with dryRun=false,apply=true. Do not issue one approval per element. The backend rechecks Wall type names contain RC and uses WALL_STRUCTURAL_SIGNIFICANT. Preserve non-RC walls and report skipped/read-only/grouped elements.


For beam-tag centering, never read IndependentTag.TagText: native AccessViolationException was confirmed in Revit 2025 crash events on 2026-10-11. Resolve tag targets using GetTaggedElementIds and position via TagHeadPosition. Null tag text with TextReadStatus=SkippedNativeCrashRisk is unavailable, not empty. If a command timeout coincides with Revit exiting, inspect Windows Application crash events before retrying; do not assume a confirmation dialog.

## Local drawing lookup preference
For drawing-index lookup requests, read `vault/drawing-index.md` when present. It is user-authorized local-only reference memory and identifies the preferred source index. Verify requested entries against that source. Never copy its project-identifying contents into tracked files or logs.

## Floor number correction by thickness
- `modify_element_parameter.floorNumberRules` is an opt-in batch mode. Require current `viewId`, explicit visible floor `elementIds`, `parameterName=編號`, and empty `value`.
- Rules use actual instance thickness in mm, with 0.1 mm tolerance. Match only the unique Text-group 編號 using `Definition.GetGroupTypeId()`; never select a same-named parameter by order.
- Default dry run; require `apply=true` and `dryRun=false`. Use one transaction with post-commit verification and group rollback. Other thicknesses, missing/ambiguous parameters, read-only and grouped instances are skipped.

## Multi-version update deployment preference (2026-10-14)
For source synchronization and release updates, the user requires Revit 2024, 2025 and 2026 to be built and deployed together. This supersedes active-version-only deployment for those updates. Fix compatibility issues before deploying; verify each version's full dependency set and manifest. Live model validation remains distinct from successful builds/deployment.

## Door tag clearance
- Use move_element with avoidDoorTagOverlap=true, current plan viewId, explicit DoorTags elementIds, dryRun/apply. Uses view-specific projected door and tag bounding boxes, preserves orientation/depth, and never reads TagText.
- Only tags overlapping measured door boxes are moved; candidates must clear all measured doors and other independent tags. Pinned/orphaned/leader/grouped or ambiguous/local-host failures skip. Missing boxes and unresolved positions are reported, not claimed clear. Single transaction with regenerated position/overlap verification and rollback group.
