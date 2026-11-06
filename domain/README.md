# domain/ 領域知識目錄

此目錄存放 BIM 工作流程 SOP、法規檢討標準和設計規範。
每個 Domain 文件是 AI 的「專業知識」，搭配 Skill 觸發機制使用。

---

## Domain ↔ Skill 對照表

### 已有對應 Skill 的 Domain（20 個）

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
| `stair-compliance-check.md` | building-compliance | 樓梯法規、淨高、級高級深 |
| `qa-checklist.md` | qa-review | QA、驗證、檢查 |
| `parking-clearance-check.md` | parking-check | 停車場、車位淨空、parking |
| `parking-space-review.md` | parking-check | 停車位、數量、法定車位 |
| `wall-check.md` | wall-orientation-check | 牆壁方向、內外側 |
| `dependent-view-crop-workflow.md` | dependent-view-crop | 從屬視圖、分區出圖 |

### 不需要成為 Skill 的 Domain（31 個，含 README）

| Domain 文件 | 類型 | 不成為 Skill 的原因 |
|------------|------|-------------------|
| `lessons.md` | 經驗規則庫 | 知識參考文件，由 `/lessons` 指令維護，供其他 Skill 引用，不直接觸發 |
| `anti-lessons.md` | 負面教材庫（lessons.md 鏡像） | 記錄「看起來對、實際不能用」的誤判／誤導／中途資料型態與偵錯方法，append-only、去識別化，供其他 Domain/Skill 除錯參考，不由使用者直接觸發成 Skill |
| `room-boundary.md` | 技術概念文件 | 說明 Room 邊界處理的兩種方案（Area Scheme / Offset），是 `building-compliance` Skill 的背景知識，非獨立工作流程 |
| `session-context-guard.md` | AI 內部守衛 | 定義 AI 互動安全等級（L1-L3），是所有 Skill 的通用行為規範，不由使用者觸發 |
| `tool-capability-boundary.md` | 工具邊界定義 | 定義 MCP 工具能力與限制，防止 AI 嘗試超出能力的操作；[L11 的 ArchwizPF Lite 接入與能力邊界](tool-capability-boundary.md#archwizpf-lite)整理商品入口、Archicad 25／26／27／28、26 個工具及知識編排路徑，是 meta-reference |
| `path-maintenance-qa.md` | 內部維護指南 | 目錄重構後的路徑交叉參照檢查清單，是開發者維護用文件 |
| `core-reload-boundary.md` | 開發流程知識（opt-in） | 定義 Loader/Core 熱重載邊界、重啟條件與效率估算；對應 opt-in 開發分支，非 main 單一 csproj 架構，供進階開發者引用 |
| `skill-authoring-standard.md` | Skill 品質規範 | 定義 Skill 編寫標準與品質要求，是 meta-reference |
| `parking-auto-numbering.md` | 工具方法定義 | `renumber_parking_spaces` 的分類、排序與起始號規則；`parking-check` Skill 目前未引用，尚無專屬 Skill |
| `revit-fill-pattern-conversion.md` | 技術參考 | 填充圖案轉換規則，被多個 Skill 引用 |
| `room-numbering-workflow.md` | 輔助工作流程 | 房間自動編號邏輯，被其他 Skill 引用 |
| `room-surface-area-review.md` | 輔助工作流程 | 房間表面積與粉刷檢討，可被 `building-compliance` Skill 引用 |
| `finish-schedule-governance.md` | 輔助工作流程 | 粉刷明細表材料代碼治理 SOP，供房間粉刷相關工作流程引用，無專屬 Skill |
| `beam-slab-alignment.md` | 輔助工作流程 | 既有結構降樑貼齊樓板底邏輯，屬結構同步背景知識，無專屬 Skill |
| `ifc-structural-native-sync.md` | 輔助工作流程 | IFC 結構轉 Revit 原生梁柱同步邏輯，屬結構背景知識，無專屬 Skill |
| `quantity-takeoff-excel.md` | 技術參考 | 以房間為基礎的數量計算 Excel 共通方法，被數量相關工作流程引用 |
| `matchline-automation.md` | 輔助工作流程 | 銜接線自動標註邏輯，屬從屬視圖出圖流程背景知識，無專屬 Skill |
| `viewport-type-scale-sync.md` | 輔助工作流程 | 視埠標題類型依比例同步邏輯，屬圖紙管理背景知識，無專屬 Skill |
| `mep-space-demand-matrix.md` | 前期方法論（Skill 待補） | MEP 前期容量與空間收斂方法。核心載體 Space 目前無 MCP 工具支援（見 `tool-capability-boundary.md` L12），須先實機驗證 `create_view_schedule` 能否建立 Space 明細表，確認自動化程度後才產生對應 Skill |
| `archicad-adapter-boundary.md` | 轉譯邊界定義 | 定義 Archicad backend 的架構原則、執行前提與能力降級策略，是 opt-in Archicad 路線的 meta-reference，不由使用者直接觸發 |
| `archicad-terminology-map.md` | 名詞對照與單位合約 | Revit↔Archicad 名詞的中文導讀，以及讀取數值前的單位鐵律；權威正本在 `.claude/skills/archicad-skill-adapter/references/revit-archicad-terminology.md` |
| `archicad-portability-matrix.md` | 可攜性分級與進度 | A/B/C 分級判準與各 Wave 驗證狀態的中文速查；權威正本在 `docs/integrations/archicad-skill-portability.md` |
| `archicad-structural-dwg-sync.md` | Archicad 結構 DWG/DXF 逐層校準、建模、版次比對、Property 標記與報表 SOP | 尚無 Skill 引用，待接線 |
| `view-template-comparison.md` | 唯讀審計規範 | 定義多 View Template 差異矩陣的比對演算法與矩陣視圖 UI 規範；嚴格禁止任何覆蓋寫入，屬出圖期 QC 審計方法，尚無專屬 Skill |
| `revit-batch-family-shared-param-injection-spec.md` | 增益集架構規範 | 門窗族群共用參數批次注入的四段解耦架構（`.rvt` 走 CategoryBinding、`.rfa` 走 `FamilyManager.AddParameter`）；本研習庫未含其實做程式碼，供開發參考，無對應 MCP 工具與 Skill |
| `material-image-relinking.md` | 工具操作規範 | `relink_material_images` 的資料夾／檔名比對規則與衝突判定表，是單一工具的使用邊界說明，非多步驟工作流程 |
| `permit-floor-area-formula.md` | 工具方法定義 | `create_floor_area_formula` 把使用者已切好的矩形／三角形區域轉成送照面積計算式並對帳；不定義哪些面積該計入（見 `floor-area-review.md`），無專屬 Skill |
| `sequence-numbering.md` | 工具方法定義 | `create_sequence_numbers` 在視圖上放順序編號文字（可跳 4）；寫參數的編號見 `parking-auto-numbering.md`，無專屬 Skill |
| `beam-clear-height-check.md` | 檢討方法 | `check_beam_clear_height` 以射線量測梁底至樓板頂淨高與天花內空間；要求值由使用者給定、無預設，尚無專屬 Skill |
| `mep-spacing-clearance-check.md` | 檢討方法 | `check_mep_spacing` 檢查管線彼此（含保溫、含連結模型）與結構的外緣淨距；預留值由使用者給定、無預設，尚無專屬 Skill |
| `README.md` | 目錄導航 | 本檔案，不是工作流程 |

---

## 貢獻新 Domain

1. 建立 `domain/你的-workflow.md`
2. 建立對應 Skill：`.claude/skills/你的-skill/SKILL.md`
3. 提 PR，格式參考現有檔案

詳見 `CONTRIBUTING.md` 和 `docs/architecture-v2-module-system.md`
