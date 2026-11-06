# Sloped beam additions

Use `create_beam_additions` with `mode: "slopedSolid"`, exactly one `sourceBeamIds` entry, and an explicit `floorId`. This creates a Structural Framing **DirectShape**, not a parameterized beam FamilyInstance. Existing rectangular mode retains its required depth, level and offset inputs.

The original beam is unchanged. Its actual solid must have a complete rectangular horizontal top; the addition bottom follows this face. The top follows the closest unambiguous planar floor top. `extendFloorPlane: true` explicitly permits extrapolation outside floor coverage; the chosen plane must be within 600 mm of the beam footprint center and every covered sample must agree within 1 mm. Folded or conflicting planes and nonpositive corner thicknesses are rejected.

The preview builds and regenerates the solid in a rolled-back transaction. It verifies analytical and regenerated volume, sampled bottom/top surfaces, and no intersection with the original beam. It writes the exact Text-group 編號 parameter (default 增築); missing/read-only/ambiguous parameters abort. The material comes from the source beam's assigned material IDs. A fresh geometry-derived preview token is mandatory for `apply: true, dryRun: false`. A source-beam/floor application key prevents repeated additions.

The result reports min/max depth, width, length, volume, covered/extrapolated samples and verification error. A committed result includes CreatedElementId; rolled-back previews never report an existing created element. Floor overlap is intentional for the requested floor-top target. Live preview is still required after deployment; successful compilation alone does not validate model geometry.
