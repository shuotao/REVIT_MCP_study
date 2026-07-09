---
name: batch-apply-view-template
description: "批次將指定的 ViewTemplate 套用到多個 view：支援以圖紙（sheets）或 view 名稱/ID 選取目標 view，可搭配 viewTypeFilter 過濾類型，並提供 dryRun 預覽 + skipIfSameTemplate 避免重複套用。適用於：跨專案複製後統一視圖樣式、出圖前批次校正 view template、把整本圖集的 view 改成統一 template、補齊新建 view 的樣板設定。觸發條件：使用者提到批次套用 view template、批次改 view template、視圖樣版批次修改、整套圖紙套同一個樣版、apply view template to sheets、batch view template、change view template for sheets、把這幾張 sheet 的 view 改成 XX template。工具：get_view_templates、get_all_sheets、get_sheet_viewport_details、get_viewport_map、batch_apply_view_template。"
---

# 批次套用 ViewTemplate 到多個 view

## Prerequisites

1. Revit 已開啟目標專案
2. 目標 ViewTemplate 已存在於專案中（不會自動建立）
3. MCP Server 已連線：
   ```
   ToolSearch select:mcp__revit-mcp__get_view_templates,mcp__revit-mcp__batch_apply_view_template,mcp__revit-mcp__get_sheet_viewport_details,mcp__revit-mcp__get_viewport_map
   ```

## 概念

ViewTemplate 控制 view 的視覺樣式（DetailLevel、HiddenCategories、Filters、Scale、CropBox 設定等）。把多個 view 套到同一個 template，可確保整套圖紙視覺一致。

`batch_apply_view_template` 用 **additive union** 方式選 view — 多種選擇器可同時使用，結果取聯集並去重：

| 選擇器 | 用途 |
|--------|------|
| `sheetIds` / `sheetNumbers` | 套用到圖紙上**所有 viewport** 對應的 view（最常用） |
| `viewIds` | 精確指定 view ElementId 清單 |
| `viewNames` | 精確匹配 view 名稱清單 |
| `viewNameContains` | 名稱子字串匹配（最方便但需注意誤配） |

可選 `viewTypeFilter`（如 `["FloorPlan", "Section"]`）做最後過濾。

## Workflow

### 步驟 1：確認目標 ViewTemplate

呼叫 `get_view_templates` 列出專案所有 template，跟使用者對齊：

- 確認目標 template 名稱（大小寫/中英文需完全一致）
- 確認 template 的 ViewType — 套到不相容的 view type 時 Revit 可能無聲忽略部分設定
- 記下 template ElementId（更穩定，不受改名影響）

> 若使用者直接給名稱，可不先列。`batch_apply_view_template` 找不到時會回列出所有可用 template 供 discovery。

### 步驟 2：盤點目標 view 範圍

跟使用者確認**選 view 的方式**：

**情境 A：以圖紙為單位（最常見）**
> 「把 A101–A105 這五張圖紙裡所有 view 都套上『建築平面圖』template」

- 直接用 `sheetNumbers: ["A101", "A102", "A103", "A104", "A105"]`
- 或先 `get_all_sheets` 撈出來確認

**情境 B：以 view 名稱模式**
> 「所有『(開審)』後綴的 view 都套上 XX template」

- 用 `viewNameContains: "(開審)"`
- 加 `viewTypeFilter: ["FloorPlan"]` 避免誤配 Section/Schedule

**情境 C：精確指定**
- 使用者給 view ID 或名稱清單，用 `viewIds` 或 `viewNames`

### 步驟 3：強制 dryRun 預覽（重要 SOP）

**先用 `dryRun: true` 跑一次**，確認會被影響的 view 清單：

```json
batch_apply_view_template({
  "viewTemplateName": "建築平面圖",
  "sheetNumbers": ["A101", "A102", "A103", "A104", "A105"],
  "viewTypeFilter": ["FloorPlan"],
  "dryRun": true
})
```

