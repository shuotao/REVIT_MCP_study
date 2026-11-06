# Coordinate points at a grid intersection

Tool: `align_coordinate_points_to_grids` (Abby).

Preview: `{"gridName1":"1","gridName2":"A","dryRun":true,"apply":false}`.
Apply: use the same grid names and the returned `previewToken`, with `dryRun:false, apply:true`.

Only exact, distinct, straight grids in the current host document are accepted. The XY intersection is computed from their lines; Z is the current project base point's internal elevation. Both points are moved to that position and its shared XYZ coordinates are set to zero. True north is retained. Pinned and survey clipped states are restored.

A single transaction inside a transaction group is used. Preview actually performs the change and rolls the group back; apply requires a matching document/location/geometry state fingerprint. All failures and Revit warnings roll back. DocumentChanged rejects additions, deletions and modifications outside the two points, active ProjectLocation and SiteLocation. Internal locations, grid curves, level project elevations and instance transforms are compared after commit. This includes CAD and Revit link instance transforms. Any mismatch rolls the group back.

This deliberately changes the host shared coordinate definition. Shared-coordinate labels and future shared-coordinate exports/placement may therefore differ, although existing model geometry and link placements must remain unchanged. It does not save the document, publish coordinates, modify linked files, or alter other add-ins such as TYBIM.

Narrow bookkeeping exception: the observed internal records named `最新反作用力` and `作用中的位置追蹤元素` may be modified only if, both before and after the transaction, they are exactly base `Element` objects with no category, location, bounding box or geometry. Their IDs, names and runtime classes are returned as `InternalBookkeepingUpdates`. This does not exempt model elements or disable geometry comparison. This exception still requires live preview validation.

Deployment and a successful live preview are required before claiming this is operational. Runtime calls must use the registered MCP tool, never injected code or a raw socket bypass.
