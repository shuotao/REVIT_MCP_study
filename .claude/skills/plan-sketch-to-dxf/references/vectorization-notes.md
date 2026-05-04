# Vectorization Notes

This skill targets color-coded architectural sketches where wall centerlines are drawn in saturated colors and columns are drawn in black. It is optimized for quick CAD cleanup, not legal construction documentation.

## Default interpretation

- Magenta or pink strokes: wall centerlines on `WALL_MAGENTA`.
- Cyan or blue strokes: wall centerlines on `WALL_CYAN`.
- Black blobs: columns on `COLUMNS_BLACK`, rendered as square outlines.
- Column crosshairs: `COLUMN_CENTERLINE`.

## Cleanup strategy

The script thresholds colors, bridges wall strokes through column gaps, detects mostly horizontal and vertical segments, clusters nearby axes, snaps endpoints to nearby intersections, and writes simple DXF entities.

Use preview images to catch obvious errors. If input colors, scan quality, or geometry differ materially from the expected sketch style, adjust thresholds or rerun in a less aggressive mode.
