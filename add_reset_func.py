import re
import os

def patch_file(filepath):
    if not os.path.exists(filepath):
        return
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Update reload button to reset storage
    content = content.replace("alert('Monstrare 看板已即時與本地 JSON 卡片庫保持同步！')", "resetKanbanState()")

    # Add resetKanbanState function before exportKanbanData
    func_str = """
    function resetKanbanState() {
      localStorage.removeItem('monstrare_cards_revit_green');
      location.reload();
    }
"""
    if "function resetKanbanState()" not in content:
        content = content.replace("function exportKanbanData()", func_str + "    function exportKanbanData()")

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"Patched {filepath}")

patch_file("kanban.html")
patch_file("tools/kanban/index.html")
