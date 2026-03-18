---
name: element-query
description: "Query, filter, and visualize Revit elements with the 3-phase workflow. TRIGGER when: user asks to find elements, query parameters, filter by category, color-code elements, list element properties, or explore project data. Tools: get_active_schema, get_category_fields, get_field_values, query_elements_with_filter, override_element_graphics, clear_element_override."
---

# Element Query & Visualization

## 3-Phase Query Protocol (MANDATORY)

### Phase 1: Exploration
`get_active_schema` → discover all categories and element counts in active view.
**ALWAYS run this first** to confirm the target category exists.

### Phase 2: Alignment
`get_category_fields` → get exact localized parameter names for a category.
**NEVER guess parameter names** — they vary by language and project template.

Optional Phase 2.5: `get_field_values` → get value distribution for a parameter (helps set filter conditions).

### Phase 3: Retrieval
`query_elements_with_filter` → query with multi-filter support.
- `field` MUST match names from Phase 2
- Operators: `equals`, `contains`, `less_than`, `greater_than`, `not_equals`
- Units typically in mm

## Visualization

After querying, color-code results:
1. `override_element_graphics` → set fill color, line color, transparency per element
2. `clear_element_override` → reset to default display

## Quick Reference

```
Simple query:     Phase 1 → Phase 3
Filtered query:   Phase 1 → Phase 2 → Phase 3
Value discovery:  Phase 1 → Phase 2 → Phase 2.5 → Phase 3
With colors:      Phase 1 → Phase 2 → Phase 3 → override_element_graphics
```

## Helper Tools

| Tool | Purpose |
|------|---------|
| `get_wall_types` | List wall types (search filter) |
| `change_element_type` | Change element type by ID (2023+ only) |
| `list_family_symbols` | Browse family symbols with name filter |
| `get_line_styles` | List available line styles |

## Common Scenarios

- **Room boundary check**: See `domain/room-boundary.md` for room boundary detection rules
- **Wall check**: See `domain/wall-check.md` for wall validation workflow

## Reference

See `domain/element-query-workflow.md`, `domain/element-coloring-workflow.md`, `domain/room-boundary.md`, `domain/wall-check.md` for full details.
