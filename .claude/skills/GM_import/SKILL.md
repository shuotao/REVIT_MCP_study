---
name: GM_import
description: Parse a green-material Set alignment request copied from the green-material-showcase.html "對齊需求與擬訂計畫" modal, and generate a Revit injection plan (planning/reporting only — for pure-material Sets it also runs a read-only Revit lookup to let the user pick a target Type; it never writes to Revit).
user-invocable: true
---

Parse a free-text `/GM_import` request (as pasted from the showcase page's modal) and produce a Revit green-material injection plan. This step never **writes** to Revit — it reads `tabc_master_database.json`, matches materials, and writes a plan report. For the "純材料" (Material) scenario it also does a **read-only** Revit query (never a mutation) to show the user candidate Types to attach to — see Step 4. The follow-up `/GM_inject revit` skill does the actual Revit writes.

## Input Shape

The argument is free text like:

```
請為材料 Set 【牆壁與塗料】 (GBM0104204, GBM0103960) 擬訂 Revit 綠建材寫入計畫。[需求對齊：組合方式: 單一組合 | 品類: Wall | 補充條件: 無]
```

For a "純材料" (pure-material) Set — TASK-005.5, e.g. a single caulk/adhesive/waterproofing material with no compound layer — the showcase page appends one more field after an extra popup:

```
請為材料 Set 【地磚填縫劑】 (GBM0104110) 擬訂 Revit 綠建材寫入計畫。[需求對齊：組合方式: 各別建立 | 品類: Material | 補充條件: 無 | 掛載類別: Wall]
```

`掛載類別` (attach category) is the existing Revit category (Wall/Floor/Ceiling) the user wants this non-geometric material attached to — see Step 4.

## Steps

0. **Check the local database's freshness first (never skip this).** The plan you are about to produce is only as current as `tabc_master_database.json`, which is local-only and can be months old without anything saying so. Run:
   ```bash
   python tools/green-material/GM_generate_revit_injection_plan.py --freshness
   ```
   This does no network I/O and writes nothing — it only reads the local database's age. Act on the `status` field:

   | `status` | What to do |
   |---|---|
   | `missing` | **Stop.** Tell the user there is no local database yet and that `/GM_update` builds it from scratch (this is the fresh-clone bootstrap path, not an error). Do not attempt the plan — Step 2 would just crash on `FileNotFoundError`. |
   | `stale` (older than `thresholdDays`, default 30) | **Tell the user before planning**: the database was fetched `fetchedAt`, `ageDays` days ago, holds `recordCount` records, and recommend running `/GM_update` first. State the consequence plainly — the plan may be based on licenses that have since expired or changed. Then **ask whether to continue anyway or update first**. If they say continue, continue; this is a recommendation, not a gate. |
   | `fresh` | Report one line — fetched `fetchedAt`, `ageDays` days ago, `recordCount` records — and continue without asking. |
   | `unknown` | Say the fetch time could not be determined, suggest `/GM_update` if they want certainty, and continue if they'd rather not wait. |

   When `fetchedAtSource` is `"mtime"` (the database predates the sidecar timestamp file, or the sidecar was lost), say so: the age is **estimated from the file's modification time**, not from a recorded fetch. Copying, restoring a backup, or an rsync all break that estimate. Don't present an mtime-derived age as if it were a recorded fetch time.

1. **Parse the text yourself** (don't write a generic parser — this input is simple enough to read directly):
   - `set_name`: the text between `【` and `】`.
   - `licnos`: all substrings matching `GBM\d+` (the parenthetical list after the Set name).
   - `purpose_override`: the text inside `[需求對齊：...]` if present, else empty string.
   - If `set_name` or `licnos` can't be found, stop and ask the user to paste the request again in the expected format.

2. **Run the plan engine** from the repo root (the engine itself lives in `tools/green-material/`, so add it to `sys.path` first):
   ```bash
   python -c "
   import sys
   sys.path.insert(0, 'tools/green-material')
   import GM_generate_revit_injection_plan as g
   plan = g.generate_injection_plan('<set_name>', ['<licno1>', '<licno2>', ...], '<original full text>')
   g.write_back_to_set_manager('<set_name>', plan, purpose_override='<purpose_override>')
   import json
   print(json.dumps(plan, ensure_ascii=False, indent=2))
   "
   ```
   Substitute the parsed values directly (Python string literals — escape embedded quotes). This:
   - Matches each licno against `tabc_master_database.json` (exact match first, then suffix-tolerant fallback for `(續)`/`(增)` certificates — see `_normalize_licno`). **Never truncate a matched licno's suffix in what you report** — always use the full licno exactly as it appears in the database record.
   - Writes `Revit_Injection_Plan.json` and `docs/green-material/Revit_Injection_Plan_Report.md`.
   - Updates the Set's entry in `exported_material_sets.json` (`planStatus: "已對齊 Agent 計畫"`, `planId`, `plannedActions`).

3. **Report a concise summary to the user** (do not paste the full plan JSON):
   - Set name and matched material count (flag if fewer materials matched than licnos requested — that means a licno wasn't found in the master DB even after suffix-tolerant matching).
   - For each matched material: licno (full, with any suffix), title, target Revit category, target layer (Structure/Finish1/Finish2/etc.), suggested thickness.
   - **Wall Structure layers (TASK-005.6)**: if any item's `mappingDetails.wallUsageUnspecified` is `true`, call this out explicitly — the plan used the conservative generic default (150mm) because no wall usage (外牆/分戶牆/輕隔間) was found in the Set's Q3 補充條件 text; tell the user they can either re-run `/GMimport` with that detail added to Q3, or override the thickness directly when `/GM_inject revit` asks for confirmation. Don't silently pass this through as if it were a confident value.
   - **Data freshness**: restate the Step 0 result in one line (fetched date, age in days, record count) so the summary is self-contained — someone reading only the summary must be able to see what vintage of data this plan rests on.
   - **Expired licenses (mandatory)**: if `plan['hasExpiredLicense']` is `true`, list every entry in `plan['expiredLicenses']` — licno, title, company, period — and warn that `/GM_inject revit` will stop and require explicit approval before writing them. Never omit this because the plan "otherwise looks fine".
   - The plan ID.
   - Tell the user: run `/GM_inject revit` next to actually write this Set into the currently-open Revit project.
   - **Exception**: if the plan's `pureMaterialAttachCategory` is set (non-null) and at least one item in `plan['materialsMapping']` has `mappingDetails.isAuxiliary: true`, do **not** tell the user to run `/GM_inject revit` yet — go to Step 4 first.

4. **Pure-material Sets only — show candidate Types and wait for the user's pick** (TASK-005.5, only when `plan['pureMaterialAttachCategory']` is set and the Set has an `isAuxiliary` material):
   - Call `get_types_by_category(category: "<pureMaterialAttachCategory>")` — **read-only**, this does not mutate the model. Supported categories: `Wall`, `Floor`, `Ceiling` (Door/Window are a different, family-based mechanism — not covered by this scenario).
   - Present the response's `Types` array as a numbered table in your reply: 編號 / TypeName / FamilyName / InstanceCount / 目前材質（`Materials` 陣列摘要，例如取前 1-2 個層＋`...`）.
   - Ask the user to reply with the row number of the Type they want this material attached to. **Do not guess or auto-pick one** — stop your turn here and wait for their reply.
   - When the user replies with a number (plain text, next turn): resolve it against the table you just showed, then persist the pick so `/GM_inject revit` can find it later without needing this conversation's full context:
     ```bash
     python -c "
     import json
     with open('exported_material_sets.json', 'r', encoding='utf-8') as f:
         sets = json.load(f)
     key = next((k for k in sets if '<set_name>' in k or k in '<set_name>'), '<set_name>')
     sets[key]['pureMaterialTarget'] = {
         'category': '<pureMaterialAttachCategory>',
         'typeId': <chosen TypeId>,
         'typeName': '<chosen TypeName>',
         'instanceCount': <chosen InstanceCount>,
     }
     with open('exported_material_sets.json', 'w', encoding='utf-8') as f:
         json.dump(sets, f, ensure_ascii=False, indent=2)
     print('OK')
     "
     ```
   - Confirm the write and tell the user they can now run `/GM_inject revit` to actually write the material into that Type.

## Error Handling

| Error | Response |
|-------|----------|
| No `【...】` found | Ask the user to re-paste the `/GM_import` text from the showcase modal |
| No `GBM\d+` matches found | Ask the user to re-paste; the licno list must be in the parentheses after the Set name |
| A licno matches nothing in `tabc_master_database.json` (even after suffix-tolerant fallback) | Report it as unmatched; don't silently drop it without telling the user |
| `tabc_master_database.json` missing | Stop, but not as a bare error — this is the normal state of a fresh clone. Tell the user to run `/GM_update`, which bootstraps the database from the TABC site from scratch. (Step 0 catches this before the plan engine is ever called.) |
| Step 0's freshness check itself fails to run | Report that you could not verify how old the local data is, and say so again in your final summary. Do not silently proceed as though the data were current. |
| Step 4: `get_types_by_category` returns zero Types for the chosen category | Tell the user there's no existing Type of that category in the current model yet — they need to create one manually first (Scenario 5's Path A still needs a source Type to duplicate from) |
| Step 4: user's reply isn't a valid row number from the table shown | Ask them to reply with one of the listed numbers; don't guess which one they meant |

## Relationship to Other Files

- `GM_generate_revit_injection_plan.py` (`tools/green-material/`) — the actual matching/plan engine this skill drives.
- `.agents/skills/combined-wall-set-import/SKILL.md` — the Revit-side procedure `/GM_inject revit` follows for Wall/单一組合 sets.
- `exported_material_sets.json` — shared state between the showcase webpage, this skill, and `/GM_inject revit`.
