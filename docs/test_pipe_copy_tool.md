# TestPipeCopy (測試用：複製直管與法蘭系統)

## 工具簡介
這是為了快速建置「法蘭 + 5m直管 + 法蘭」測試環境而開發的專屬 pyRevit 工具。
當在 Revit 中遇到需要重複測試管線打斷、長度分配、或是法蘭位置放置時，手動調整長度並複製對向法蘭較為繁瑣。此工具能將選定的一個法蘭與一段直管自動處理成標準測試長度。

## 執行流程
1. **選取基準物件**：使用者在畫面上選取「一根直管」加「一個法蘭」。
2. **偏移複製**：腳本自動將您選取的管線往南（Y 軸負向）距離原位置偏移 20cm。
3. **長度標準化**：尋找管線上靠近法蘭的一端作為「固定端」，另一端則縮減或延伸，強制讓直管的長度變為精準的 5000 mm (5米)。
4. **對稱補齊**：自動複製基準法蘭至直管的另一端，並沿著 Z 軸（或法線軸）旋轉 180 度做翻面對齊，使其方向正確朝外。

## 核心程式碼紀實 (`script.py`)
```python
# -*- coding: utf-8 -*-
from pyrevit import revit, DB, forms, script
import math

doc = revit.doc
uidoc = revit.uidoc

refs = uidoc.Selection.GetElementIds()

# 南移 20cm (Y軸負向)
vec_south = DB.XYZ(0, -200.0 / 304.8, 0)
target_len_ft = 5000.0 / 304.8

with revit.Transaction("測試：複製出 法蘭+5m直管+法蘭"):
    # 1. 複製並偏移
    new_ids = DB.ElementTransformUtils.CopyElements(doc, refs, vec_south)
    doc.Regenerate()

    new_pipe = None
    new_flange1 = None
    for eid in new_ids:
        elem = doc.GetElement(eid)
        if isinstance(elem, DB.Plumbing.Pipe):
            new_pipe = elem
        elif isinstance(elem, DB.FamilyInstance):
            new_flange1 = elem

    if not new_pipe or not new_flange1:
        forms.alert("請確保您在畫面上選取了『一根直管』及『一個法蘭』再執行此功能！", exitscript=True)

    # 2. 判斷法蘭方向並重設直管端點
    curve = new_pipe.Location.Curve
    pt0 = curve.GetEndPoint(0)
    pt1 = curve.GetEndPoint(1)
    flange_pt = new_flange1.Location.Point
    
    pipe_dir = (pt1 - pt0).Normalize()
    if flange_pt.DistanceTo(pt0) < flange_pt.DistanceTo(pt1):
        fixed_pt = pt0
        free_pt = pt0 + pipe_dir * target_len_ft
    else:
        fixed_pt = pt1
        free_pt = pt1 - pipe_dir * target_len_ft

    new_pipe.Location.Curve = DB.Line.CreateBound(fixed_pt, free_pt)
    doc.Regenerate()

    # 3. 對稱補齊法蘭並旋轉 180 度
    move_vec = free_pt - fixed_pt
    new_flange2_id = DB.ElementTransformUtils.CopyElement(doc, new_flange1.Id, move_vec)[0]
    
    up_vec = DB.XYZ.BasisZ
    if abs(pipe_dir.DotProduct(up_vec)) > 0.99:
        up_vec = DB.XYZ.BasisX
        
    rot_axis_dir = pipe_dir.CrossProduct(up_vec).Normalize()
    rot_axis = DB.Line.CreateUnbound(free_pt, rot_axis_dir)
    DB.ElementTransformUtils.RotateElement(doc, new_flange2_id, rot_axis, math.pi)
    
    # 結束後自動選取新物件以利連續測試
    uidoc.Selection.SetElementIds(new_ids)

```

> **用途**：可以作為日後開發管段模組化 (Modular Piping) 時的產生測試資料指令，或擴充寫進 MCP Server 工具中執行。
