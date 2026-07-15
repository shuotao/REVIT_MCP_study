---
name: quantity-takeoff-excel
description: "Create or review auditable Revit quantity-takeoff Excel reports for partition walls, baseboards, interior wall finishes, room schedules, and scaffolding. Use when the user asks for 數量計算、算量、輕隔間 Excel、踢腳 Excel、內牆粉刷 Excel、空間表更新 Excel、施工架數量、room perimeter、opening deductions、net height, or wants an existing pyRevit takeoff method reused through RevitMCP."
---

# Revit Quantity Takeoff Excel

Use current-turn Revit data and produce an auditable calculation, not an unexplained total.

## Route The Request

Choose the calculation family before querying data:

| Request | Formula basis |
|---|---|
| Partition wall | wall length × report height - openings hosted by that wall |
| Baseboard | room Finish perimeter - widths of openings hosted by boundary walls |
| Interior wall finish | room Finish perimeter × net height - opening areas hosted by boundary walls |
| Room schedule update | room number/name/Finish perimeter/area, updated in place |
| General interior scaffold | room perimeter × height |
| Stair/elevator finish scaffold | room length × width × height |
| Exterior scaffold | user-verified detail-line or filled-region perimeter × specified height |

Use a more specific skill when one exists, especially `partition-takeoff` or `scaffold-takeoff`. Apply this skill for the shared data, Excel, and validation rules.

## Query Current Revit State

1. Confirm that RevitMCP tools are available and Revit is connected.
2. Re-query the current document, rooms, levels, relevant walls, ceilings, floors, doors, and windows in this turn.
3. Preserve ElementIds and source fields in an intermediate record before creating Excel rows.
4. If the current client cannot call Revit tools, state that limitation. Do not claim to have calculated the open project.

Keep at least these room fields:

```text
RoomId, Number, Name, Level,
BoundaryLoops, BoundaryElementIds, PerimeterM, AreaM2,
FinishCodes, HeightM, HeightSource,
Openings, Warnings
```

## Apply Boundary Rules

Use `GetBoundarySegments(Finish)` for baseboards, wall finishes, and room schedule perimeter. Sum every valid boundary loop and preserve each boundary segment ElementId.

Do not silently substitute Revit `ROOM_PERIMETER`, which may represent a centerline or another boundary convention. Use it only as a labeled fallback when Finish boundary data is unavailable.

List every valid room even when it has no finish code or quantity. A complete room schedule and a material quantity schedule are separate requirements.

## Deduct Openings Only With Evidence

For partition walls, deduct an opening only when `Opening.Host.Id` equals the calculated wall ElementId.

For baseboards and interior wall finishes, deduct an opening only when `Opening.Host.Id` is present in the room's `BoundaryElementIds`. This excludes doors on internal toilet or service partitions that do not form the calculated room boundary.

Never infer host ownership from nearest-wall distance, same level, room coordinates, or bounding-box proximity. Put missing hosts, missing dimensions, and ambiguous room relations into warnings.

Use the Revit door/window type name in Excel, such as `D5a-120x220 cm`; do not replace it with a family name or reconstructed label.

## Resolve Net Height

For interior wall finish, follow this evidence chain and retain `HeightSource`:

1. Match actual `OST_Ceilings` to the room using sample points inside the room/ceiling bounding-box intersection.
2. Prefer the ceiling `Height Offset From Level`:

```text
net height = ceiling level elevation + ceiling offset - room base elevation
```

3. If the offset is unavailable, use the matched ceiling geometry bottom as a labeled fallback.
4. If the room has no actual ceiling, use:

```text
net height = upper level elevation - representative upper slab thickness - room base elevation
```

5. Use explicit room/Revit height only as a final labeled fallback.

Keep full precision during calculation. Round only report display values. Never derive a cosmetically precise height by dividing area by perimeter.

For partition walls, use the separate wall-height rules: full-height walls normally reach the upper slab underside; attached, sloped, low, lining, and non-full-height walls retain their validated Revit geometry or wall height.

## Map Finish Codes And Names

Split multi-value B/W finish parameters on `+`; treat blank and `-` as no material. Create columns only for codes actually used by room instances.

When material names come from a selected material-board family, remove prefixes such as `B1=` or `W2=` and retain the material description. Do not hardcode project-specific family names; query or ask for the family selection.

## Build An Auditable Workbook

- Derive the title and default filename from the current Revit file name.
- Preserve formulas such as `perimeter - SUM(openings)` and `perimeter × height - SUM(opening areas)`.
- Add a final merged quantity-total label and `SUM` formulas for each material or wall-type column.
- Omit wall/material columns with no actual instances, but keep all room rows.
- Use OpenXML for a new `.xlsx` when possible. Use Excel COM only when an existing `.xls/.xlsx` workbook and its cross-sheet formulas must be updated in place.
- For in-place updates, run dry-run, create a timestamped backup, then write. Preserve stable room identity and existing formula structure.

## Validate Before Delivery

1. Check one room with an internal toilet/service partition opening; it must not be deducted from the outer Finish boundary quantity.
2. Check one room with a real ceiling and one without a ceiling; confirm their height sources differ correctly.
3. Confirm all valid rooms are present, including zero-quantity rooms.
4. Compare detail formulas with material/wall-type totals.
5. Scan for `#REF!`, `#VALUE!`, `#NAME?`, and `#DIV/0!`.
6. Report counts for boundary fallbacks, height fallbacks, deducted openings, skipped openings, missing sizes, missing hosts, and missing heights.

Do not report completion when unresolved warnings could materially change the quantity.
