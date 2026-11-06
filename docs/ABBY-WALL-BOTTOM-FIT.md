# Wall bottom fitted to beam top

Use `join_wall_tops` with `wallBottomToBeam: {wallId, beamId}`. This mode is mutually exclusive with join-order modes. It edits the existing wall profile rather than creating a dynamic attachment or replacing the wall.

Supports straight, vertical basic walls with no existing edited profile, hosted inserts, pinning, grouping or top/base attachment. Source wall must have a flat top and a rectangular four-edge profile. The beam top must cover the wall width and length, with one longitudinal plane and no transverse slope. This mode trims the bottom within the original wall height only.

A TransactionGroup contains profile creation, SketchEditScope and geometry verification. Dry-run rolls the whole group back. Apply requires dryRun=false, apply=true and a fresh previewToken. Any verification failure rolls back. Verification samples 205 points across the wall, requires bottom-to-beam error <=1 mm, preserves the wall top and location curve, and does not edit beam geometry. Future beam changes do not automatically update this profile.
