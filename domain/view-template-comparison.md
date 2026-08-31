---
name: view-template-comparison
description: "Revit 視圖樣板差異矩陣比對 (View Template Comparison Matrix) SOP 與 API 規範。定義多 View Template 的品類覆蓋、過濾器與基礎屬性比對演算法、QC 審計 UI/UX 矩陣視圖規範，以及唯讀安全邊界。"
metadata:
  version: "1.0"
  updated: "2026-08-29"
  references:
    - "Autodesk Revit API - ViewTemplate & View.GetCategoryOverrides"
    - "Autodesk Revit API - View.GetFilters / ParameterFilterElement"
    - "frontmatter-standard.md"
  related:
    - pdf-export-comparison.md
    - sheet-viewport-management.md
    - qa-checklist.md
  referenced_by:
    - compare_view_templates
  tags: [ViewTemplate, QC, 視圖樣板, 差異比對, Overrides, Filters, CategoryVisibility, Audit]
---

# 視圖樣板差異矩陣比對 (View Template Comparison Matrix)

本文件定義 Revit 專案中 ** View Template 差異矩陣比對** 的技術架構、MCP API 資料結構、GUI 介面規範與 AI Agent 分析邏輯。旨在作為專案後期圖面品質管制 (QC) 的標準審計工具。

---

## 🎯 核心定位與安全邊界 (Positioning & Safety Boundaries)

### 1. 工具定位
* **專案階段**：細部設計、施工圖出圖期、多區段協調期。
* **主要痛點**：專案後期 View Template 數量龐大（如 `A-平面圖-1/100`、`A-平面圖-建照`、`M-空調平面`），常因個別品類隱藏、過濾器覆蓋或細緻度微小差異，導致出圖圖面不一致。
* **核心價值**：提供**一目瞭然的多視圖樣板矩陣比對**，迅速找出跨樣板設定矛盾。

### 2. 嚴格安全邊界（Strict Exclusions）
> [!IMPORTANT]
> **純審計唯讀 (Pure Read-Only Audit Tool)**
> * **嚴格禁止** 提供 `SetCategoryOverrides`、`SetFilterOverrides` 或修改視圖樣板參數的寫入/覆蓋按鈕。
> * **原因**： View Template 影響全專案數十至數百個視圖，批次覆蓋極易引發連鎖誤刪圖面資訊。修改操作必須回到 Revit 原生 UI 手動二次確認。

---

## 📊 MCP API 規範: `compare_view_templates`

`compare_view_templates` 為 MCP Server 註冊之核心 Tool，供 LLM 或前端 GUI 取得結構化 JSON 差異報告。

### 1. 輸入參數 (Input Schema)
```json
{
  "type": "object",
  "properties": {
    "viewTemplateIds": {
      "type": "array",
      "items": { "type": "number" },
      "description": "要比對的 View Template ElementId 清單 (例: [123456, 123457])"
    },
    "viewTemplateNames": {
      "type": "array",
      "items": { "type": "string" },
      "description": "要比對的 View Template 名稱清單 (與 viewTemplateIds 二擇一，精確匹配)"
    },
    "compareScopes": {
      "type": "array",
      "items": { 
        "type": "string", 
        "enum": ["basic", "model_category", "annotation_category", "filters"]
      },
      "description": "比對範疇，預設全部包含",
      "default": ["basic", "model_category", "annotation_category", "filters"]
    },
    "diffOnly": {
      "type": "boolean",
      "description": "是否僅傳回有差異的項目 (預設 false 傳回完整矩陣)",
      "default": false
    }
  },
  "required": []
}
```

### 2. 輸出 JSON 結構 (Response Payload Contract)

> [!NOTE]
> **真實範例數據說明 (Built-in Sample Data Note)**
> 本 API 響應範例直接採集自 **Revit 官方內建範例檔 (`Snowdon Towers Sample Architectural.rvt`)** 之真實 API 回傳資料。
> 比對標的為內建樣板 **`Architectural Plan` (ElementId: `29485`)** 與 **`Architectural Life Safety Plan` (ElementId: `2157001`)**，方便開發者與 AI Agent 於開箱測試時直接比對與驗證。

