---
name: GM_inject
description: "/GM_inject revit — actually write a previously-aligned green-material Set (from /GM_import) into the currently-open Revit project. Supports Wall / 單一組合 (combined wall+paint), 各別建立 (each material gets its own Type), and Material 純材料 (attaching a non-geometric material like caulk/adhesive to an existing Type, either as a new duplicate Type or by overwriting it) sets. Also supports Window/Door/loadable-family RFA green-material injection (not Set-driven — user names the base Type directly), and Column/Beam 結構材質指派 (single Structural Material parameter, not a CompoundStructure layer — e.g. 綠混凝土 assigned to a column or beam Type)."
user-invocable: true
---

Execute the Revit-side half of the `/GM_import` → `/GM_inject revit` two-step flow: take a Set that has already been aligned into a plan, and actually create the Material(s)/Type(s)/parameters in the live Revit document. This step **does mutate the user's Revit model** — always confirm the concrete plan with the user before calling any Revit-mutating tool.

## Lessons Reference
- **L-031**：Type 層級寫入工具（如 `assign_existing_material`）回報成功不代表值真的生效——部分族群把該參數設計成 Instance 範疇或關聯到公式，`Set()` 不拋例外但讀回值沒變。Scenario 8 已依此原則要求驗證失敗就停下來，不繼續往下寫共享參數。詳見 `domain/lessons.md`。

## Which Set does this act on?

- `revit` alone (no Set name) → pick the Set to act on:
  1. If a `/GM_import` was already run earlier **in this conversation**, use that Set — you already know its name from context.
  2. Otherwise, read `exported_material_sets.json` and find entries whose `planStatus` is `"已對齊 Agent 計畫"` (aligned but not yet injected). If exactly one, use it. If more than one, list them (name + items) and ask the user which one. If none, tell the user to run `/GM_import` first.
- `revit <SetName>` → use that Set explicitly (look it up in `exported_material_sets.json`).
- Exception: if the user is explicitly asking for Window/Door/loadable-family RFA injection (Scenario 7 below), there's no Set to look up — skip straight to the Scope check.

## Mandatory pre-write gate — expired green-material licenses

**Run this before the Scope check, on every path that writes to Revit, including Scenario 7 (RFA) and Scenario 8 (structural material).** A TABC label has an end date (`period`, e.g. `115/07/09 ~ 119/07/08`). Writing an already-expired certificate number into `GreenMaterial_Mat*_CertNo` / `_ValidUntil` puts a dead license into the delivered model, its schedules, and any submission documents produced from them. The plan engine detects this; this skill is what stops it.

1. Regenerate (or read) the plan for this Set as the scenario section tells you to, then check `plan['hasExpiredLicense']`.
2. If it is `false`, continue to the Scope check — say nothing further about expiry.
3. If it is `true`, **stop your turn before calling any Revit-mutating tool** and:
   - List every entry in `plan['expiredLicenses']`: licno, title, company, `period`, and the parsed `validUntil` end date.
   - State plainly which fields would carry the expired certificate, and that the model, schedules, and submission documents inherit it.
   - Note the database's age from `plan['databaseFreshness']` — if it is `stale` or `missing`, the expiry may simply be a stale local snapshot, and `/GM_update` is the right first move rather than approving the write.
   - Ask for **explicit approval to write the expired license(s) anyway**. Silence, "go ahead" for an earlier question, or a general confirmation from before this list was shown do **not** count. Do not offer to skip just the expired material unless the user asks — dropping a material from a Set silently is its own failure mode.
4. If the user approves, proceed — but the write is no longer silent: in your final report, state which Type(s) and which `Mat*` slot(s) carry an expired license, with the licno and its end date. Repeat it in the Set status update text you write to `exported_material_sets.json` (`plannedActions`), so the record survives this conversation.
5. If the user declines, stop. Do not write anything, and tell them the two ways forward: run `/GM_update` then `/GM_import` again (if the local snapshot is stale), or replace the material in the Set on the showcase page and re-align.

Never treat an expired license as a warning to mention in passing while continuing to write. The method-layer rule is in `domain/GM_parameter-schema.md`; if this section and that file ever disagree, the domain file wins.

---

## Scope check — which scenario?

Read the Set's `purpose` field in `exported_material_sets.json` (e.g. `"組合方式: 單一組合 | 品類: Wall | 補充條件: 無"`).

- **`品類: Wall` + `組合方式: 單一組合`, exactly 2 materials (one board/structure + one paint/finish)** → **Scenario 1**, go to that section below.
- **`組合方式: 各別建立`** (any `品類`: Floor/Wall/Ceiling) → **Scenario 2**, go to that section below.
- **`組合方式: 單一組合`** with **anything else** — non-Wall category (Floor/Ceiling), or more than 2 materials, or materials that need `needsManualReview`/Set-category-override resolution → **Scenario 3** (general multi-layer), go to that section below.
- **`品類: Material`** (pure/non-geometric material — caulk, adhesive, waterproofing) **and** the Set has a `pureMaterialTarget` entry in `exported_material_sets.json` → **Scenario 5** (TASK-005.5), go to that section below.
- **`品類: Material`** but **no** `pureMaterialTarget` yet → **stop**. Tell the user to run `/GM_import` first — it will show them a numbered table of candidate Types and wait for their pick before `pureMaterialTarget` gets written.
- **User explicitly asks to inject a green material into a Window/Door/loadable-family Type** (this path isn't wired into the `/GM_import` alignment flow yet — there's no `品類: Window`/`Door` convention in `exported_material_sets.json`) → **Scenario 7** (TASK-005.7 / `domain/GM_rfa-family-injection.md`), go to that section below. Only take this path when the user names a specific base Type themselves; don't infer it from a Set's `purpose` field.
- **`品類: Column`** or **`品類: Beam`** → **Scenario 8** (structural material assignment, not a CompoundStructure build — `組合方式` is ignored for this category), go to that section below.
- Anything else → **stop**. Tell the user this scenario has no wired Revit tool yet and that building it out is a separate task, not something to improvise on the spot.

