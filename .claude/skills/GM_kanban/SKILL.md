---
name: GM_kanban
description: Open the project kanban board (tools/green-material/GM_kanban.html). Windows-tooling based (PowerShell Start-Process).
user-invocable: true
---

Open the project kanban board.

## Usage

- **`monstrare`** or **no args** → Open the project kanban board (`tools/green-material/GM_kanban.html`)

## `/GM_kanban monstrare` — Project Kanban Board

1. Locate `tools/green-material/GM_kanban.html` relative to the repo root (search upward from cwd if needed to find the repo root; fall back to `tools/kanban/index.html` if that copy is missing — both embed the same `cardsData`, regenerated directly from `tools/kanban/cards/*.json` whenever a card changes; there is no separate sync script).
2. Open it directly in the default browser:
   ```powershell
   Start-Process "<repo-root>\tools\green-material\GM_kanban.html"
   ```
3. Report: `✅ Opened project kanban board (tools/green-material/GM_kanban.html)`.

No server is needed — `GM_kanban.html` embeds its card data (`cardsData`) directly in the page, so it works from a plain `file://` open. If not on Windows, tell the user to open `tools/green-material/GM_kanban.html` directly in a browser instead of using `Start-Process`.

## Error Handling

| Error | Response |
|-------|----------|
| `tools/green-material/GM_kanban.html` not found | Fall back to `tools/kanban/index.html` and mention both mirror `tools/kanban/cards/*.json` |
| Browser doesn't open automatically | Give the user the direct path to open manually |

## Relationship to Other Files

- For the green-material search & Set Manager page, use `/GM_web open` instead — that used to be duplicated here as a `search` action, but the two were redundant (same target page, same steps), so this skill was narrowed to kanban-only and `/GM_web` is now the single entry point for the search page.
- `tools/kanban/index.html`'s in-page "🔗 連結專案資料夾" (File System Access API) write-back feature looks up this file by its new path (`tools/green-material/GM_kanban.html`) inside the connected repo-root folder handle — see `getGmKanbanFileHandle()` in that file. If this skill's file ever moves again, that lookup needs updating too.