```json
{
  "projectInfo": {
    "projectName": "Snowdon Towers Sample Architectural",
    "buildingName": "Snowdon Towers"
  },
  "templates": [
    { "id": 29485, "name": "Architectural Plan" },
    { "id": 2157001, "name": "Architectural Life Safety Plan" }
  ],
  "summary": {
    "totalItems": 148,
    "diffCount": 19,
    "matchCount": 129
  },
  "diffOnly": true,
  "sections": [
    {
      "sectionKey": "basic",
      "sectionName": "基本屬性差異 (Basic Properties Diff)",
      "rows": [
        {
          "key": "HiddenCategoryCount",
          "label": "隱藏品類總數量 (Hidden Categories Count)",
          "hasDiff": true,
          "values": {
            "29485": "38 個品類隱藏",
            "2157001": "13 個品類隱藏"
          }
        },
        {
          "key": "FilterCount",
          "label": "過濾器總數量 (Filters Count)",
          "hasDiff": true,
          "values": {
            "29485": "11 個過濾器",
            "2157001": "14 個過濾器"
          }
        }
      ]
    },
    {
      "sectionKey": "category_visibility",
      "sectionName": "品類可見性差異 (Category Visibility Diff)",
      "rows": [
        {
          "key": "OST_DuctAccessory",
          "label": "風管附件 (Duct Accessories)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": false, "override": "Hidden (隱藏)" },
            "2157001": { "visible": true, "override": "Visible (顯示)" }
          }
        },
        {
          "key": "OST_MEPSpaces",
          "label": "空間 (Spaces)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": false, "override": "Hidden (隱藏)" },
            "2157001": { "visible": true, "override": "Visible (顯示)" }
          }
        },
        {
          "key": "OST_Piping",
          "label": "管 (Piping)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": false, "override": "Hidden (隱藏)" },
            "2157001": { "visible": true, "override": "Visible (顯示)" }
          }
        },
        {
          "key": "OST_FireProtect",
          "label": "防火 (Fire Protection)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": false, "override": "Hidden (隱藏)" },
            "2157001": { "visible": true, "override": "Visible (顯示)" }
          }
        },
        {
          "key": "OST_FlexDuct",
          "label": "撓性風管 (Flex Ducts)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": false, "override": "Hidden (隱藏)" },
            "2157001": { "visible": true, "override": "Visible (顯示)" }
          }
        },
        {
          "key": "OST_DuctInsulation",
          "label": "風管隔熱層 (Duct Insulation)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": false, "override": "Hidden (隱藏)" },
            "2157001": { "visible": true, "override": "Visible (顯示)" }
          }
        },
        {
          "key": "OST_Sections",
          "label": "剖面標籤 (Sections)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": true, "override": "Visible (顯示)" },
            "2157001": { "visible": false, "override": "Hidden (隱藏)" }
          }
        },
        {
          "key": "OST_CLines",
          "label": "參考平面 (Reference Planes)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": true, "override": "Visible (顯示)" },
            "2157001": { "visible": false, "override": "Hidden (隱藏)" }
          }
        },
        {
          "key": "OST_Mass",
          "label": "量體 (Mass)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": true, "override": "Visible (顯示)" },
            "2157001": { "visible": false, "override": "Hidden (隱藏)" }
          }
        },
        {
          "key": "OST_SectionBox",
          "label": "剖面框 (Section Boxes)",
          "hasDiff": true,
          "values": {
            "29485": { "visible": true, "override": "Visible (顯示)" },
            "2157001": { "visible": false, "override": "Hidden (隱藏)" }
          }
        }
      ]
    },
    {
      "sectionKey": "filters",
      "sectionName": "視圖過濾器套用差異 (View Filters Diff)",
      "rows": [
        {
          "key": "Filter_1Hour",
          "label": "過濾器: 1 Hour (防火 1 小時牆)",
          "hasDiff": true,
          "values": {
            "29485": { "applied": false, "visibility": false, "colorOverride": "Not Applied" },
            "2157001": { "applied": true, "visibility": true, "colorOverride": "#FF0000 (Red Solid Fill)" }
          }
        },
        {
          "key": "Filter_2Hour",
          "label": "過濾器: 2 Hour (防火 2 小時牆)",
          "hasDiff": true,
          "values": {
            "29485": { "applied": false, "visibility": false, "colorOverride": "Not Applied" },
            "2157001": { "applied": true, "visibility": true, "colorOverride": "#FFA500 (Orange Solid Fill)" }
          }
        },
        {
          "key": "Filter_3Hour",
          "label": "過濾器: 3 Hour (防火 3 小時牆)",
          "hasDiff": true,
          "values": {
            "29485": { "applied": false, "visibility": false, "colorOverride": "Not Applied" },
            "2157001": { "applied": true, "visibility": true, "colorOverride": "#FFFF00 (Yellow Solid Fill)" }
          }
        },
        {
          "key": "Filter_4Hour",
          "label": "過濾器: 4 Hour (防火 4 小時牆)",
          "hasDiff": true,
          "values": {
            "29485": { "applied": false, "visibility": false, "colorOverride": "Not Applied" },
            "2157001": { "applied": true, "visibility": true, "colorOverride": "#800080 (Purple Solid Fill)" }
          }
        },
        {
          "key": "Filter_1HourSmoke",
          "label": "過濾器: 1 Hour (Smoke 防煙)",
          "hasDiff": true,
          "values": {
            "29485": { "applied": false, "visibility": false, "colorOverride": "Not Applied" },
            "2157001": { "applied": true, "visibility": true, "colorOverride": "#00FFFF (Cyan Solid Fill)" }
          }
        },
        {
          "key": "Filter_WallsToHide",
          "label": "過濾器: Walls - to Hide",
          "hasDiff": true,
          "values": {
            "29485": { "applied": true, "visibility": false, "colorOverride": "Hidden Filter" },
            "2157001": { "applied": false, "visibility": false, "colorOverride": "Not Applied" }
          }
        },
        {
          "key": "Filter_StructuralFloors",
          "label": "過濾器: Elements to Hide - Structural Floors",
          "hasDiff": true,
          "values": {
            "29485": { "applied": true, "visibility": false, "colorOverride": "Hidden Filter" },
            "2157001": { "applied": false, "visibility": false, "colorOverride": "Not Applied" }
          }
        }
      ]
    }
  ]
}
```

