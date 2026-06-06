# Document Audience Inventory

This inventory defines which project documents are for AI agents, human readers, or both. It is the reference for future documentation cleanup and mojibake prevention.

## Policy

| Audience | Rule |
|---|---|
| AI-only | Write in English. Keep instructions explicit, operational, and encoding-safe. |
| Human-facing | Use the reader's language. `README.md` is Traditional Chinese; `README.en.md` is English. |
| Shared | Keep readable for both humans and AI. Domain files must not become English-only. |
| Historical | Preserve unless the user explicitly asks for migration. Do not let archive content drive current rules. |

## Current Counts

| Item | Count | Source |
|---|---:|---|
| Runtime MCP tools | 92 | `registerRevitTools()` |
| Domain SOP files | 43 | `domain/*.md` except README, plus `domain/references/*.md` |
| Claude skills | 20 | `.claude/skills/*/SKILL.md` |

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
| `README.md` | Traditional Chinese onboarding | Installation, architecture, common workflows |
| `README.en.md` | English onboarding | English counterpart of README |
| `CONTRIBUTING.md` | contributors | Contribution process |
| `CHANGELOG.md` | users and maintainers | Release history |
| `scripts/README.md` | installers and maintainers | Script usage |
| `pyRevit_Tools/README.md` | pyRevit users | pyRevit-specific notes |
| `docs/BIM_MCP/**` | public knowledge site | Teaching and visual explanations |
| `docs/troubleshoot-first-install.md` | users | First-install troubleshooting |
| `docs/slides.md` | presenters | Slide index |

## Historical or Archive Documents

| Path | Rule |
|---|---|
| `docs/_archive/**` | Preserve by default |
| old event logs | Preserve by default |
| bundled external references | Preserve source snapshot |

## Future Writing Rules

1. New AI instructions must be English and must identify whether they are canonical, redirect, command, or skill.
2. New Domain files must keep Chinese readability and may add English labels for AI precision.
3. Do not copy large procedural content from README into `CLAUDE.md`; link or summarize instead.
4. Do not put model-state claims in docs unless they are generic examples.
5. Any document that states global counts must identify or follow the source of truth.
6. Any new setup path must use `Release.R{YY}` output paths.
7. Avoid box-drawing characters and emoji in canonical AI docs; they are unnecessary encoding risk.
