---
name: import
description: "/import revit — actually write a previously-aligned green-material Set (from /GMimport) into the currently-open Revit project. Supports Wall / 單一組合 (combined wall+paint) and 各別建立 (each material gets its own Type) sets."
user-invocable: true
---

Execute the Revit-side half of the `/GMimport` → `/import revit` two-step flow: take a Set that has already been aligned into a plan, and actually create the Material(s)/Type(s)/parameters in the live Revit document. This step **does mutate the user's Revit model** — always confirm the concrete plan with the user before calling any Revit-mutating tool.

## Which Set does this act on?

- `revit` alone (no Set name) → pick the Set to act on:
  1. If a `/GMimport` was already run earlier **in this conversation**, use that Set — you already know its name from context.
  2. Otherwise, read `exported_material_sets.json` and find entries whose `planStatus` is `"已對齊 Agent 計畫"` (aligned but not yet injected). If exactly one, use it. If more than one, list them (name + items) and ask the user which one. If none, tell the user to run `/GMimport` first.
- `revit <SetName>` → use that Set explicitly (look it up in `exported_material_sets.json`).

## Scope check — which scenario?

Read the Set's `purpose` field in `exported_material_sets.json` (e.g. `"組合方式: 單一組合 | 品類: Wall | 補充條件: 無"`).

- **`品類: Wall` + `組合方式: 單一組合`, exactly 2 materials (one board/structure + one paint/finish)** → **Scenario 1**, go to that section below.
- **`組合方式: 各別建立`** (any `品類`: Floor/Wall/Ceiling) → **Scenario 2**, go to that section below.
- **`組合方式: 單一組合`** with **anything else** — non-Wall category (Floor/Ceiling), or more than 2 materials, or materials that need `needsManualReview`/Set-category-override resolution → **Scenario 3** (general multi-layer), go to that section below.
- Anything else (non-geometric adhesive/sealant, Windows/.rfa family injection, etc.) → **stop**. Tell the user this scenario has no wired Revit tool yet and that building it out is a separate task, not something to improvise on the spot.

---

## Scenario 1 — Wall / 單一組合 (combined wall + paint into one Type)

This mirrors `.agents/skills/combined-wall-set-import/SKILL.md` — read that file too if anything here is ambiguous.

