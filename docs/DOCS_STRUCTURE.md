# 文檔目錄結構說明

## 目錄職責

| 目錄 | 用途 | 讀者 |
|------|------|------|
| **`docs/tools/`** | 工具 API 技術文檔 | 開發者 |
| **`docs/workflows/`** | 工作流程設計文檔 | 開發者 |
| **`domain/`** | 領域知識與工作流程 SOP | AI Agent |
| **`教材/`** | 教學講義、投影片、學習筆記 | 學生 / 老師 |

---

## docs/tools/ - 技術文檔

**目的：** 記錄 MCP 工具的技術設計和 API 使用方式

**內容類型：**
- 工具設計規格
- API 參數說明
- 使用範例代碼

**目前檔案：**
- `override_element_color_design.md` - 元素圖形覆寫工具設計
- `override_graphics_examples.md` - 圖形覆寫 API 範例

---

## docs/workflows/ - 工作流程設計

**目的：** 記錄特定功能的開發設計過程與 Code Review

**目前檔案：**
- `corridor_code_review.md` - 走廊分析程式碼審查
- `corridor_dimension_review.md` - 走廊標註審查

---

## docs/ 根目錄 - 歷史紀錄

- `QUICK_TEST.md` - 外牆開口檢討功能測試文件
- `Recent_Update_Review.md` - GitHub PR/Issue 解析報告

---

## domain/ - 領域知識

**目的：** 給 AI 讀取的工作流程和業務知識

**內容類型：**
- 操作工作流程 SOP
- 業務規則與法規參考
- 品質檢查清單

**完整清單：** 請參考 `domain/README.md`

---

## 教材/ - 教學資源

**目的：** 24 小時深度課程的講義與學習材料

**內容類型：**
- 堂次講義（01~08）
- 投影片與圖片
- Skill 學習筆記與範例解說

**完整清單：** 請參考 `教材/README.md`

---

## 新增文檔時的選擇

| 如果要記錄... | 放在... |
|--------------|--------|
| 工具的 API 設計和參數 | `docs/tools/` |
| 如何一步步執行某任務（給 AI） | `domain/` |
| 業務規則和法規注意事項 | `domain/` |
| 代碼範例和技術細節 | `docs/tools/` |
| 教學講義或學習筆記 | `教材/` |
