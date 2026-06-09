---
name: section-auto-numbering
description: "剖面自動編排與命名 SOP：根據剖面標記在視圖中的空間幾何位置，單一方向（如由左至右）進行排序，並可自訂前綴自動重新命名。當使用者提到剖面重新命名、剖面編號、section numbering、自動編排剖面時觸發。"
metadata:
  version: "1.0"
  updated: "2026-05-20"
  created: "2026-05-20"
  contributors:
    - "AI Assistant"
  references: [] 
  related: ["parking-auto-numbering.md"]
  referenced_by: []
  tags: [Revit, Automation, Section, MCP, 剖面自動命名, section numbering]
---

# 剖面自動編排與命名標準作業程序 (Section Auto-Numbering SOP)

## 1. 目的
自動化地對 Revit 中選取的剖面視圖（或平面圖上的剖面標記），根據其在平面上的幾何座標位置進行順序編排，並統一套用自訂前綴進行重新命名。

## 2. 適用對象
- Revit 中的剖面視圖 (`ViewSection`) 或平面上的剖面標記元件 (`SectionMarker`)。
- 工程師希望將平面上散亂的剖面符號（如：剖面 1、剖面 5、剖面 2）整理為有條理的名稱（如：`剖面-牆面開口-4F-剖面-1`、`剖面-牆面開口-4F-剖面-2` ...）。

## 3. 前置準備
- Revit 專案已開啟，且使用者**已在視圖中手動選取**需要重新命名的剖面標記。

## 4. 作業流程 (AI 執行步驟)

### Step 1: 取得選取元素與幾何座標
- 呼叫 `get_selected_elements` 取得目前選取的元素。
- 過濾出 `Category` 為 "Views"、"視圖"、"Sections"、"剖面" 的元素，並取得其回傳的 `Origin` (X, Y, Z) 幾何座標。

### Step 2: 前綴名稱自動推論與格式化
- 檢視選取元素的現有名稱，自動分析並推導最長共同前綴。
- 格式化前綴，移除數字尾綴，並在末端適當補上連字號 `-` 以確保產出如 `剖面-牆面開口-4F-剖面-1` 的格式。
- 若無法自動推論，則預設為 `剖面-`，或於文字對話中向使用者確認。

### Step 3: 排帶式幾何空間排序演算法 (Row-major Grouping)
為了讓排序最契合人類的「看圖與閱讀習慣（從上至下，從左至右）」，採用**「橫排分組，排內左至右排序」**的演算法：
1. **高度分帶**：依據 Y 座標將剖面劃分為橫向分組帶（例如區分為 4 排高度區間：Row 1 至 Row 4）。
   - **防橋接 (Chaining) 容錯**：不採用單純的「相鄰差值聚類」，以防大量密集剖面產生橋接滾雪球效應，導致不同排的剖面被融合成同一組。改用**固定高度帶劃分法**（例如以 `28500 mm`, `18500 mm`, `8500 mm` 為界線分出 4 個高度帶）。
2. **排內由左至右**：在每一個高度帶（Row）內部，將裡面的剖面依據 **X 座標由小到大（由左至右）** 進行精準排序。
3. **最終合併**：將 Row 1 (最上方) 到 Row 4 (最下方) 排序後的陣列依次合併，生成最終的命名編號順序。

### Step 4: 雙階段 (Two-Pass) 重新命名交易
為了徹底解決 Revit 視圖「名稱在專案中必須唯一 (Name must be unique)」的底層限制，重新命名必須拆分為兩個階段執行：
*   **第一階段 (臨時重命名)**：
    *   依次將所有選取的剖面暫時重新命名為加上時間戳與索引的**絕對唯一臨時名稱**（例如：`{原名稱}_temp_{timestamp}_{index}`）。
    *   這能完全釋放原本被佔用的目標名稱空間，防止直接命名時與同組其他剖面發生名稱重複錯誤。
*   **第二階段 (正式重命名)**：
    *   將已暫時重命名的視圖，依次修改為正式的連續編號名稱（如：`剖面-牆面開口-4F-剖面-1`）。
    *   此時由於目標名稱已被全部釋放，將 100% 不會發生衝突。

---

## 5. 常見問題與處理

### 1. 選取到的不是 `View` 而是 `SectionMarker` (剖面標記)
- **現象**：使用者在平面圖框選的是剖面符號，在 C# 中其底層型別是 `Element` 而非 `View` 物件，直接類型轉換會回傳 `null`，導致無法使用 `view.Name = newName` 修改名稱。
- **處理方法**：在 C# [`CommandExecutor.ViewOps.cs`](../MCP/Core/Commands/CommandExecutor.ViewOps.cs) 的 `RenameView` 函數中加入類型檢索容錯：
  ```csharp
  View view = elem as View;
  if (view == null)
  {
      // 若選取到剖面標記元件，利用其與對應視圖「名稱同名」的特性，在所有 View 中尋找同名視圖
      string viewName = elem.Name;
      view = new FilteredElementCollector(doc)
          .OfClass(typeof(View))
          .Cast<View>()
          .FirstOrDefault(v => v.Name == viewName);
  }
  ```
  如此即可完美對應，順利修改該剖面標記背後的視圖名稱。

### 2. 名稱重複衝突 (Name must be unique)
- 必須遵循上述 **Step 4** 的 Two-Pass 兩階段命名法，嚴禁在單一階段直接覆寫可能已被同組其他成員佔用的名稱。
- 臨時命名階段使用的隨機尾綴必須包含時間戳與唯一 index，以保證絕對不重複。
