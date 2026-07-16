---
name: scaffold-takeoff
description: "施工架算量 SOP：室外施工架以外牆周長×高度、室內施工架以房間周長×高度計算，含框式施工架與防護設施規則。"
metadata:
  version: "1.0"
  updated: "2026-07-03"
  created: "2026-07-03"
  contributors:
    - "Jacky820507"
  references:
    - "MCP/Core/Commands/CommandExecutor.ScaffoldTakeoff.cs"
    - "MCP-Server/src/tools/scaffold-tools.ts"
  referenced_by:
    - "scaffold-takeoff"
---

# 施工架算量 Domain

本 Domain 記錄 RevitMCP 中施工架數量計算的規則。

## 室外施工架

`框式施工架(含防護設施)<室外施工架>` 採用人工周長流程。

優先工具：
- `calculate_selected_detail_line_perimeter`

原因：
- 使用者直接用 detail line 或 filled region 描出外部施工架範圍。
- 當專案存在擋土牆、局部外牆、偏移、矮牆或外圍條件不明確時，人工描繪比自動外牆偵測更容易稽核。
- 結果應視為周長來源；只有在明確提供施工架高度時，才再換算為面積。

## 與地下室外牆防水毯的周長共用

同一個室外外部周長也可作為 `domain/floor-area-review.md` 的「地下室外牆防水毯面積規則」之周長來源。

防水毯使用此周長時，只共用「外部周長」這個幾何基準，不共用施工架高度。防水毯面積應改用各地下層樓高或圖說指定防水高度：

```text
地下室外牆防水毯面積
= Σ(B1F/B2F/.../BnF 各層外部周長 × 該層樓層高度或防水高度)
```

若 B1F、B2F、B3F 等地下層外框不同，應分層取得或標註各層外部周長，不可用單一周長靜默套用全部地下層。

`FN` 不列入地下室外牆防水毯的地下層分層高度計算；若圖說要求筏基側面、基礎側面或大底高低差側面防水，應回到 `domain/floor-area-review.md` 另列為大底防水正式加項。

## 室內施工架分類

### 施工架(含防護設施)

用於一般室內房間，也就是不是樓梯/電梯類，且未被排除的房間。

公式：

```text
周長 * 高
```

`周長` 採用 Revit Room 在 Finish location 的 boundary perimeter。

### 室內裝修施工架

用於樓梯/電梯相關房間。

觸發字詞：
- `安全梯`
- `無障礙梯`
- `樓梯`
- `電梯`
- `貨梯`
- `昇降機`
- `升降機`
- `客梯`

公式：

```text
長 * 寬 * 高
```

`長` 與 `寬` 使用 room boundary extents；若有提供 `scaffoldHeightMm` 就直接使用。若沒有提供高度，工具可能改用 room bounding-box 高度，但正式數量報告必須把這個假設寫清楚。

## 排除規則

當房間名稱或編號包含以下任一字詞時，從 `施工架(含防護設施)` 排除：

- `戶外平台`
- `戶外平臺`
- `露臺`
- `露台`
- `陽台`
- `陽臺`
- `管道間`
- `水箱`

排除樓層：
- `FN`
- `TF`

樓層排除的優先序高於房間名稱分類。例如位於 `FN` 的電梯機坑仍然要排除。

## 分類備註

`梯廳` 不等於 `樓梯`。除非使用者另外加上專案特定排除規則，否則 `梯廳` 視為一般施工架房間，應採 `周長 * 高`。

`陽台` 與 `陽臺` 必須同時列入，因為專案房間名稱可能混用 `台` 與 `臺`。

`客梯` 應視為電梯相關關鍵字，因為專案房間可能命名為 `無障礙客梯`。

## Tool 輸出語意

`calculate_room_scaffold_perimeters` 的單位必須分開處理：

- `GeneralScaffold.QuantitySqM`: final quantity for general rooms when height is known.
- `GeneralScaffold.AuditPerimeterM`: perimeter-only check value.
- `InteriorFinishScaffold.QuantityM3`: final quantity for stair/elevator rooms.
- `InteriorFinishScaffold.AuditPerimeterM`: perimeter-only check value, not the final interior finish quantity.
- `Excluded.AuditPerimeterM`: perimeter of excluded rooms for audit only.

不要把 `m`、`m2` 與 `m3` 的輸出直接加總。

## 檢查清單

- 確認使用者要的是正式施工架高度，還是接受 room bounding-box 高度。
- 確認室外施工架採用使用者選取的 detail-line 或 filled-region perimeter。
- 若室外施工架外周長被拿來算地下室外牆防水毯，只共用周長；高度必須改用地下層樓高或圖說指定防水高度。
- 確認 `FN` 與 `TF` 已排除。
- 確認 `FN` 排除規則只適用於施工架與外牆防水毯分層樓高；若是大底防水，`FN`/筏基/基礎底版需依 `domain/floor-area-review.md` 納入水平投影聯集。
- 確認排除字詞同時包含 `台` 與 `臺` 版本。
- 回報室內施工架時明確顯示公式。
