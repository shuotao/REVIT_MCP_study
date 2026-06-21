---
description: How to perform a corridor building code review (width check) in Revit
---

# Corridor Code Review Workflow

This workflow describes the logic for checking corridor width compliance and creating annotation dimensions in Revit using the MCP tools.

## 1. Preparation
Ensure Revit is open and the correct floor plan view is active.
- Use `get_active_view` to get the current view ID.

## 2. Identify Corridor
- If a specific room is known, use `get_room_info` to get its **Center point only**.
- **⚠️ WARNING**: Do NOT use BoundingBox directly for dimensioning!
  - BoundingBox is approximate and may include wall thickness inconsistently
  - BoundingBox does not represent actual wall face positions
  - BoundingBox should only be used for initial search location
- If not, use `select_element` to ask the user to select the corridor, or `query_elements` filter by 'Room'.

## 3. Get Wall Data (Critical Step - MUST DO)
**Priority Order:**
1. **FIRST**: Use `query_walls_by_location` with corridor center point
2. **SECOND**: Extract actual wall face coordinates (Face1/Face2)
3. **LAST**: Only use BoundingBox as reference/validation if needed

**Why this order matters:**
- `query_walls_by_location` returns precise wall face positions
- Wall Face1/Face2 represent actual interior surfaces (net width)
- Wall LocationLine represents structural centerline
- BoundingBox is a rough estimate and should not drive dimensions

**Implementation:**
- Use `query_walls_by_location` with the corridor's center `(x, y)` from `get_room_info`
- Set `searchRadius` appropriately (e.g., 3000-5000mm)
- Filter the results to find the boundary walls (usually the two closest parallel/perpendicular walls)
- Extract Face coordinates facing the corridor interior

## 4. Determine Dimension Points (Use Wall Data)
From the wall data, identify two sets of coordinates:
1.  **Net Width (Effective Width)**: Use `Face1` or `Face2` of the walls (the faces facing the corridor). This is used for code compliance.
2.  **Structural Width (Centerline)**: Use the `LocationLine` of the walls.

## 5. Create Dual Dimensions
Create two separate dimension lines using `create_dimension` to provide complete context.
**Do not** make them overlap.

- **Dimension 1 (Code Compliance)**:
    - Coordinates: Wall Interior Faces.
    - Offset: Smaller value (e.g., 1200mm).
- **Dimension 2 (Reference)**:
    - Coordinates: Wall Centerlines.
    - Offset: Larger value (e.g., 2000mm).

## 6. Compliance Check
Compare the **Net Width** against regulations:
- **Japan (Building Standards Act)**:
    - Single-sided rooms: ≥ 1.2m
    - Double-sided rooms: ≥ 1.6m
- **Taiwan (Building Technical Regulations)**:
    - Single-sided rooms: ≥ 1.2m
    - Double-sided rooms: ≥ 1.6m

## Example Tool Chain (Correct Priority)
```javascript
// Step 1: Get view and room location
const view = await get_active_view();  // -> ViewId
const room = await get_room_info({ roomId: XXX });  // -> CenterX, CenterY

// Step 2: Query ACTUAL walls (DO THIS FIRST for dimensions)
const walls = await query_walls_by_location({
  x: room.CenterX,
  y: room.CenterY,
  searchRadius: 3000,
  level: '2FL'
});  // -> Wall Face1, Face2, LocationLine

// Step 3: Use wall face coordinates for dimensioning
// Find the two walls facing the corridor
const wall1_face = walls[0].Face1;  // or Face2, whichever faces corridor
const wall2_face = walls[1].Face1;

// Step 4: Create dimensions using ACTUAL wall faces
await create_dimension({
  viewId: view.ElementId,
  startX: X,
  startY: wall1_face.Y,  // ✓ Use wall face
  endX: X,
  endY: wall2_face.Y,    // ✓ Use wall face
  offset: 1200
});  // -> Net Width (Code Compliance)

// Step 5 (optional): Add centerline dimension for reference
await create_dimension({
  viewId: view.ElementId,
  startX: X,
  startY: walls[0].LocationLine.StartY,  // Centerline
  endX: X,
  endY: walls[1].LocationLine.StartY,    // Centerline
  offset: 2000
});  // -> Structural Width (Reference)

// ❌ WRONG - Don't do this:
// startY: room.BoundingBox.MinY  // BAD!
// endY: room.BoundingBox.MaxY    // BAD!
```

## Key Takeaways
- **BoundingBox**: Initial location only, NOT for dimensions
- **query_walls_by_location**: Primary source for dimension coordinates
- **Wall Faces**: For net/effective width (code compliance)
- **LocationLine**: For structural/centerline reference
