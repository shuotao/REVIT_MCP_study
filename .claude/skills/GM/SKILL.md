---
name: GM
description: Open the project kanban board (kanban.html). Windows-tooling based (PowerShell Start-Process).
user-invocable: true
---

Open the project kanban board.

## Usage

- **`monstrare`** or **no args** → Open the project kanban board (`kanban.html`)

## `/GM monstrare` — Project Kanban Board

1. Locate `kanban.html` at the repo root (search upward from cwd if needed; fall back to `tools/kanban/index.html` if the root copy is missing — both embed the same `cardsData`, regenerated directly from `tools/kanban/cards/*.json` whenever a card changes; there is no separate sync script).
2. Open it directly in the default browser:
   ```powershell
   Start-Process "<repo-root>\kanban.html"
   ```
3. Report: `✅ Opened project kanban board (kanban.html)`.

No server is needed — `kanban.html` embeds its card data (`cardsData`) directly in the page, so it works from a plain `file://` open. If not on Windows, tell the user to open `kanban.html` directly in a browser instead of using `Start-Process`.

## Error Handling

| Error | Response |
|-------|----------|
| `kanban.html` not found at repo root | Fall back to `tools/kanban/index.html` and mention both mirror `tools/kanban/cards/*.json` |
| Browser doesn't open automatically | Give the user the direct path to open manually |

## Relationship to Other Files

- For the green-material search & Set Manager page, use `/GMweb open` instead — that used to be duplicated here as a `search` action, but the two were redundant (same target page, same steps), so this skill was narrowed to kanban-only and `/GMweb` is now the single entry point for the search page.
