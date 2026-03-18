---
name: curtain-wall
description: "Design and apply curtain wall panel patterns with visual preview in Revit. TRIGGER when: user mentions curtain wall, panel pattern, facade design, glass panel, curtain grid, or panel arrangement. Tools: get_curtain_wall_info, get_curtain_panel_types, apply_panel_pattern."
---

# Curtain Wall Panel Pattern Design

## Workflow

1. **Inspect**: `get_curtain_wall_info` → get grid structure, panel count, dimensions
2. **Browse types**: `get_curtain_panel_types` → list available panel types with colors/transparency
3. **Design pattern**: Define a matrix pattern (rows × columns) with panel type assignments
4. **Preview** (optional): Use the HTML preview tool for visual confirmation
5. **Apply**: `apply_panel_pattern` → apply the matrix to the actual curtain wall

## Pattern Matrix Format

```json
{
  "wallId": 12345,
  "pattern": [
    ["Type A", "Type B", "Type A"],
    ["Type B", "Type A", "Type B"],
    ["Type A", "Type B", "Type A"]
  ],
  "repeat": true
}
```

The pattern repeats across the curtain wall grid if `repeat: true`.

## Design Tips

- Start by getting the actual grid dimensions with `get_curtain_wall_info`
- Match pattern matrix size to grid rows/columns
- Use contrasting panel types for visual interest
- Consider solar orientation when assigning transparent vs opaque panels

## Reference

See `domain/curtain-wall-pattern.md` for technical specifications and SOP.
