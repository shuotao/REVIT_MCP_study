---
name: GM
description: Open the project kanban board (monstrare) or the green-material search / Set Manager web page (search). Windows only.
user-invocable: true
---

Open one of two local project web pages depending on the argument.

## Usage

- **`monstrare`** → Open the project kanban board (`kanban.html`)
- **`search`** → Open the green-material search & Set Manager page (`assets/green-material-showcase.html`, served via `local_server.py` at `http://localhost:8888`)
- **No args** → Ask the user which of the two they want (list both with a one-line description each)

## Platform Check

This skill launches a browser and, for `search`, a local Python server. It is Windows-only. If not Windows, tell the user to open the file(s) manually:
- Kanban: open `kanban.html` directly in a browser.
- Search: run `python local_server.py` from the repo root, then open `http://localhost:8888`.

Then stop.

## `/GM monstrare` — Project Kanban Board

1. Locate `kanban.html` at the repo root (search upward from cwd if needed; fall back to `tools/kanban/index.html` if the root copy is missing — both embed the same `cardsData`, regenerated directly from `tools/kanban/cards/*.json` whenever a card changes; there is no separate sync script).
2. Open it directly in the default browser:
   ```powershell
   Start-Process "<repo-root>\kanban.html"
   ```
3. Report: `✅ Opened project kanban board (kanban.html)`.

No server is needed — `kanban.html` embeds its card data (`cardsData`) directly in the page, so it works from a plain `file://` open.

## `/GM search` — Green Material Search & Set Manager

1. Check whether something is already listening on port 8888:
   ```powershell
   Get-NetTCPConnection -LocalPort 8888 -ErrorAction SilentlyContinue
   ```
2. **If already listening** → just open the browser directly (do not start a second server):
   ```powershell
   Start-Process "http://localhost:8888"
   ```
3. **If not listening** → start `local_server.py` in the background from the repo root:
   ```bash
   python local_server.py
   ```
   Run this as a background task (it's a `serve_forever()` loop, it never returns). The script opens the browser itself on startup (`webbrowser.open`), so no separate `Start-Process` call is needed once it's running.
4. After starting, poll `Get-NetTCPConnection -LocalPort 8888` briefly to confirm the server actually came up before reporting success.
5. Report: `✅ Opened green-material search & Set Manager at http://localhost:8888`. Mention that the Set Manager's "匯出至 Agent" button needs this server running to save `exported_material_sets.json` — a plain `file://` open of the HTML would load the page but that button would fail.

## Why `local_server.py` and not a plain `file://` open for `search`

The green-material showcase page's Set Manager calls `POST /api/save-sets` / `GET /api/get-sets` against `http://localhost:8888`, which only `local_server.py` provides. Opening `assets/green-material-showcase.html` directly via `file://` loads the page, but Set save/sync buttons silently fail with no server to talk to.

## Error Handling

| Error | Response |
|-------|----------|
| `kanban.html` not found at repo root | Fall back to `tools/kanban/index.html` and mention both mirror `tools/kanban/cards/*.json` |
| Port 8888 already used by something other than `local_server.py` | Warn the user and ask whether to stop the other process, or open `http://localhost:8888` anyway if it looks like the right server |
| `python` / `python3` not found | Tell the user to install Python 3, or run `local_server.py` manually |
| Browser doesn't open automatically | Give the user the direct URL/path to open manually |
