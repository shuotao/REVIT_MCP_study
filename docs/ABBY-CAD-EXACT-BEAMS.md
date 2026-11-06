# Exact-type CAD beams at specified top elevation

Use `create_beams_from_dwg` with `exactTypeId`, its `familyName`, `referenceLevelId`, `zOffsetMm` and `expectedCount`. All parsed CAD candidates, including diagonal and differing drawn widths, use the explicitly chosen existing type without modifying it. CAD extraction remains the existing double-line algorithm; preview is a candidate interpretation, not a validated structural support layout.

The specified level is the reference level, Z justification is Top, Y justification Center, end elevation offsets zero. End joins are disabled to preserve CAD endpoints. Collinear overlapping existing beams at the target top and overlapping same-batch candidates are skipped and reported. No labels or marks are written.

`dryRun:true` (default) creates and verifies actual solids, commits within a rolled-back TransactionGroup. `apply:true,dryRun:false` requires the fresh matching previewToken. Geometry, type, level, top elevation and duplicate state participate in the token. Exceptions and Revit failure messages roll back the batch, except the specific Warning-severity InaccurateBeamOrBrace notice: it is recorded in WarningCount/Warnings and dismissed while retaining CAD direction. No other warnings or errors are suppressed. Actual solid top and lateral center plus axis length and reference level are verified. Runtime validation pending deployment.

## Continuous paths and replacement

`mergeContinuous:true` groups near-collinear CAD candidates and uses the outermost endpoints, bridging intervening gaps. This requires an explicit request for a continuous beam. `mergeToleranceMm` bounds lateral grouping and resulting-axis deviation (default 50 mm, maximum 100 mm). Distinct parallel paths remain separate.

`replaceElementIds` permits an atomic replacement of explicitly identified earlier beams. Each must match an original CAD candidate, exact type, reference level and physical top; pinned/grouped beams are rejected. Deletion of other dependent elements aborts the transaction. Preview rolls back both deletion and construction; apply requires the matching token. The result reports ContinuousPaths and Replaced.

Duplicate elevation detection now uses actual solid top instead of the element bounding box, which may include nonphysical family geometry.

Replacement cleanup permits only preidentified SketchPlane dependents automatically removed with old beams; tags, dimensions and other dependent elements still abort the transaction and are reported by type/ID.

A native dependent exposed as base Element may also be cleaned up only if it has no name, category, type, geometry or dependent children. This is checked inside the rolled-back preview before any persistent replacement.

## CAD width-constrained pairing

`cadWidthMm` constrains double-line pairing before candidates consume source lines. It is independent of the Revit type width: e.g. a 300 mm CAD outline may map to a 350 mm steel type. `cadWidthToleranceMm` defaults to 5 mm (0..20 mm). Width-constrained pairing requires stricter parallelism and checks the distance of both endpoints, preventing 900/1200/1500 mm beam gaps from becoming candidates. Supported by preview_dwg_beams and exactTypeId creation. expectedCount refers to the constrained preview count.

`replaceMergedPaths:true` supports undoing a prior continuous-path interpretation by rebuilding original width-constrained CAD segments. It requires mergeContinuous=false and explicit replacement IDs. Each old beam must match a merged path, selected type and top elevation. Preview restores old beams; apply atomically replaces them. It does not bridge empty CAD spans.

## Restore contiguous layer edges
With cadWidthMm, restore collinear overlapping or touching edges on the exact requested layer before pairing (0.1 mm numerical tolerance). Do not bridge empty intervals. Enumerate supported pairs because one continuous side can face several separate opposite edges. Other layers do not trim or extend candidates. This differs from opt-in mergeContinuous, which bridges gaps.
replaceOriginalSegments=true allows explicit replacement IDs matching the previous unnormalized pairing, with the existing type/level/top and dependency safeguards. Requires nonmerged exact mode and CAD width.
