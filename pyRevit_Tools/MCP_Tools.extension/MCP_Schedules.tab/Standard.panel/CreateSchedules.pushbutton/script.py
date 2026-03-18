# -*- coding: utf-8 -*-
"""Create standard MEP Procurement schedules based on MCP protocol V1.1."""

__title__ = '建立標準\n明細表'
__author__ = 'MCP'

from Autodesk.Revit import DB
from pyrevit import revit, forms

# 定義標準協議內容 (V1.1)
PROTOCOLS = {
    "Pipes": {
        "Name": "MCP_管材採購明細表(V1.1)",
        "Category": DB.BuiltInCategory.OST_PipeCurves,
        "Fields": ["標記", "製造商", "系統類型", "族群", "類型", "大小", "外徑", "長度", "參考樓層", "工項編碼", "備註"]
    },
    "Pipe Fittings": {
        "Name": "MCP_管配件採購明細表(V1.1)",
        "Category": DB.BuiltInCategory.OST_PipeFitting,
        "Fields": ["標記", "製造商", "族群", "描述", "大小", "數量", "工項編碼", "樓層", "備註"]
    },
    "Pipe Accessories": {
        "Name": "MCP_管附件採購明細表(V1.1)",
        "Category": DB.BuiltInCategory.OST_PipeAccessory,
        "Fields": ["標記", "製造商", "系統類型", "族群", "類型", "描述", "大小", "數量", "工項編碼", "樓層", "備註"]
    }
}

def create_standard_schedule(doc, data):
    """建立明細表並根據協議添加欄位"""
    try:
        cat_id = DB.ElementId(int(data["Category"]))
        schedule = DB.ViewSchedule.CreateSchedule(doc, cat_id)
        schedule.Name = data["Name"]

        definition = schedule.Definition
        schedulable_fields = definition.GetSchedulableFields()

        print("--- 建立明細表: {} ---".format(data["Name"]))
        for field_name in data["Fields"]:
            found_field = None
            for sf in schedulable_fields:
                if sf.GetName(doc) == field_name:
                    found_field = sf
                    break

            if found_field:
                definition.AddField(found_field)
                print("已添加欄位: [{}]".format(field_name))
            else:
                print("! 警告: 找不到欄位 [{}]，請檢查參數是否存在".format(field_name))
        return True
    except Exception as e:
        print("! 錯誤: 建立 {} 時發生異常: {}".format(data["Name"], str(e)))
        return False

# 主程式執行
try:
    doc = revit.doc
    with revit.Transaction("MCP: Create Standard Schedules"):
        success_count = 0
        for key in ["Pipes", "Pipe Fittings", "Pipe Accessories"]:
            if create_standard_schedule(doc, PROTOCOLS[key]):
                success_count += 1

        if success_count > 0:
            forms.alert("成功建立 {} 份標準明細表！".format(success_count), title="執行結果")
        else:
            forms.alert("未成功建立任何明細表，請查看輸出視窗。", title="執行結果", icon="warning")

except Exception as ex:
    forms.alert("程式執行發生嚴重錯誤:\n{}".format(str(ex)), title="Error")
