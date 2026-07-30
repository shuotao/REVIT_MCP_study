import json
import os
import re

cards_dir = "tools/kanban/cards"

tasks_data = [
    {
        "id": "TASK-001",
        "title": "整理 TABC 1041 筆綠建材標章資料庫與 Schema 規範",
        "content": "### 任務說明\\n整理與正規化 tabc_master_database.json 內全量 1,141 筆台灣綠建材標章資料（含健康、生態、高性能、再生綠建材類別），建立標準化 JSON Schema 供 Revit 材料對接與 Skill 檢索。\\n\\n### Readiness (DOR)\\n- [x] problem_clear: 確定 TABC 全量資料結構與欄位規格\\n- [x] scope_defined: 定義標章編號、產品名稱、申請公司、有效期限、綠建材分類等欄位\\n\\n### Sign-off Gates (DOD)\\n- [x] architecture: 完成 JSON 資料庫結構正規化\\n- [x] test_passed: 建立自動化查詢驗證腳本與地端 Master 快取\\n- [x] code_review: 審查資料精確度與缺漏處理",
        "stage": "done",
        "risk": "medium",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-1: TABC 綠建材資料庫與法規模組化",
        "userStory": "US-1: 綠建材數據資料庫化",
        "track": "backend",
        "dependsOn": [],
        "order": 1,
        "readiness": {
            "problem_clear": True,
            "non_goals_clear": True,
            "acceptance_testable": True,
            "files_known": True,
            "scope_defined": True,
            "verification_contract": True,
            "human_approval_recorded": True
        },
        "gates": {
            "product": True,
            "ui": True,
            "architecture": True,
            "security": True,
            "test": True,
            "code_review": True
        }
    },
    {
        "id": "TASK-002",
        "title": "優化 TABC 綠建材地端檢索與 Showcase 平台之 UI/UX 介面與操作體驗",
        "content": "### 任務說明\\n優化 tabc_search.html 與 assets/green-material-showcase.html 的檢索介面與互動操作體驗。包含全量 1,141 筆綠建材標章檢索、100% 復刻原網頁紅字「產品規格與性能 (依 CNS 國家標準試驗、合格項目、試驗項目實測數據)」、網格與表格雙視圖切換、一鍵複製 Revit 共享參數 JSON 彈窗、彈窗完整呈現產品照片全貌 (Full Image contain)，以及專案材料 Set 組合管理與一鍵推送操作優化。\\n\\n### Readiness (DOR)\\n- [x] problem_clear: 確定全量檢索網頁與 Showcase 的 UI/UX 改善點\\n- [x] scope_defined: 包含 CNS 試驗項目高亮、雙視圖切換、彈窗產品照片全貌 (object-fit: contain)、Revit JSON 預覽 Modal 與 Set 管理\\n\\n### Sign-off Gates (DOD)\\n- [x] architecture: 完成網頁前端架構與 1,141 筆資料結構嵌入\\n- [x] ui: 完成紅字標題「產品規格與性能」視覺復刻與模態彈窗產品圖全貌\\n- [x] test_passed: 通過關鍵字搜尋、分類過濾、視圖切換與參數 JSON 匯出功能測試",
        "stage": "verify",
        "risk": "low",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-1: TABC 綠建材資料庫與法規模組化",
        "userStory": "US-2: 檢索平台介面與操作體驗優化",
        "track": "frontend",
        "dependsOn": ["TASK-001"],
        "order": 2,
        "readiness": {
            "problem_clear": True,
            "non_goals_clear": True,
            "acceptance_testable": True,
            "files_known": True,
            "scope_defined": True,
            "verification_contract": True,
            "human_approval_recorded": True
        },
        "gates": {
            "product": True,
            "ui": True,
            "architecture": True,
            "security": True,
            "test": True,
            "code_review": True
        }
    },
    {
        "id": "TASK-003",
        "title": "精準對映 Revit 元件類別 (Walls/Floors/Ceilings/Windows) 之綠建材共享參數規範",
        "content": "### 任務說明\\n依據 Revit 建築元件類別（牆面 Walls、地坪 Floors、天花板 Ceilings、門窗與幕牆 Windows/Doors/CurtainPanels、材料 Materials、非幾何輔助材料 Adhesives/Sealants），完成全量 11 大工程情境（包含牆體塗料、地磚 Hatch、單選非模型材料、門窗 RFA 備份等）之建築 Agent 優化分析，建立 19 個共享參數檔 GreenMaterial_SharedParams.txt 與全情境對映規範 Master 文件。\\n\\n### Readiness (DOR)\\n- [x] problem_clear: 完成 11 大 BIM 元件工程情境之 Agent 評估與優化建議\\n- [x] scope_defined: 定義標章身份、物化性能、算量評定、CNS 驗證、非幾何輔助材料等 5 大群組 19 個共享參數\\n\\n### Sign-off Gates (DOD)\\n- [x] architecture: 完成全情境（情境 1~11）建築 Agent 剖析與 Master Mapping 表格\\n- [x] test_passed: 通過 19 個共享參數 GUID 與語法自動化校驗\\n- [x] code_review: 完成 SharedParams 擴充與文件產出修訂",
        "stage": "verify",
        "risk": "medium",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-2: Revit 綠建材參數與材料庫 Mapping",
        "userStory": "US-3: Revit 元件級綠建材參數 schema 定義",
        "track": "integration",
        "dependsOn": ["TASK-001", "TASK-002"],
        "order": 3,
        "readiness": {
            "problem_clear": True,
            "non_goals_clear": True,
            "acceptance_testable": True,
            "files_known": True,
            "scope_defined": True,
            "verification_contract": True,
            "human_approval_recorded": True
        },
        "gates": {
            "product": True,
            "ui": True,
            "architecture": True,
            "security": True,
            "test": True,
            "code_review": True
        }
    },
    {
        "id": "TASK-004",
        "title": "AI Agent 綠建材推送 Revit 需求對齊與互動式計畫擬訂機制",
        "content": "### 任務說明\\n當使用者在地端檢索網頁中「勾選材料」或「將材料 Set」點擊推送至 Revit 時，系統將觸發回傳至 AI Agent (建築 Agent)。Agent 透過 /GMimport 指令讀取材料 Set 的真實 licno 清單（由 exported_material_sets.json 橋接），依據 TASK-003 最新 11 大工程情境（包含牆體/地磚/天花板/門窗.rfa/非幾何填縫劑等）動態分析，對映 19 個共享參數與厚度推判矩陣，自動擬訂「Revit 共享參數寫入計畫 (v3 專業版)」，並提供兩條簽核確認路徑。\\n\\n### 已完成優化功能\\n- [x] /GMimport 指令解析 licno 清單並精確匹配 Master DB\\n- [x] generate_revit_injection_plan.py v3 計畫擬訂引擎（升級對映 19 個共享參數）\\n- [x] 整合 TASK-003 11 大工程情境（自動推判 Finish/Substrate 位階與預設厚度）\\n- [x] 支援非幾何材料（接著劑/填縫劑/防水膜）寫入 Construction 群組屬性\\n- [x] write_back_to_set_manager() 路徑 A：對齊計畫回傳 exported_material_sets.json\\n- [x] 路徑 B 框架：回傳 + 啟動 TASK-005 自動注入\\n- [x] Set 管理器：🔄 重新整理按鈕即時從伺服器同步最新對齊計畫\\n- [x] 預備執行動作 SOP 條列式呈現\\n\\n### Sign-off Gates (DOD)\\n- [x] architecture: 完成 Web -> Agent -> JSON -> Web 全情境對映迴路\\n- [x] ui: Set 管理器顯示對齊狀態、專案用途、條列式 SOP\\n- [x] test_passed: /GMimport 11大工程情境與 19 個共享參數端到端對映測試通過",
        "stage": "verify",
        "risk": "medium",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": True,
        "createdAt": "2026-07-30",
        "epic": "Epic-2: Revit 綠建材參數與材料庫 Mapping",
        "userStory": "US-4: 互動式 Revit 推送計畫與需求對齊",
        "track": "integration",
        "dependsOn": ["TASK-003"],
        "order": 4,
        "readiness": {
            "problem_clear": True,
            "non_goals_clear": True,
            "acceptance_testable": True,
            "files_known": True,
            "scope_defined": True,
            "verification_contract": True,
            "human_approval_recorded": True
        },
        "gates": {
            "product": True,
            "ui": True,
            "architecture": True,
            "security": True,
            "test": True,
            "code_review": True
        }
    },
    {
        "id": "TASK-005",
        "title": "開發 Revit 材料庫自動對接與注入指令 (pyRevit / MCP Bridge)",
        "content": "### 任務說明\\n開發 pyRevit 腳本與 Revit MCP 工具介面，根據 AI Agent 與使用者確定的「推送 Revit 執行計畫」，自動將 TABC 綠建材資料庫與 16 個共享參數批量寫入 Revit 模型中現有牆面/地面/天花板材料，並自動比對與提示無標章之舊材料。\\n\\n### Readiness (DOR)\\n- [x] problem_clear: 確立 Revit Material API 讀寫與共享參數填入機制\\n- [x] scope_defined: 支援依材料名稱模糊比對 TABC 標章資料庫\\n\\n### Sign-off Gates (DOD)\\n- [ ] architecture: 完成 pyRevit 腳本與 Python 自動對接工具\\n- [ ] test_passed: 於測試 Revit 模型成功執行批次材料注入\\n- [ ] code_review: 審查 API 例外處理與模型效能影響",
        "stage": "ready",
        "risk": "high",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": True,
        "createdAt": "2026-07-30",
        "epic": "Epic-2: Revit 綠建材參數與材料庫 Mapping",
        "userStory": "US-5: Revit 材料自動化寫入工具",
        "track": "integration",
        "dependsOn": ["TASK-004"],
        "order": 5,
        "readiness": {
            "problem_clear": True,
            "non_goals_clear": False,
            "acceptance_testable": True,
            "files_known": True,
            "scope_defined": True,
            "verification_contract": True,
            "human_approval_recorded": False
        },
        "gates": {
            "product": False,
            "ui": False,
            "architecture": False,
            "security": False,
            "test": False,
            "code_review": False
        }
    },
    {
        "id": "TASK-006",
        "title": "撰寫 green-material-takeoff SKILL.md (領域知識與 AI 觸發條件)",
        "content": "### 任務說明\\n撰寫 `.agents/skills/green-material-takeoff/SKILL.md`，定義 AI 助手在遇到使用者詢問「檢討綠建材率」、「匯出綠建材計算書」、「標註模型綠建材」時的觸發語條件、工作流程、領域知識說明與腳本呼叫規範。\\n\\n### Readiness (DOR)\\n- [x] problem_clear: 確定 Skill YAML frontmatter 與 markdown 規範\\n- [x] scope_defined: 包含綠建材算量、評定計算、報表產出之完整步驟\\n\\n### Sign-off Gates (DOD)\\n- [ ] architecture: 完成 SKILL.md 寫作與規範審查\\n- [ ] test_passed: 於 Antigravity / Agent 環境下測試觸發率\\n- [ ] code_review: 審查自然語言觸發與指令精確度",
        "stage": "ready",
        "risk": "medium",
        "owner": "shuotao",
        "agent": "antigravity",
        "approvalRequired": False,
        "createdAt": "2026-07-30",
        "epic": "Epic-3: 綠建材 Skill 開發與轉譯",
        "userStory": "US-6: 綠建材 Skill 包裝",
        "track": "n/a",
        "dependsOn": ["TASK-004"],
        "order": 6,
        "readiness": {
            "problem_clear": True,
            "non_goals_clear": False,
            "acceptance_testable": True,
            "files_known": True,
            "scope_defined": True,
            "verification_contract": True,
            "human_approval_recorded": False
        },
        "gates": {
            "product": False,
            "ui": False,
            "architecture": False,
            "security": False,
            "test": False,
            "code_review": False
        }
    },
    {
        "id": "TASK-007",
        "title": "套用 hj-pr-proposal 將綠建材 Skill 轉譯成 HJPLUS 雙層草案",
        "content": "### 任務說明\\n運用 `hj-pr-proposal` SOP，將綠建材 Skill 拆解轉譯為 HJPLUS 台灣建築師知識庫標準格式：上層中文 `domain.md`（綠建材評定法規與 BIM 實務指南）與下層英文 `SKILL.md`（AI Agent 執行 SOP），並產出完整 PR 提案草稿。\\n\\n### Readiness (DOR)\\n- [x] problem_clear: 遵循 HJPLUS 雙層結構規格\\n- [x] scope_defined: 產生可審核的 PR 說明與檔案結構\\n\\n### Sign-off Gates (DOD)\\n- [ ] architecture: 產出正確的 `domain.md` 與 `SKILL.md` 雙層文件\\n- [ ] test_passed: 經作者確認提案結構與註記正確\\n- [ ] code_review: 符合 HJPLUS PR 規範與無痛導入品質",
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
        "order": 7,
        "readiness": {
            "problem_clear": False,
            "non_goals_clear": False,
            "acceptance_testable": False,
            "files_known": False,
            "scope_defined": False,
            "verification_contract": False,
            "human_approval_recorded": False
        },
        "gates": {
            "product": False,
            "ui": False,
            "architecture": False,
            "security": False,
            "test": False,
            "code_review": False
        }
    },
    {
        "id": "TASK-008",
        "title": "執行 Monstrare DOD 驗證合約與綠建材 Skill 端到端整合測試",
        "content": "### 任務說明\\n進行 Monstrare 驗證門檻 (DOR/DOD) 檢查，跑通綠建材 Skill 的端到端流程 (TABC 資料檢索 -> Agent 需求對齊與計畫確認 -> Revit 參數匹配 -> 寫入驗證 -> 驗證門檻點收)，確保整體交付符合要求。\\n\\n### Readiness (DOR)\\n- [x] problem_clear: TASK-001 ~ TASK-007 均完成並準備點收\\n- [x] scope_defined: 包含端到端全流程驗證測試與驗證報告產出\\n\\n### Sign-off Gates (DOD)\\n- [ ] architecture: 架構審查符合 AI Agent/MCP 規範\\n- [ ] test_passed: 端到端自動化測試無報錯\\n- [ ] code_review: Monstrare DoD gate 簽核完畢並移至 Done 階段",
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
        "order": 8,
        "readiness": {
            "problem_clear": False,
            "non_goals_clear": False,
            "acceptance_testable": False,
            "files_known": False,
            "scope_defined": False,
            "verification_contract": False,
            "human_approval_recorded": False
        },
        "gates": {
            "product": False,
            "ui": False,
            "architecture": False,
            "security": False,
            "test": False,
            "code_review": False
        }
    }
]

