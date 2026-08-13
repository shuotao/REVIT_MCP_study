---
name: mep-settings-curation
description: Inventory and curate Revit's Manage → MEP Settings — pipe segment size catalogs, duct Rectangular/Oval/Round size tables, fitting angles, pipe slopes, fluids. Four-phase flow, catalog inventory → real model usage → standards reconciliation → add/remove. Use when the user says 盤點管徑 / 風管尺寸表 / segments and sizes / 這個尺寸有沒有人在用 / 刪掉沒用到的尺寸 / 增加管徑 / mechanical settings / CNS 對帳, names bare duct sizes like "2438 1371", or asks what sizes a model actually uses. Removal is guarded — never delete a size without running the usage scan first.
user-invocable: true
---

# mep-settings-curation

Revit hides the duct/pipe **size catalogs** and MEP system parameters inside `Manage → MEP Settings`. Schedules and the System Browser cannot reach them — they are setting definitions, not model elements, so the Revit API is the only path.

Four MCP tools cover this area. **The method lives in `domain/mep-mechanical-settings.md`; that file wins on any conflict with this skill.**

## The one thing that matters most

**Catalog ≠ usage.** These are different questions and need different tools:

| Question | Tool |
|---|---|
| What sizes are **listed** in Mechanical Settings? | `get_mep_segments_and_sizes` |
| What sizes do model elements **actually use**? | `get_mep_size_usage` |

Deleting based on the catalog alone is blind deletion. The catalog does not know whether anything uses it.

Also: `Used in Size Lists` / `Used in Sizing` are **not** usage. They only control whether a size appears in dropdowns / participates in auto-sizing. A size with both unchecked can still have existing elements using it.

## Tools

| Tool | Reads / writes | Covers |
|---|---|---|
| `get_mep_segments_and_sizes` | read | Pipe segment catalogs (material × schedule) + duct size tables |
| `get_mep_settings` | read | Angles, slopes, fluids, calculation, naming/annotation, hidden line |
| `get_mep_size_usage` | read | Which catalog sizes real elements use; orphans; removable candidates |
| `curate_mep_sizes` | **write** | Add / remove sizes, with the four-step removal protocol |

## Procedure