---

## Scenario 1 — Wall / 單一組合 (combined wall + paint into one Type)

This mirrors `.agents/skills/combined-wall-set-import/SKILL.md` — read that file too if anything here is ambiguous.

1. **Re-anchor the live document**: call `get_project_info` to confirm a real Revit connection this turn (per CLAUDE.md's MCP Connection Status protocol). If it fails, retry once; if it still fails, stop and report the limitation.

2. **Get the plan's two materials**: re-run the match (don't reuse a stale `Revit_Injection_Plan.json` from a different Set) —
   ```bash
   python -c "
   import sys
   sys.path.insert(0, 'tools/green-material')
   import GM_generate_revit_injection_plan as g, json
   plan = g.generate_injection_plan('<SetName>', <items_list_from_json>, '')
   print(json.dumps(plan, ensure_ascii=False, indent=2))
   "
   ```
   From `plan['materialsMapping']`, identify which item is the **board/structure** material (`mappingDetails.layer` contains `Structure`) and which is the **paint/finish** material (`layer` contains `Finish`). If there aren't exactly one of each, stop and ask the user — this flow assumes exactly one board + one paint material per domain.md's rule.

3. **Pick the source WallType**: call `get_wall_types`. Prefer a type whose name contains `加粉刷` or `粉刷` (per `.agents/skills/combined-wall-set-import/domain.md` — duplicate from a type that already has a plaster/finish layer, not a bare structural wall). If more than one plausible candidate, show them and ask the user to pick; if exactly one obvious match, propose it and ask for a quick confirm rather than assuming.

