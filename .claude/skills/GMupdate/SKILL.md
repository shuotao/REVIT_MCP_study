---
name: GMupdate
description: Refresh tabc_master_database.json from the live TABC green-material website (https://tabcmgr.hopto.org) and re-sync the showcase page's offline cache. Trigger keywords: 更新綠建材資料、更新原始網頁資料、GMupdate、refresh TABC database、更新 TABC 資料庫。
user-invocable: true
---

Refresh the local green-material master database (`tabc_master_database.json`) from the live TABC official site, then re-sync the same data into `assets/green-material-showcase.html`'s embedded offline cache. This is the only supported entry point for pulling fresh data from the source website — see `tools/green-material/README.md` for why the archived scrapers in `tools/green-material/archive/scripts/catalog/` are not used directly.

## What This Updates and What It Doesn't

- **Refreshed from the live site (real data)**: `licno`, `title`, `company`, `period` (含 (續)/(增)/(變) 後綴), `category`, `subCategory`, `img`.
- **Re-derived from a keyword-rule template, not the live site's detail page** (this matches the existing database's own precedent — see `_enrich_record()` in `update_tabc_database.py`): `cnsSpec`, `testItems`, `qualifiedItems`, `productSpecFull`, `specList`, `specs`, `keywords`. These are plausible-looking placeholder values, not per-product certified test data scraped from TABC's detail page. **Always disclose this limitation to the user when reporting results** — do not imply these fields are authoritative lab data.
- **Never auto-deleted**: licnos present in the old database but not seen in this crawl are kept as-is and only listed as "本次未再出現" in the report — a partial network failure must never be allowed to silently wipe real records.

## Steps

1. **Dry run first** (no files are written):
   ```bash
   python update_tabc_database.py --dry-run
   ```
   This hits the live TABC site (`https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx`), pages through all 4 categories (健康/高性能/再生/生態), and prints a JSON diff report: `added` (new licnos), `updated` (licno + which fields changed), `notSeen` (licnos not seen this crawl — not deleted), `totalBefore`/`totalAfter`.
   - This can take 1–3 minutes (roughly 60–100 HTTP requests to an external site with a small delay between each). Tell the user it's running.
   - If the process raises `RuntimeError` ("本次未從 TABC 官網抓取到任何資料...") — the site is unreachable or its HTML structure changed. Stop, report this to the user, do not retry blindly.

2. **Report the dry-run diff to the user** before writing anything:
   - Counts: `len(added)`, `len(updated)`, `len(notSeen)`.
   - A few sample licnos from each bucket (not all — these lists can be large).
   - If `added == [] and updated == [] and notSeen == []`, tell the user the local database is already up to date and **stop here** — no need to run the real update.

3. **Ask the user to confirm** before running the real update (this overwrites `tabc_master_database.json` and rewrites a large chunk of `assets/green-material-showcase.html`, both git-tracked files):
   ```bash
   python update_tabc_database.py
   ```
   - This performs the same crawl again (the site has no bulk-export API, so a second live fetch is unavoidable — do not try to reuse the dry-run's in-memory result across a separate process invocation), merges into `tabc_master_database.json` (atomic write via temp file + `os.replace`), and rewrites the `const tabcDatabase = [...]` block in `assets/green-material-showcase.html` so the webpage's offline cache matches.
   - Report `showcaseSynced` from the result — if `false`, the HTML file's markers weren't found (structure changed) and the JSON file was still updated correctly; tell the user the showcase page needs manual re-sync.

4. **Log the change** per `CLAUDE.md`'s Logging Protocol — append an entry to the current `log/YYYY-MM.md` (find it via `Get-ChildItem log\*.md | Sort-Object Name | Select-Object -Last 1`), e.g.:
   ```markdown
   ## [YYYY-MM-DD HH:MM] data-update | TABC 綠建材主資料庫更新
   - actor: claude-sonnet-5 (via Claude Code)
   - files: tabc_master_database.json, assets/green-material-showcase.html
   - trigger: manual
   - summary: +N 新增／M 更新／K 本次未再出現，共 totalAfter 筆
   ```

5. **Suggest next step**: recommend the user run `/GMset compare` next to see whether this refresh changed anything relevant to their existing material Sets (expired licenses, renamed materials, licnos no longer found).

## Platform / Network Notes

- Requires outbound network access to `tabcmgr.hopto.org` (a dynamic-DNS-hosted government-contracted site — it can be slow or briefly unreachable; that is not this script's bug).
- Pure Python (`urllib`), no extra dependencies — runs the same on Windows/macOS/Linux, unlike the `/GMweb` skill which is Windows-only.

## Error Handling

| Error | Response |
|-------|----------|
| `RuntimeError` — zero items fetched | Site unreachable or HTML structure changed. Stop, report to user, do not modify any file. |
| `FileNotFoundError` — `tabc_master_database.json` missing | Stop, tell the user the master database file is missing from the repo root. |
| `showcaseSynced: false` in the real-run result | JSON updated successfully but `assets/green-material-showcase.html`'s `const tabcDatabase = [...]` markers weren't found — tell the user the showcase page's offline cache is now out of sync with the JSON and needs a manual look (the file's structure may have changed). |
| Dry run shows a very large `notSeen` count (e.g. hundreds) | Likely a partial crawl (network hiccup mid-run cut off several categories), not a real mass delisting. Warn the user and suggest re-running the dry run before proceeding to the real update. |

## Relationship to Other Files

- `update_tabc_database.py` (repo root) — the fetch/merge/sync engine this skill drives; also the canonical reference for exactly which fields are real vs. template-derived.
- `tabc_master_database.json` (repo root) — the file this skill refreshes; consumed by `generate_revit_injection_plan.py` (`/GMimport`, `/GMset compare`) and `assets/green-material-showcase.html`.
- `tools/green-material/README.md` — governance notes on why the old `archive/scripts/catalog/*.py` scrapers are historical, not live dependencies.
- `.claude/skills/GMset/SKILL.md` — the natural follow-up (`/GMset compare`) once the master database has fresh data.
