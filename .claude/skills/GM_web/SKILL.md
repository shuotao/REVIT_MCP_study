---
name: GM_web
description: Open the green-material search & Set Manager web page directly via a single "open" argument. Windows only. Trigger keywords: 開啟檢索平台網頁、開啟綠建材檢索平台、GM_web open、open search platform.
user-invocable: true
---

Open the green-material search & Set Manager web page (`assets/green-material-showcase.html`, served via `local_server.py` at `http://localhost:8888`).

This is the single entry point for the green-material search & Set Manager page. (`/GM_kanban` used to have a duplicate `search` action pointing at the same page — that branch was removed since it was fully redundant with this skill; `/GM_kanban` now only opens the project kanban board.)

## Usage

- **`open`** → Open the green-material search & Set Manager page
- **No args** → Treat as `open` (there is only one action)

## Platform Check

This skill launches a browser and a local Python server. It is Windows-only. If not Windows, tell the user to run `python local_server.py` from the repo root, then open `http://localhost:8888` manually. Then stop.

## `/GM_web open`

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
5. Report: `✅ Opened green-material search & Set Manager at http://localhost:8888`. Mention that the Set Manager's "匯出至 Agent" / "另存專案檔" buttons need this server running (or, for 另存/開啟專案檔 specifically, a File System Access API-capable browser) — a plain `file://` open of the HTML would load the page but the server-dependent buttons would fail.

## Why `local_server.py` and not a plain `file://` open

The green-material showcase page's Set Manager calls `POST /api/save-sets` / `GET /api/get-sets` against `http://localhost:8888`, which only `local_server.py` provides. Opening `assets/green-material-showcase.html` directly via `file://` loads the page, but Set save/sync buttons silently fail with no server to talk to.

## Error Handling

| Error | Response |
|-------|----------|
| Port 8888 already used by something other than `local_server.py` | Warn the user and ask whether to stop the other process, or open `http://localhost:8888` anyway if it looks like the right server |
| `python` / `python3` not found | Tell the user to install Python 3, or run `local_server.py` manually |
| Browser doesn't open automatically | Give the user the direct URL to open manually: `http://localhost:8888` |
| Page loads a 404 / "找不到 assets/green-material-showcase.html" (fresh clone, no local data yet) | `assets/green-material-showcase.html` is local-only and doesn't exist until it's been built once. Tell the user to run `/GM_update` first (it bootstraps both `tabc_master_database.json` and the showcase page from scratch on a first run), then retry `/GM_web open`. |

## Reference

See `.claude/skills/GM_kanban/SKILL.md` for the project kanban board opener (a separate, unrelated page).
