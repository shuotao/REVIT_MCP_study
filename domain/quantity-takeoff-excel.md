---
name: quantity-takeoff-excel
description: "Revit 數量計算 Excel 共通方法：以房間邊界、實體宿主、天花板／樓板高度與材料代碼建立可追溯算式。適用於輕隔間、踢腳、內牆粉刷、空間表更新、施工架與其他以房間為基礎的數量計算。"
metadata:
  version: "1.0"
  updated: "2026-07-15"
  references:
    - "Autodesk Revit API: SpatialElement.GetBoundarySegments"
  related:
    - revit-partition-takeoff.md
    - room-space-table-sync.md
    - scaffold-takeoff.md
    - finish-schedule-governance.md
    - room-boundary.md
  referenced_by:
    - quantity-takeoff-excel
  tags: [Revit, quantity-takeoff, Excel, room-boundary, opening, net-height, 輕隔間, 踢腳, 內牆粉刷, 數量計算]
---

# Revit 數量計算 Excel 共通方法

## 目的

本 Domain 把多個 pyRevit 數量計算按鈕累積出的判斷整理成 RevitMCP 可重用的方法論。重點不是「把數字寫進 Excel」，而是讓每個數字都能回答三件事：來源元素是什麼、為何屬於這個房間、計算式如何形成。

## 現有實作入口

| 按鈕 | 主數量 | 核心資料來源 |
|---|---|---|
| `PartitionTakeoff` 輕隔間 Excel | 牆長 × 牆高 - 宿主開口 | TYPE 牆、牆高來源、Door/Window Host |
| `Baseboard` 踢腳 Excel | 完成面周長 - 邊界開口寬 | Room boundary、B 代碼、門類型 |
| `InteriorWallPaint` 內牆粉刷 Excel | 完成面周長 × 淨高 - 邊界開口面積 | Room boundary、W 代碼、天花板／樓板底 |
| `SpaceTableUpdate` 空間表更新 Excel | 房號、名稱、Finish 周長、面積 | Rooms、既有 Excel 固定引用、dry-run 與備份 |
| `ScaffoldRooms` 室內施工架 | 周長 × 高，或長 × 寬 × 高 | Rooms、用途分類、樓層排除 |
| `ScaffoldExterior` 室外施工架 | 人工描繪外周長 × 指定高度 | Detail Lines / Filled Region |

`TallPartition` 是相鄰的品質檢核流程，不是本報表引擎的通用輸出；高牆規則仍由 `tall-partition-index-workflow.md` 管理。

## 共通資料契約

RevitMCP 或 pyRevit 收集資料時，先建立可稽核的中間資料，不要直接拼 Excel 列。Room-based 記錄至少保留：

```text
RoomId, Number, Name, Level,
BoundaryLoops, BoundaryElementIds, PerimeterM, AreaM2,
FinishCodes, HeightM, HeightSource,
Openings[], QuantitySource, Warnings[]
```

開口記錄至少保留 `ElementId`、Category、TypeName、HostId、WidthM、HeightM 與被扣除的 RoomId。牆記錄至少保留 `ElementId`、WallType、LengthM、HeightM、HeightSource、AreaM2 與宿主開口 IDs。

## 房間周長

1. 踢腳、內牆粉刷與空間表周長一律優先使用 `GetBoundarySegments(Finish)`。
2. 加總所有有效邊界迴圈；不得只取第一圈，也不得以 Revit `ROOM_PERIMETER` 中心線值取代完成面邊界。
3. 同時保存每個 boundary segment 的 `ElementId`，後續用來判斷門窗宿主是否真的位於該房間計算邊界。
4. `ROOM_PERIMETER` 只能在 boundary API 無資料時作明確標記的備援值，不可悄悄混用。

## 開口扣除

開口歸屬必須先證明宿主，再談房間關係。

### 輕隔間

只有 `Door/Window.Host.Id == 本次計算牆 ElementId` 才能從該牆扣除。不得用最近牆、同樓層、房間座標或 BoundingBox 接近推定。宿主不是 TYPE 牆時，即使門位在同一房間，也不得從 TYPE 牆扣除。

### 踢腳與內牆粉刷

只有 `opening.Host.Id` 存在於該房間 `BoundaryElementIds` 時才扣除。這道判斷會排除位在房間內部廁所隔牆、設備間隔牆等不構成該房間完成面邊界的門窗。

開口名稱使用 Revit 門窗品類的類型名稱，例如 `D5a-120x220 cm`，不要用族名稱、實例 Mark 或自行重組名稱取代。房間關係不足、宿主缺失或尺寸缺失者列入 warnings，不自動猜測。

## 淨高證據鏈

內牆粉刷的淨高按以下順序決定，並保留 `HeightSource`：

