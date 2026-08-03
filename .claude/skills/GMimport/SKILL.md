---
name: GMimport
description: Parse a green-material Set alignment request copied from the green-material-showcase.html "對齊需求與擬訂計畫" modal, and generate a Revit injection plan (no Revit writes — planning/reporting only).
user-invocable: true
---

Parse a free-text `/GMimport` request (as pasted from the showcase page's modal) and produce a Revit green-material injection plan. This step never touches Revit — it only reads `tabc_master_database.json`, matches materials, and writes a plan report. The follow-up `/import revit` skill does the actual Revit writes.

## Input Shape

The argument is free text like:

```
請為材料 Set 【牆壁與塗料】 (GBM0104204, GBM0103960) 擬訂 Revit 綠建材寫入計畫。[需求對齊：組合方式: 單一組合 | 品類: Wall | 補充條件: 無]
```

## Steps

1. **Parse the text yourself** (don't write a generic parser — this input is simple enough to read directly):
   - `set_name`: the text between `【` and `】`.
   - `licnos`: all substrings matching `GBM\d+` (the parenthetical list after the Set name).
   - `purpose_override`: the text inside `[需求對齊：...]` if present, else empty string.
   - If `set_name` or `licnos` can't be found, stop and ask the user to paste the request again in the expected format.

2. **Run the plan engine** from the repo root:
   ```bash
   python -c "
   import generate_revit_injection_plan as g
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
   - The plan ID.
   - Tell the user: run `/import revit` next to actually write this Set into the currently-open Revit project.

## Error Handling

| Error | Response |
|-------|----------|
| No `【...】` found | Ask the user to re-paste the `/GMimport` text from the showcase modal |
| No `GBM\d+` matches found | Ask the user to re-paste; the licno list must be in the parentheses after the Set name |
| A licno matches nothing in `tabc_master_database.json` (even after suffix-tolerant fallback) | Report it as unmatched; don't silently drop it without telling the user |
| `tabc_master_database.json` missing | Stop and tell the user the master database file is missing from the repo root |

## Relationship to Other Files

- `generate_revit_injection_plan.py` (repo root) — the actual matching/plan engine this skill drives.
- `.agents/skills/combined-wall-set-import/SKILL.md` — the Revit-side procedure `/import revit` follows for Wall/单一組合 sets.
- `exported_material_sets.json` — shared state between the showcase webpage, this skill, and `/import revit`.