**Re-anchor first** (repo CLAUDE.md rule): call `get_project_info` before anything else. One call at a time — the bridge holds a single-connection lock; concurrent calls wedge it with HTTP 409 (recover via the ribbon's 「切換/釋放連線」 button).

### Phase 1 — Catalog inventory

`get_mep_segments_and_sizes` with `summaryOnly: true` first. A full dump runs to tens of thousands of characters and will blow the response limit; the summary gives every segment's size count and checkbox counts. Then drill with `segmentName` (e.g. `"Copper - K"`) only where needed.

Use `get_mep_settings` when the question is about angles / slopes / fluids rather than sizes.

### Phase 2 — Real usage

`get_mep_size_usage`. Scope it (`scope`, `shape`, `segmentName`) to what the user actually asked about.

Read three things out of the result:
- `usageCount` per catalog size — `0` means removable.
- `orphans` — sizes the model uses that the catalog lacks. These are **add** candidates, not errors.
- `unattributedFittingSizes` (pipe only) — fitting connectors carry a diameter but no segment, so they cannot be attributed. `curate_mep_sizes` blocks matching sizes by default.

Set `includeElementIds: true` when the user needs to know *who* is holding a size.

### Phase 3 — Standards reconciliation

Compare the catalog against what the work requires. Read `domain/mep-mechanical-settings.md` §3 before making any claim about codes — it draws a line this phase depends on:

- **Taiwan has no regulation for duct sizing friction rate or velocity limits.** The only article naming duct velocity is 建築設備編 §106 (450 m/min), and it is a **smoke-exhaust lower bound in m/min** — the opposite direction and a different unit from supply-air sizing. Never present it as the value to type into the Sizing dialog.
- Real sizing values come from the **design-manual layer** (ASHRAE / SMACNA / SHASE / 公會), not from law. Carry their honesty markers: ✅ = primary source read, ⚠️ = second-hand. Never launder a ⚠️ value into a stated fact.
- **Display units first.** Sizing dialog units follow Project Units, so run `set_project_units` with `mode='taiwan'` *before* opening it — then metric design values can be typed directly.
- **The catalog gates sizing.** Auto-sizing can only pick sizes that are in the catalog *and* have `Used in Sizing` checked. Changing units alone does not give metric sizes; the catalog has to carry them.

Taiwan CNS specifics: §6.1 has the judgement (metal pipe A-呼稱 needs no change; PVC does). **§6.2's discrete CNS size series is still missing — do not invent one.** Note the distinction: continuous design values (friction/velocity) are sourced; the discrete size list is not. The add/remove protocol does not depend on it, so curation can proceed without it.

### Phase 4 — Add / remove

`curate_mep_sizes`. **Always `dryRun: true` first** (it is the default) and show the plan to the user before executing.

- **Add is unrestricted.** One more option cannot break an existing element.
- **Remove is guarded.** The tool runs its own usage scan and blocks anything in use, reporting who is using it. Never argue past a block — either the user fixes those elements first, or that size stays.

The tool executes the four-step protocol internally: ① list → ② single Transaction → ③ QC by re-reading and diffing the catalog → ④ auto-restore anything accidentally removed. It always returns `RestorePayload` with the full definition of every removed size.

**Read the `Qc` block back to the user.** `Qc.passed: false` means something needs human attention — do not report success.

## Parameters

### `get_mep_size_usage`
| Param | Values | Notes |
|---|---|---|
| `scope` | `both` \| `duct` \| `pipe` | Default `both`. |
| `shape` | `Round` \| `Rectangular` \| `Oval` | Duct only. |
| `segmentName` | substring | Pipe only, e.g. `Copper`. |
| `includeUnused` | bool | Default `true` — these are the removable candidates. |
| `includeElementIds` | bool | Default `false`. Turn on to identify blockers. |

### `curate_mep_sizes`
| Param | Values | Notes |
|---|---|---|
| `target` | `pipe` \| `duct` | Required. |
| `segmentName` | exact-ish name | Required for `pipe`. Ambiguous matches are rejected, not guessed. |
| `shape` | `Round` \| `Rectangular` \| `Oval` | Required for `duct`. |
| `add` | array | `pipe` needs `nominal_mm` + `inner_mm` + `outer_mm`; `duct` needs only `nominal_mm`. |
| `remove` | array of mm | In-use sizes are blocked. |
| `dryRun` | bool | Default `true`. |
| `ignoreUnattributedFittings` | bool | Default `false`. Only set true when the user has confirmed those fittings do not belong to this segment. |

## Guardrails

- **Never delete without Phase 2 in the same session.** If the usage scan has not run this turn, run it.
- **Never invent pipe inner/outer diameters.** They drive hydraulic calculation. The tool rejects an add that omits them; ask the user for the real values instead of estimating.
- **Do not tell the user to fix sizes through the dialog.** Opening `Segments and Sizes` and pressing OK silently quantizes every inner diameter to the display precision (measured: Copper-K's true IDs 0.305" / 0.995" / 5.741" all snapped to 1/32" multiples). API writes do not do this — that is a reason to prefer the tool, and a reason to warn before the user opens that dialog on a model that matters.
- **Angles are a separate risk class.** Disabling a fitting angle can invalidate existing elbows. `curate_mep_sizes` deliberately does not touch angles; do not work around that.
- **Report counts from tool output only** (repo Tool-Call-Data-Honesty rule). Never state a size list or usage count from memory.

## Reference

- `domain/mep-mechanical-settings.md` — the method: usage-scan sources, the four-step removal protocol, Revit API unit traps (angles are already degrees, slopes are percent, connector tolerance is an angle, duct inner/outer are placeholders), cross-version differences, and the Taiwan CNS judgement.
- `set-project-units` skill — display units, Taiwan §102 air flow.