---

## 🎨 GUI UI/UX 體驗與佈局規範

GUI 畫面規範必須讓使用者與 AI 產製 UI 時高度對齊（對齊 Add-in 審計介面）：

```
+---------------------------------------------------------------------------------------------------------------+
|  視圖樣板比較矩陣 — 2 個樣板比對結果                                                                            |
+---------------------------------------------------------------------------------------------------------------+
|  [ 📊 完整矩陣 (共 878 項) ]  [ ⚠️ 僅顯示差異項 (57 項) ]                                                      |
+--------------------------+----------------------------+-------------------------------+-----------------------+
|  分組類別                |  設定項目 / 類別名稱        | Architectural Life Safety Plan| Architectural Plan   | 比對結果
+--------------------------+----------------------------+-------------------------------+-----------------------+
|  模型品類可見性 (Model)   |  防火                     | 顯示 (On) [紅底]              | 隱藏 (Off) [藍底]     | ⚠️ 差異
|  模型品類可見性 (Model)   |  空間                     | 顯示 (On) [紅底]              | 隱藏 (Off) [藍底]     | ⚠️ 差異
|  模型品類可見性 (Model)   |  風管                     | 顯示 (On) [紅底]              | 隱藏 (Off) [藍底]     | ⚠️ 差異
|  註解品類可見性 (Annot.)  |  剖面                     | 隱藏 (Off) [紅底]              | 顯示 (On) [藍底]     | ⚠️ 差異
|  註解品類可見性 (Annot.)  |  參考平面 > Balconies      | 隱藏 (Off) [紅底]              | 顯示 (On) [藍底]     | ⚠️ 差異
|  註解品類可見性 (Annot.)  |  參考線                   | 隱藏 (Off) [紅底]              | 顯示 (On) [藍底]     | ⚠️ 差異
+--------------------------+----------------------------+-------------------------------+-----------------------+
|  共發現 57 項跨樣板差異 (已按選項套用多彩顏色標示)                                                               |
|  [ 📊 匯出 .xlsx ]  [ 📊 匯出 .csv ]  [ 🟩 建立製圖視圖 ]                                       [ 關閉 ]|
+---------------------------------------------------------------------------------------------------------------+
```

