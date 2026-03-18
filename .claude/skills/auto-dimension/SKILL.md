---
name: auto-dimension
description: "Automatically create dimensions in Revit using Ray-Casting or BoundingBox methods. TRIGGER when: user mentions dimension, annotation, measurement, room clearance, MEP clearance, net width, or asks to dimension rooms/corridors. Tools: create_dimension_by_ray, create_dimension_by_bounding_box, get_room_info."
---

# Auto Dimension Workflow

Automatically annotate room/space dimensions in Revit using two methods:

## Method Selection

| Scenario | Method | Tool |
|----------|--------|------|
| Regular rectangular room | Ray-Casting | `create_dimension_by_ray` |
| L-shaped or complex room | BoundingBox | `create_dimension_by_bounding_box` |
| MEP clearance check | Ray-Casting | `create_dimension_by_ray` |

## Ray-Casting Flow

1. Get room center: `get_room_info` → extract `Location` point
2. Fire rays in X+/X- directions → detect wall faces → create X-axis dimension
3. Fire rays in Y+/Y- directions → detect wall faces → create Y-axis dimension

```
origin → direction (X+)  →  hits right wall  ─┐
origin → counterDirection (X-) → hits left wall ─┤→ X dimension
```

### Key Parameters
- `viewId`: Target plan view (MUST be a plan view, NOT 3D)
- `origin`: Room center point (from `get_room_info`)
- `direction` / `counterDirection`: Ray vectors (e.g., `{x:1,y:0,z:0}` and `{x:-1,y:0,z:0}`)

## BoundingBox Flow (Fallback)

1. Get room BoundingBox via `create_dimension_by_bounding_box`
2. Specify axis (`X` or `Y`) and offset (default 500mm)
3. System creates DetailCurves at min/max bounds and dimensions between them

## Critical Rules

- **NEVER** create 2D dimensions in a 3D view — always check `ActiveView` type first
- Dimension line position = `(BoundingBox.Max + BoundingBox.Min) / 2` center trajectory
- Ray-Casting internally uses `ReferenceIntersector` which requires a 3D view in C#, but the dimension is placed on the plan view
- If a column blocks the ray, it dimensions to the column (correct for MEP net clearance)

## Reference

See `domain/auto-dimension-workflow.md` for full details and Mermaid diagrams.
