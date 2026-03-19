# domain/ 領域知識目錄

此目錄存放 BIM 工作流程 SOP、法規檢討標準和設計規範。
每個 Domain 文件是 AI 的「專業知識」，搭配 Skill 觸發機制使用。

---

## Domain ↔ Skill 對照表

### 已有對應 Skill 的 Domain（18 個）

| Domain 文件 | 對應 Skill | 觸發關鍵字 |
|------------|-----------|-----------|
| `fire-rating-check.md` | fire-safety-check | 防火、耐燃、fire rating |
| `corridor-analysis-protocol.md` | fire-safety-check | 走廊、逃生、corridor |
| `exterior-wall-opening-check.md` | fire-safety-check | 外牆開口、鄰地距離、Article 45 |
| `daylight-area-check.md` | building-compliance | 採光、daylight、§41 |
| `floor-area-review.md` | building-compliance | 容積、FAR、樓地板面積 |
| `element-query-workflow.md` | element-query | 查詢元素、filter、上色 |
| `element-coloring-workflow.md` | element-coloring | 上色、顏色標示、color code |
| `curtain-wall-pattern.md` | curtain-wall | 帷幕牆、面板排列 |
| `facade-generation.md` | facade-generation | 立面、facade、弧形面板 |
| `smoke-exhaust-review.md` | smoke-exhaust | 排煙、排煙窗、§101、§188 |
| `auto-dimension-workflow.md` | auto-dimension | 自動標註、尺寸標註 |
| `detail-component-sync.md` | detail-component-sync | 詳圖同步、detail header |
| `sheet-viewport-management.md` | sheet-management | 圖紙、viewport、編號 |
| `stair-hidden-line-workflow.md` | stair-hidden-line | 樓梯、隱藏線、stair |
| `qa-checklist.md` | qa-review | QA、驗證、檢查 |
| `parking-clearance-check.md` | parking-check | 停車場、車位淨空、parking |
| `parking-space-review.md` | parking-check | 停車位、數量、法定車位 |
| `wall-check.md` | wall-orientation-check | 牆壁方向、內外側 |
| `dependent-view-crop-workflow.md` | dependent-view-crop | 從屬視圖、分區出圖 |

### 不需要成為 Skill 的 Domain（6 個）

| Domain 文件 | 類型 | 不成為 Skill 的原因 |
|------------|------|-------------------|
| `lessons.md` | 經驗規則庫 | 知識參考文件，由 `/lessons` 指令維護，供其他 Skill 引用，不直接觸發 |
| `room-boundary.md` | 技術概念文件 | 說明 Room 邊界處理的兩種方案（Area Scheme / Offset），是 `building-compliance` Skill 的背景知識，非獨立工作流程 |
| `session-context-guard.md` | AI 內部守衛 | 定義 AI 互動安全等級（L1-L3），是所有 Skill 的通用行為規範，不由使用者觸發 |
| `tool-capability-boundary.md` | 工具邊界定義 | 定義 MCP 工具「不能做的事」（L1-L5 能力等級），防止 AI 嘗試超出能力的操作，是 meta-reference |
| `path-maintenance-qa.md` | 內部維護指南 | 目錄重構後的路徑交叉參照檢查清單，是開發者維護用文件 |
| `README.md` | 目錄導航 | 本檔案，不是工作流程 |

---

## 貢獻新 Domain

1. 建立 `domain/你的-workflow.md`
2. 建立對應 Skill：`.claude/skills/你的-skill/SKILL.md`
3. 提 PR，格式參考現有檔案

詳見 `CONTRIBUTING.md` 和 `docs/architecture-v2-module-system.md`
