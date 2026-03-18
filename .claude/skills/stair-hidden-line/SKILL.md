---
name: stair-hidden-line
description: "Automatically draw hidden dashed lines for stairs behind section cut planes in Revit. TRIGGER when: user mentions hidden stairs, stair dashed lines, section view stairs, stair visualization, or assembled stair hidden edges. Tools: trace_stair_geometry, create_detail_lines, get_line_styles."
---

# Stair Hidden Line Visualization

Draw dashed detail lines for stair treads hidden behind stringers in section views.

## When to Use

- Section views where assembled stairs (with stringers) hide the tread profiles
- RC stairs are automatically EXCLUDED (no side obstruction)

## Workflow

1. Open the target **section view** in Revit
2. `trace_stair_geometry` → analyzes all `StairsRun` elements:
   - Filters: only `Assembled` stairs (checks `FamilyName` for "assembled" or "combined")
   - Extracts 3D geometry edges
   - Computes depth relative to section cut plane
   - Rejects foreground stairs (minDepth <= 0.05 ft)
   - Keeps only first-row edges (depth <= minDepth + 2.5 ft)
   - Preserves short segments (< 0.65 ft) as nosing/profile details
3. `get_line_styles` → find "dashed (very dense)" style ID
4. `create_detail_lines` with coordinates from step 2 + styleId from step 3

## Geometry Logic

```
Section Cut Plane
    |
    |  depth=0        depth > 0 (behind)
    |  ← foreground    background →
    |
    |  SKIP            DRAW (first row only)
```

- **Depth calculation**: `(viewOrigin - edgeMidpoint) · viewDirection`
- **First row filter**: `depth <= minDepth + 2.5ft` (~75cm tolerance)
- **Profile detection**: horizontal (treads), vertical (risers), or short segments < 20cm (nosing profiles)

## Recommended Line Style

`ID: 11911982` — "dashed very dense" (`虛線(極密)`)

Use `get_line_styles` to confirm the correct ID in your project.

## Reference

See `domain/stair-hidden-line-workflow.md` for full details.