**dryRun 回傳檢查重點**：
- `ModifiedCount` — 數量是否符合預期（與圖紙上的 view 數對得上嗎？）
- `Modified[].PreviousTemplate` — 從哪個 template 換過來？有沒有意外（例如其實已經是對的 template）
- `Modified[].ViewType` — 是否包含不該被改的類型（用 `viewTypeFilter` 排除）
- `Skipped[]` — 哪些被跳過（`已使用相同 template` 是正常的）
- `Failed[]` — 是否有錯誤

### 步驟 4：跟使用者確認後正式執行

把 `dryRun` 拿掉再跑一次（其他參數一樣）：

```json
batch_apply_view_template({
  "viewTemplateName": "建築平面圖",
  "sheetNumbers": ["A101", "A102", "A103", "A104", "A105"],
  "viewTypeFilter": ["FloorPlan"]
})
```

### 步驟 5：視覺驗證

1. 在 Revit 開任一張處理過的 sheet
2. 點選 view → Properties 面板查看 `View Template` 欄位是否為目標 template
3. 確認視覺效果（HiddenCategories / Filters / Scale 等）符合預期

## 常見陷阱

### 1. ViewTemplate 找不到（錯字／繁簡體／空白差異）
工具回傳會列出所有可用 template。仔細比對名稱（含中英文/全半形/前後空白）。建議用 `viewTemplateId` 替代 name 避免改名後失效。

### 2. View 已經是對的 template（被當成跳過）
預設 `skipIfSameTemplate: true`，已套用相同 template 的 view 會記到 `Skipped[]`（Reason: `已使用相同 template`）。這是**設計如此**，不是錯誤。

### 3. View 是 template 本身
工具會跳過 `IsTemplate == true` 的 view（不能把 template 套到 template 上）。

### 4. ViewType 不相容
Schedule、Legend、DraftingView 等可以接收 ViewTemplate，但 template 內針對 ViewType 不適用的設定（如 DraftingView 沒有 DetailLevel）會被忽略。建議用 `viewTypeFilter` 限定到目標類型。

### 5. 圖紙上的 view 不只主圖
`sheetNumbers` 會抓**該 sheet 上所有 viewport** 對應的 view，包含 callout、detail view 等。若只想處理主平面圖，加 `viewTypeFilter: ["FloorPlan"]`。

### 6. Additive 選擇器疊加
若同時給 `sheetNumbers` + `viewNames`，結果是**聯集**（不是交集）。要交集效果請拆兩次呼叫：先 dryRun 看 sheet 上有哪些 view，再用 `viewNames` 精確指定。

### 7. CropBox 會跟著 template 改變
ViewTemplate 若控制了 CropBox（CropBoxActive / CropBoxVisible / 甚至 CropBox 邊界），套用後 view 的裁剪範圍會被覆蓋。若 view 之前有手動調整的 CropBox，會被 template 蓋掉。**先 dryRun 看 PreviousTemplate**，若是 `<None>` 表示這個 view 之前沒 template，套用後改變最大。

### 8. Phase / Phase Filter 不會跟著改
ViewTemplate 在 Revit 預設**不**控制 Phase / Phase Filter（兩個欄位通常設成 `<not controlled>`）。若需要連 Phase 也統一，要另外用 `modify_element_parameter` 處理。

## 與其他 Skill 的搭配

### 跨專案複製後統一樣式
```
copy-sheets-cross-project (跨專案複製 sheets + views)
    ↓
batch-apply-view-template (本 skill：把新 view 套上目標檔的 template)
    ↓
align-views-on-sheets (套 ScopeBox + 對齊 viewport 位置)
```

### 補齊新建樓層平面圖
```
floor-plan-from-template (依範本建新 view，已自帶範本的 ViewTemplate)
    ↓
（通常不需要本 skill，因為建立時已套）
但若要改成不同 template → 用本 skill
```

## Reference

- C# 實作：`MCP/Core/Commands/CommandExecutor.ViewCreation.cs`（`BatchApplyViewTemplate` 方法）
- Tool schema：`MCP-Server/src/tools/view-creation-tools.ts`
- 相關 skill：
  - `align-views-on-sheets` — 套完 template 後對齊範圍與位置
  - `floor-plan-from-template` — 建立新 view 時帶入 template
  - `copy-sheets-cross-project` — 跨專案複製，常接本 skill 後處理
