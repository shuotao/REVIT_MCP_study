---
name: scaffold-takeoff
description: Revit 施工架數量計算流程，適用於台灣工程估算。當使用者提到 frame scaffold、scaffold with protection facilities、外部施工架周長、地下室外牆防水毯周長來源、室內房間施工架、樓梯/電梯室內裝修施工架、room perimeter x height 或 length x width x height takeoff 時使用。Primary tools: calculate_selected_detail_line_perimeter and calculate_room_scaffold_perimeters.
---

# 施工架算量

在 RevitMCP 中執行施工架數量計算時使用此 Skill。

## 工作流程

1. 針對 `框式施工架(含防護設施)<室外施工架>`，優先採人工描繪，並執行 `calculate_selected_detail_line_perimeter`。
   - 先請使用者選取已描繪好的 detail line 或 filled region 邊界。
   - 這是較可信的室外數量路徑，因為使用者可以直接目視確認外周長。
   - 只有在使用者要算 `周長 * 高` 面積時才帶入 `scaffoldHeightMm`。
   - 若使用者要算地下室外牆防水毯，可沿用此室外外部周長作為周長來源，但高度必須改用各地下層樓高或圖說指定防水高度，不直接沿用施工架高度。

2. 針對室內房間施工架，執行 `calculate_room_scaffold_perimeters`。
   - 納入已放置的 Rooms。
   - 排除樓層 `FN` 與 `TF`。
   - 除非使用者另外指定，否則使用預設排除字詞。
   - 回報時同時寫清楚公式與單位，避免周長、面積與體積混在一起。

## 室內規則

一般施工架 `施工架(含防護設施)`:
- 適用範圍：不是樓梯/電梯類房間，且未被排除字詞或排除樓層命中的 rooms。
- 公式：`周長 * 高`。
- 輸出欄位：有高度時使用 `GeneralScaffold.QuantitySqM`；`AuditPerimeterM` 只作周長檢核。

室內裝修施工架 `室內裝修施工架`:
- 適用範圍：房間名稱或編號包含 `安全梯`、`無障礙梯`、`樓梯`、`電梯`、`貨梯`、`昇降機`、`升降機` 或 `客梯`。
- 公式：`長 * 寬 * 高`。
- 輸出欄位：使用 `InteriorFinishScaffold.QuantityM3`。

排除房間：
- 排除房間名稱或編號包含 `戶外平台`、`戶外平臺`、`露臺`、`露台`、`陽台`、`陽臺`、`管道間` 或 `水箱`。
- 排除樓層的優先序高於房間關鍵字。位於 `FN` 或 `TF` 的房間，就算包含 `電梯` 也仍然排除。

## 回報方式

回報結果時：
- 明確列出公式，尤其是 `室內裝修施工架`。
- 室內裝修列請寫成 `長 * 寬 * 高 = 數量`。
- 一般施工架列請寫成 `周長 * 高 = 數量`。
- 周長檢核值要和最終數量分開。
- 如果沒有提供高度，要註明工具是使用 room bounding-box 高度，還是仍需正式指定 `scaffoldHeightMm`。
- 若同時牽涉地下室外牆防水毯，要明確分開回報：施工架是 `外部周長 * 施工架高度`，防水毯是 `地下層外部周長 * 樓層高度/防水高度`。
- `FN` 在施工架與地下室外牆防水毯分層高度中不列入地下層；但若議題是大底防水，`FN`/筏基/基礎底版需依 `domain/floor-area-review.md` 納入水平投影聯集。

## 參考

- `domain/scaffold-takeoff.md`
- `Lessons/scaffold-takeoff-lessons.md`
