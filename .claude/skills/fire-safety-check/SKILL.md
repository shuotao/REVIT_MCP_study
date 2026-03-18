---
name: fire-safety-check
description: "Execute fire safety compliance checks in Revit: fire rating visualization, corridor fire analysis, and exterior wall opening distance checks. TRIGGER when: user mentions fire rating, fire resistance, corridor fire, escape route, exterior wall openings, property line distance, Article 45, Article 110, fire compartment, or fire safety review. Tools: override_element_graphics, check_exterior_wall_openings, query_elements_with_filter."
---

# Fire Safety Compliance Check

## Sub-Workflows

### 1. Fire Rating Visualization

Color-code walls by fire rating parameter:
1. `get_category_fields` for Walls → find fire rating parameter name
2. `get_field_values` → list all rating values in project
3. `query_elements_with_filter` → filter walls by rating
4. `override_element_graphics` → apply colors per rating level

| Rating | Color |
|--------|-------|
| 1hr | Green |
| 2hr | Yellow |
| 3hr | Red |
| Unrated | Gray |

### 2. Corridor Fire Analysis

Analyze corridor widths and escape routes:
1. Query rooms with corridor keywords: `走廊`, `Corridor`, `廊道`, `通道`, `廊下` (Japanese)
2. Check net width against minimum requirements
3. Verify fire compartment boundaries
4. Color-code results on plan view

**Language tolerance**: Always search with multiple keywords (zh/en/jp) per Lesson L-001.

### 3. Exterior Wall Opening Check (Articles 45 & 110)

`check_exterior_wall_openings` performs:
- Read PropertyLine (site boundary) geometry
- Calculate distance from each wall opening to boundary
- **Article 45**: Opening distance ≥ 1.0m from boundary, ≥ 2.0m between buildings
- **Article 110**: Fire separation requirements by distance
- Auto-colorize: Red = violation, Orange = warning, Green = pass
- Create dimension annotations for violations

Parameters:
- `checkArticle45`: boolean (default true)
- `checkArticle110`: boolean (default true)
- `colorizeViolations`: boolean (default true)
- `exportReport`: boolean + `reportPath` for JSON output

## Reference

See `domain/fire-rating-check.md`, `domain/corridor-analysis-protocol.md`, `domain/exterior-wall-opening-check.md` for full details.
