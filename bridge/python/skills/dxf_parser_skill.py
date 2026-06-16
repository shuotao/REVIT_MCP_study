# -*- coding: utf-8 -*-
"""
DXF Parser Skill
這是一個封裝好的 Revit Python 模組，用於透過點選 CAD 底圖上的文字，
反查圖層名稱與實體路徑，並在背景使用 ezdxf 瞬間解析出同圖層所有文字的座標資訊。

相依套件: ezdxf
執行環境: Dynamo Python Script node 或 pyRevit
"""

import sys
import os
import json
import clr

clr.AddReference('RevitAPI')
clr.AddReference('RevitAPIUI')
clr.AddReference('RevitServices')

from Autodesk.Revit.DB import *
from Autodesk.Revit.UI import *
from Autodesk.Revit.UI.Selection import *
import RevitServices
from RevitServices.Persistence import DocumentManager

# 嘗試引入 ezdxf，如果沒有則提供錯誤提示
import sys
import os
site_packages = r"C:\Users\ROY\AppData\Local\Python\pythoncore-3.14-64\Lib\site-packages"
if site_packages not in sys.path:
    sys.path.append(site_packages)

try:
    import ezdxf
    HAS_EZDXF = True
    EZDXF_ERROR = ""
except Exception as e:
    HAS_EZDXF = False
    EZDXF_ERROR = str(e)

def get_dxf_filepath(import_instance, doc):
    """
    從 ImportInstance 反查其對應的真實檔案路徑
    """
    cad_link_type = doc.GetElement(import_instance.GetTypeId())
    if cad_link_type and isinstance(cad_link_type, CADLinkType):
        ext_ref = cad_link_type.GetExternalFileReference()
        if ext_ref:
            model_path = ext_ref.GetPath()
            path_str = ModelPathUtils.ConvertModelPathToUserVisiblePath(model_path)
            # 如果是相對路徑，將其轉換為相對於 Revit 專案檔的絕對路徑
            if not os.path.isabs(path_str) and doc.PathName:
                doc_dir = os.path.dirname(doc.PathName)
                path_str = os.path.normpath(os.path.join(doc_dir, path_str))
            return path_str
    return None

def extract_text_from_dxf(filepath, target_layer):
    """
    使用 ezdxf_worker.py 在背景獨立執行解析，避開 pyRevit/numpy 的 C-extension 衝突
    """
    import subprocess
    import json
    
    worker_script = os.path.join(os.path.dirname(__file__), "ezdxf_worker.py")
    if not os.path.exists(worker_script):
        raise Exception("找不到 ezdxf_worker.py，請確認檔案存在。")
        
    try:
        # 使用系統環境的 python 執行
        # 為了避免 cmd 視窗彈出，設定 creationflags
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        
        result = subprocess.check_output(
            ["python", worker_script, filepath, target_layer],
            startupinfo=startupinfo,
            stderr=subprocess.STDOUT
        )
        
        # 解析回傳的 JSON
        output_str = result.decode('utf-8', errors='ignore').strip()
        response = json.loads(output_str)
        
        if response.get("error"):
            raise Exception(response["error"])
            
        return response.get("data", [])
        
    except subprocess.CalledProcessError as e:
        error_output = e.output.decode('utf-8', errors='ignore')
        raise Exception("背景解析程序失敗:\n" + error_output)
    except Exception as e:
        raise Exception("呼叫背景解析時發生錯誤: " + str(e))

def pick_and_read_dxf_text(prompt_message="請點選 CAD 底圖上的目標文字 (以鎖定圖層)"):
    """
    核心 Skill 入口點
    """
    # 判斷執行環境 (PyRevit/RevitPythonShell 或 Dynamo)
    try:
        # 嘗試 PyRevit / RPS 環境
        uidoc = __revit__.ActiveUIDocument
        doc = uidoc.Document
    except NameError:
        # 嘗試 Dynamo 環境
        try:
            from RevitServices.Persistence import DocumentManager
            doc = DocumentManager.Instance.CurrentDBDocument
            uidoc = DocumentManager.Instance.CurrentUIApplication.ActiveUIDocument
        except Exception:
            raise Exception("無法取得 Revit Document，請確認執行環境是否為 pyRevit 或 Dynamo！")
            
    if not uidoc:
        raise Exception("無法取得 ActiveUIDocument，在 pyRevit 執行時可能尚未開啟任何視圖。")
    
    try:
        # 1. 提示使用者點選 CAD 中的幾何子元素 (不使用 Filter 以避免 CPython/pythonnet 介面繼承報錯)
        ref = uidoc.Selection.PickObject(ObjectType.PointOnElement, prompt_message)
        if not ref:
            return []
            
        # 2. 獲取所選的 CAD ImportInstance
        import_instance = doc.GetElement(ref.ElementId)
        if not isinstance(import_instance, ImportInstance):
            print("錯誤: 點選到的不是 CAD 連結檔 (ImportInstance)，請重新執行並確保點選的是 CAD 底圖！")
            return []
        
        # 3. 獲取所選子元素 (GeometryObject) 的圖層名稱
        geom_obj = import_instance.GetGeometryObjectFromReference(ref)
        if not geom_obj:
            print("無法解析選取到的幾何圖形。")
            return []
            
        category_id = geom_obj.GraphicsStyleId
        graphics_style = doc.GetElement(category_id)
        if not graphics_style:
            print("無法取得圖層資訊 (GraphicsStyle)。")
            return []
            
        layer_category = graphics_style.GraphicsStyleCategory
        layer_name = layer_category.Name
        
        print("已鎖定圖層: " + layer_name)
        
        # 4. 反查真實 DXF 檔案路徑
        filepath = get_dxf_filepath(import_instance, doc)
        if not filepath:
            print("無法解析此連結檔的檔案路徑，請確認這是一個連結檔 (Link CAD) 而非匯入檔 (Import CAD)。")
            return []
            
        print("正在背景讀取 DXF 檔案: " + filepath)
        
        # 5. 進行背景解析
        dxf_data = extract_text_from_dxf(filepath, layer_name)
        return dxf_data
        
    except Exception as e:
        if "OperationCanceledException" in str(type(e)):
            print("使用者取消了點選操作。")
        else:
            print("執行過程中發生錯誤: " + str(e))
        return []

# 如果這個腳本被當作主程式執行 (例如在 Dynamo Python Node 內直接測試)
if __name__ == "__main__":
    OUT = pick_and_read_dxf_text()
