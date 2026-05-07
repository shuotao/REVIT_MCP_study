#!/usr/bin/env python3
"""Convert a color-coded floor plan sketch image into DXF centerlines."""
import argparse
import json
import math
from pathlib import Path

import cv2
import numpy as np
from skimage.morphology import skeletonize


def round_half(v):
    return round(float(v) * 2.0) / 2.0


def dxf_y(y, h, scale):
    return (h - float(y)) * scale


def add_line(parts, layer, x1, y1, x2, y2, h, scale):
    parts += [
        "0", "LINE", "8", layer,
        "10", f"{x1 * scale:.3f}", "20", f"{dxf_y(y1, h, scale):.3f}", "30", "0.0",
        "11", f"{x2 * scale:.3f}", "21", f"{dxf_y(y2, h, scale):.3f}", "31", "0.0",
    ]


def add_lwpolyline(parts, layer, pts, h, scale, closed=False):
    parts += ["0", "LWPOLYLINE", "8", layer, "90", str(len(pts)), "70", "1" if closed else "0"]
    for x, y in pts:
        parts += ["10", f"{x * scale:.3f}", "20", f"{dxf_y(y, h, scale):.3f}"]


def masks_from_image(img):
    b, g, r = cv2.split(img)
    mag = ((r > 145) & (b > 115) & (g < 145)).astype(np.uint8) * 255
    cyan = ((b > 120) & (g > 75) & (r < 185)).astype(np.uint8) * 255
    black = ((b < 85) & (g < 85) & (r < 85)).astype(np.uint8) * 255
    mag = cv2.morphologyEx(mag, cv2.MORPH_OPEN, np.ones((2, 2), np.uint8))
    cyan = cv2.morphologyEx(cyan, cv2.MORPH_CLOSE, np.ones((2, 2), np.uint8))
    black = cv2.morphologyEx(black, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8), iterations=2)
    return mag, cyan, black


def detect_columns(black):
    contours, _ = cv2.findContours(black, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    cols = []
    for c in contours:
        area = cv2.contourArea(c)
        x, y, w, h = cv2.boundingRect(c)
        if area > 300 and w > 15 and h > 15:
            m = cv2.moments(c)
            if m["m00"]:
                cols.append((m["m10"] / m["m00"], m["m01"] / m["m00"], w, h))
    return sorted(cols, key=lambda t: (t[1], t[0]))


def bridge_through_columns(mask, cols, side=40.0):
    mask = mask.copy()
    hh, ww = mask.shape
    half = side / 2.0
    for cx0, cy0, _, _ in cols:
        cx, cy = int(round(cx0)), int(round(cy0))
        best = None
        for y in range(max(0, cy - int(half) - 5), min(hh, cy + int(half) + 6)):
            l0, l1 = max(0, cx - int(2.5 * side)), max(0, cx - int(half) + 1)
            r0, r1 = min(ww, cx + int(half)), min(ww, cx + int(2.5 * side) + 1)
            lx = np.where(mask[y, l0:l1] > 0)[0]
            rx = np.where(mask[y, r0:r1] > 0)[0]
            if lx.size and rx.size:
                x1, x2 = l0 + lx[-1], r0 + rx[0]
                if 0 < x2 - x1 < side * 4:
                    cand = (abs(y - cy), x2 - x1, x1, x2, y)
                    if best is None or cand < best:
                        best = cand
        if best is not None:
            _, _, x1, x2, y = best
            cv2.line(mask, (x1, y), (x2, y), 255, 2)
        best = None
        for x in range(max(0, cx - int(half) - 5), min(ww, cx + int(half) + 6)):
            t0, t1 = max(0, cy - int(2.5 * side)), max(0, cy - int(half) + 1)
            b0, b1 = min(hh, cy + int(half)), min(hh, cy + int(2.5 * side) + 1)
            ty = np.where(mask[t0:t1, x] > 0)[0]
            by = np.where(mask[b0:b1, x] > 0)[0]
            if ty.size and by.size:
                y1, y2 = t0 + ty[-1], b0 + by[0]
                if 0 < y2 - y1 < side * 4:
                    cand = (abs(x - cx), y2 - y1, y1, y2, x)
                    if best is None or cand < best:
                        best = cand
        if best is not None:
            _, _, y1, y2, x = best
            cv2.line(mask, (x, y1), (x, y2), 255, 2)
    return cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8), iterations=1)


