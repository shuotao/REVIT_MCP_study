---
name: building-compliance
description: "Execute building code compliance reviews: daylight area, floor area ratio (FAR), and parking space checks. TRIGGER when: user mentions daylight, natural lighting, floor area, FAR, building coverage, parking, parking clearance, parking space review, or regulatory compliance review. Tools: get_room_daylight_info, query_elements_with_filter, get_rooms_by_level."
---

# Building Code Compliance Review

## Sub-Workflows

### 1. Daylight Area Check (Article 41)

Verify room natural lighting compliance per Taiwan Building Technical Regulations:
1. `get_room_daylight_info` → returns window areas and room floor areas
2. Calculate: effective daylight area ÷ floor area ≥ required ratio
3. Flag non-compliant rooms
4. Visualize results with color overrides

**Key rules:**
- Residential rooms: daylight area ≥ 1/8 of floor area
- Only count windows facing outdoors (not interior windows)
- Measure from window sill to ceiling for effective opening

### 2. Floor Area Review (FAR Check)

Review floor area ratio compliance:
1. `get_rooms_by_level` → collect all rooms per floor
2. Calculate gross/net floor areas per level
3. Compare against allowed FAR from zoning
4. Generate compliance summary

**Calculation notes:**
- Exclude: mechanical rooms, stairs, elevators (per regulations)
- Include: balconies at 50% if enclosed
- Underground floors: different calculation rules

### 3. Parking Space Review

Verify parking dimensions and clearance:
1. Query parking-related elements
2. Check standard dimensions (2.5m × 5.5m minimum for standard)
3. Check clearance around columns and walls
4. Verify accessible parking requirements

**Standards:**
- Standard: 2500mm × 5500mm
- Accessible: 3500mm × 5500mm
- Aisle width: 5500mm (two-way)

## Reference

See `domain/daylight-area-check.md`, `domain/floor-area-review.md`, `domain/parking-space-review.md`, `domain/parking-clearance-check.md` for full details.
