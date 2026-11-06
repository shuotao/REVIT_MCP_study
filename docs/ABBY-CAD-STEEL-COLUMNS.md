# Exact H steel columns from CAD

`create_columns_from_dwg` supports `exactTypeId`, `topLevelId`, `topOffsetMm`, `downwardLengthMm`, `expectedCount`, `dryRun`, `apply`.

Exact-type mode uses visible CAD geometry on the exact named layer. Closed, orthogonal, centrally symmetric 12-vertex H outlines are required. Flange direction comes from the longest outer edges, including square sections. Nested CAD instances are traversed with layer filtering. Other profiles and ambiguous geometry fail rather than being interpreted as rectangles.

The existing structural column type is used unchanged. Both constraint levels reference the selected top level; base offset equals top offset minus downward length. Each created solid is checked for CAD center, flange direction, section size and top/bottom elevations. Existing columns at the same position with overlapping vertical extents are skipped and reported.

Defaults: trial batch committed inside a rolled-back TransactionGroup. Only `apply:true,dryRun:false` persists the batch. Revit warnings/errors roll back the entire batch. Preview IDs are omitted. Run fresh preview after deployment before applying. Existing rectangular modes remain unchanged.

Limitations: H polylines only; no CAD arcs/line networks or arbitrary profiles. Geometry verification tolerance is 1 mm for elevations/centers and 2 mm for section size. Model verification is pending live deployment.
