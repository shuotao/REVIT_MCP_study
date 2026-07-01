---
name: qa-checklist
description: "Revit MCP QA/QC checklist for repository structure, documentation alignment, Domain frontmatter, and deployment safety. This Domain file is shared by humans and AI, so it intentionally keeps bilingual terminology."
metadata:
  version: "2.0"
  updated: "2026-06-01"
  created: "2026-01-04"
  contributors:
    - "Admin"
    - "shuotao"
  references:
    - "domain/frontmatter-standard.md"
    - "domain/path-maintenance-qa.md"
  related:
    - "domain/frontmatter-standard.md"
    - "domain/path-maintenance-qa.md"
    - "domain/session-context-guard.md"
    - "domain/tool-capability-boundary.md"
  referenced_by:
    - "qa-review"
    - "hj-pr-proposal"
  tags: [QA, QC, documentation, deployment, bilingual]
---

# QA/QC Checklist / 品質檢查清單

This Domain file defines the shared QA/QC method for Revit MCP. It is intentionally bilingual because Domain files are read by both humans and AI agents.

本檔是 Revit MCP 的共同品質檢查 SOP。Domain 不可改成全英文；必要時以英文關鍵詞搭配中文說明，避免 AI 與人類任一方失去語意。

## Canonical Script / 主要腳本

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```

Full validation on Windows:

```powershell
.\scripts\verify-qaqc.ps1 -Version 2024
```

## Phase 1 - File Structure Integrity / 檔案結構

Must pass:

- No version-specific `RevitMCP.2024.csproj` or `RevitMCP.2024.addin`.
- No nested `MCP/MCP/`.
- No deleted path-fix scripts such as `fix_addin_path.ps1`.
- Required source files exist:
  - `MCP/RevitMCP.csproj`
  - `MCP/RevitMCP.addin`
  - `MCP/Application.cs`
  - `MCP/Core/SocketService.cs`
  - `MCP/Core/ExternalEventManager.cs`
  - `MCP-Server/src/index.ts`
  - `MCP-Server/src/tools/index.ts`
  - `MCP-Server/package.json`

## Phase 2 - Cross-Reference Consistency / 路徑與文件引用

Scan active docs for stale references:

- `RevitMCP.2024.csproj`
- `RevitMCP.2024.addin`
- `bin\Release.2024`
- `bin\Release\RevitMCP.dll`
- `MCP\MCP\`
- `fix_addin_path`

Allowed exceptions:

- Historical archive docs.
- Changelog entries.
- Domain lessons that intentionally preserve historical mistakes.
- Explicit "do not create this file" rules.

## Phase 3 - Build Configuration / 建置設定

`MCP/RevitMCP.csproj` must:

- Use Nice3point Revit SDK.
- Keep `<DeployAddin>false</DeployAddin>`.
- Define `Release.R22`, `Release.R23`, `Release.R24`, `Release.R25`, `Release.R26`.

`MCP/RevitMCP.addin` must:

- Use a relative `<Assembly>` path.
- Avoid absolute drive paths.
- Use `RevitMCP.Application` as `FullClassName`.
- Avoid duplicate add-in entries.

## Phase 4 - Build Verification / 建置驗證

When build checks are enabled:

- Build selected Revit versions with `dotnet build -c Release.R{YY}`.
- Verify output under `MCP/bin/Release.R{YY}/RevitMCP.dll`.
- Run `npm run build` in `MCP-Server`.
- Verify `MCP-Server/build/index.js`.

## Phase 5 - Deployment Verification / 部署驗證

When deployment checks are enabled:

- Inspect `%APPDATA%\Autodesk\Revit\Addins\{version}`.
- Each Revit version should have at most one RevitMCP `.addin`.
- The `.addin` assembly path must resolve to an existing DLL.
- Duplicated manifests are a failure because Revit may load the add-in twice.

## Phase 6 - Domain Metadata / Domain frontmatter

Every active `domain/*.md` file except `domain/README.md` must have YAML frontmatter:

- `name`
- `description`
- `metadata.version`
- `metadata.updated`

Recommended:

- `metadata.related`
- `metadata.referenced_by`
- `metadata.tags`

Validation rule:

- Missing required frontmatter is `FAIL`.
- Broken `metadata.related` references are `FAIL`.
- Old `metadata.updated` values may be `WARN`, not automatic `FAIL`.

## Phase 7 - Cross-Document Alignment / 跨文件統計同步

Grand-total claims must match source of truth:

- Runtime MCP tools: count from `registerRevitTools()`.
- Domain SOP files: `domain/*.md` except `domain/README.md`, plus `domain/references/*.md`.
- Skills: `.claude/skills/*/SKILL.md`.

The check must compare:

- `CLAUDE.md`
- `README.md`
- `README.en.md`
- `docs/BIM_MCP/**`
- active demo docs that make grand-total claims

If a count changes, update all claim sites in the same change.

## Phase 8 - Document Audience and Encoding / 文件受眾與編碼

Documents must declare or fit one of these audiences:

| Audience | Examples | Language Policy |
|---|---|---|
| AI-only | `CLAUDE.md`, `.claude/commands/*.md`, `.claude/skills/*/SKILL.md` | Prefer English |
| Human-facing | `README.md`, `README.en.md`, `scripts/README.md`, teaching docs | Match target audience |
| Shared | `domain/*.md`, `log/README.md` | Bilingual or Chinese-friendly; never English-only for Domain |
| Historical | `docs/_archive/**`, old logs | Preserve unless explicitly migrated |

Mojibake risk patterns such as `嚗`, `銝`, `蝣`, `摰`, `撠`, `閬`, `�` should be treated as content-quality warnings or failures depending on file class:

- Canonical AI docs: `FAIL`.
- README docs: `FAIL`.
- Domain docs: `WARN` unless the edited file is in scope, because Domain migration must preserve Chinese meaning carefully.
- Historical archive and logs: ignore by default.

## Phase 9 - Live Revit Preconditions / Revit 即時狀態前置檢查

Before any live model operation:

1. Revit must be open.
2. MCP service must be enabled from the Revit ribbon.
3. Port `8964` or `REVIT_MCP_PORT` must match both sides.
4. The AI client must expose the Revit MCP tools.
5. Active view or selection dependent actions must re-anchor with `get_active_view` or equivalent.

## Failure Report Template

```text
QA/QC failed.
- Phase:
- File:
- Problem:
- Expected:
- Fix:
```

## Maintenance Rule / 維護規則

When this checklist changes, update:

- `.claude/commands/qaqc.md`
- `scripts/verify-qaqc.ps1`
- `CLAUDE.md` QA/QC section
- `docs/DOCUMENT_AUDIENCE_INVENTORY.md` if document classes changed
