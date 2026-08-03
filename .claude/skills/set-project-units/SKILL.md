---
name: set-project-units
description: Switch a whole Revit project's display units in one tool call via the set_project_units MCP tool. Use when the user asks to change project units, "改專案單位 / 切公制 / 一鍵改單位", set Air Flow to m³/h for Taiwan §102, or align units to a project standard. For Taiwan MEP use mode='taiwan'.
user-invocable: true
---

# set-project-units

One-call project-wide unit switching, replacing the manual `Manage → Project Units → per-discipline` clicking. Backed by the `set_project_units` MCP tool (`MCP/Core/Commands/CommandExecutor.ProjectUnits.cs`), which calls `Document.SetUnits(new Units(system))` inside a single reversible Transaction (Ctrl+Z).

## When to use
- "改專案單位", "切成公制/英制", "一口氣改所有單位", "set project units", "unit conversion".
- Taiwan MEP load/ventilation work where 建築技術規則 §102 needs Air Flow in **m³/h** (the course dataset is imperial CFM).
- Aligning a model to a project unit standard before building schedules / load tables (Appendix B workflow).

## Key facts
- **Whole-project action.** It changes display units for every element at once, in one Transaction. Reversible with Ctrl+Z.
- Units are **per-discipline**: changing Length/Area does NOT change HVAC Air Flow — those are independent `SpecTypeId`s. `mode='taiwan'` handles the Air Flow one for you.
- ⚠️ **C-c caution (report.md 附錄 C):** changing units is a project-wide act. If the model already has sized duct/pipe systems, confirm it did not disturb Mechanical Settings / Segments-and-Sizes. Decide units at the project-setup stage (階段 2-C), not mid-way.

## Procedure
1. **Re-anchor** active Revit state first (repo CLAUDE.md rule): call `get_project_info` (and `get_active_view` if a view matters). If the bridge is unreachable, stop and say so.
2. **Call `set_project_units`** with the right shape:
   - Taiwan MEP (recommended): `{ "mode": "taiwan" }` → metric base + Air Flow = m³/h.
   - Pure metric / imperial: `{ "mode": "metric" }` or `{ "mode": "imperial" }`.
   - Fine control (applied on top of mode): `length` (m/mm/cm/ft/ft-in), `area` (m2/sf), `volume` (m3/l/cf), `airFlow` (m3/h / l/s / cfm).
   - Example: `{ "mode": "taiwan", "length": "mm", "area": "m2" }`.
3. **One call at a time.** The revit-mcp bridge holds a single-connection lock; never issue concurrent calls (parallel calls wedge it with HTTP 409 — recover with the ribbon's 「切換/釋放連線」 button).
4. **Verify.** The tool returns `Result` (resolved Length/Area/Volume/AirFlow ForgeTypeIds) and `Applied`. Confirm against what the user asked. Optionally read a Space schedule with `read_schedule` to see Area now in m² and Air Flow in the chosen unit.
5. Report what changed and remind the user it is Ctrl+Z reversible.

## Parameters (set_project_units)
| Param | Values | Notes |
|---|---|---|
| `mode` | `taiwan` \| `metric` \| `imperial` | `taiwan` = metric + Air Flow m³/h. Choose this OR `system`. |
| `system` | `metric` \| `imperial` | Base system when `mode` omitted (default metric). |
| `length` | `m` `mm` `cm` `ft` `ft-in` | Optional override. |
| `area` | `m2` `sf` | Optional override. |
| `volume` | `m3` `l` `cf` | Optional override. |
| `airFlow` | `m3/h` `l/s` `cfm` | Optional override (§102 → `m3/h`). |

## Guardrails
- Never claim units changed without the tool's success `Result` in this turn (repo Tool-Call-Data-Honesty rule).
- Do not run against a model with committed duct/pipe sizing without flagging the C-c side-effect risk first.
