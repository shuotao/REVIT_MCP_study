# Beam top fit with partial floor overlap

`fit_beam_to_floor` defaults to full floor coverage. Set `extendOverhang: true` only when the user explicitly requests preserving the beam XY and extending the floor underside slope beyond the slab edge.

The overlap is sampled at 41 longitudinal stations and nine lateral positions. Each station needs at least three covered lateral positions. Least-squares longitudinal elevation fitting must stay within 1 mm at all covered samples; transverse slopes, stepped planes and uncovered longitudinal ends are rejected. Regenerated beam top samples within the slab use the actual underside; uncovered samples use the fitted plane. Solid intersection checks still reject floor penetration. Original XY endpoints remain fixed. Existing beam joins are removed and end joins disabled, as in strict mode.

Preview uses rollback; apply requires matching previewToken and explicit dryRun=false/apply=true. The token includes the extrapolation mode and sampled coverage. Results report OverlapVerifiedSamples, ExtendedVerifiedSamples and MissingInputSamples. Compilation does not replace live preview validation.

## Top surface
Set targetSurface=top to align the beam TOP to the floor TOP; bottom remains the default. Actual selected-surface samples drive the same single-slope fit and XY-preservation checks. Top mode intentionally permits beam/floor solid overlap, so the underside-only solid-intersection rejection is not used. It does not mean placing the beam bottom above the slab. Surface choice participates in the preview token and result.

## Edge extension
Set extendAlongEdge=true only for an explicit request to extend a nearby parallel straight slab edge. The chosen edge must overlap at least 500 mm and 20% of the beam length in plan, and lie within 600 mm of the beam width. Equally near edges with conflicting elevations are rejected. The edge supplies longitudinal end elevations; covered surface samples must still agree within 1 mm. Regenerated beam-top samples and unchanged XY are verified. Results include ReferenceEdge. Live rollback preview is required before apply.