### 1. 介面主要區塊與按鈕 (GUI Controls)
1. **頂部頁籤切換 (Filter Tabs)**
   - `[ 📊 完整矩陣 (共 N 項) ]`：顯示全部比對列。
   - `[ ⚠️ 僅顯示差異項 (M 項) ]`：即時切換至 `hasDiff == true` 的列（支援黃底/紅藍高亮）。
2. **多樣板對比表格欄位 (Matrix Table Columns)**
   - `分組類別`：標示類別範疇（模型品類 Model、註解品類 Annotation、過濾器 Filters、基本屬性 Basic）。
   - `設定項目 / 類別名稱`：顯示項目名稱（支援階層父子項，如 `停車場 > Parking Layout`）。
   - `樣板對比欄`：每個 View Template 獨立一欄，內容標明 `顯示 (On)` / `隱藏 (Off)` 或設定值。
   - `比對結果`：顯式標記 `⚠️ 差異` 或 `✓ 一致`。
3. **底部操作按鈕區 (Bottom Action Bar)**
   - `[ 📊 匯出 .xlsx ]` / `[ 📊 匯出 .csv ]`：匯出符合品質審計格式之矩陣報表。
   - `[ 🟩 建立製圖視圖 (Create Drafting View) ]`：可在 Revit 專案內自動生成一張「視圖樣板對比矩陣圖冊 Drafting View」，將差異繪製於模型圖紙中存檔。
   - `[ 關閉 ]`：結束審計。

### 2. 高亮視覺規範 (Highlight Styling Rules)
* **無差異項目 (`hasDiff == false`)**：
  - 文字顏色：`#333333` / 背景色：`#FFFFFF` (預設黑字白底)。
* **差異儲存格 (`hasDiff == true`)**：
  - **背景色 (Background)**：淡黃/粉紅/淡藍分色高亮 (`#FFF3CD` 淡黃，或相應紅藍高亮底色)。
  - **文字顏色 (Text Color)**：鮮紅/暗紅色字體 (`#DC3545` 或 `#B02A37` font-weight: bold)。
  - **狀態標籤**：醒目顯示 `⚠️ 差異` 警告符號。

---

## 🤖 AI Agent 轉化與分析演算法規範 (AI Protocol)

當 AI Agent 收到使用者指令（例：「幫我分析『施工圖樣板』跟『建照樣板』差在哪裡？」）或欲根據本 domain 檔生成 C#/Python/JS 代碼時，應遵照以下流程處理：

```mermaid
flowchart TD
    A[收到 View Template 比對請求] --> B{檢查參數}
    B -- 指定 2+ 個 View Templates --> C[呼叫 MCP compare_view_templates]
    B -- 未指定樣板名稱 --> D[呼叫 get_view_templates 取得樣板列表]
    D --> E[讓使用者/LLM 選擇比對標的] --> C
    C --> F[接收結構化 JSON 比對報告]
    F --> G[執行 差異演算 (hasDiff Filtering)]
    G --> H[歸納三大差異類別: 可見性 / 顯色覆蓋 / 過濾器]
    H --> I[生成 Markdown 報告 / GUI 矩陣渲染]
```

### 1. 比對演算法 (Diff Matrix Engine Algorithm)
1. **Key 鍵值歸一化 (Normalization)**：
   - Category 使用 BuiltInCategory Enum 名稱 (如 `OST_Walls`, `OST_Doors`) 作為 Key，解決跨語系 (繁中/簡中/英文) 品類名稱不一致問題。
   - View Filter 使用 Filter Name 或 BuiltInParameter 作為 Key。
2. **三態可見性判定 (Tri-State Visibility Evaluation)**：
   - 區分：`Explicitly Visible (顯式顯示)`、`Explicitly Hidden (顯式隱藏)`、`By Parent/Category (繼承預設)`。
3. **差異標記 (Diff Flagging)**：
   - 對於列 $R$，比較所有 Template 欄位的 Value。若存在任意 $V_a \neq V_b$，則設 `hasDiff = true`。

### 2. QC 報告 CSV/TXT 匯出範例
匯出報告標註時間戳記與專案資訊：

