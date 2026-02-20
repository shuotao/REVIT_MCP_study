# Domain Analysis: Parking Clearance Check (B1F)

## 1. Goal Description
Check if the clearance of parking spaces meets the requirement of **>210cm**. If the clearance is insufficient (<= 210cm), override the graphic display of those parking spaces to **Red**.

## 2. Technical Approach
1. **Identify Target Elements**: 
   - Category: `Parking` (OST_Parking)
   - Level: Check only elements on the B1F level (or active view level).
   
2. **Calculate Clearance**:
   - Get the bounding box of each parking element.
   - Use `ReferenceIntersector` to raycast upwards from the center of the parking space.
   - Find the distance to the nearest obstruction (Ceiling, Beam, Duct, Pipe, Floor above).
   - Clearance = Distance from floor to obstruction.

3. **Validation Logic**:
   - `Clearance > 210cm` -> Pass (Color: Green or Default)
   - `Clearance <= 210cm` -> Fail (Color: Red)

4. **Visualization**:
   - Use `OverrideGraphicSettings` to set the projection surface pattern color to Red for failing elements.
   
## 3. Implementation Plan
- [ ] Create `MCP-Server/src/tools/parking_clearance.ts` (or similar JS script).
- [ ] Implement raycasting logic using Revit API.
- [ ] Implement graphic override logic.