4. **Confirm before writing anything**: show the user a summary and get explicit go-ahead —
   - Source WallType (name + ID)
   - New type name: `TABC_<SetName>` (no square brackets, per domain.md)
   - Board material name: `<licno>_<title>` (full licno, keep any `(續)`/`(增)` suffix) → `Structure [1]`, thickness 150mm (or the plan's `defaultThickness` if it differs)
   - Paint material name: `<licno>_<title>` → `Finish 1 [4]` / `Finish 2 [5]`, thickness 20mm (or plan's `defaultThickness`)
   - **TASK-005.6**: if the board material's `mappingDetails.wallUsageUnspecified` is `true`, explicitly flag it here — "no wall usage (外牆/分戶牆/輕隔間) was specified, using the conservative 150mm default — confirm or tell me the real thickness". If `wallUsageHint` is set (Exterior/PartyWall/LightPartition), state which one was detected and its matrix thickness.
   Do not proceed past this point without the user confirming.

5. **Create the type + materials**: call `duplicate_element_type` with `sourceTypeId`, `newTypeName`, `finishMaterialName` (paint), `structureMaterialName` (board), and thickness overrides if the plan specified non-default ones.

6. **Verify materials exist** (mandatory — do not skip): call `get_all_materials(searchKeyword: "<the Set's licno prefix or GBM>")` and confirm both new materials appear with the IDs `duplicate_element_type` returned.

7. **Bind shared parameters if needed**: call `load_shared_parameters` with `filePath` pointing to `GreenMaterial_SharedParams.txt` (absolute path, `tools/green-material/`) and `categories: ["Walls"]`, `bindToInstance: false`. Safe to call even if already bound (idempotent — reports `已存在相符綁定，跳過`).

8. **Write the 31 shared parameters**: call `set_green_material_type_parameters` on the new `typeId` with:
   - `certified: true`
   - `mat1` = the **board** material's data from the plan/database record: `name` (title), `certNo` (full licno with suffix), `category`, `subCategory`, `applicant` (company), `validUntil` (period), `cnsSpec`, `testItems`, `qualifiedItems`. Only include `tvoc`/`formaldehyde` if you have real per-material numeric values — do not invent numbers from the prose in `testItems`.
   - `mat2` = the **paint** material's data, same shape.
   Report any `MissingParameters` in the response — that means `load_shared_parameters` didn't actually bind them; don't silently ignore it.

9. **Verify the written values**: call `get_element_info` on the new `typeId` and confirm the `GreenMaterial_Mat1_*` / `GreenMaterial_Mat2_*` values match what you intended to write.

10. **Update the Set's status**: call
    ```bash
    python -c "
    import sys
    sys.path.insert(0, 'tools/green-material')
    import GM_generate_revit_injection_plan as g
    g.write_back_to_set_manager('<SetName>', plan_dict, planned_actions_override='已建立 Element ID <NewTypeId> 與材質 Element ID <finishMaterialId>/<structureMaterialId>')
    "
    ```
    (the `'Element ID'` substring in `planned_actions_override` is what flips `planStatus` to `已完成 Revit 牆體元件注入` — see `write_back_to_set_manager` in `GM_generate_revit_injection_plan.py`).

11. **Report**: new TypeId + TypeName, both MaterialIds + names, which 31-field values were written vs missing, and (optionally) offer to `select_element` + `zoom_to_element` on an existing instance of that type if one exists in the model.

---

## Scenario 2 — 各別建立 (each material gets its own independent Type)

Each material in the Set becomes its own new ElementType (Floor/Wall/Ceiling — whatever the Set's `品類` says), with **one** material filling every layer of that Type's compound structure. Unlike Scenario 1, there's no board/paint pairing here — just N materials → N Types. Type name and Material name are the **same string** (`<licno>_<title>`, no `TABC_` prefix — that prefix is reserved for Scenario 1's combined Type naming).

1. **Re-anchor the live document**: call `get_project_info` to confirm a real Revit connection this turn. Retry once on failure; if it still fails, stop and report the limitation.

2. **Get the plan's materials**: re-run the match for this Set —
   ```bash
   python -c "
   import sys
   sys.path.insert(0, 'tools/green-material')
   import GM_generate_revit_injection_plan as g, json
   plan = g.generate_injection_plan('<SetName>', <items_list_from_json>, '')
   print(json.dumps(plan, ensure_ascii=False, indent=2))
   "
   ```
   Each item in `plan['materialsMapping']` becomes one new Type. Note each item's `targetRevitCategory` (e.g. `OST_Floors`) — they should all match the Set's `品類`; if one doesn't, flag it rather than silently forcing it into the same category.

3. **Pick a source Type per category**: call `get_types_by_category(category: "Floors")` (or `Walls`/`Ceilings` matching the Set's `品類`). This lists existing Types with their current materials — pick one plain/basic Type as the duplication source (all new Types can share the same source, or you can ask the user for a per-material source if they want different base builds). Show the candidates and confirm with the user rather than silently guessing.

4. **Confirm before writing anything**: show the user the full list —
   - Source TypeId (shared across all, or per-material)
   - For each material: new Type name = new Material name = `<licno>_<title>` (full licno, keep any `(續)`/`(增)` suffix)
   Do not proceed past this point without the user confirming.

5. **Create each Type + material**: for each material, call `create_single_material_type` with `sourceTypeId` and `materialName` (`<licno>_<title>`). This duplicates the source Type, creates the material, and assigns it to every compound-structure layer of the new Type in one step.

5b. **Floor materials only — apply Surface Pattern** (TASK-005.2): if the Set's `品類` is `Floor` and the material is a finish/wear layer (tile, stone, or wood flooring — not a soundproof buffer), call `set_material_surface_pattern` with `materialId` = the material ID `create_single_material_type` just returned:
   - Tile/stone material (title contains `磚`/`石材` etc.) → `patternType: "Grid"` (`spacingMm` defaults to 600 for a 600×600 grid; override if the product spec states a different module size).
   - Wood flooring (title contains `木地板`/`木質地板` etc.) → `patternType: "Wood"`.
   - Soundproof buffer / non-visible substrate materials → skip this step, no pattern needed.
   This tool dedups by pattern name, so calling it again for another material of the same spacing reuses the existing FillPatternElement rather than creating a duplicate.

6. **Verify materials exist** (mandatory): call `get_all_materials(searchKeyword: "<Set's GBM prefix>")` and confirm all N new materials appear with the IDs each `create_single_material_type` call returned.

7. **Bind shared parameters if needed**: call `load_shared_parameters` with `categories` matching the Set's `品類` (e.g. `["Floors"]`), `bindToInstance: false`. Idempotent — safe to call even if already bound.

8. **Write shared parameters per Type**: for each new Type, call `set_green_material_type_parameters` with `typeId` = that Type's new ID and `mat1` = that one material's data (`name`, `certNo` full licno, `category`, `subCategory`, `applicant`, `validUntil`, `cnsSpec`, `testItems`, `qualifiedItems` — only include `tvoc`/`formaldehyde` if real per-material numbers exist). Leave `mat2`/`mat3` empty — there's only one material per Type in this scenario. Report any `MissingParameters`.

9. **Verify the written values**: call `get_element_info` on each new `typeId` and spot-check the `GreenMaterial_Mat1_*` values.

10. **Update the Set's status**: call `write_back_to_set_manager('<SetName>', plan_dict, planned_actions_override='已建立 Element ID <id1>, <id2>, ... 與對應材質')` — list every new Element ID so the `'Element ID'` substring check flips `planStatus` to done.

11. **Report**: a table of material → new TypeId → new MaterialId, and which shared-parameter fields were written vs missing for each.

---

## Scenario 3 — General multi-layer 單一組合 (2+ materials, any category)

Use `create_multi_layer_type` — it takes an ordered `layers` array (`{materialName, layerFunction, thicknessMm}`) instead of hardcoding 2 materials, so it covers Floor/Wall/Ceiling combined builds with any number of materials (e.g. a Floor with finish tile + soundproof buffer + structural concrete).

1. **Re-anchor the live document**: call `get_project_info`. Retry once on failure; otherwise stop and report the limitation.

2. **Get the plan's materials**: re-run the match for this Set. Materials with `mappingDetails.needsManualReview` (e.g. concrete that could be Wall or Floor) must already have been resolved — either by a `resolvedBySetCategoryOverride` in the plan, or by asking the user directly which layer/role each such material plays. Never silently guess a layer assignment for an unresolved material.

3. **Get the layer order and function**: **⚠️ Two completely independent orderings exist — do not conflate them:**
   - **Physical CompoundStructure layer order** (what goes in the `layers` array for step 6, top-to-bottom / exterior-to-interior): if the Set has `layerComposition.sequence`, `plan['materialsMapping']` is *already reordered to match it* — just build the `layers` array by iterating `plan['materialsMapping']` in the order it comes back, using each item's `targetLayer`/`mappingDetails` for `layerFunction`. **Skip any item with `mappingDetails.isAuxiliary: true`** (adhesive/sealant/waterproofing, routed via `layerComposition.auxiliary` in the showcase page's "🧴 輔助材料" drop zone, or via keyword detection) — it has no `layerFunction`/thickness and does not belong in the `layers` array at all; it still gets a `matN` slot in step 9, just not a physical layer. **Never re-sort the remaining items by `assignedSlot`/Mat-number** — `mat1`→`mat2`→`mat3`... is a shared-parameter metadata slot number (step 9), not a construction position, and sorting the physical layers by it silently corrupts the layer order even though the shared-parameter write still looks successful.
   - If the Set has no `layerComposition` (no sequence to inherit), Scenario 3 has no fixed convention — **ask the user** which material goes in which `layerFunction` and in what order, unless they already told you in this conversation. Do not assume order from the Set's `items` list order.

4. **Pick the source Type**: call `get_types_by_category` for the Set's `品類` (Walls/Floors/Ceilings). Show candidates and confirm with the user — same as Scenario 2 step 3.

5. **Confirm before writing anything**: show the full layer stack **in the physical order from step 3** —
   - Source TypeId
   - New type name (ask the user for a naming convention if the Set doesn't imply one — e.g. `TABC_<SetName>` for a genuinely combined build)
   - Each layer, in construction order: material name (`<licno>_<title>`, full licno with any suffix) → `layerFunction` → thickness
   - **TASK-005.6**: for any Wall `Structure` layer whose `mappingDetails.wallUsageUnspecified` is `true`, flag it and state the conservative default (150mm) is being used — ask the user to confirm or override. If `wallUsageHint` is set, state which wall usage (外牆/分戶牆/輕隔間) was detected and its matrix thickness instead of a bare number.
   Do not proceed without explicit confirmation.

6. **Create the type**: call `create_multi_layer_type` with `sourceTypeId`, `newTypeName`, and the confirmed `layers` array (same physical order as steps 3 and 5 — do not reorder by Mat-slot number). Sanity-check the response's `ExteriorShellLayers`/`InteriorShellLayers`: if the Set's `layerComposition` has Finish-role material(s) at one or both ends of the sequence and the response comes back with `0` shell layers on that side, the `layers` array order was probably wrong — stop and re-check before writing shared parameters.

6b. **Floor Finish layer only — apply Surface Pattern** (TASK-005.2, e.g. a Floor combining a Finish1 tile layer over a Substrate 打底 layer): for each layer in the response's `Layers` list whose `LayerFunction` is `Finish1`/`Finish2` and whose category is Floors, call `set_material_surface_pattern` with `materialId` = that layer's `MaterialId`:
   - Tile/stone finish (title contains `磚`/`石材` etc.) → `patternType: "Grid"` (`spacingMm` 600 default = 600×600 grid; override per product spec if stated).
   - Wood flooring finish (title contains `木地板`/`木質地板` etc.) → `patternType: "Wood"`.
   Skip `Structure`/`Substrate`/`Insulation` layers (e.g. the 打底/緩衝 layer) — no pattern needed there. The tool dedups patterns by name, so reuse across materials/Sets is automatic.

7. **Verify materials exist** (mandatory): call `get_all_materials(searchKeyword: "<Set's GBM prefix>")` and confirm every material in the response's `Layers` list appears. Auxiliary materials (skipped from `layers` in step 3/6) will **not** appear here — by design they never get a Revit `Material` element, only a text record in the Parent Type's Identity Data (step 9) — so don't treat their absence from `get_all_materials` as a failure.

8. **Bind shared parameters if needed**: call `load_shared_parameters` with `categories` matching the Set's `品類`.

9. **Write shared parameters**: the schema has 6 slots (`Mat1`~`Mat6` — see `domain/GM_parameter-schema.md`), so slot count normally equals material count (auxiliary materials included — see below); a Set only overflows if it has more than 6 materials total. **Do not decide the slot assignment yourself** — the plan JSON's `materialSlotAssignment` field (and each `materialsMapping[i].assignedSlot`) already contains the deterministic result, computed by `_assign_material_slots()` in `GM_generate_revit_injection_plan.py` (priority: Structure > Finish > Substrate > Other, tie-broken by construction order — auxiliary materials fall into `Other`, same as any material whose role can't be determined). Read `plan['materialSlotAssignment']['assignment']['mat1'..'mat6']` for which material goes in each slot, build the corresponding `mat1`..`mat6` objects from each material's full record, call `set_green_material_type_parameters`, and **tell the user explicitly which materials are in `plan['materialSlotAssignment']['unassigned']`** if any — don't silently drop them. Note `Mat3` is the one slot with a lighter field shape (no TVOC/Formaldehyde/CNS — see `domain/GM_parameter-schema.md` §1.3); whichever material lands there loses that data even though it still gets a real CompoundStructure layer.
   - **Auxiliary materials (`mappingDetails.isAuxiliary: true`) still need a `matN` object** — Mat1~Mat6 is a manifest of every green material the component uses, not just the ones with a physical layer, so skipping them here would make the component's material inventory incomplete even though `create_multi_layer_type` correctly left them out of the CompoundStructure (step 3/6). **In addition** to their `matN` slot, pass the top-level `adhesive`/`sealant`/`waterproofing` string parameter (whichever matches `mappingDetails.auxiliaryParam`, i.e. `GreenMaterial_Adhesive`→`adhesive`, `GreenMaterial_Sealant`→`sealant`, `GreenMaterial_Waterproofing`→`waterproofing`) using the exact string already computed in `sharedParameters[mappingDetails.auxiliaryParam]` (format `"產品名稱 (標章編號)"`) — don't reformat it yourself. A Set can have more than one auxiliary material of different types (e.g. one sealant + one waterproofing); pass each as its own top-level param in the same `set_green_material_type_parameters` call.

10. **Verify the written values**: call `get_element_info` on the new `typeId`.

11. **Update the Set's status**: call `write_back_to_set_manager` with `planned_actions_override` containing `'Element ID <id>'` plus all material IDs.

12. **Report**: the full layer stack with material IDs, the new TypeId, which shared-parameter fields were written vs missing, and which materials (if any) exceeded the 6-slot schema.

---

## Scenario 5 — Material 純材料附掛既有 Type (TASK-005.5)

A single non-geometric material (caulk/adhesive/waterproofing — no physical CompoundStructure layer) gets attached to an *existing* Wall/Floor/Ceiling Type the user picked during `/GM_import`, either by duplicating that Type (Path A, default, non-destructive) or by overwriting it directly (Path B, mutates every instance of that Type).

1. **Re-anchor the live document**: call `get_project_info`. Retry once on failure; otherwise stop and report the limitation.

2. **Get the material's classification**: re-run the match —
   ```bash
   python -c "
   import sys
   sys.path.insert(0, 'tools/green-material')
   import GM_generate_revit_injection_plan as g, json
   plan = g.generate_injection_plan('<SetName>', <items_list_from_json>, '')
   print(json.dumps(plan, ensure_ascii=False, indent=2))
   "
   ```
   There should be exactly one material with `mappingDetails.isAuxiliary: true`. Its `mappingDetails.auxiliaryParam` (`GreenMaterial_Adhesive`/`Sealant`/`Waterproofing`) tells you which top-level `set_green_material_type_parameters` field to write (`adhesive`/`sealant`/`waterproofing`), and `sharedParameters[auxiliaryParam]` has the exact `"產品名稱 (標章編號)"` string already formatted — don't reformat it. If there's more than one `isAuxiliary` material or none, stop and clarify with the user — this scenario assumes exactly one.

3. **Read the target Type the user already picked**: load `exported_material_sets.json`, find this Set, read `pureMaterialTarget` (`category`, `typeId`, `typeName`, `instanceCount`). If missing, this shouldn't happen given the Scope check above — stop and tell the user to run `/GM_import` again.

4. **Ask the user: Path A or Path B?** Default/recommend **Path A**. Never pick Path B without an explicit, separate confirmation from the user (per CLAUDE.md's action-care guidance — Path B mutates every existing instance of that Type).

   - **Path A — new Type, existing model untouched (default)**:
     a. Propose a new type name (e.g. `<pureMaterialTarget.typeName>_TABC_<licno>`) and confirm it with the user.
     b. Call `duplicate_type_only(sourceTypeId: <pureMaterialTarget.typeId>, newTypeName: <confirmed name>)` — this only duplicates the Type; it does not touch CompoundStructure or create any Material. Note the returned `NewTypeId`.
     c. `load_shared_parameters` with `categories: ["<pureMaterialTarget.category>s"]` (e.g. `["Walls"]`), `bindToInstance: false` — idempotent, safe even if already bound.
     d. `set_green_material_type_parameters(typeId: <NewTypeId>, mat1: <the material's data>, <adhesive|sealant|waterproofing>: <sharedParameters[auxiliaryParam]>)`.
     e. Affected scope to report: **0 existing instances** — it's a brand-new Type; the original `pureMaterialTarget.typeId` is untouched.

   - **Path B — overwrite the existing Type (requires explicit confirmation)**:
     a. Show the user `pureMaterialTarget.typeName` and `pureMaterialTarget.instanceCount` — i.e. exactly how many placed instances in the model will be affected — and get an explicit go-ahead before writing anything.
     b. **Snapshot "old" values first** (TASK-005.11): call `get_element_info(elementId: <pureMaterialTarget.typeId>)` *before* writing anything and keep whatever `GreenMaterial_*` fields are currently present (usually none/empty on a Type that's never been tagged). This is the "old value" half of the required change summary — don't skip it, you can't reconstruct it after overwriting.
     c. `load_shared_parameters` same as Path A step c.
     d. `set_green_material_type_parameters(typeId: <pureMaterialTarget.typeId>, mat1: <the material's data>, <adhesive|sealant|waterproofing>: <sharedParameters[auxiliaryParam]>)` — writes directly onto the existing Type.
     e. Affected scope to report: `pureMaterialTarget.instanceCount` existing instances now carry this material's data.

5. **Verify the written values**: call `get_element_info` on the written `typeId` (new or existing depending on path) and confirm the `GreenMaterial_Mat1_*` and the `GreenMaterial_Adhesive`/`Sealant`/`Waterproofing` field match what you intended to write. **Path B only**: this is also the "new value" half of the change summary — pair it against the "old value" snapshot from step 4b when reporting.

6. **Update the Set's status**: call
   ```bash
   python -c "
   import sys
   sys.path.insert(0, 'tools/green-material')
   import GM_generate_revit_injection_plan as g
   g.write_back_to_set_manager('<SetName>', plan_dict, planned_actions_override='已建立/覆蓋 Element ID <typeId>（<Path A 新建|Path B 覆蓋>）')
   "
   ```

7. **Report**: target TypeId + TypeName, which path was taken, which shared-parameter fields were written (including the top-level `adhesive`/`sealant`/`waterproofing` field), and the affected element scope (0 for Path A, `instanceCount` for Path B). **Path B only**: also report the old-value → new-value diff for every written field (TASK-005.11) — for a Type that's never been tagged before, "old" is typically "not set" for every `GreenMaterial_*` field, which is itself worth stating explicitly rather than omitting.

**Every write in this scenario (Path A's `duplicate_type_only` + `set_green_material_type_parameters`, or Path B's `set_green_material_type_parameters` alone) runs inside the C# tool's own single `Transaction`, so it's atomic and revertible via Revit's normal Undo — no extra transaction-grouping work needed here.**

---

## Scenario 7 — Window/Door/loadable-family RFA injection (TASK-005.7)

Read `domain/GM_rfa-family-injection.md` in full before running this scenario — it defines four hard rules (no-generation-from-scratch, backup-before-any-edit, Identity Data + dedicated shading/acoustic param placement, new-family-name to dodge LoadFamily overwrite ambiguity) that this section only summarizes. This scenario is **not** entered from a `/GM_import`-aligned Set — there's no `exported_material_sets.json` convention for Window/Door yet, so everything here is driven directly by what the user tells you in the conversation.

1. **Re-anchor the live document**: call `get_project_info`. Retry once on failure; otherwise stop and report the limitation.

2. **Get the user to name a base Type — never pick one yourself** (domain rule 1): ask which existing Window/Door/loadable-family Type is the closest match to the product being injected. Help them find candidates with `list_family_symbols(filter: "<keyword>")` (or `get_selected_elements` if they've selected a placed instance in Revit) and show the list — but the final pick is theirs, not an inference from a product spec sheet.

3. **Gather the material data**: the green material's `name`/`certNo`/`category`/`subCategory`/`applicant`/`validUntil`/`cnsSpec`/`testItems`/`qualifiedItems` (only include `tvoc`/`formaldehyde` if real per-material numbers exist — same rule as every other scenario). If the user gives a 遮陽係數 (shading coefficient) number, that's `shadingCoefficient` — Window/Curtain Wall cases only, leave it out entirely for a Door case. If they give a 隔音 Rw number, that's `acousticRw` — valid for both Window and Door cases.

4. **Confirm before writing anything**: show the user —
   - Base FamilySymbol (name + ID) they picked in step 2
   - New Type name (ask them, or propose `<base type name>_TABC_<licno>`)
   - Backup folder that will be used (default: the project file's folder + `_rfa_backup/`)
   - The material data, `certified: true`, and shading/acoustic values about to be written
   Do not proceed without explicit confirmation — this scenario opens and saves a separate Revit family document, which is a heavier operation than the Type-duplication scenarios above.

5. **Run the injection**: call `inject_green_material_into_family` with `sourceTypeId`, `newTypeName`, `sharedParamFilePath` (absolute path to `GreenMaterial_SharedParams.txt` in `tools/green-material/`), `mat1`, `certified: true`, and `shadingCoefficient`/`acousticRw` as applicable. Always pass `certified: true` — a 2026-08-13 fix added this field to the tool (it was previously missing from the Window/Door path only). **Known limitation (2026-08-13, confirmed on a real family)**: this field is best-effort for the RFA path only — on the tested case (`雙開落地窗- (2)_TABC_GBM0104092`), Revit rejected adding this specific YESNO shared parameter to the family with a generic `Shared parameter creation failed.` error, while every other field (Mat1, AcousticRw) wrote fine. Still pass `certified: true` (it may succeed on other families, and failure at least surfaces a clear diagnostic), but **do not treat a missing `GreenMaterial_Certified` as a failed run** — see step 6. This single call covers the whole family-document lifecycle (EditFamily → backup → new Type → write params → SaveAs under a new family name → LoadFamily back into the project) — it can't be split into smaller steps because the family document can't stay open across separate MCP calls.

6. **Read the response carefully**:
   - `BackupPath` — confirm this file path was actually reported; that's the "可復原備份" the domain file requires.
   - `MissingParameters` — for a Door case, `GreenMaterial_Window_ShadingCoefficient` legitimately not existing is expected (you didn't pass `shadingCoefficient`), not a failure. `GreenMaterial_Certified` failing with the `Shared parameter creation failed.` diagnostic is the known limitation above — report it to the user as "Mat1 data written successfully, Certified could not be added to this family (known Revit-side limitation)", not as a broken run. Anything else missing means `load_shared_parameters`-equivalent binding failed inside the family document — report it plainly.
   - `SiblingTypesBeforeLoad` / `SiblingTypesAfterLoad` / `AffectedExistingTypes` — this is the domain rule 4 verification; `AffectedExistingTypes` should always be `0`. If the tool call itself errored with "LoadFamily 會異動非目標 Type，已整批回滾" the whole operation was rolled back automatically — nothing was written, tell the user and stop rather than retrying blindly.
   - **Don't retry with a new `newTypeName` just because `Certified` failed** — retrying reproduces the exact same Revit-side rejection and only leaves behind an extra near-duplicate Type with no Certified either (confirmed by direct testing: a `_v2` and `_v3` retry both failed identically). If `Certified` is missing for this reason, stop after one attempt and report the limitation instead of looping.

7. **Verify**: call `get_element_info` on the returned `NewTypeId` and confirm the `GreenMaterial_Mat1_*` fields (and `GreenMaterial_Window_ShadingCoefficient`/`GreenMaterial_AcousticRw` where applicable) match what you intended to write. Check `GreenMaterial_Certified` too, but its absence is expected given the known limitation — don't treat it as something to fix by re-running.

8. **Report**: new Type ID + name, new family file path, backup file path, which parameters were written vs. missing, and confirm 0 existing Types were affected. There is no `exported_material_sets.json`/kanban write-back for this scenario yet — say so if the user asks about Set status.

**Validation requirement (per domain file and TASK-005.7's acceptance criteria)**: at least one Window case and one Door case must each go through this full flow before the scenario is considered proven out — don't treat a single Window run as covering Door too, since the Identity Data field set and shading/acoustic applicability genuinely differ between them.

---

## Scenario 8 — Column / Beam 結構材質指派 (2026-08-12)

Columns and structural framing (beams) don't have a `CompoundStructure` — a Column/Beam Type has exactly **one** structural material, set via a single `Structural Material` parameter on the `FamilySymbol`, not layered like Wall/Floor/Ceiling. `組合方式` (Q1: 單一組合/各別建立) is meaningless here and should be ignored — there's no layering to combine. This scenario writes the material straight onto that one parameter, then tags the new Type with `GreenMaterial_Mat1_*`.

1. **Re-anchor the live document**: call `get_project_info`. Retry once on failure; otherwise stop and report the limitation.

2. **Get the plan's material(s)**: re-run the match for this Set —
   ```bash
   python -c "
   import sys
   sys.path.insert(0, 'tools/green-material')
   import GM_generate_revit_injection_plan as g, json
   plan = g.generate_injection_plan('<SetName>', <items_list_from_json>, '')
   print(json.dumps(plan, ensure_ascii=False, indent=2))
   "
   ```
   Every material in `plan['materialsMapping']` becomes its own new Type (same one-material-per-Type shape as Scenario 2 — a single structural material parameter can't hold more than one material, so there's no combining even with 2+ materials in the Set).

3. **Pick a source Type**: call `get_types_by_category(category: "Columns")` for `品類: Column` (this covers both architectural `OST_Columns` and structural `OST_StructuralColumns` — show the user which kind each candidate is if it's not obvious from the name) or `get_types_by_category(category: "StructuralFraming")` for `品類: Beam`. Show the candidates and confirm a source Type with the user — same as Scenario 2 step 3, don't guess.

4. **Confirm before writing anything**: show the full list —
   - Source TypeId (shared across all materials, or per-material if the user wants different base sections)
   - For each material: new Type name = new Material name = `<licno>_<title>` (same convention as Scenario 2 — full licno, keep any `(續)`/`(增)` suffix)
   Do not proceed past this point without the user confirming.

5. **Create each Type**: for each material, call `duplicate_type_only(sourceTypeId: <source>, newTypeName: <licno>_<title>)`. This only duplicates the Type (geometry/family parameters untouched) — it does not create or assign any material, unlike `duplicate_element_type`/`create_single_material_type`.

6. **Create the material**: call `create_material(materialName: <same licno>_<title> string>)` — creates a standalone `OST_Materials` entry, no `AppearanceAssetElement` sharing with anything else.

7. **Assign the material to the Structural Material parameter**: call `assign_existing_material(typeIds: [<new TypeId from step 5>], materialName: <same string as step 6>)`. The C# side dispatches on the element's runtime type — for a `FamilySymbol` (Column/Beam Types are `FamilySymbol`, not `WallType`/`FloorType`/`CeilingType`) it calls `SetStructuralMaterial`, writing the `Structural Material` parameter directly rather than touching a `CompoundStructure`. **If this reports a non-zero `ErrorCount` for a Type, stop for that Type before step 9/10** (don't write `GreenMaterial_*` shared parameters onto a Type whose material assignment didn't actually take — see the Error Handling entry on `assign_existing_material` failures below).

8. **Verify materials exist** (mandatory): call `get_all_materials(searchKeyword: "<Set's GBM prefix>")` and confirm every new material from step 6 appears with the ID `create_material` returned.

9. **Bind shared parameters if needed**: call `load_shared_parameters` with `categories: ["Columns"]` or `["StructuralFraming"]` (matching the Set's `品類`), `bindToInstance: false`. Idempotent — safe even if already bound. **Columns binds to both `OST_Columns` and `OST_StructuralColumns` in one call** — don't call it twice per category.

10. **Write shared parameters per Type**: for each new Type, call `set_green_material_type_parameters(typeId: <new TypeId>, certified: true, mat1: <that material's data>)` — same field shape as every other scenario (`name`/`certNo`/`category`/`subCategory`/`applicant`/`validUntil`/`cnsSpec`/`testItems`/`qualifiedItems`; only include `tvoc`/`formaldehyde` if real per-material numbers exist). Always pass `certified: true` explicitly — Columns/StructuralFraming get the same global `GreenMaterial_Certified` field as every other category once step 9's binding covers them, don't skip it just because this scenario's material data is otherwise mat1-only. Only `mat1` for the material slot — one material per Type, same as Scenario 2. Report any `MissingParameters`; if it's every `GreenMaterial_*` field including `Certified`, step 9's `load_shared_parameters` call likely targeted the wrong category string.

11. **Verify the written values**: call `get_element_info` on each new `typeId` and spot-check `GreenMaterial_Certified` = Yes and the `GreenMaterial_Mat1_*` values, plus confirm the `Structural Material` parameter (or equivalent material field in the response) now points at the new material from step 6.

12. **Update the Set's status**: call `write_back_to_set_manager('<SetName>', plan_dict, planned_actions_override='已建立 Element ID <id1>, <id2>, ... 與對應材質')`.

13. **Report**: a table of material → new TypeId → new MaterialId, which shared-parameter fields were written vs missing for each, and which Revit column/beam category (architectural vs structural) each ended up in.

---

## Error Handling

| Error | Response |
|-------|----------|
| No Set pending and none named | Tell the user to run `/GM_import` first, or name the Set explicitly: `/GM_inject revit <SetName>` |
| Set's `品類`/`組合方式` matches neither scenario | Stop; explain this path isn't implemented yet |
| Scenario 1: plan doesn't have exactly one board + one paint material | Stop and show the mismatch to the user rather than guessing |
| Scenario 1: `get_wall_types` has no plausible "加粉刷" candidate | List all wall types and ask the user to pick a source explicitly |
| Scenario 2: a material's `targetRevitCategory` doesn't match the Set's declared `品類` | Flag it and ask the user how to handle that one material rather than forcing it |
| Scenario 3: user hasn't specified layer order/function for a multi-material Set | Ask directly — never assume board=Structure/paint=Finish like Scenario 1, and never assume order from the Set's `items` list |
| `set_material_surface_pattern` can't resolve `materialId`/`materialName` | Confirm the material was actually created in this run (check the prior step's response) before retrying — don't guess an ID |
| Scenario 3: more than 6 materials in one Set | Warn that only 6 fit the shared-parameter schema (`Mat1`~`Mat6`); tell the user which ones (from `plan['materialSlotAssignment']['unassigned']`) will be left out of the parameter write (they still get a real CompoundStructure layer, just no `GreenMaterial_Mat*` metadata) |
| `set_green_material_type_parameters` returns non-empty `MissingParameters` | Report it — usually means `load_shared_parameters` needs to target a different category, or the file path is wrong |
| Revit connection unavailable | State the limitation per CLAUDE.md's MCP Connection Status section; don't fabricate results |
| Scenario 5: `pureMaterialTarget.typeId` no longer exists in the model (deleted/renamed since `/GM_import` ran) | Stop; tell the user to run `/GM_import` again to re-pick a target Type |
| Scenario 5: plan has zero or more than one `isAuxiliary` material | Stop and clarify with the user — this scenario assumes exactly one non-geometric material per Set |
| Scenario 5: user hasn't picked Path A or Path B yet | Ask directly; never default to Path B without an explicit separate confirmation, since it mutates every existing instance of that Type |
| Scenario 7: user hasn't named a base FamilySymbol yet | Ask directly and offer `list_family_symbols`/`get_selected_elements` candidates — never guess a "similar" base yourself (domain rule 1) |
| Scenario 7: `inject_green_material_into_family` errors with "已整批回滾" (rolled back) | The whole family-document lifecycle was already rolled back server-side — nothing was written and no partial state exists; report the error and ask the user how to proceed rather than retrying with the same inputs blindly |
| Scenario 7: `MissingParameters` includes something other than the expected Door-case shading-coefficient gap | Report it — likely means the family document's shared-parameter binding failed; check `sharedParamFilePath` points at the real `GreenMaterial_SharedParams.txt` |
| Scenario 8: `get_types_by_category(category: "Columns")` returns a mix of architectural and structural columns | Show both kinds to the user with which is which (check the response's category info if present, or ask) — don't assume one over the other |
| Scenario 8: `assign_existing_material` reports the Type "不是支援的類型" | The duplicated Type isn't a `FamilySymbol`/`WallType`/`FloorType`/`CeilingType`/`MullionType` the C# side recognizes — very unlikely for a genuine Column/Beam Type; re-check `sourceTypeId` actually came from `get_types_by_category(category: "Columns"/"StructuralFraming")` and not a different category |
| Scenario 8: user's Set has more than one material | Not an error — each material gets its own new Type (step 5), same as Scenario 2; don't try to combine multiple materials onto one Column/Beam's single Structural Material parameter |
| Scenario 8: `assign_existing_material` reports "寫入 STRUCTURAL_MATERIAL_PARAM 未生效" | **Known limitation, confirmed 2026-08-12 on a real family (「混凝土樑-矩形」)**: some families' structural material Type parameter is present, reports non-read-only, and accepts `.Set()` without throwing — but the value silently doesn't persist even within the same Transaction (verified via immediate readback). Root cause is inside that family's own definition (likely a formula/association on the parameter), not something a Type-parameter API call can fix. `duplicate_type_only` still created the Type and `GreenMaterial_Mat1_*` may already be written on it — tell the user the Type exists but its structural material is NOT actually the new material, and ask whether to leave it for them to fix manually in the Family Editor, delete it, or try a different source Type from a family that doesn't have this constraint (test with `assign_existing_material` on the new Type before writing `GreenMaterial_*` params, so a broken family is caught before the shared-parameter write, not after) |
