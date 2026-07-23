# -*- coding: utf-8 -*-
"""ezdxf_floor_worker.py — 樓板半自動翻模的前段：讀 DXF/DWG 樓板圖層的封閉 polyline。

用法:
    python ezdxf_floor_worker.py <filepath> <target_layer> [min_area_mm2] [sagitta_mm]

輸出 JSON（stdout）:
    {
      "success": true,
      "insunits": 4, "insunits_to_mm": 1.0, "unit_warning": null,
      "slabs": [
        {
          "index": 1,
          "vertices_mm": [[x, y], ...],     # 已依 $INSUNITS 換算成 mm，可直接餵 create_floor
          "vertex_count": 4,
          "area_mm2": 12000000.0,
          "area_m2": 12.0,
          "bbox_mm": [minx, miny, maxx, maxy],
          "possible_opening": false          # bbox 完全落在另一塊 slab 內 → 可能是開口而非樓板
        }, ...
      ],
      "skipped_open": 2                      # 圖層上未封閉而略過的 polyline 數
    }

設計對齊 ezdxf_worker.py（柱號讀取）：DWG 經 ODA File Converter、同樣的 no_oda 錯誤協議。
弧形邊（bulge）用 ezdxf.path.flattening 離散化成短直線（create_floor 只吃直線段）。
座標為 DXF 原始座標換算 mm——尚未套 Revit 連結變位，放樣健檢由呼叫端（AI 斷點）負責。
"""
import sys
import json
import os

try:
    import ezdxf
    from ezdxf import path as ezpath
except ImportError:
    print(json.dumps({"error": "缺少 ezdxf 套件，請在系統 Python 環境中安裝 (pip install ezdxf)"}))
    sys.exit(1)

_ODA_SEARCH_PATHS = [
    r"C:\Program Files\ODA\ODAFileConverter 27.1.0",
    r"C:\Program Files\ODA\ODAFileConverter 25.12.0",
    r"C:\Program Files\ODA\ODAFileConverter 24.6.0",
    r"C:\Program Files\ODA\ODAFileConverter",
    r"C:\Program Files (x86)\ODA\ODAFileConverter",
]


def _inject_oda_path():
    current_path = os.environ.get("PATH", "")
    for p in _ODA_SEARCH_PATHS:
        if os.path.isdir(p) and p not in current_path:
            os.environ["PATH"] += os.pathsep + p
            return True
    return False


def _read_doc(filepath):
    if filepath.lower().endswith(".dwg"):
        _inject_oda_path()
        try:
            import ezdxf.addons.odafc as odafc
            return "ok", odafc.readfile(filepath)
        except Exception as e:
            msg = str(e).lower()
            if any(k in msg for k in ["oda", "converter", "not found", "executable", "no such file"]):
                return "no_oda", None
            return "error", str(e)
    else:
        try:
            return "ok", ezdxf.readfile(filepath)
        except Exception as e:
            return "error", str(e)


def _shoelace_area(pts):
    n = len(pts)
    s = 0.0
    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        s += x1 * y2 - x2 * y1
    return abs(s) / 2.0


