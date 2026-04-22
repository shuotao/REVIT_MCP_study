# -*- coding: utf-8 -*-
from pyrevit import revit, DB, forms
import os
import sys

# Get the button index from the folder name
# folder name is PipeX.pushbutton, so X is at index 4 (0-indexed)
# Wait, folder name is "Pipe1.pushbutton", index of '1' is 4.
button_name = os.path.basename(os.path.dirname(__file__))
pipe_index = int(button_name.replace("Pipe", "").replace(".pushbutton", "")) - 1

# Add lib folder to sys.path
lib_path = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(__file__)))), "lib")
if lib_path not in sys.path:
    sys.path.append(lib_path)

import quick_access

doc = revit.doc
uidoc = revit.uidoc

def place_favorite_pipe():
    # 讀取對應索引的管材名稱
    target_type_name = quick_access.get_pipe_type_name(pipe_index)
    
    if not target_type_name:
        forms.alert("尚未設定第 {} 個常用管材！請點擊『設定』按鈕選取。".format(pipe_index + 1))
        return

    # 在模型中尋找該管材類型
    collector = DB.FilteredElementCollector(doc).OfClass(DB.Plumbing.PipeType) \
                  .WhereElementIsElementType()
    
    pipe_type = next((pt for pt in collector if pt.get_Parameter(DB.BuiltInParameter.SYMBOL_NAME_PARAM).AsString() == target_type_name), None)
    
    if not pipe_type:
        forms.alert("找不到名為 '{}' 的管材類型，請重新設定。".format(target_type_name))
        return

    # 設定當前選取的類型
    from Autodesk.Revit.UI import RevitCommandId, PostableCommand
    
    # 必須使用 Transaction 才能更改預設類型
    t = DB.Transaction(doc, "切換常用管材")
    t.Start()
    try:
        # 針對系統族群(管材)，必須使用 SetDefaultElementTypeId
        doc.SetDefaultElementTypeId(DB.ElementTypeGroup.PipeType, pipe_type.Id)
    except Exception as e:
        print("無法設定預設管材: {}".format(e))
    t.Commit()
    
    # 啟動繪製管線指令
    try:
        command_id = RevitCommandId.LookupPostableCommandId(PostableCommand.Pipe)
        uidoc.Application.PostCommand(command_id)
    except Exception as e:
        print("無法開啟繪製指令: {}".format(e))

if __name__ == "__main__":
    place_favorite_pipe()
