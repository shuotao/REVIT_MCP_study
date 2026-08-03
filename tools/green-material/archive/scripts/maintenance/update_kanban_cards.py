import json
import os
import glob

cards_dir = "tools/kanban/cards"

tasks_data = [
    {
        "id": "TASK-001",
        "title": "整理 TABC 1041 筆綠建材標章資料庫與 Schema 規範",
        "content": "### 任務說明\n整理與正規化 `tabc_master_database.json` 內 1,041 筆 (全量 1,141 筆) 台灣綠建材標章資料（含健康、生態、高性能、再生綠建材類別），建立標準化 JSON Schema 供 Revit 材料對接與 Skill 檢索。\n\n### Readiness (DOR)\n- [x] problem_clear: 確定 TABC 全量資料結構與欄位規格\n- [x] scope_defined: 定義標章編號、產品名稱、申請公司、有效期限、綠建材分類等欄位\n\n### Sign-off Gates (DOD)\n- [x] architecture: 完成 JSON 資料庫結構正規化\n- [x] test_passed: 建立自動化查詢驗證腳本與地端 Master 快取\n- [x] code_review: 審查資料精確度與缺漏處理",
        "stage": "ready",
        "risk": "medium",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-1: TABC 綠建材資料庫與法規模組化",
        "userStory": "US-1: 綠建材數據資料庫化",
        "track": "backend",
        "dependsOn": [],
        "order": 1
    },
    {
        "id": "TASK-002",
        "title": "優化 TABC 綠建材地端檢索與 Showcase 平台之 UI/UX 介面與操作體驗",
        "content": "### 任務說明\n優化 `tabc_search.html` 與 `assets/green-material-showcase.html` 的檢索介面與互動操作體驗。包含全量 1,141 筆綠建材標章檢索、100% 復刻原網頁紅字「產品規格與性能 (依 CNS 國家標準試驗、合格項目、試驗項目實測數據)」、網格與表格雙視圖切換、一鍵複製 Revit 共享參數 JSON 彈窗，以及專案材料 Set 組合管理與一鍵推送操作優化。\n\n### Readiness (DOR)\n- [x] problem_clear: 確定全量檢索網頁與 Showcase 的 UI/UX 改善點\n- [x] scope_defined: 包含 CNS 試驗項目高亮、雙視圖切換、Revit JSON 預覽 Modal 與 Set 管理\n\n### Sign-off Gates (DOD)\n- [x] architecture: 完成網頁前端架構與 1,141 筆資料結構嵌入\n- [x] ui: 完成紅字標題「產品規格與性能」視覺復刻與模態彈窗\n- [x] test_passed: 通過關鍵字搜尋、分類過濾、視圖切換與參數 JSON 匯出功能測試",
        "stage": "ready",
        "risk": "low",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-1: TABC 綠建材資料庫與法規模組化",
        "userStory": "US-2: 檢索平台介面與操作體驗優化",
        "track": "frontend",
        "dependsOn": ["TASK-001"],
        "order": 2
    },
    {
        "id": "TASK-003",
        "title": "精準對映 Revit 元件類別 (Walls/Floors/Ceilings/Windows) 之綠建材共享參數規範",
        "content": "### 任務說明\n重新依據 Revit 建築元件類別（牆面 Walls、地坪 Floors、天花板 Ceilings、門窗與幕牆 Windows/Doors/CurtainPanels、材料 Materials），將台灣 TABC 綠建材標章產品精準分類，釐清「上層標章履歷常數 (Identity & CNS Base)」與「下層元件動態算量屬性 (Element & Spatial Performance)」關係，並建立 16 個共享參數檔 `GreenMaterial_SharedParams.txt` 與結構分析文件。\n\n### Readiness (DOR)\n- [x] problem_clear: 完成 Revit 5 大品類與綠建材分類對映及上下層邏輯釐清\n- [x] scope_defined: 定義通用身分、物理化學性能、評定算量、CNS 試驗等 4 大群組 16 個共享參數\n\n### Sign-off Gates (DOD)\n- [x] architecture: 完成元件視角與上下層參數 Mapping 表格規範\n- [x] test_passed: 通過 Python GUID 與語法自動化校驗\n- [x] code_review: 完成共享參數檔匯出與文件產出",
        "stage": "ready",
        "risk": "medium",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-2: Revit 綠建材參數與材料庫 Mapping",
        "userStory": "US-3: Revit 元件級綠建材參數 schema 定義",
        "track": "integration",
        "dependsOn": ["TASK-001", "TASK-002"],
        "order": 3
    },
    {
        "id": "TASK-004",
        "title": "開發 Revit 材料庫自動對接與注入指令 (pyRevit / MCP Bridge)",
        "content": "### 任務說明\n開發 pyRevit 腳本與 Revit MCP 工具介面，自動將 TABC 綠建材資料庫與 16 個共享參數批量寫入 Revit 模型中現有牆面/地面/天花板材料，並自動比對與提示無標章之舊材料。\n\n### Readiness (DOR)\n- [x] problem_clear: 確立 Revit Material API 讀寫與共享參數填入機制\n- [x] scope_defined: 支援依材料名稱模糊比對 TABC 標章資料庫\n\n### Sign-off Gates (DOD)\n- [ ] architecture: 完成 pyRevit 腳本與 Python 自動對接工具\n- [ ] test_passed: 於測試 Revit 模型成功執行批次材料注入\n- [ ] code_review: 審查 API 例外處理與模型效能影響",
        "stage": "backlog",
        "risk": "high",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": True,
        "createdAt": "2026-07-30",
        "epic": "Epic-2: Revit 綠建材參數與材料庫 Mapping",
        "userStory": "US-4: Revit 材料自動化寫入工具",
        "track": "integration",
        "dependsOn": ["TASK-003"],
        "order": 4
    },
    {
        "id": "TASK-005",
        "title": "撰寫 green-material-takeoff SKILL.md (領域知識與 AI 觸發條件)",
        "content": "### 任務說明\n撰寫 `.agents/skills/green-material-takeoff/SKILL.md`，定義 AI 助手在遇到使用者詢問「檢討綠建材率」、「匯出綠建材計算書」、「標註模型綠建材」時的觸發語條件、工作流程、領域知識說明與腳本呼叫規範。\n\n### Readiness (DOR)\n- [x] problem_clear: 確定 Skill YAML frontmatter 與 markdown 規範\n- [x] scope_defined: 包含綠建材算量、評定計算、報表產出之完整步驟\n\n### Sign-off Gates (DOD)\n- [ ] architecture: 完成 SKILL.md 寫作與規範審查\n- [ ] test_passed: 於 Antigravity / Agent 環境下測試觸發率\n- [ ] code_review: 審查自然語言觸發與指令精確度",
        "stage": "backlog",
        "risk": "medium",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-3: 綠建材 Skill 開發與轉譯",
        "userStory": "US-5: 綠建材 Skill 包裝",
        "track": "n/a",
        "dependsOn": ["TASK-003"],
        "order": 5
    },
    {
        "id": "TASK-006",
        "title": "建立綠建材面積算量與 Excel/Schedule 評定報告產出工具",
        "content": "### 任務說明\n開發算量輔助腳本 `calculate_green_material_ratio.py`，從 Revit 提取所有牆面、地板、天花板面積與材料參數，計算總裝修面積與綠建材面積比例，並自動生成符合綠建築標章審查格式的 Excel 計算書。\n\n### Readiness (DOR)\n- [x] problem_clear: 確定計算書產出欄位與公式\n- [x] scope_defined: 支援產出 Excel 報表與 Markdown 摘要\n\n### Sign-off Gates (DOD)\n- [ ] architecture: 完成 算量與報表生成腳本開發\n- [ ] test_passed: 使用測試資料庫產出正確格式之 Excel 報表\n- [ ] code_review: 審查算量精度與表格呈現風格",
        "stage": "backlog",
        "risk": "medium",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-3: 綠建材 Skill 開發與轉譯",
        "userStory": "US-6: 綠建材算量與報表工具",
        "track": "backend",
        "dependsOn": ["TASK-004"],
        "order": 6
    },
    {
        "id": "TASK-007",
        "title": "套用 hj-pr-proposal 將綠建材 Skill 轉譯成 HJPLUS 雙層草案",
        "content": "### 任務說明\n運用 `hj-pr-proposal` SOP，將綠建材 Skill 拆解轉譯為 HJPLUS 台灣建築師知識庫標準格式：上層中文 `domain.md`（綠建材評定法規與 BIM 實務指南）與下層英文 `SKILL.md`（AI Agent 執行 SOP），並產出完整 PR 提案草稿。\n\n### Readiness (DOR)\n- [x] problem_clear: 遵循 HJPLUS 雙層結構規格\n- [x] scope_defined: 產生可審核的 PR 說明與檔案結構\n\n### Sign-off Gates (DOD)\n- [ ] architecture: 產出正確的 `domain.md` 與 `SKILL.md` 雙層文件\n- [ ] test_passed: 經作者確認提案結構與註記正確\n- [ ] code_review: 符合 HJPLUS PR 規範與無痛導入品質",
        "stage": "backlog",
        "risk": "medium",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-4: HJPLUS 知識庫 PR 與 Monstrare 驗證合約",
        "userStory": "US-7: HJPLUS 知識庫貢獻草案",
        "track": "integration",
        "dependsOn": ["TASK-005", "TASK-006"],
        "order": 7
    },
    {
        "id": "TASK-008",
        "title": "執行 Monstrare DOD 驗證合約與綠建材 Skill 端到端整合測試",
        "content": "### 任務說明\n進行 Monstrare 驗證門檻 (DOR/DOD) 檢查，跑通綠建材 Skill 的端到端流程 (TABC 資料檢索 -> Revit 參數匹配 -> 法規面積算量 -> 生成報告 -> 驗證門檻點收)，確保整體交付符合要求。\n\n### Readiness (DOR)\n- [x] problem_clear: TASK-001 ~ TASK-007 均完成並準備點收\n- [x] scope_defined: 包含端到端全流程驗證測試與驗證報告產出\n\n### Sign-off Gates (DOD)\n- [ ] architecture: 架構審查符合 AI Agent/MCP 規範\n- [ ] test_passed: 端到端自動化測試無報錯且綠建材率計算精確\n- [ ] code_review: Monstrare DoD gate 簽核完畢並移至 Done 階段",
        "stage": "backlog",
        "risk": "high",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": True,
        "createdAt": "2026-07-30",
        "epic": "Epic-4: HJPLUS 知識庫 PR 與 Monstrare 驗證合約",
        "userStory": "US-8: 端到端整合測試與驗證點收",
        "track": "integration",
        "dependsOn": ["TASK-007"],
        "order": 8
    }
]

# Write JSON card files
for t in tasks_data:
    filepath = os.path.join(cards_dir, f"{t['id']}.json")
    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(t, f, ensure_ascii=False, indent=2)

print(f"Updated {len(tasks_data)} card JSON files in {cards_dir}.")

# Inject into kanban.html and tools/kanban/index.html
def update_kanban_html(html_path):
    if not os.path.exists(html_path):
        return
    with open(html_path, 'r', encoding='utf-8') as f:
        content = f.read()

    cards_json_str = json.dumps(tasks_data, ensure_ascii=False, indent=2)
    
    import re
    new_content = re.sub(
        r'const initialCards = \[.*?\];',
        f'const initialCards = {cards_json_str};',
        content,
        flags=re.DOTALL
    )

    with open(html_path, 'w', encoding='utf-8') as f:
        f.write(new_content)
    print(f"Successfully updated {html_path}!")

update_kanban_html("kanban.html")
update_kanban_html("tools/kanban/index.html")
