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

3. **Get the layer order and function from the user**: unlike Scenario 1 (which always assumes board=Structure/paint=Finish), Scenario 3 has no fixed convention — **ask the user** which material goes in which `layerFunction` (`Finish1`/`Finish2`/`Substrate`/`Insulation`/`Structure`/`Membrane`) and in what order (top to bottom / exterior to interior), unless they already told you in this conversation. Do not assume order from the Set's `items` list order.

4. **Pick the source Type**: call `get_types_by_category` for the Set's `品類` (Walls/Floors/Ceilings). Show candidates and confirm with the user — same as Scenario 2 step 3.

5. **Confirm before writing anything**: show the full layer stack —
   - Source TypeId
   - New type name (ask the user for a naming convention if the Set doesn't imply one — e.g. `TABC_<SetName>` for a genuinely combined build)
   - Each layer: material name (`<licno>_<title>`, full licno with any suffix) → `layerFunction` → thickness
   Do not proceed without explicit confirmation.

6. **Create the type**: call `create_multi_layer_type` with `sourceTypeId`, `newTypeName`, and the confirmed `layers` array.

7. **Verify materials exist** (mandatory): call `get_all_materials(searchKeyword: "<Set's GBM prefix>")` and confirm every material in the response's `Layers` list appears.

8. **Bind shared parameters if needed**: call `load_shared_parameters` with `categories` matching the Set's `品類`.

9. **Write shared parameters**: this scenario can have more materials than the 3 `Mat1`/`Mat2`/`Mat3` slots support (the schema only has 3 slots — see `domain/green-material-parameter-schema.md`). Map the first 3 materials in construction significance order (typically Structure, then the two most relevant Finish/Substrate layers) into `mat1`/`mat2`/`mat3`, call `set_green_material_type_parameters`, and **tell the user explicitly which materials didn't get a slot** if there are more than 3 — don't silently drop them.

10. **Verify the written values**: call `get_element_info` on the new `typeId`.

11. **Update the Set's status**: call `write_back_to_set_manager` with `planned_actions_override` containing `'Element ID <id>'` plus all material IDs.

12. **Report**: the full layer stack with material IDs, the new TypeId, which shared-parameter fields were written vs missing, and which materials (if any) didn't fit the 3-slot schema.

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
| Scenario 3: more than 3 materials in one Set | Warn that only 3 fit the shared-parameter schema (`Mat1`/`Mat2`/`Mat3`); tell the user which ones will be left out of the parameter write (they still get a real CompoundStructure layer, just no `GreenMaterial_Mat*` metadata) |
| `set_green_material_type_parameters` returns non-empty `MissingParameters` | Report it — usually means `load_shared_parameters` needs to target a different category, or the file path is wrong |
| Revit connection unavailable | State the limitation per CLAUDE.md's MCP Connection Status section; don't fabricate results |
