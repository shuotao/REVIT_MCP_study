---
name: GM_set
description: /GM_set compare — compare existing green-material Sets in exported_material_sets.json against the current tabc_master_database.json snapshot (missing/expired/renamed materials), and refresh their plan files for Sets with differences. Trigger keywords: 比對 Set、Set 比對最新資訊、GM_set compare、Set 過期檢查、憑證過期。
user-invocable: true
---

Compare the material Sets already saved in `exported_material_sets.json` against the current `tabc_master_database.json` snapshot, surface any differences (a licno no longer found, a license whose validity period has ended, a material title that changed since the Set's plan was last generated), and — only for Sets the user confirms — refresh that Set's Revit injection plan/report so it reflects the latest data.

**This skill never writes to Revit.** It only updates `Revit_Injection_Plan.json`, `docs/green-material/Revit_Injection_Plan_Report.md`, and the Set's own entry in `exported_material_sets.json` (`plannedActions`/`planStatus`/`planId`/`updatedAt`). If the user wants the refreshed plan actually pushed into the open Revit project, tell them to run `/GM_inject revit` afterward — same as the normal `/GM_import` flow.

## Usage

- **`compare`** (no Set name) → compare **all** Sets in `exported_material_sets.json`.
- **`compare <Set 名稱>`** → compare only that one Set.
- No args → treat as `compare` with no Set name (i.e. compare all).

## Before Comparing

Mention to the user (don't block on it) that this compares against the **local** `tabc_master_database.json` — if they haven't run `/GM_update` recently, the comparison reflects whatever was last pulled from the live site, not necessarily today's. Suggest `/GM_update` first if they want a fully current comparison; proceed with the local snapshot either way if they don't want to wait.

## Steps

1. **Run the comparison** from the repo root:
   ```bash
   python -c "
   import sys
   sys.path.insert(0, 'tools/green-material')
   import GM_generate_revit_injection_plan as g
   import json
   results = g.compare_all_sets()
   print(json.dumps(results, ensure_ascii=False, indent=2))
   "
   ```
   For a single named Set, filter the printed list to the entry whose `setName` matches (case-sensitive exact match first, then substring match — same tolerance `_find_set_entry()` uses elsewhere in this codebase). If no entry matches, tell the user the Set name wasn't found and list the actual Set names from `exported_material_sets.json`.

   Each result has: `setName`, `totalItems`, `matched` (licnos still found), `missing` (licnos no longer in the database), `expired` (licenses whose validity period has ended, with `licno`/`title`/`period`), `changed` (licnos whose title differs from what was recorded the last time a plan was generated for this Set, with `oldTitle`/`newTitle`), `hasDiff` (bool).

2. **Report a per-Set summary table** to the user:
   - `✅ 一致` for Sets with `hasDiff: false`.
   - `⚠️ 有差異` for Sets with `hasDiff: true` — list each `missing` licno, each `expired` entry (title + period, and call out that the certificate has expired as of today), and each `changed` entry (old title → new title).
   - If a Set's `changed` list is empty simply because it has no prior plan snapshot yet (never run through `/GM_import`), don't report that as "no changes" without qualification — note it as "尚無先前計畫快照可比對名稱異動，僅檢查 missing/expired".

3. **For Sets with `hasDiff: true`, ask the user which ones to refresh** (default suggestion: all of them). Do not refresh silently — regenerating a Set's plan overwrites the single shared `Revit_Injection_Plan.json` / `docs/green-material/Revit_Injection_Plan_Report.md` files, so if the user is mid-review of an earlier Set's report, an unprompted refresh would clobber it under their feet.

4. **For each confirmed Set**, refresh it:
   ```bash
   python -c "
   import sys
   sys.path.insert(0, 'tools/green-material')
   import GM_generate_revit_injection_plan as g
   import json
   result = g.compare_and_refresh_set('<set_name>')
   print(json.dumps(result, ensure_ascii=False, indent=2))
   "
   ```
   This re-runs `generate_injection_plan()` against the current database and calls `write_back_to_set_manager()`, same engine `/GM_import` uses. Report the new `planId` for each refreshed Set.

   **Important**: because `Revit_Injection_Plan.json` / the Markdown report are single shared files (not one per Set), refreshing Set B after Set A overwrites Set A's plan file with Set B's. If the user asked to refresh multiple Sets, tell them clearly that only the **last** refreshed Set's `Revit_Injection_Plan.json`/report currently reflects on disk — each Set's own `exported_material_sets.json` entry still correctly tracks its own latest `planId`/`plannedActions`/`planStatus`, so nothing is lost, but if they want to see Set A's report again they'll need to re-run `/GM_set compare` (or `/GM_import`) for Set A specifically to regenerate it on demand.

5. **Close out**: for any Set actually refreshed, tell the user to run `/GM_inject revit` if they want the corrected material data pushed into the currently-open Revit project.

## Error Handling

| Error | Response |
|-------|----------|
| `exported_material_sets.json` has zero Sets | Report there's nothing to compare; suggest building a Set via `/GM_web open` first. |
| `tabc_master_database.json` missing | Stop and tell the user to run `/GM_update` — it treats a missing database as empty and does a full import, so it is the recovery path, not a dead end. (This row previously claimed `/GM_update` could not help because it only merged into an existing file; that stopped being true once `update_tabc_database()` gained the bootstrap path.) |
| Named Set not found (in `compare <Set 名稱>`) | List the actual Set names from `exported_material_sets.json` and ask the user to pick one, or re-check spelling. |
| `KeyError` from `compare_and_refresh_set()` | Same as above — the Set name didn't resolve; don't retry blindly. |
| A Set has `missing` licnos | Never silently drop them from the Set's `items` — report them and let the user decide (they may want to remove the item, or it may be a temporary site glitch resolved by re-running `/GM_update`). |

## Relationship to Other Files

- `GM_generate_revit_injection_plan.py` (`tools/green-material/`) — `diff_set_with_latest()` / `compare_all_sets()` / `compare_and_refresh_set()` are the comparison engine this skill drives, alongside the existing `generate_injection_plan()` / `write_back_to_set_manager()` also used by `/GM_import`.
- `exported_material_sets.json` — the Set data being compared; also the only place a licno → title snapshot from the last plan run persists (embedded in each Set's `purpose` text), since `Revit_Injection_Plan.json` itself is overwritten on every run.
- `.claude/skills/GM_update/SKILL.md` — the natural predecessor (`/GM_update`) to make sure the comparison is against fresh source data.
- `.claude/skills/GM_import/SKILL.md` — shares the same plan-generation engine; use that instead of this skill when building a **new** Set's plan from scratch rather than comparing an existing one.
