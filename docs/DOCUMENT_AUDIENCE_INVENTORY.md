# Document Audience Inventory

This inventory defines which project documents are for AI agents, human readers, or both. It is the reference for future documentation cleanup and mojibake prevention.

## Policy

| Audience | Rule |
|---|---|
| AI-only | Write in English. Keep instructions explicit, operational, and encoding-safe. |
| Human-facing | Use the reader's language. `README.md` is English; `README.zh-TW.md` is Traditional Chinese. |
| Shared | Keep readable for both humans and AI. Domain files must not become English-only. |
| Historical | Preserve unless the user explicitly asks for migration. Do not let archive content drive current rules. |

## Current Counts

| Item | Count | Source |
|---|---:|---|
| Runtime MCP tools | 253 | `registerRevitTools()` |
| Domain SOP files | 94 | `domain/*.md` except README, plus `domain/references/*.md` |
| Claude skills | 61 | `.claude/skills/*/SKILL.md` |

## AI-Only Documents

These should be English-first.

| Path | Status | Notes |
|---|---|---|
| `CLAUDE.md` | canonical | Main AI constitution and project map |
| `AGENTS.md` | redirect | Must contain only `CLAUDE.md` |
| `GEMINI.md` | redirect | Must contain only `CLAUDE.md` |
| `.claude/commands/*.md` | command docs | Slash-command behavior |
| `.claude/skills/*/SKILL.md` | skill docs | AI orchestration; migrate gradually to English while preserving exact local BIM terms |
| `.github/copilot-instructions.md` | AI rules if present | Must align with `CLAUDE.md` |
| `.mcp.json` | machine config | Project-level MCP server config |
| `.vscode/mcp.json` | machine config | VS Code MCP server config |
| `templates/personal-vault/VAULT-CLAUDE.md` | local-only template | Personal vault schema; Fixed Core copied verbatim by users' agents, never loaded by this repo's agents |
| `docs/mcp2026-upgrade/impl-log.md` | implementation audit log | English, append-only `[step ...]` entries recording the MCP 2026-07-28 upgrade's build/verify results; audit trail for AI agents and maintainers, not onboarding content |
| `docs/mcp2026-upgrade/doc-alignment-log.md` | implementation audit log | English, append-only log of the documentation-alignment pass that synced `CLAUDE.md` / README / audience inventory to the MCP 2026-07-28 upgrade |

## Shared Human + AI Documents

These must remain understandable by both sides.

| Path | Status | Language Policy |
|---|---|---|
| `domain/*.md` | authoritative SOP | Bilingual or Chinese-friendly; never English-only |
| `domain/references/*.md` | regulatory reference | Keep source-language legal terms where needed |
| `domain/README.md` | domain catalog | Bilingual preferred |
| `log/README.md` | logging policy | Bilingual acceptable |
| `log/YYYY-MM.md` | append-only history | Preserve existing entries; new entries should be UTF-8 readable |

## Human-Facing Documents

