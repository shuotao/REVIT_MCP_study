# -*- coding: utf-8 -*-
from pyrevit import revit, DB, forms
import os
import sys

# Add lib folder to sys.path
lib_path = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(__file__))), "lib")
if lib_path not in sys.path:
    sys.path.append(lib_path)

import quick_access

doc = revit.doc

def select_pipe_types():
    # Get all pipe types in the model
    pipe_types = DB.FilteredElementCollector(doc).OfClass(DB.Plumbing.PipeType).ToElements()
    type_names = sorted([pt.get_Parameter(DB.BuiltInParameter.SYMBOL_NAME_PARAM).AsString() for pt in pipe_types])
    
    if not type_names:
        forms.alert("專案中找不到任何管材類型！", title="錯誤")
        return

    current_config = quick_access.get_config()
    current_favorites = current_config.get("pipe_types", [None] * 5)
    
    new_favorites = []
    available_types = list(type_names)
    
    for i in range(5):
        default_val = current_favorites[i] if i < len(current_favorites) else None
        # Ensure default_val is still available, if not, use None
        if default_val and default_val not in available_types:
            default_val = None
            
        selected = forms.SelectFromList.show(
            available_types,
            title="請選擇第 {} 個常用管材 (排除已選項目)".format(i + 1),
            multiselect=False,
            default=default_val
        )
        if selected:
            new_favorites.append(selected)
            # Exclude from next selections
            if selected in available_types:
                available_types.remove(selected)
        else:
            new_favorites.append(default_val)
            if default_val in available_types:
                available_types.remove(default_val)
            
    # Save the new configuration
    quick_access.save_config({"pipe_types": new_favorites})
    
    # Update ribbon titles immediately
    quick_access.update_ribbon_titles()
    
    forms.toast("常用管材與按鈕名稱已更新！", title="Quick Access")

if __name__ == "__main__":
    select_pipe_types()