```csv
"QC View Template Audit Report","Project: Snowdon Towers Sample Architectural","Generated: 2026-08-29"
"Section","Item","Template: Architectural Plan (29485)","Template: Architectural Life Safety Plan (2157001)","Status"
"Basic","HiddenCategoryCount","38","13","DIFF"
"Basic","FilterCount","11","14","DIFF"
"Model Category","Furniture (家具)","Hidden","Visible","DIFF"
"Model Category","Piping (管)","Hidden","Visible","DIFF"
"Filters","Filter: 1 Hour","Not Applied","Active (#FF0000 Red Solid)","DIFF"
"Filters","Filter: 2 Hour","Not Applied","Active (#FFA500 Orange Solid)","DIFF"
```

---

## 🛠️ 開發者如何根據本 Domain 實作相容 API / GUI

若其他開發者或 AI 欲實作與本規格 100% 相容的 API 或 GUI，請遵守：

1. **接口合約 (API Contract)**：
   - 必須註冊 `compare_view_templates` 工具名。
   - 回傳格式必須符合本文 Section「MCP API 規範」之 JSON Schema。
2. **GUI 狀態同步**：
   - UI 狀態必須以 `diffOnly: boolean` 控制顯示列數。
   - 差異判定必須在 Server 端算好 `hasDiff` 標籤，前端僅需依據 `hasDiff` 綁定 `.diff-highlight` CSS class (黃底紅字)。
3. **極致安全**：
   - 不得於該 UI 內開放修改 View Template 的 Command。

---

## 💻 開源 C# 外部增益集實作參考 (C# Add-in Open Source Reference)

除了透過 MCP JSON API 進行與 AI 互動外，若開發者或 AI 欲將本功能獨立封裝為 **Revit 外部增益集 (`IExternalCommand` / WPF 獨立插件 / pyRevit 工具)**，可直接參考本專案開放之 **`RevitFamilyTypeExporter` 核心源碼邏輯**：

### 1. C# 深層比對引擎核心 (Deep Comparison Engine)
```csharp
private List<CompareRow> RunDeepComparison(Document doc, List<string> selectedNames, List<View> templates)
{
    var rows = new List<CompareRow>();
    TryAddBasicProperties(rows, templates);
    TryAddFirstLevelParams(rows, doc, templates);
    TryAddVgFilters(rows, doc, templates);
    TryAddCategoryVisibility(rows, doc, templates);
    TryAddVgOverrides(rows, doc, templates);
    return rows;
}

// 跨樣板品類可見性比對 (Category Visibility Comparison)
private CompareRow BuildCategoryVisRow(string group, string itemName, ElementId catId, List<View> templates)
{
    var vals = templates.Select(t => {
        try { return t.GetCategoryHidden(catId) ? "隱藏 (Off)" : "顯示 (On)"; }
        catch { return "N/A"; }
    }).ToList();

    bool isDiff = vals.Where(v => v != "N/A").Distinct().Count() > 1;
    return new CompareRow { Group = group, Item = itemName, Values = vals, IsDiff = isDiff };
}
```

### 2. 跨版本 Revit API 防呆與反射封裝 (Cross-Version Reflection Helpers)
> [!TIP]
> 由於 Revit 2022 ~ 2025+ 對於 `ElementId.Value` (long vs int) 與 `OverrideGraphicSettings` 屬性方法有 API 變更，實作外部插件時建議加入反射防呆機制：

```csharp
private static long GetIdVal(ElementId id)
{
    if (id == null) return -1;
    try {
        var propVal = typeof(ElementId).GetProperty("Value");
        if (propVal != null) return (long)propVal.GetValue(id);
    } catch {}
    try {
        var propInt = typeof(ElementId).GetProperty("IntegerValue");
        if (propInt != null) return Convert.ToInt64(propInt.GetValue(id));
    } catch {}
    return -1;
}
```

### 3. 自動建立 Revit 原生製圖視圖報表 (Auto Create Drafting View Matrix)
```csharp
// 可在 Revit 模型內直接新建一張 Drafting View 將比對報告繪製於圖面中
using (var tx = new Transaction(doc, "建立樣板矩陣差異對照圖面"))
{
    tx.Start();
    var newView = ViewDrafting.Create(doc, draftingTypeId);
    newView.Name = $"視圖樣板矩陣比較_{DateTime.Now:yyyyMMdd_HHmm}";

    TextNote.Create(doc, newView.Id, XYZ.Zero, reportText, new TextNoteOptions {
        TypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)
    });
    tx.Commit();
    uiDoc.ActiveView = newView;
}
```