| Path | Audience | Notes |
|---|---|---|
| `README.md` | English onboarding | Installation, architecture, common workflows |
| `README.zh-TW.md` | Traditional Chinese onboarding | Traditional Chinese counterpart of README |
| `CONTRIBUTING.md` | contributors | Contribution process |
| `CHANGELOG.md` | users and maintainers | Release history |
| `scripts/README.md` | installers and maintainers | Script usage |
| `scripts/skills-index-map.json` | maintainers (shared) | Curated Traditional-Chinese input for the generated skills index (groups, descriptions, trigger tags, Domain links); no counts — counts are derived by `scripts/gen-skills-index.py` |
| `pyRevit_Tools/README.md` | pyRevit users | pyRevit-specific notes |
| `docs/BIM_MCP/**` | public knowledge site | Teaching and visual explanations |
| `docs/troubleshoot-first-install.md` | users | First-install troubleshooting |
| `docs/slides.md` | presenters | Slide index |
| `Antigravity_MCP_Complete_Guide.md` | Antigravity users | Manual MCP setup for Antigravity client |
| `docs/UPDATE-PULL-GUIDE.md`, `docs/branch-index.md` | users and maintainers | Update flow and branch overview |
| `docs/MIGRATION_GUIDE.md` | users and fork contributors | Traditional Chinese; append-only, newest-first record of upgrades that affect existing users or fork contributors (e.g. the MCP 2026-07-28 dual-era upgrade) |
| `docs/integrations/*.md` | users, fork contributors, and the `archicad-skill-adapter` Skill | Traditional Chinese; opt-in Archicad MCP backend docs (setup, Revit/Archicad Skill portability matrix, Wave 2 translation proposal) salvaged from issue #98. Not yet Skill-level live-tested; carry explicit status disclaimers where claims are unverified. |
| `docs/mep-design-playbook-ch1.zh-TW.md` | junior MEP engineers, and architects who coordinate with MEP | Traditional Chinese; teaching companion to `domain/mep-space-demand-matrix.md`. Chapter 1 covers where pre-design quantities come from (the five kinds), the MEP-architecture loop, and the three gates. Derived from one exercise on an Autodesk course model, not a client project; carries its own honesty-boundary section. The domain file wins on method. |
| `docs/mep-design-playbook-ch2.zh-TW.md` | junior MEP engineers continuing from Chapter 1 | Traditional Chinese; Chapter 2 turns the Chapter 1 numbers into something the model can check itself. Covers topology-before-values, why ventilation is modelled before HVAC, which field a value belongs in (supply vs exhaust, decided by the code's ventilation-method clause), two write-time traps (internal units, schedule-in-foreground), a step-by-step air-terminal placement walkthrough, and the supply/return/exhaust connector trap. |
| `docs/mep-design-playbook-ch3.zh-TW.md` | junior MEP engineers continuing from Chapter 2 | Traditional Chinese; teaching companion to `domain/mep-space-demand-matrix.md` 4-1/4-2. Chapter 3 covers grouping air terminals and equipment into systems (traceable-vs-decided grouping basis, equipment identity by geometry vs by decision, type-vs-instance system labels), and the checks that only unlock once ductwork is drawn (system flow validity, the supply/exhaust-vs-endpoint-sum leak check, and three same-shape boolean-flag traps). Derived from the same course-model exercise as Chapters 1-2, not a client project; carries its own honesty-boundary section flagging which post-routing figures postdate the log. |
| `docs/mep-design-playbook-ch1-model-guide.zh-TW.md` | learners opening the Chapter 1 closing model | Traditional Chinese; orientation for the Revit model that Chapter 1 ends at — what each schedule column teaches, the four opening-verification numbers, what the model has NOT done yet, and common misreadings. Paired with `mep-design-playbook-ch1.zh-TW.md`. |
| `docs/camera-manager/spec.zh-TW.md`, `adr.zh-TW.md`, `changelog.zh-TW.md`, `roadmap.zh-TW.md` | Camera Manager users, the maintainer, and AI agents working on the pane | Traditional Chinese; the Revit camera-manager dockable pane and Key Plan feature. `spec` = current behaviour only; `adr` = decision records D-001+ (context → decision → rationale → consequences, append-only); `changelog` = dated changes, newest first; `roadmap` = phase status and the list of behaviours not yet verified in Revit. |
| `docs/camera-manager-spec.zh-TW.md` | redirect | Former single-file spec, kept only so existing links do not rot; points to `docs/camera-manager/`. Do not add content. |
| `docs/claude-code-cheatsheet.html`, `docs/karpathy-gist-zh-tw.html` | readers | Reference pages |
| other `docs/*.md` topic guides | mixed | Per-topic guides (agent handoff, guard rails, architecture, slope analysis, docs structure); classify individually if promoted to canonical |
| root-level Chinese notes (`HJPLUS *.md`, `[好學生筆記]*.md`) | community readers | Event notes and adoption guides |

## Historical or Archive Documents

| Path | Rule |
|---|---|
| `docs/_archive/**` | Preserve by default |
| old event logs | Preserve by default |
| bundled external references | Preserve source snapshot |
| date-prefixed `docs/MMDD-*.html` (e.g. `0425-presentation.html`, `0523-monthly.html`) | Immutable event snapshots. Must carry a `data-snapshot="YYYY-MM-DD"` banner. Their counts reflect the event date and are intentionally excluded from count sync (QAQC Phase 7). |
| event pages `docs/BIM_MCP/YYYY-MM/*.html` (e.g. `2026-07/harvest-waves.html`, `2026-10/fork-harvest.html`) | Immutable event snapshots. Must carry the canonical `data-snapshot="YYYY-MM-DD"` banner right after `<body>`, month matching the folder (QAQC 7-8). Excluded from count sync; meta-refresh redirect stubs are banner-exempt but still count-scanned. |
| `docs/0523-dry-run-retrospective.md` | Event retrospective; preserve as written |

## Future Writing Rules

1. New AI instructions must be English and must identify whether they are canonical, redirect, command, or skill.
2. New Domain files must keep Chinese readability and may add English labels for AI precision.
3. Do not copy large procedural content from README into `CLAUDE.md`; link or summarize instead.
4. Do not put model-state claims in docs unless they are generic examples.
5. Any document that states global counts must identify or follow the source of truth.
6. Any new setup path must use `Release.R{YY}` output paths.
7. Avoid box-drawing characters and emoji in canonical AI docs; they are unnecessary encoding risk.

## Abby Local Development Documents

| Path | Audience | Notes |
|---|---|---|
| ABBY-DEVELOPMENT.md | Human-facing | Traditional Chinese; independent repository and runtime boundaries |
| tests/ParameterCopy.Tests/README.md | Human-facing | Command test scope and limitations |