def raw_hough(mask, minlen=18, gap=25):
    sk = (skeletonize(mask > 0).astype(np.uint8)) * 255
    lines = cv2.HoughLinesP(sk, 1, np.pi / 180, threshold=18, minLineLength=minlen, maxLineGap=gap)
    segs = []
    if lines is None:
        return segs
    for [[x1, y1, x2, y2]] in lines:
        dx, dy = x2 - x1, y2 - y1
        if math.hypot(dx, dy) < minlen:
            continue
        if abs(dx) >= abs(dy) * 2:
            segs.append(("H", (y1 + y2) / 2.0, float(min(x1, x2)), float(max(x1, x2))))
        elif abs(dy) >= abs(dx) * 2:
            segs.append(("V", (x1 + x2) / 2.0, float(min(y1, y2)), float(max(y1, y2))))
    return segs


def merge_same_line(segs, coord_tol=8.0, gap_tol=24.0, min_span=14.0):
    out = []
    for orient in ("H", "V"):
        arr = sorted([s for s in segs if s[0] == orient], key=lambda t: (t[1], t[2]))
        groups = []
        for s in arr:
            for g in groups:
                if abs(np.mean([x[1] for x in g]) - s[1]) <= coord_tol:
                    g.append(s)
                    break
            else:
                groups.append([s])
        for g in groups:
            coord = float(np.mean([x[1] for x in g]))
            intervals = sorted((float(x[2]), float(x[3])) for x in g)
            a, b = intervals[0]
            for c, d in intervals[1:]:
                if c <= b + gap_tol:
                    b = max(b, d)
                else:
                    if b - a >= min_span:
                        out.append((orient, round_half(coord), round_half(a), round_half(b)))
                    a, b = c, d
            if b - a >= min_span:
                out.append((orient, round_half(coord), round_half(a), round_half(b)))
    return out


def cluster_coords(vals, weights, tol):
    if not vals:
        return []
    order = np.argsort(vals)
    vals = [float(vals[i]) for i in order]
    weights = [float(weights[i]) for i in order]
    groups = []
    cur = [vals[0]]
    curw = [weights[0]]
    center = vals[0]
    for v, w in zip(vals[1:], weights[1:]):
        if abs(v - center) <= tol:
            cur.append(v); curw.append(w)
            center = sum(a * b for a, b in zip(cur, curw)) / sum(curw)
        else:
            groups.append((cur, curw)); cur = [v]; curw = [w]; center = v
    groups.append((cur, curw))
    return [round_half(sum(a * b for a, b in zip(g, gw)) / sum(gw)) for g, gw in groups]


def nearest(v, centers, tol=None):
    if not centers:
        return float(v)
    c = min(centers, key=lambda x: abs(x - v))
    if tol is not None and abs(c - v) > tol:
        return float(v)
    return float(c)


def snap_and_extend(segs, cols, mode):
    if mode == "basic":
        return merge_same_line(segs, coord_tol=7, gap_tol=20)
    merged = merge_same_line(segs, coord_tol=8, gap_tol=26)
    x_vals, x_w, y_vals, y_w = [], [], [], []
    for o, c, a, b in merged:
        if o == "V":
            x_vals.append(c); x_w.append(abs(b - a))
        else:
            y_vals.append(c); y_w.append(abs(b - a))
    for cx, cy, _, _ in cols:
        x_vals.append(cx); x_w.append(120 if mode == "strong-ortho" else 80)
        y_vals.append(cy); y_w.append(120 if mode == "strong-ortho" else 80)
    tol = 15 if mode == "strong-ortho" else 12
    x_axes = cluster_coords(x_vals, x_w, tol)
    y_axes = cluster_coords(y_vals, y_w, tol)
    ctol = 15 if mode == "strong-ortho" else 12
    etol = 22 if mode == "strong-ortho" else 16
    snapped = []
    for o, c, a, b in merged:
        if o == "H":
            c = nearest(c, y_axes, ctol); a = nearest(a, x_axes, etol); b = nearest(b, x_axes, etol)
        else:
            c = nearest(c, x_axes, ctol); a = nearest(a, y_axes, etol); b = nearest(b, y_axes, etol)
        if b < a:
            a, b = b, a
        snapped.append((o, round_half(c), round_half(a), round_half(b)))
    snapped = merge_same_line(snapped, coord_tol=0.25, gap_tol=40 if mode == "strong-ortho" else 32)
    extended = extend_to_intersections(snapped, 26 if mode == "strong-ortho" else 18, 24 if mode == "strong-ortho" else 18)
    return stitch_across_columns(extended, cols)


