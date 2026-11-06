# Steel beams from elevation CAD

Extension of the existing `create_beams_from_dwg` tool, `mappingMode: elevationSteel`; no extra tool registration or separate add-in.

User workflow (2026-10-28): active section `LINE_C_構架立面`; `CW-PN_STG` supplies beam-top lines; `CW-PN_T_STG` supplies exact beam marks. The plan centerline is grid `C`. Read PDF sheet 1919 to resolve each mark, create missing full-section types, and write `編號` in both Constraints and Text using `Definition.GetGroupTypeId()`. BOX thickness uses `牆標稱厚度`, never `牆設計厚度`.

The independently transcribed table is [steel-schedule-1919.csv](steel-schedule-1919.csv). Source: user PDF `●鋼構尺寸表1919.pdf`, one scanned page, checked visually at enlarged resolution. 60 steel beam entries; SB13 is absent. SB41 has two thicknesses, 30 and 50 mm: do not force this into a single-thickness BOX family. Grades and CSB mortar notes are recorded for context; this command sets section dimensions and numbers, not material grades or mortar infill.

## Parameters and safeguards

- `familyName` remains required by the existing schema, but elevation mode resolves each `steelRules[].baseTypeId` independently (so one batch supports multiple families).
- `centerGridName`, `layerName`, `textLayerNameX`; optionally `importInstanceUniqueId` to disambiguate visible linked CADs. Linked source required.
- `cadUnits`: mm/cm/m; omitted means supported nonzero DWG INSUNITS. Never infer unitless CAD. `pythonExecutable` must have ezdxf and access to ODA.
- Omit `steelRules` on the first read-only discovery to receive transformed/projected marks, candidate endpoints, and steel family length parameters. No Revit transaction occurs in preview.
- Rules contain exact `numbers`, `sectionKind` (BH/RH/BOX), `baseTypeId`, `typeName`, and complete `dimensionsMm` keyed by exact family parameter names. Check both actual dimensions and writable parameters; no existing type is modified.
- Only horizontal beam-top segments at least 300 mm are supported in this release. Sloped/vertical lines are reported as skipped. Do not merge across gaps or infer a beam from other layers.
- Model XY is projected onto the vertical plane through the straight center grid; model Z is the transformed CAD Z. Section right direction must parallel that grid. Nearest level at/below the beam top becomes the reference level, with explicit top offset.
- Label matching uses the same elevation bay (within endpoints), vertical distance <=1500 mm by default, and a 100 mm nearest/second-nearest ambiguity margin. A label cannot create multiple beams. Exact SB/CSB/SD number tokens only; unparsed mixed text requires inspection, not guessing.
- Preview fingerprints document, view, CAD, grid, arguments and planned rows. Apply requires the same settings with `dryRun:false`, `apply:true`, and `previewToken`.
- Single transaction inside a rollback group creates types, beams and both group-specific marks. Missing/read-only/ambiguous number parameters roll back the whole batch. Existing beam overlap is skipped. Disallow both end joins, center in Y, justify top in Z, verify type dimensions, marks, XY endpoints and physical bounding-box top after commit; any mismatch rolls the group back.

## Validation status

Offline tests cover floor/bay separation, reversed direction, ambiguity, cutoff and boundaries. Live geometry, family and mark validation requires deployment and read-only preview in Revit before applying. Do not claim beams exist before receiving a successful creation result.