1. **Re-anchor the live document**: call `get_project_info` to confirm a real Revit connection this turn (per CLAUDE.md's MCP Connection Status protocol). If it fails, retry once; if it still fails, stop and report the limitation.

2. **Get the plan's two materials**: re-run the match (don't reuse a stale `Revit_Injection_Plan.json` from a different Set) —
   ```bash
   python -c "
   import generate_revit_injection_plan as g, json
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
   Do not proceed past this point without the user confirming.

5. **Create the type + materials**: call `duplicate_element_type` with `sourceTypeId`, `newTypeName`, `finishMaterialName` (paint), `structureMaterialName` (board), and thickness overrides if the plan specified non-default ones.

6. **Verify materials exist** (mandatory — do not skip): call `get_all_materials(searchKeyword: "<the Set's licno prefix or GBM>")` and confirm both new materials appear with the IDs `duplicate_element_type` returned.

7. **Bind shared parameters if needed**: call `load_shared_parameters` with `filePath` pointing to `GreenMaterial_SharedParams.txt` (absolute path, repo root) and `categories: ["Walls"]`, `bindToInstance: false`. Safe to call even if already bound (idempotent — reports `已存在相符綁定，跳過`).

8. **Write the 31 shared parameters**: call `set_green_material_type_parameters` on the new `typeId` with:
   - `certified: true`
   - `mat1` = the **board** material's data from the plan/database record: `name` (title), `certNo` (full licno with suffix), `category`, `subCategory`, `applicant` (company), `validUntil` (period), `cnsSpec`, `testItems`, `qualifiedItems`. Only include `tvoc`/`formaldehyde` if you have real per-material numeric values — do not invent numbers from the prose in `testItems`.
   - `mat2` = the **paint** material's data, same shape.
   Report any `MissingParameters` in the response — that means `load_shared_parameters` didn't actually bind them; don't silently ignore it.

9. **Verify the written values**: call `get_element_info` on the new `typeId` and confirm the `GreenMaterial_Mat1_*` / `GreenMaterial_Mat2_*` values match what you intended to write.

10. **Update the Set's status**: call
    ```bash
    python -c "
    import generate_revit_injection_plan as g
    g.write_back_to_set_manager('<SetName>', plan_dict, planned_actions_override='已建立 Element ID <NewTypeId> 與材質 Element ID <finishMaterialId>/<structureMaterialId>')
    "
    ```
    (the `'Element ID'` substring in `planned_actions_override` is what flips `planStatus` to `已完成 Revit 牆體元件注入` — see `write_back_to_set_manager` in `generate_revit_injection_plan.py`).

11. **Report**: new TypeId + TypeName, both MaterialIds + names, which 31-field values were written vs missing, and (optionally) offer to `select_element` + `zoom_to_element` on an existing instance of that type if one exists in the model.

---

## Scenario 2 — 各別建立 (each material gets its own independent Type)

Each material in the Set becomes its own new ElementType (Floor/Wall/Ceiling — whatever the Set's `品類` says), with **one** material filling every layer of that Type's compound structure. Unlike Scenario 1, there's no board/paint pairing here — just N materials → N Types. Type name and Material name are the **same string** (`<licno>_<title>`, no `TABC_` prefix — that prefix is reserved for Scenario 1's combined Type naming).

1. **Re-anchor the live document**: call `get_project_info` to confirm a real Revit connection this turn. Retry once on failure; if it still fails, stop and report the limitation.

2. **Get the plan's materials**: re-run the match for this Set —
   ```bash
   python -c "
   import generate_revit_injection_plan as g, json
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
   Do not proceed without explicit confirmation.

6. **Create the type**: call `create_multi_layer_type` with `sourceTypeId`, `newTypeName`, and the confirmed `layers` array (same physical order as steps 3 and 5 — do not reorder by Mat-slot number). Sanity-check the response's `ExteriorShellLayers`/`InteriorShellLayers`: if the Set's `layerComposition` has Finish-role material(s) at one or both ends of the sequence and the response comes back with `0` shell layers on that side, the `layers` array order was probably wrong — stop and re-check before writing shared parameters.

6b. **Floor Finish layer only — apply Surface Pattern** (TASK-005.2, e.g. a Floor combining a Finish1 tile layer over a Substrate 打底 layer): for each layer in the response's `Layers` list whose `LayerFunction` is `Finish1`/`Finish2` and whose category is Floors, call `set_material_surface_pattern` with `materialId` = that layer's `MaterialId`:
   - Tile/stone finish (title contains `磚`/`石材` etc.) → `patternType: "Grid"` (`spacingMm` 600 default = 600×600 grid; override per product spec if stated).
   - Wood flooring finish (title contains `木地板`/`木質地板` etc.) → `patternType: "Wood"`.
   Skip `Structure`/`Substrate`/`Insulation` layers (e.g. the 打底/緩衝 layer) — no pattern needed there. The tool dedups patterns by name, so reuse across materials/Sets is automatic.

7. **Verify materials exist** (mandatory): call `get_all_materials(searchKeyword: "<Set's GBM prefix>")` and confirm every material in the response's `Layers` list appears. Auxiliary materials (skipped from `layers` in step 3/6) will **not** appear here — by design they never get a Revit `Material` element, only a text record in the Parent Type's Identity Data (step 9) — so don't treat their absence from `get_all_materials` as a failure.

8. **Bind shared parameters if needed**: call `load_shared_parameters` with `categories` matching the Set's `品類`.

