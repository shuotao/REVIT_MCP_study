---
name: detail-component-sync
description: "Synchronize detail component headers (AE-numbering) with sheet numbers in Revit. TRIGGER when: user mentions detail headers, detail sync, sheet number sync, AE-numbering, detail component type creation, or detail drawing management. Tools: get_detail_components, sync_detail_component_numbers, create_detail_component_type, list_family_symbols."
---

# Detail Component Sync Workflow (v3.5 Safeguard Mode)

## Core Principle

Ensure detail header type parameters (`detail number`, `sheet name`) match the sheet they belong to. **v3.5 ONLY updates elements whose type name already matches the sheet number** — shared/standard details are protected.

## Available Tools

| Tool | Purpose |
|------|---------|
| `get_detail_components` | Query detail components by family name filter |
| `sync_detail_component_numbers` | Batch sync parameters (safeguard mode) |
| `create_detail_component_type` | Create new type for a specific sheet |
| `list_family_symbols` | Browse available family symbols |

## Workflow 1: Create New Detail Header Type

1. Confirm sheet number (e.g., `ARB-D0921`)
2. Confirm detail name (e.g., `shallow ditch`)
3. `create_detail_component_type` → creates type named `ARB-D0921-{sheetName}-{detailName}`
4. Sets parameters: `detail number` = sheet number, `sheet name` = sheet name, `detail name` = user input

## Workflow 2: Batch Sync Parameters

1. `get_detail_components` with `familyName: "AE-numbering"` → audit
2. `sync_detail_component_numbers` → for each element:
   - Find which sheet it's on (via viewport mapping)
   - Check: does type name start with that sheet number?
   - YES → update `detail number` and `sheet name` parameters
   - NO → **SKIP** (protects shared standard details)

## Safeguard Logic (CRITICAL)

```
Type: ARB-D0921-...-shallow ditch
On sheet: ARB-D0921  → MATCH → Update parameters ✅
On sheet: AR-B-D02X2 → NO MATCH → Skip ⏭️ (standard detail referenced elsewhere)
```

## Type Naming Convention

```
{SheetNumber}-{SheetName}-{DetailName}
Example: ARB-D0921-Logistics Center Details-Shallow Ditch
```

## After Sheet Renumbering

v3.5 does NOT auto-rename types. Manual steps required:
1. Rename type in Revit (change sheet number prefix)
2. Run `sync_detail_component_numbers` to update parameters
3. OR create new type + switch elements manually

## Reference

See `domain/detail-component-sync.md` for full version history (v1.0→v3.5), FAQ, and edge cases.