def _point_in_polygon(x, y, pts):
    inside = False
    n = len(pts)
    j = n - 1
    for i in range(n):
        xi, yi = pts[i]
        xj, yj = pts[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


def _is_closed(entity):
    t = entity.dxftype()
    if t == "LWPOLYLINE":
        return bool(entity.closed)
    if t == "POLYLINE":
        return bool(entity.is_closed)
    return False


def main():
    if len(sys.argv) < 3:
        print(json.dumps({"error": "參數錯誤，需要 filepath 與 target_layer（選填 min_area_mm2、sagitta_mm）"}))
        sys.exit(1)

    filepath = sys.argv[1]
    target_layer = sys.argv[2]
    min_area_mm2 = float(sys.argv[3]) if len(sys.argv) > 3 else 10000.0  # 預設 0.01 m²，濾雜訊
    sagitta_mm = float(sys.argv[4]) if len(sys.argv) > 4 else 5.0        # 弧離散化容差

    if not os.path.exists(filepath):
        print(json.dumps({"error": f"找不到檔案: {filepath}"}))
        sys.exit(1)

    status, doc = _read_doc(filepath)

    if status == "no_oda":
        print(json.dumps({
            "error_type": "no_oda",
            "error": (
                "DWG 格式需要 ODA File Converter 才能讀取。"
                "請安裝 ODA File Converter（https://www.opendesign.com/guestfiles/oda_file_converter），"
                "或將圖檔另存為 DXF 格式後重試。"
            )
        }))
        sys.exit(0)

    if status == "error":
        print(json.dumps({"error": f"檔案解析失敗: {doc}"}))
        sys.exit(1)

    insunits = doc.header.get("$INSUNITS", 0)
    insunits_to_mm = {
        0: 1.0, 1: 25.4, 2: 304.8, 4: 1.0,
        5: 10.0, 6: 1000.0, 14: 100.0,
    }.get(insunits, 1.0)
    unit_warning = (
        "$INSUNITS=0（未指定單位），暫以 mm 處理；請在放樣健檢時特別核對尺寸"
        if insunits == 0 else None
    )

    slabs = []
    skipped_open = 0
    try:
        msp = doc.modelspace()
        sagitta_units = sagitta_mm / insunits_to_mm
        for entity in msp.query("LWPOLYLINE POLYLINE"):
            if entity.dxf.layer != target_layer:
                continue
            if not _is_closed(entity):
                skipped_open += 1
                continue

            # path.flattening 統一處理 bulge 弧段與直線段
            p = ezpath.make_path(entity)
            pts = [(v.x, v.y) for v in p.flattening(distance=sagitta_units)]
            # 收尾點與起點重合時去掉，避免零長度線段
            if len(pts) >= 2 and abs(pts[0][0] - pts[-1][0]) < 1e-9 and abs(pts[0][1] - pts[-1][1]) < 1e-9:
                pts = pts[:-1]
            if len(pts) < 3:
                continue

            pts_mm = [(x * insunits_to_mm, y * insunits_to_mm) for x, y in pts]
            area = _shoelace_area(pts_mm)
            if area < min_area_mm2:
                continue

            xs = [pt[0] for pt in pts_mm]
            ys = [pt[1] for pt in pts_mm]
            slabs.append({
                "vertices_mm": [[round(x, 1), round(y, 1)] for x, y in pts_mm],
                "vertex_count": len(pts_mm),
                "area_mm2": round(area, 0),
                "area_m2": round(area / 1e6, 2),
                "bbox_mm": [round(min(xs), 1), round(min(ys), 1), round(max(xs), 1), round(max(ys), 1)],
            })

        # 面積由大到小排序，標記「bbox 中心落在更大 slab 內」者為可能開口
        slabs.sort(key=lambda s: -s["area_mm2"])
        for i, s in enumerate(slabs):
            cx = (s["bbox_mm"][0] + s["bbox_mm"][2]) / 2
            cy = (s["bbox_mm"][1] + s["bbox_mm"][3]) / 2
            s["possible_opening"] = any(
                _point_in_polygon(cx, cy, [(v[0], v[1]) for v in big["vertices_mm"]])
                for big in slabs[:i]
            )
        for i, s in enumerate(slabs):
            s["index"] = i + 1
            # index 排前面方便閱讀
            slabs[i] = {"index": s.pop("index"), **s}

        print(json.dumps({
            "success": True,
            "insunits": insunits,
            "insunits_to_mm": insunits_to_mm,
            "unit_warning": unit_warning,
            "slabCount": len(slabs),
            "slabs": slabs,
            "skipped_open": skipped_open,
        }, ensure_ascii=False))

    except Exception as e:
        print(json.dumps({"error": f"解析樓板輪廓時發生錯誤: {str(e)}"}))
        sys.exit(1)


if __name__ == "__main__":
    main()