9. **Write shared parameters**: the schema has 6 slots (`Mat1`~`Mat6` — see `domain/green-material-parameter-schema.md`), so slot count normally equals material count (auxiliary materials included — see below); a Set only overflows if it has more than 6 materials total. **Do not decide the slot assignment yourself** — the plan JSON's `materialSlotAssignment` field (and each `materialsMapping[i].assignedSlot`) already contains the deterministic result, computed by `_assign_material_slots()` in `generate_revit_injection_plan.py` (priority: Structure > Finish > Substrate > Other, tie-broken by construction order — auxiliary materials fall into `Other`, same as any material whose role can't be determined). Read `plan['materialSlotAssignment']['assignment']['mat1'..'mat6']` for which material goes in each slot, build the corresponding `mat1`..`mat6` objects from each material's full record, call `set_green_material_type_parameters`, and **tell the user explicitly which materials are in `plan['materialSlotAssignment']['unassigned']`** if any — don't silently drop them. Note `Mat3` is the one slot with a lighter field shape (no TVOC/Formaldehyde/CNS — see `domain/green-material-parameter-schema.md` §1.3); whichever material lands there loses that data even though it still gets a real CompoundStructure layer.
   - **Auxiliary materials (`mappingDetails.isAuxiliary: true`) still need a `matN` object** — Mat1~Mat6 is a manifest of every green material the component uses, not just the ones with a physical layer, so skipping them here would make the component's material inventory incomplete even though `create_multi_layer_type` correctly left them out of the CompoundStructure (step 3/6). **In addition** to their `matN` slot, pass the top-level `adhesive`/`sealant`/`waterproofing` string parameter (whichever matches `mappingDetails.auxiliaryParam`, i.e. `GreenMaterial_Adhesive`→`adhesive`, `GreenMaterial_Sealant`→`sealant`, `GreenMaterial_Waterproofing`→`waterproofing`) using the exact string already computed in `sharedParameters[mappingDetails.auxiliaryParam]` (format `"產品名稱 (標章編號)"`) — don't reformat it yourself. A Set can have more than one auxiliary material of different types (e.g. one sealant + one waterproofing); pass each as its own top-level param in the same `set_green_material_type_parameters` call.

10. **Verify the written values**: call `get_element_info` on the new `typeId`.

11. **Update the Set's status**: call `write_back_to_set_manager` with `planned_actions_override` containing `'Element ID <id>'` plus all material IDs.

12. **Report**: the full layer stack with material IDs, the new TypeId, which shared-parameter fields were written vs missing, and which materials (if any) exceeded the 6-slot schema.

---

## Error Handling

| Error | Response |
|-------|----------|
| No Set pending and none named | Tell the user to run `/GMimport` first, or name the Set explicitly: `/import revit <SetName>` |
| Set's `品類`/`組合方式` matches neither scenario | Stop; explain this path isn't implemented yet |
| Scenario 1: plan doesn't have exactly one board + one paint material | Stop and show the mismatch to the user rather than guessing |
| Scenario 1: `get_wall_types` has no plausible "加粉刷" candidate | List all wall types and ask the user to pick a source explicitly |
| Scenario 2: a material's `targetRevitCategory` doesn't match the Set's declared `品類` | Flag it and ask the user how to handle that one material rather than forcing it |
| Scenario 3: user hasn't specified layer order/function for a multi-material Set | Ask directly — never assume board=Structure/paint=Finish like Scenario 1, and never assume order from the Set's `items` list |
| `set_material_surface_pattern` can't resolve `materialId`/`materialName` | Confirm the material was actually created in this run (check the prior step's response) before retrying — don't guess an ID |
| Scenario 3: more than 6 materials in one Set | Warn that only 6 fit the shared-parameter schema (`Mat1`~`Mat6`); tell the user which ones (from `plan['materialSlotAssignment']['unassigned']`) will be left out of the parameter write (they still get a real CompoundStructure layer, just no `GreenMaterial_Mat*` metadata) |
| `set_green_material_type_parameters` returns non-empty `MissingParameters` | Report it — usually means `load_shared_parameters` needs to target a different category, or the file path is wrong |
| Revit connection unavailable | State the limitation per CLAUDE.md's MCP Connection Status section; don't fabricate results |