# Clean up any stale card files higher than current task count
valid_ids = {t['id'] for t in tasks_data}
for filename in os.listdir(cards_dir):
    if filename.endswith('.json'):
        cid = filename[:-5]
        if cid not in valid_ids:
            os.remove(os.path.join(cards_dir, filename))
            print(f"Removed stale card file: {filename}")

# Write JSON card files
for t in tasks_data:
    filepath = os.path.join(cards_dir, f"{t['id']}.json")
    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(t, f, ensure_ascii=False, indent=2)

print(f"Updated {len(tasks_data)} card JSON files in {cards_dir}.")

def update_kanban_html(html_path):
    if not os.path.exists(html_path):
        return
    with open(html_path, 'r', encoding='utf-8') as f:
        content = f.read()

    cards_json_str = json.dumps(tasks_data, ensure_ascii=False, indent=2)
    
    content = re.sub(
        r'let cardsData = \[.*?\];',
        f'let cardsData = {cards_json_str};',
        content,
        flags=re.DOTALL
    )
    content = re.sub(
        r'const initialCards = \[.*?\];',
        f'const initialCards = {cards_json_str};',
        content,
        flags=re.DOTALL
    )

    with open(html_path, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"Successfully updated {html_path}!")

update_kanban_html("kanban.html")
update_kanban_html("tools/kanban/index.html")
