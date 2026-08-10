# Archicad Pilot: Quantity Takeoff Excel

## Scope

Reuse the evidence and workbook method from `quantity-takeoff-excel` with current-turn Archicad data. The first pilot covers Zone inventory and evidence-backed boundary quantities. Partition, finish-height, opening deduction, and scaffold results remain partial until every required Archicad relationship is verified.

## Backend Route

- Revit selected: return to `.claude/skills/quantity-takeoff-excel/SKILL.md` and use its existing Revit workflow.
- Archicad selected: continue below and replace Revit-specific record fields with the mapping in this document.
- Target ambiguous: ask which application and project to use.

## Evidence Contract

| Method field | Archicad pilot field | Guardrail |
|---|---|---|
| `RoomId` | `ZoneGuid` | Never label a GUID as ElementId. |
| `Number`, `Name` | Verified Zone number/name property | Discover exact property identifiers. |
| `Level` | `Story` | Confirm actual Story membership and elevation behavior. |
| `BoundaryElementIds` | `BoundaryElementGuids` | Accept only relationships returned for the current Zone. |
| `PerimeterM` | `BoundaryPerimeterM` | Compute only from verified geometry and units. |
| `Openings` | Related Door/Window GUIDs with evidence | Do not deduct by proximity. |
| `HeightM`, `HeightSource` | Verified Zone/element height plus source | Mark unresolved when ceiling/slab evidence is missing. |

## Archicad Workflow

1. Anchor one live project and port.
2. Discover and list all target Zones, including those with missing finish codes or zero calculated quantity.
3. Discover Zone detail/property reads and retain exact property identifiers, units, GUIDs, and warnings in an intermediate record.
4. Discover Zone boundary and related-element commands. Preserve every returned relationship; do not silently replace it with nearest-element inference.
5. Apply only formulas whose inputs are supported by current evidence. Mark each unsupported deduction or height source as unresolved rather than substituting Revit API behavior.
6. Build the workbook with source GUIDs, formulas, warning columns, fallback counts, and a selected-project/port provenance sheet.
7. Reconcile detail rows against totals and scan formulas for spreadsheet errors before delivery.

## Observed Capability Hints

Use these only as discovery hints:

| Intent | Observed command hint |
|---|---|
| List Zones or related element types | `elements_get_elements_by_type` |
| Read Zone details | `elements_get_details_of_elements` |
| Read Zone properties | `properties_get_property_values_of_elements` |
| Read connected boundary elements | `elements_get_zone_boundaries` |
| Read elements related to Zones | `elements_get_elements_related_to_zones` |

## Stop Conditions

- Boundary geometry or units are not explicit enough to compute a perimeter.
- Door/window ownership is inferred rather than returned as a verified relationship.
- Net height requires a ceiling/slab match that the discovered commands cannot establish.
- Workbook rows cannot retain source GUIDs and warnings.

## Live-Test Evidence

```text
backend: archicad
canonical_skill: quantity-takeoff-excel
domain_method: domain/quantity-takeoff-excel.md
adapter_reference: pilot-quantity-takeoff-excel.md
project_port: <current port>
zones_read: <count>
supported_formulas: <list>
unresolved_inputs: <list/count>
verification: detail/total reconciliation and spreadsheet error scan
```

## Reference

- [Terminology and boundary map](revit-archicad-terminology.md)
- `domain/quantity-takeoff-excel.md`
- `domain/tool-capability-boundary.md`
