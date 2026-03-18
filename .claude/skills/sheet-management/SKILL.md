---
name: sheet-management
description: "Manage Revit sheets, viewports, titleblocks, and dependent views. TRIGGER when: user mentions sheets, titleblock, viewport, sheet numbering, renumber, dependent view, crop view, or grid-based view splitting. Tools: get_all_sheets, get_titleblocks, create_sheets, auto_renumber_sheets, get_viewport_map, calculate_grid_bounds, create_dependent_views."
---

# Sheet & Viewport Management

## Available Tools

| Tool | Purpose |
|------|---------|
| `get_all_sheets` | List all sheets with numbers and names |
| `get_titleblocks` | List available titleblock family types |
| `create_sheets` | Batch create sheets with titleblock |
| `auto_renumber_sheets` | Fix `-1` suffix conflicts + semantic reorder |
| `get_viewport_map` | Get view-to-sheet mapping |
| `calculate_grid_bounds` | Calculate BoundingBox from grid intersections |
| `create_dependent_views` | Create dependent views with crop regions |

## Workflow 1: Batch Create Sheets

1. `get_titleblocks` → note the `titleBlockId`
2. `get_all_sheets` → verify no number conflicts
3. `create_sheets` with `titleBlockId` + sheet array `[{number, name}]`

## Workflow 2: Fix Sheet Numbering

1. `get_all_sheets` → identify `-1` suffix sheets
2. `auto_renumber_sheets` → executes:
   - Phase 0: Emergency recovery of `_MCPFIX` remnants
   - Chain displacement: source → target, displaced → next available
   - Semantic sort: reorder by `(一)/(二)/(三)` or `(1/3)/(2/3)/(3/3)` within continuous number groups
   - Two-pass execution: temp names → final names (avoids conflicts)

## Workflow 3: Grid-Based Dependent View Crop

1. `calculate_grid_bounds` with grid names (`xGrids`, `yGrids`) + `offset_mm`
2. `create_dependent_views` with parent view IDs + bounding box from step 1
3. System auto-numbers: `{parentName}-1`, `{parentName}-2`, etc.

### Grid Bounds Logic
- 2 grids on same axis → use their coordinates as range ± offset
- 1 grid on axis → center ± offset (fallback)
- Z axis always set to extreme values (-100m to +100m)

## Sheet Numbering Convention

```
[ProfessionCode]-[SheetType][SerialNumber]
Example: ARB-D0408 (Architecture-Detail-0408)
```

## Reference

See `domain/sheet-viewport-management.md` and `domain/dependent-view-crop-workflow.md` for full details.