1. 收集 `OST_Ceilings`，以房間 bbox 與天花板 bbox 的交集範圍做多點取樣；只有 `Room.IsPointInRoom` 命中的實體天花板才是候選。
2. 實體天花板優先讀 `CEILING_HEIGHTABOVELEVEL_PARAM`／`距樓層的高度偏移`：

```text
淨高 = 天花板基準樓層標高 + 天花板高度偏移 - 房間基準標高
```

3. offset 不可用時，才以天花板幾何底面或 BoundingBox bottom 作備援，來源標記為 geometry/bbox。
4. 房間沒有實體天花板時，使用：

```text
淨高 = 上層樓層標高 - 上層代表結構樓板厚度 - 房間基準標高
```

5. 所有幾何來源都失敗時，才使用明確標記的 room explicit height / Revit height fallback。

計算途中保留完整精度；分組與顯示可採 0.05m 分級與小數兩位，但不得在乘法前逐步四捨五入，也不得用 `面積 / 周長` 反推一個看似精準但無法解釋的淨高。

## 輕隔間牆高是另一條規則

一般全高牆使用基準樓層到上層樓板底；斜板或局部降板可用「貼附頂/底」與幾何取樣判斷。低矮牆、襯板、腰牆或明顯未到頂牆保留自己的 Revit 牆高。詳細判斷以 `revit-partition-takeoff.md` 為準，不要把房間淨高規則直接套到所有牆。

## 材料與代碼對應

1. 踢腳讀房間 B 代碼，內牆粉刷讀 W 代碼；以 `+` 分割多值，空白與 `-` 視為無材料。
2. 欄位只建立專案中房間實際使用的代碼；只有類型定義但沒有實體數量時不列空欄。
3. 材料名稱可由使用者選擇的 `AE-材料版` 類型對應。去掉 `B1=`、`B2=`、`W1=` 等前綴，只保留材料名稱。
4. 房間沒有踢腳或粉刷仍須列出房間明細，數量欄保持空白或 0；完整房間清單與材料數量是兩個不同需求。

## Excel 報表契約

- 標題與預設檔名取目前 Revit `doc.PathName` 的檔名；只有未儲存文件才 fallback 到 `doc.Title`。
- 明細保留公式，例如 `周長 - SUM(開口)`、`周長 × 淨高 - SUM(開口面積)`，不要只輸出硬編結果。
- 表尾加入對應的「數量總計」，左側固定欄跨欄置中，各材料／牆型欄使用 `SUM`。
- 新建 `.xlsx` 可使用 OpenXML，避免依賴 Excel COM；更新既有 `.xls/.xlsx` 且需保護跨分頁公式時，可用 Excel COM，但必須 dry-run、備份、再寫入。
- 更新既有空間表時以 Revit ElementId 為優先身份、房號為人可讀鍵；不得任意重排固定引用列。

## RevitMCP 實作邊界

若把 pyRevit 邏輯轉成 MCP command，分成三層：

1. C# / Revit API：收集元素、房間 boundary、宿主、尺寸與高度來源，回傳結構化 JSON。
2. MCP Server schema：暴露範圍、代碼欄、排除條件、dry-run 與輸出選項，不寫死專案名稱或材料族名稱。
3. Skill / 報表層：選擇算量類型、套用公式、建立 Excel、顯示 warnings 與驗證摘要。

同一個工具回傳中應包含來源統計，例如 `boundary/fallback`、`actual-ceiling-offset/slab-bottom`、`deducted/skipped openings`。沒有本 turn 的 Revit 回讀資料時，不得宣稱已計算目前專案。

## 交付驗證

- 抽查一間有內部廁所隔牆門的房間，確認該門不從房間完成面邊界數量扣除。
- 抽查一間有實體天花板的房間，確認淨高等於天花板 offset 邏輯且 `HeightSource` 正確。
- 抽查一間無天花板房間，確認使用上層樓板底且樓板厚度來源可追溯。
- 確認所有有效 Rooms 都列出，包括無 B/W 代碼與零數量房間。
- 掃描 `#REF!`、`#VALUE!`、`#NAME?`、`#DIV/0!`，並比對明細 SUM 與材料／牆型總計。
- 回報 skipped openings、missing sizes、missing hosts、missing heights 與 fallback 數量，不能只說「匯出成功」。

## 參考實作

- `pyRevit_Tools/MCP_Tools.extension/MCP_Macros.tab/Takeoff.panel/PartitionTakeoff.pushbutton/script.py`
- `pyRevit_Tools/MCP_Tools.extension/MCP_Macros.tab/Takeoff.panel/Baseboard.pushbutton/script.py`
- `pyRevit_Tools/MCP_Tools.extension/MCP_Macros.tab/Takeoff.panel/InteriorWallPaint.pushbutton/script.py`
- `pyRevit_Tools/MCP_Tools.extension/MCP_Macros.tab/Takeoff.panel/SpaceTableUpdate.pushbutton/script.py`
