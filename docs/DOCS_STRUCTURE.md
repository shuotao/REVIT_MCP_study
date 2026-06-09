# Docs Structure

This file explains the purpose of the documentation folders. For the AI/human/shared audience classification, see [DOCUMENT_AUDIENCE_INVENTORY.md](./DOCUMENT_AUDIENCE_INVENTORY.md).

## Top-Level Documentation Map

| Path | Audience | Purpose |
|---|---|---|
| `README.md` | Human, Traditional Chinese | Primary installation and onboarding entry |
| `README.en.md` | Human, English | English installation and onboarding entry |
| `CLAUDE.md` | AI-only | Canonical AI constitution and project map |
| `AGENTS.md` | AI-only redirect | Redirects to `CLAUDE.md` |
| `GEMINI.md` | AI-only redirect | Redirects to `CLAUDE.md` |
| `domain/` | Shared | BIM SOPs, regulatory workflows, computation methods |
| `.claude/commands/` | AI-only | Slash-command behavior |
| `.claude/skills/` | AI-only | AI workflow orchestration |
| `docs/BIM_MCP/` | Human-facing | Public knowledge site source |
| `docs/_archive/` | Historical | Old notes, handoffs, reviews, and snapshots |
| `scripts/` | Human + maintainer | Setup, deployment, QA/QC scripts |
| `log/` | Shared history | Append-only event and session log |

## `docs/BIM_MCP/`

Public knowledge-site source for architecture explanations, deployment guidance, contributor onboarding, Domain and Skill indexes, and visual teaching assets.

Default handling:

- Keep it human-readable.
- Keep public links working.
- Avoid using old site snapshots as current source of truth.
- Source-of-truth rules still live in `CLAUDE.md`, `domain/*.md`, and scripts.

## `docs/_archive/`

Historical material such as old handoffs, design notes, PR review notes, and post-mortems.

Default handling:

- Preserve content unless the user explicitly asks for migration.
- Do not treat archive content as current project rules.
- QA/QC should normally exclude archive content from stale-count failures.

## `domain/`

Shared BIM method layer. This is authoritative for workflows, calculations, legal/regulatory review logic, and AI method compliance.

Important:

- Domain files must remain readable by both humans and AI.
- Do not convert Domain files to English-only.
- Bilingual headings and English keywords are acceptable when they improve AI precision.
- Every active Domain file should follow `domain/frontmatter-standard.md`.

## `.claude/`

AI operating layer.

| Path | Purpose |
|---|---|
| `.claude/commands/` | Slash-command instructions such as `/qaqc`, `/domain`, `/lessons`, `/review` |
| `.claude/skills/` | Reusable AI workflows such as `/build-revit`, `/deploy-addon`, `/fire-safety-check` |
| `.claude/hooks/` | Claude Code hooks used for reminders or automation |

AI-only docs should be migrated toward English to avoid encoding drift.

## `log/`

Append-only monthly log. It complements `git log` by recording AI decisions, lessons, manual reviews, and session summaries.

Default handling:

- Read the latest entry at session start.
- Append new entries for meaningful AI-driven documentation or rule changes.
- Do not rewrite historical entries unless explicitly requested.

## Where To Put New Content

| If you are adding... | Put it in... |
|---|---|
| Installation instructions | `README.md`, `README.en.md`, or `scripts/README.md` |
| AI behavior rules | `CLAUDE.md` or `.claude/commands/` |
| Reusable AI workflow | `.claude/skills/{skill}/SKILL.md` |
| BIM calculation method or regulation workflow | `domain/*.md` |
| Public teaching page | `docs/BIM_MCP/` |
| One-off implementation notes | `docs/_archive/YYYY-qN/` |
| Setup/deployment automation | `scripts/` |
| Significant AI session event | `log/YYYY-MM.md` |

## Current QA/QC Entry Point

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```
