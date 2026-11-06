# CAD continuous RC walls

Tool: `create_walls_from_dwg`. This Abby extension reads straight planar double lines from a selected CAD layer in the active base-level plan. Geometry uses Revit instance transforms and internal feet converted to mm; CAD coordinates are not rescaled by guessed units.

Required: `baseLevelId`, `topLevelId`. Defaults: `layerName=WALL`, both offsets 0 mm, tolerance 2 mm, maximum opening gap 3000 mm, `dryRun=true`, `apply=false`. Specify `importInstanceUniqueId` when multiple CAD instances exist.

Only existing Basic Wall types beginning `RC-` with fixed compound width are eligible. Match actual `WallType.Width`, not the type name alone. Missing or multiple width matches skip. Placement is on the detected centerline, structural, with explicit base/top constraints.

Collinear equal-width paired runs merge across gaps only when geometry from explicit opening layers covers the gap within the wall strip. Default layers: DOOR, DOOR-GLAZ, DOOR-HDLN, DOOR-OPNG, WINDOW. No doors, windows or openings are created. Unsupported curved walls and adjacent candidate regions are excluded; unsupported geometry that cannot be localized stops the operation. Single lines, thickness transitions and incomplete double lines are not inferred. Complex symbols may remain fragmented and require preview review.

Preview returns candidate endpoints, width, matched type, bridges, skip reasons and `PreviewToken`. Apply requires `dryRun=false`, `apply=true`, identical settings and fresh `previewToken`. Existing wall overlaps skip whole candidates. Local creation failures roll back the affected wall; a fatal error rolls back the whole group. A successful batch is one Undo operation.

Validation: `dotnet run --project tests/CadWalls.Tests/CadWalls.Tests.csproj`; `node --test MCP-Server/tests/cad-walls.test.mjs`. Live drawing validation is required after deployment. This is not a guarantee that every CAD wall can be inferred.

## Explicit width-to-type selection
Optional wallTypeOverrides accepts entries {widthMm, wallTypeId}. Each ID must be an eligible RC basic type whose actual width matches within tolerance. Overlapping override ranges are rejected. For matching CAD widths, the explicit type resolves otherwise ambiguous candidates; other widths retain automatic matching. Settings participate in the preview token. Existing overlap checks and level constraints remain unchanged.

## Automatic type consistency checks
Automatic CAD-width matching excludes RC types whose parseable RC-<number>cm name differs from actual width by more than 0.1 mm. TypeWarnings reports type IDs, named/actual thickness and affected wall instance count, with a prompt to ask the user whether to correct the type. Never modify a type automatically. A consistent alternative remains eligible without a manual override. Multiple remaining matches still skip. User confirmation is handled by the calling assistant; no Revit modal is opened by this command.

## Lining walls: preserve the CAD interior face
Set `lining` with `wallTypeId`, `cadWidthMm`, `expectedWidthMm`, and exact `referenceWallTypeNames`. Optional `maxReferenceGapMm` defaults to 1000 mm. This mode directly creates the selected fixed-width Basic Wall as nonstructural; it is mutually exclusive with `wallTypeOverrides`.

A parallel reference wall must cover at least 80% of the CAD run (union of intervals, not summed duplicates). Exactly one reference side must qualify. Missing or opposite-side matches skip with diagnostics. The actual target width must match `expectedWidthMm` within 0.1 mm. The centerline shifts half the width difference toward reference walls: CAD 100 mm and actual 104 mm shifts 2 mm, retaining the interior face and adding all 4 mm toward the reference wall. Thinner target types are rejected.

Preview includes reference IDs, actual width, center shift, placed endpoints and fixed interior plane. Collision checks use the placed run and actual width. New wall endpoint joins are disabled to avoid coupled movement. Interior faces and widths are verified before and after individual transaction commits; existing straight-wall axes must remain unchanged before group assimilation, otherwise the batch rolls back. An entire batch can be undone together. Use a fresh preview token before applying; inspect skipped regions rather than guessing their orientation.
