import sys
import json
import os

try:
    import ezdxf
except ImportError:
    print(json.dumps({"error": "缺少 ezdxf 套件，請在系統 Python 環境中安裝 (pip install ezdxf)"}))
    sys.exit(1)

# 常見 ODA File Converter 安裝路徑，依版本由新到舊排列
_ODA_SEARCH_PATHS = [
    r"C:\Program Files\ODA\ODAFileConverter 27.1.0",
    r"C:\Program Files\ODA\ODAFileConverter 25.12.0",
    r"C:\Program Files\ODA\ODAFileConverter 24.6.0",
    r"C:\Program Files\ODA\ODAFileConverter",
    r"C:\Program Files (x86)\ODA\ODAFileConverter",
]

def _inject_oda_path():
    """動態將 ODA 路徑注入 PATH，繞過 Revit/pyRevit 啟動前未更新環境的問題。"""
    current_path = os.environ.get("PATH", "")
    for p in _ODA_SEARCH_PATHS:
        if os.path.isdir(p) and p not in current_path:
            os.environ["PATH"] += os.pathsep + p
            return True
    return False

def _read_doc(filepath):
    """讀取 DXF 或 DWG，DWG 需要 ODA；ODA 找不到時回傳 ("no_oda", None)。"""
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

def main():
    if len(sys.argv) < 3:
        print(json.dumps({"error": "參數錯誤，需要 filepath 與 target_layer"}))
        sys.exit(1)

    filepath = sys.argv[1]
    target_layer = sys.argv[2]

    if not os.path.exists(filepath):
        print(json.dumps({"error": f"找不到檔案: {filepath}"}))
        sys.exit(1)

    status, doc = _read_doc(filepath)

    if status == "no_oda":
        print(json.dumps({
            "error_type": "no_oda",
            "error": (
                "DWG 格式需要 ODA File Converter 才能讀取文字標注。"
                "請安裝 ODA File Converter（https://www.opendesign.com/guestfiles/oda_file_converter），"
                "或將 CAD 連結改為 DXF 格式後重試。"
            )
        }))
        sys.exit(0)

    if status == "error":
        print(json.dumps({"error": f"檔案解析失敗: {doc}"}))
        sys.exit(1)

    # INSUNITS → mm 換算係數，讓 C# 可以在套 GetTotalTransform 前先轉成 feet
    insunits = doc.header.get("$INSUNITS", 0)
    insunits_to_mm = {
        0: 1.0, 1: 25.4, 2: 304.8, 4: 1.0,
        5: 10.0, 6: 1000.0, 14: 100.0,
    }.get(insunits, 1.0)

    results = []
    try:
        msp = doc.modelspace()
        for entity in msp.query("TEXT MTEXT"):
            if entity.dxf.layer != target_layer:
                continue
            if entity.dxftype() == "TEXT":
                align = entity.dxf.get("halign", 0)
                pt = entity.dxf.align_point if (align != 0 and entity.dxf.hasattr("align_point")) else entity.dxf.insert
                text_content = entity.dxf.text
                rotation = entity.dxf.get("rotation", 0.0)
                height = entity.dxf.get("height", 0.0)
            else:  # MTEXT
                pt = entity.dxf.insert
                text_content = entity.text
                rotation = entity.dxf.get("rotation", 0.0)
                height = entity.dxf.get("char_height", 0.0)

            results.append({
                "text": text_content,
                "layer": target_layer,
                "x": pt.x,
                "y": pt.y,
                "z": pt.z,
                "rotation": rotation,
                "height": height
            })

        print(json.dumps({
            "success": True,
            "insunits": insunits,
            "insunits_to_mm": insunits_to_mm,
            "data": results
        }))

    except Exception as e:
        print(json.dumps({"error": f"解析文字時發生錯誤: {str(e)}"}))
        sys.exit(1)

if __name__ == "__main__":
    main()