def stitch_across_columns(segs, cols, coord_tol=6, gap_max=160):
    """合併同方向、coord 相近、且兩段之間正好有柱子穿過的線段。
    柱子是合理的「斷裂點」（手繪時筆抬起或柱遮擋），這時兩段視為同一道牆。"""
    if not cols:
        return list(segs)
    segs = [list(s) for s in segs]
    changed = True
    while changed:
        changed = False
        for i in range(len(segs)):
            broken = False
            for j in range(i + 1, len(segs)):
                a, b = segs[i], segs[j]
                if a[0] != b[0] or abs(a[1] - b[1]) > coord_tol:
                    continue
                lo, hi = (a, b) if a[2] <= b[2] else (b, a)
                gap = hi[2] - lo[3]
                if not (0 < gap < gap_max):
                    continue
                hit = False
                for cx, cy, cw, ch in cols:
                    half = max(cw, ch) * 0.6
                    if a[0] == "H":
                        if lo[3] <= cx <= hi[2] and abs(cy - lo[1]) <= half:
                            hit = True; break
                    else:
                        if lo[3] <= cy <= hi[2] and abs(cx - lo[1]) <= half:
                            hit = True; break
                if hit:
                    segs[i] = [a[0],
                               round_half((a[1] + b[1]) / 2),
                               round_half(min(a[2], b[2])),
                               round_half(max(a[3], b[3]))]
                    del segs[j]
                    changed = True
                    broken = True
                    break
            if broken:
                break
    return [tuple(s) for s in segs]


def extend_to_intersections(segs, end_tol, cross_tol):
    hs = [list(s) for s in segs if s[0] == "H"]
    vs = [list(s) for s in segs if s[0] == "V"]
    for h in hs:
        _, y, x1, x2 = h
        cand = [v for v in vs if v[2] - cross_tol <= y <= v[3] + cross_tol]
        left = [v[1] for v in cand if abs(v[1] - x1) <= end_tol]
        right = [v[1] for v in cand if abs(v[1] - x2) <= end_tol]
        if left:
            h[2] = min(left, key=lambda q: abs(q - x1))
        if right:
            h[3] = min(right, key=lambda q: abs(q - x2))
    for v in vs:
        _, x, y1, y2 = v
        cand = [h for h in hs if h[2] - cross_tol <= x <= h[3] + cross_tol]
        top = [h[1] for h in cand if abs(h[1] - y1) <= end_tol]
        bot = [h[1] for h in cand if abs(h[1] - y2) <= end_tol]
        if top:
            v[2] = min(top, key=lambda q: abs(q - y1))
        if bot:
            v[3] = min(bot, key=lambda q: abs(q - y2))
    return merge_same_line([tuple(s) for s in hs + vs], coord_tol=0.25, gap_tol=32, min_span=14)


def write_outputs(img, mag, cyan, cols, out_dxf, preview, column_side, scale):
    h, _ = img.shape[:2]
    parts = ["0", "SECTION", "2", "HEADER", "9", "$ACADVER", "1", "AC1009", "0", "ENDSEC",
             "0", "SECTION", "2", "TABLES", "0", "TABLE", "2", "LAYER", "70", "4"]
    for name, color in (("WALL_MAGENTA", 6), ("WALL_CYAN", 4), ("COLUMNS_BLACK", 7), ("COLUMN_CENTERLINE", 8)):
        parts += ["0", "LAYER", "2", name, "70", "0", "62", str(color), "6", "CONTINUOUS"]
    parts += ["0", "ENDTAB", "0", "ENDSEC", "0", "SECTION", "2", "ENTITIES"]
    for layer, segs in (("WALL_MAGENTA", mag), ("WALL_CYAN", cyan)):
        for o, c, a, b in segs:
            if o == "H":
                add_line(parts, layer, a, c, b, c, h, scale)
            else:
                add_line(parts, layer, c, a, c, b, h, scale)
    half, ext = column_side / 2.0, 10.0
    for cx, cy, _, _ in cols:
        cx, cy = round_half(cx), round_half(cy)
        square = [(cx - half, cy - half), (cx + half, cy - half), (cx + half, cy + half), (cx - half, cy + half)]
        add_lwpolyline(parts, "COLUMNS_BLACK", square, h, scale, closed=True)
        add_line(parts, "COLUMN_CENTERLINE", cx - half - ext, cy, cx + half + ext, cy, h, scale)
        add_line(parts, "COLUMN_CENTERLINE", cx, cy - half - ext, cx, cy + half + ext, h, scale)
    parts += ["0", "ENDSEC", "0", "EOF"]
    Path(out_dxf).write_text("\n".join(parts) + "\n", encoding="ascii")

    canvas = np.full_like(img, 255)
    for segs, color in ((mag, (255, 0, 255)), (cyan, (255, 180, 0))):
        for o, c, a, b in segs:
            if o == "H":
                cv2.line(canvas, (int(round(a)), int(round(c))), (int(round(b)), int(round(c))), color, 2)
            else:
                cv2.line(canvas, (int(round(c)), int(round(a))), (int(round(c)), int(round(b))), color, 2)
    for cx, cy, _, _ in cols:
        cx, cy = round_half(cx), round_half(cy)
        cv2.rectangle(canvas, (int(cx - half), int(cy - half)), (int(cx + half), int(cy + half)), (0, 0, 0), 2)
        cv2.line(canvas, (int(cx - half - ext), int(cy)), (int(cx + half + ext), int(cy)), (120, 120, 120), 1)
        cv2.line(canvas, (int(cx), int(cy - half - ext)), (int(cx), int(cy + half + ext)), (120, 120, 120), 1)
    cv2.imwrite(str(preview), canvas)


