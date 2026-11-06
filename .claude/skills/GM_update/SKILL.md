---
name: GM_update
description: Refresh tabc_master_database.json from the live TABC green-material website (https://tabcmgr.hopto.org) and re-sync the showcase page's offline cache. Also the first-time bootstrap entry point after a fresh clone — creates both files from scratch if they don't exist yet. Trigger keywords: 更新綠建材資料、更新原始網頁資料、GM_update、refresh TABC database、更新 TABC 資料庫、第一次抓取、初始化綠建材資料庫。
user-invocable: true
---

Refresh the local green-material master database (`tabc_master_database.json`) from the live TABC official site, then rebuild `assets/green-material-showcase.html` from `assets/green-material-showcase.template.html` (the git-tracked UI source) with the latest data spliced in. This is the only supported entry point for pulling fresh data from the source website. The original development branch (PR #116) also had several one-off scraper scripts under `archive/scripts/catalog/` — that whole `archive/` directory was excluded from this repo (third-party TABC data, not redistributable under this MIT repo; see `tools/green-material/README.md`'s "`archive/` 目錄未隨本 repo 收編" section), so those scripts do not exist here. Do not try to use them even if referenced elsewhere; `GM_update_tabc_database.py` is the only supported path.

**This is also the first-time setup command.** `tabc_master_database.json` and `assets/green-material-showcase.html` are both local-only (gitignored — see `tools/green-material/README.md`), so a fresh `git clone` has neither. Running this skill with neither file present is not an error case: `update_tabc_database()` treats a missing `tabc_master_database.json` as an empty database and does a full import, and it always rebuilds `assets/green-material-showcase.html` from the tracked template regardless of whether it existed before. The result dict's `"bootstrap"` field is `true` when this was a first-time run (existing database was empty/missing) — mention this to the user explicitly ("this was your first run, N records imported from scratch") rather than reporting it identically to a routine incremental update.

## What This Updates and What It Doesn't

- **Refreshed from the live site (real data)**: `licno`, `title`, `company`, `period` (含 (續)/(增)/(變) 後綴), `category`, `subCategory`, `img`.
- **Re-derived from a keyword-rule template, not the live site's detail page** (this matches the existing database's own precedent — see `_enrich_record()` in `GM_update_tabc_database.py`): `cnsSpec`, `testItems`, `qualifiedItems`, `productSpecFull`, `specList`, `specs`, `keywords`. These are plausible-looking placeholder values, not per-product certified test data scraped from TABC's detail page. **Always disclose this limitation to the user when reporting results** — do not imply these fields are authoritative lab data.
- **Never auto-deleted**: licnos present in the old database but not seen in this crawl are kept as-is and only listed as "本次未再出現" in the report — a partial network failure must never be allowed to silently wipe real records.

## Steps

1. **Dry run first** (no files are written):
   ```bash
   python tools/green-material/GM_update_tabc_database.py --dry-run
   ```
   This hits the live TABC site (`https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx`), pages through all 4 categories (健康/高性能/再生/生態), and prints a JSON diff report: `added` (new licnos), `updated` (licno + which fields changed), `notSeen` (licnos not seen this crawl — not deleted), `totalBefore`/`totalAfter`.
   - This can take 1–3 minutes (roughly 60–100 HTTP requests to an external site with a small delay between each). Tell the user it's running.
   - If the process raises `RuntimeError` ("本次未從 TABC 官網抓取到任何資料...") — the site is unreachable or its HTML structure changed. Stop, report this to the user, do not retry blindly.

2. **Report the dry-run diff to the user** before writing anything:
   - Counts: `len(added)`, `len(updated)`, `len(notSeen)`.
   - A few sample licnos from each bucket (not all — these lists can be large).
   - If `added == [] and updated == [] and notSeen == []`, tell the user the local database is already up to date and **stop here** — no need to run the real update.

3. **Ask the user to confirm** before running the real update (this overwrites `tabc_master_database.json` and fully regenerates `assets/green-material-showcase.html` — both are local-only files, not git-tracked, so this only affects this machine):
   ```bash
   python tools/green-material/GM_update_tabc_database.py
   ```
   - This performs the same crawl again (the site has no bulk-export API, so a second live fetch is unavoidable — do not try to reuse the dry-run's in-memory result across a separate process invocation), merges into `tabc_master_database.json` (atomic write via temp file + `os.replace`), and rebuilds `assets/green-material-showcase.html` from `assets/green-material-showcase.template.html` with the merged data spliced into the `const tabcDatabase = [...]` marker.
   - Report `showcaseSynced` from the result — if `false`, the template's markers weren't found (it may be corrupted) and the JSON file was still updated correctly; tell the user the showcase page needs a manual look at `assets/green-material-showcase.template.html`.

## Refreshing the UI Without a Live Fetch

If the user just did `git pull` and `assets/green-material-showcase.template.html` changed (a UI/feature update from another machine or contributor), they don't need a full live TABC crawl to see it — that only refreshes data, and the template is the UI source now. Run:
```bash
python tools/green-material/GM_update_tabc_database.py --resync-html
```
This does no network I/O: it reads the existing local `tabc_master_database.json` (or treats it as empty if missing) and rebuilds `assets/green-material-showcase.html` from the current template. Use this whenever the goal is "get the latest UI" rather than "get the latest TABC data" — it's near-instant versus the 1–3 minute live crawl.

4. **Log the change** per `CLAUDE.md`'s Logging Protocol — append an entry to the current `log/YYYY-MM.md` (find it via `Get-ChildItem log\*.md | Sort-Object Name | Select-Object -Last 1`), e.g.:
   ```markdown
   ## [YYYY-MM-DD HH:MM] data-update | TABC 綠建材主資料庫更新
   - actor: claude-sonnet-5 (via Claude Code)
   - files: tabc_master_database.json, tabc_master_database.meta.json, assets/green-material-showcase.html
   - trigger: manual
   - summary: +N 新增／M 更新／K 本次未再出現，共 totalAfter 筆
   ```

5. **Suggest next step**: recommend the user run `/GM_set compare` next to see whether this refresh changed anything relevant to their existing material Sets (expired licenses, renamed materials, licnos no longer found).

## Platform / Network Notes

- Requires outbound network access to `tabcmgr.hopto.org` (a dynamic-DNS-hosted government-contracted site — it can be slow or briefly unreachable; that is not this script's bug).
- Pure Python (`urllib`), no extra dependencies — runs the same on Windows/macOS/Linux, unlike the `/GM_web` skill which is Windows-only.

## Error Handling

| Error | Response |
|-------|----------|
| `RuntimeError` — zero items fetched | Site unreachable or HTML structure changed. Stop, report to user, do not modify any file. |
| `tabc_master_database.json` missing | No longer an error — treated as an empty database, the run becomes a full import (`diff["bootstrap"] = true`). Report this to the user as a first-time setup, not a routine update. |
| `metaWritten: false` in the real-run result | The fetch timestamp sidecar (`tabc_master_database.meta.json`) could not be written — the database itself updated fine. Mention it: until it is writable, `/GM_import` falls back to estimating the data's age from the database file's mtime instead of the recorded fetch time. |
| `showcaseSynced: false` in the real-run result | `assets/green-material-showcase.template.html`'s `const tabcDatabase = [...]` markers weren't found (template missing or corrupted) — the JSON file was still updated correctly; tell the user the template needs a manual look. |
| Dry run shows a very large `notSeen` count (e.g. hundreds) | Likely a partial crawl (network hiccup mid-run cut off several categories), not a real mass delisting. Warn the user and suggest re-running the dry run before proceeding to the real update. |

## Relationship to Other Files

- `GM_update_tabc_database.py` (`tools/green-material/`) — the fetch/merge/sync engine this skill drives; also the canonical reference for exactly which fields are real vs. template-derived.
- `tabc_master_database.meta.json` (repo root, local-only) — the fetch-timestamp sidecar written by this skill's real run only (not `--dry-run`, not `--resync-html`, since neither makes the local data any newer). `/GM_import` reads it back to tell the user how old their data is (issue #128). The master database is a bare JSON array with nowhere to put a header, which is why the timestamp lives beside it rather than inside it.
- `tabc_master_database.json` (repo root, local-only) — the file this skill refreshes; consumed by `GM_generate_revit_injection_plan.py` (`/GM_import`, `/GM_set compare`) and `assets/green-material-showcase.html`.
- `assets/green-material-showcase.template.html` (repo root, **git-tracked**) — the UI/JS/CSS source of truth for the showcase page. Edit this file for any UI/feature change, never `assets/green-material-showcase.html` directly (it's a generated, local-only file that this skill overwrites on every run).
- `tools/green-material/README.md` — governance notes on why the old `archive/scripts/catalog/*.py` scrapers (excluded from this repo entirely — see its "`archive/` 目錄未隨本 repo 收編" section) are historical, not live dependencies, and on the template/generated-file split.
- `.claude/skills/GM_set/SKILL.md` — the natural follow-up (`/GM_set compare`) once the master database has fresh data.
