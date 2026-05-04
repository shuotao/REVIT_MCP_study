#!/usr/bin/env python3
"""Rescale a vectorized DXF + geometry.json so coordinates land on real-world mm.

The original `vectorize_plan_to_dxf.py` writes DXF coords in raw image pixels
and bakes a hardcoded `refMmDistance = 10000` into geometry.json. When the user
knows the actual size of the building (or the actual C1 to C2 distance), this
script linearly rescales the DXF and patches geometry.json so downstream Stage
5 (px to mm conversion) produces correct Revit coordinates.

Usage (one of --ref-mm / --target-width-mm / --target-height-mm required):

    python rescale_geometry.py \
        --geometry output/sketch_geometry.json \
        --dxf output/sketch.dxf \
        --out-dxf output/sketch_scaled.dxf \
        --ref-mm 9500
"""
import argparse
import datetime
import json
from pathlib import Path


DXF_X_CODES = {"10", "11", "12", "13"}
DXF_Y_CODES = {"20", "21", "22", "23"}


def compute_bbox_px(geom):
    xs, ys = [], []
    for c in geom.get("columns", []):
        xs.append(float(c["px"]))
        ys.append(float(c["py"]))
    for s in geom.get("magenta_segments", []) + geom.get("cyan_segments", []):
        xs += [float(s["sx"]), float(s["ex"])]
        ys += [float(s["sy"]), float(s["ey"])]
    if not xs:
        raise SystemExit("no geometry to rescale")
    return min(xs), max(xs), min(ys), max(ys)


def resolve_mm_per_px(geom, args):
    minx, maxx, miny, maxy = compute_bbox_px(geom)
    width_px = maxx - minx
    height_px = maxy - miny
    scale = geom.get("scale") or {}
    ref_px = scale.get("refPxDistance")

    if ref_px is None:
        if args.ref_mm is not None:
            raise SystemExit("--ref-mm requires geometry scale.refPxDistance")
        if args.target_width_mm is not None:
            ref_px = width_px
            scale["refPair"] = ["BBOX_LEFT", "BBOX_RIGHT"]
        elif args.target_height_mm is not None:
            ref_px = height_px
            scale["refPair"] = ["BBOX_TOP", "BBOX_BOTTOM"]
        else:
            raise SystemExit("specify one of --ref-mm / --target-width-mm / --target-height-mm")
        scale["refPxDistance"] = round(ref_px, 2)
        geom["scale"] = scale
    else:
        ref_px = float(ref_px)

    if args.ref_mm is not None:
        return float(args.ref_mm) / ref_px, "ref-mm"
    if args.target_width_mm is not None and args.target_height_mm is not None:
        mx = float(args.target_width_mm) / width_px
        my = float(args.target_height_mm) / height_px
        if abs(mx - my) / max(mx, my) > 0.01:
            print(f"[warn] width and height factors differ ({mx:.4f} vs {my:.4f}); "
                  f"averaging — pick one axis if you want stricter control")
        return (mx + my) / 2.0, "target-bbox-avg"
    if args.target_width_mm is not None:
        return float(args.target_width_mm) / width_px, "target-width"
    if args.target_height_mm is not None:
        return float(args.target_height_mm) / height_px, "target-height"
    raise SystemExit("specify one of --ref-mm / --target-width-mm / --target-height-mm")


def rescale_dxf(in_path, out_path, factor):
    lines = Path(in_path).read_text(encoding="ascii").splitlines()
    out = []
    i = 0
    n = len(lines)
    while i < n:
        code = lines[i].strip()
        out.append(lines[i])
        if code in DXF_X_CODES or code in DXF_Y_CODES:
            if i + 1 < n:
                try:
                    val = float(lines[i + 1].strip())
                    out.append(f"{val * factor:.3f}")
                except ValueError:
                    out.append(lines[i + 1])
                i += 2
                continue
        i += 1
    Path(out_path).write_text("\n".join(out) + "\n", encoding="ascii")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--geometry", required=True, help="path to sketch_geometry.json (will be updated in place)")
    ap.add_argument("--dxf", required=True, help="input DXF (raw pixel coords)")
    ap.add_argument("--out-dxf", required=True, help="output DXF in mm")
    ap.add_argument("--ref-mm", type=float, default=None,
                    help="actual mm distance between the reference column pair (refPair in geometry.json)")
    ap.add_argument("--target-width-mm", type=float, default=None,
                    help="actual mm width of the element bounding box (X axis)")
    ap.add_argument("--target-height-mm", type=float, default=None,
                    help="actual mm height of the element bounding box (Y axis)")
    args = ap.parse_args()

    geom_path = Path(args.geometry)
    geom = json.loads(geom_path.read_text(encoding="utf-8"))
    if "scale" not in geom:
        raise SystemExit("geometry.json missing 'scale' field")

    mm_per_px, mode = resolve_mm_per_px(geom, args)
    ref_px = float(geom["scale"]["refPxDistance"])
    minx, maxx, miny, maxy = compute_bbox_px(geom)

    rescale_dxf(args.dxf, args.out_dxf, mm_per_px)

    geom["scale"]["refMmDistance"] = round(mm_per_px * ref_px, 3)
    geom["scale"]["mmPerPx"] = round(mm_per_px, 6)
    geom["scale"]["method"] = f"user-corrected via rescale_geometry.py ({mode})"
    geom["scale"]["rescaled_at"] = datetime.datetime.now().isoformat(timespec="seconds")
    geom_path.write_text(json.dumps(geom, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"mode={mode}  mmPerPx={mm_per_px:.4f}")
    print(f"refMmDistance: {geom['scale']['refMmDistance']:.1f} mm "
          f"(refPxDistance={ref_px:.2f}, pair={geom['scale']['refPair']})")
    print(f"new element bbox: "
          f"{(maxx - minx) * mm_per_px:,.1f} x {(maxy - miny) * mm_per_px:,.1f} mm "
          f"({(maxx - minx) * mm_per_px / 1000:.2f} x {(maxy - miny) * mm_per_px / 1000:.2f} m)")
    print(f"wrote {args.out_dxf}")
    print(f"updated {args.geometry}")


if __name__ == "__main__":
    main()