def write_geometry_json(path, img, cols, mag, cyan, mode, column_side):
    h, w = img.shape[:2]

    def serialize_segs(segs):
        out = []
        for o, c, a, b in segs:
            if o == "H":
                out.append({"sx": float(a), "sy": float(c), "ex": float(b), "ey": float(c)})
            else:
                out.append({"sx": float(c), "sy": float(a), "ex": float(c), "ey": float(b)})
        return out

    col_list = [{"id": f"C{i + 1}", "px": float(cx), "py": float(cy), "w": float(cw), "h": float(ch)}
                for i, (cx, cy, cw, ch) in enumerate(cols)]

    ref = None
    if len(cols) >= 2:
        best = None
        for i in range(len(cols)):
            for j in range(i + 1, len(cols)):
                d = math.hypot(cols[i][0] - cols[j][0], cols[i][1] - cols[j][1])
                if best is None or d < best[0]:
                    best = (d, f"C{i + 1}", f"C{j + 1}")
        ref = {"refPxDistance": round(best[0], 2),
               "refPair": [best[1], best[2]],
               "refMmDistance": 10000,
               "method": "nearest column pair (assumed 10m)"}

    payload = {
        "image": {"width": int(w), "height": int(h)},
        "mode": mode,
        "column_side_px": float(column_side),
        "scale": ref,
        "columns": col_list,
        "magenta_segments": serialize_segs(mag),
        "cyan_segments": serialize_segs(cyan),
    }
    Path(path).write_text(json.dumps(payload, indent=2), encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("input")
    ap.add_argument("--out", required=True)
    ap.add_argument("--preview", required=True)
    ap.add_argument("--json", dest="json_out", default=None,
                    help="optional JSON output with columns + segments in pixel coords")
    ap.add_argument("--mode", choices=["basic", "aligned", "strong-ortho"], default="strong-ortho")
    ap.add_argument("--column-side", type=float, default=40.0)
    ap.add_argument("--scale", type=float, default=1.0)
    args = ap.parse_args()
    img = cv2.imread(args.input)
    if img is None:
        raise SystemExit(f"could not read input image: {args.input}")
    mag_mask, cyan_mask, black_mask = masks_from_image(img)
    cols = detect_columns(black_mask)
    mag_mask = bridge_through_columns(mag_mask, cols, args.column_side)
    cyan_mask = bridge_through_columns(cyan_mask, cols, args.column_side)
    mag = snap_and_extend(raw_hough(mag_mask), cols, args.mode)
    cyan = snap_and_extend(raw_hough(cyan_mask), cols, args.mode)
    write_outputs(img, mag, cyan, cols, args.out, args.preview, args.column_side, args.scale)
    if args.json_out:
        write_geometry_json(args.json_out, img, cols, mag, cyan, args.mode, args.column_side)
        print(f"wrote {args.json_out}")
    print(f"wrote {args.out}")
    print(f"wrote {args.preview}")
    print(f"columns {len(cols)}")
    print(f"magenta_segments {len(mag)}")
    print(f"cyan_segments {len(cyan)}")


if __name__ == "__main__":
    main()
