---
name: room-surface-area-review
description: "計算 Revit 房間內部表面積（牆面、地板、天花板），支援門窗開口扣除與粉刷層偵測。適用於材料估算、塗裝面積計算、聲學分析、裝修報價。當使用者提到表面積、粉刷面積、塗裝面積、材料估算、surface area 時觸發。"
metadata:
  version: "1.0"
  updated: "2026-03-25"
  created: "2026-03-24"
  contributors:
    - "Gemini"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by: []  # TODO: 月小聚補（被哪些 skill 引用）
  tags: [表面積, surface area, 牆面面積, 塗裝面積, 材料估算, 粉刷, 貼磚, interior surface, wall area, ceiling area]
---

# 房間內部表面積檢討

計算 Revit 房間的內部表面積（牆面、地板、天花板），支援門窗開口扣除。適用於材料估算、塗裝面積計算、聲學分析、裝修報價。

## 前提條件

1. Revit 模型中已建立房間（Room），且房間已正確封閉（Area > 0）
2. 牆壁邊界元素已正確建模。地板與天花板元素為選用——若無實體元素，面積將以房間平面面積估算
3. 門、窗、Opening 等開口族群已嵌入牆中（使用 Insert 方式放置）

## 步驟

### 1. 確認目標範圍

詢問使用者要檢討的範圍：
- 單一房間（by roomId 或 roomName）
- 整層（by level）
- 全部房間（不指定篩選條件）

### 2. 呼叫 `get_room_surface_areas`

**`includeFinishLayers` 為必填參數**，必須明確指定 `true` 或 `false`。

```
get_room_surface_areas({
  level: "1F",              // 或 roomId / roomName
  includeBreakdown: true,   // 顯示每面牆的詳細面積
  subtractOpenings: true,   // 扣除門窗開口
  includeFinishLayers: true // ← 必填！要偵測粉刷層請設 true
})
```

> **⚠️ 常見錯誤：** 省略 `includeFinishLayers` 會導致粉刷層偵測完全不執行，明細表和 Excel 不會產生。即使使用者口頭要求「分析粉刷層」，仍必須在參數中明確傳 `true`。

### 2.5 詢問預設粉刷層（未偵測到粉刷的表面）

> **設計原理：** C# 工具本身不會主動詢問使用者。「詢問預設材料」的互動由 AI Client 在兩次呼叫之間負責。這是 **AI-in-the-loop** 設計——工具是無狀態的，互動邏輯由 AI 編排。

#### 判斷條件

收到第一次 `get_room_surface_areas` 結果後，AI **必須**檢查各房間的回傳欄位：

| 欄位 | 位置 | 判斷 |
|------|------|------|
| `FloorFinishLayers` | 房間層級 | `null` = 該房間地板無粉刷 |
| `CeilingFinishLayers` | 房間層級 | `null` = 該房間天花無粉刷 |
| `FinishLayers` | Breakdown 每面牆項目內 | `null` = 該面牆無粉刷 |

若**任何一個房間的任何一個表面**缺少粉刷層，**必須**向使用者詢問：

> 以下表面未偵測到粉刷層：
> - 地板：{列出缺少的房間名稱與編號}
> - 牆面：{列出缺少的房間名稱與編號}
> - 天花：{列出缺少的房間名稱與編號}
>
> 請問要統一填入什麼粉刷層類型標記（Type Mark）？（每種只能填一種，留空表示不填）
> 1. 地板預設粉刷層：
> 2. 牆面預設粉刷層：
> 3. 天花預設粉刷層：

> **⚠️ 所有房間表面都有粉刷時，跳過此步驟**，直接進入步驟 3。

#### 第二次呼叫（帶預設值）

使用者回覆後，以 `defaultFloorFinish`、`defaultWallFinish`、`defaultCeilingFinish` 參數**再次呼叫** `get_room_surface_areas`：

```
get_room_surface_areas({
  level: "1F",
  includeBreakdown: true,
  subtractOpenings: true,
  includeFinishLayers: true,
  defaultFloorFinish: "使用者填入的地板類型標記",
  defaultWallFinish: "使用者填入的牆面類型標記",
  defaultCeilingFinish: "使用者填入的天花類型標記"
})
```

若使用者某項留空，則該參數不傳入。

#### 快取機制（C# 端實作細節）

第二次呼叫觸發的是**快取快速路徑**，不會重新做幾何分析：

1. **快取時機**：第一次呼叫完成後，C# 端將 `targetRooms` 和 `roomResults` 存入 instance 變數（`_cachedTargetRooms`、`_cachedRoomResults`）
2. **快速路徑觸發條件**：第二次呼叫時，若同時滿足 (a) 有任一 `defaultXxxFinish` 參數 (b) 快取存在且非空，則跳過所有幾何計算
3. **填入邏輯**（`ApplyDefaultsToResults`）：
   - 地板/天花板：`FloorFinishLayers == null` 時填入，已有粉刷的不覆蓋
   - 牆面：逐面檢查 Breakdown，`FinishLayers == null` 的牆面才填入
   - 面積取值：地板/天花板用該房間的 `FloorArea_m2`/`CeilingArea_m2`；牆面用該面牆的 `NetArea_m2`
   - `AreaMethod` 標記為 `DefaultFill`
4. **後處理**：填入預設值後，才執行寫入參數、建立明細表、匯出 Excel 三項操作

#### 類型標記查找（`LookupFinishType`）

C# 端收到類型標記字串後，會在專案中查找對應的粉刷類型：

1. 依類別（Wall/Floor/Ceiling）遍歷所有 WallType/FloorType/CeilingType
2. 比對 `ALL_MODEL_TYPE_MARK` 參數（不分大小寫）
3. 找到時：取得完整類型名稱與 `CompoundStructure` 複合層資訊
4. 找不到時：以類型標記字串直接填入（`typeName = typeMark`，無複合層）
5. 查找結果有內部快取（`_finishTypeCache`），同一類型標記只查一次

#### 不覆蓋原則

預設值只會填入**沒有偵測到粉刷層的表面**——已有偵測到粉刷的表面永遠不會被覆蓋。牆面是逐面判斷：同一房間中，已有粉刷的牆保留原值，無粉刷的牆填入預設。

### 3. 解讀結果

回傳結構包含：

| 欄位 | 說明 |
|------|------|
| `FloorArea_m2` | 地板面積（m²） |
| `CeilingArea_m2` | 天花板面積（m²），斜天花板為實際斜面面積 |
| `WallGrossArea_m2` | 牆面毛面積（含所有開口） |
| `OpeningArea_m2` | 開口面積（門、窗、Opening 族群等所有牆上嵌入物） |
| `WallNetArea_m2` | 牆面淨面積（已扣除所有開口） |
| `TotalNetSurfaceArea_m2` | 總淨表面積 = 地板 + 天花板 + 牆面淨 |
| `Method` | 計算方法（精確 or Fallback） |

### 4. 檢查 Method 欄位

- `SpatialElementGeometryCalculator`：精確 3D 計算，結果可信
- `Fallback_BoundarySegments`：簡化計算（邊界線段 × 高度），斜天花板或不規則幾何可能不準確

### 5. 分析 Breakdown（選用）

`includeBreakdown: true` 時，每面牆會列出：
- 宿主元素 ID 與類型名稱
- 該牆面的毛面積、開口面積、淨面積

可用於：
- 依牆壁類型分類統計（如：內牆 vs 外牆的粉刷面積）
- 確認特定牆面的開口面積是否合理

### 6. 彙總報告

回傳的 `Summary` 包含所有房間的加總：
- `TotalFloorArea_m2` / `TotalCeilingArea_m2` / `TotalWallNetArea_m2`
- `TotalNetSurfaceArea_m2`：最終的總淨表面積

## 注意事項

- **未封閉房間**（Area = 0）會被自動跳過，出現在 `Warnings` 中
- **連結模型的牆**：宿主元素可能在連結模型中，Breakdown 中會顯示連結實例的 ElementId
- **帷幕牆**：Calculator 視為牆面正常計算
- **共用牆**：兩個房間共用的牆，各自會計算各自那側的面積
- **單位**：所有面積輸出為平方公尺（m²），四捨五入至小數點後 2 位

## 面積分類原理（法線優先）

精確計算模式（`SpatialElementGeometryCalculator`）從 room solid 的各個面（Face）計算面積，分類規則如下：

| 面法線方向 | 分類 | 說明 |
|-----------|------|------|
| \|normal.Z\| > 0.8 且 Z < 0 | **Floor**（地板） | 朝下的水平面 |
| \|normal.Z\| > 0.8 且 Z > 0 | **Ceiling**（天花板） | 朝上的水平面 |
| \|normal.Z\| ≤ 0.8 | **Wall**（牆面） | 非水平面，進入 host breakdown |

**關鍵設計：** 水平面的分類完全由面法線方向決定，不查詢 boundaryFaceInfo 的宿主元素（host）。這是因為底面的 boundary info 可能包含牆壁元素（牆延伸到樓層底部），若依 host 判斷會導致地板面積被錯誤歸入牆面。

牆面的 breakdown 則透過 `boundaryFaceInfo` 追蹤每面牆的宿主元素、毛面積、開口面積。

## 開口偵測原理（不受 Computation Height 影響）

**重要：** 本工具的開口偵測**不使用** Revit 的 Computation Height 水平切面。即使房間的計算高度設為 0，仍可正確偵測所有開口。

偵測流程：
1. 從 room solid 的牆面找到宿主牆（Wall）元素
2. 對每面牆呼叫 `wall.FindInserts(true, true, true, true)` — 搜尋牆上**所有**嵌入物，不受高度限制
3. 接受所有類別的嵌入物（門 `OST_Doors`、窗 `OST_Windows`、Opening 族群、其他嵌入物）
4. 每面牆只計算一次開口（以 `processedWallOpenings` HashSet 防止重複）
5. 面積取得優先順序：Instance 內建參數 → 粗開口參數（ROUGH_WIDTH/HEIGHT） → FamilySymbol Type 參數 → BoundingBox fallback

**⚠️ AI 不應聲稱本工具「偵測不到窗戶」或需要「手動修正開口面積」。** 若 `OpeningArea_m2 > 0`，代表開口已被自動偵測並扣除。

## 無實體天花板/地板的處理

當 room solid 的水平面面積為 0（極少數情況，如房間幾何異常）：
- 自動以房間平面面積（`room.Area`）補償天花板/地板面積，假設為水平面
- 回傳結果中的 `EstimatedSurfaces` 欄位會標示哪些面積為估算值：
  - `CeilingEstimated: true`：天花板面積為估算
  - `FloorEstimated: true`：地板面積為估算
- **一般情況下**，即使模型中沒有建天花板或地板元素，room solid 仍會有水平面，面積由法線方向正確分類，不會觸發估算
- 斜天花板的實際面積會反映在 room solid 的斜面上（normal.Z > 0.8 但非完全水平）

## 粉刷層偵測功能（`includeFinishLayers: true`）

當 `includeFinishLayers` 設為 `true` 時，除了計算房間表面積，還會偵測房間內部的粉刷層（面飾層），並自動執行三項操作。

### 建模前提

粉刷層（牆面粉刷、地板面飾、天花板面飾）在建模時必須設為 **Room Bounding = No**（非房間邊界），讓房間邊界停在結構牆上，粉刷層元素包含在 Room Solid 體積內。

典型建模方式：結構牆（RC，Room Bounding = Yes）上貼一層薄牆（磁磚/粉刷，Room Bounding = No，構造功能設為 Finish 1/Finish 2）。地板和天花板同理。

### 偵測原理

1. **BoundingBox 預篩**：快速找出 BB 與房間重疊的候選牆/樓板/天花板
2. **排除邊界元素**：移除已知的房間邊界（boundary segments + host areas）
3. **Solid 交集精篩**：用 3D 布林運算確認候選元素確實在房間體積內
4. **IsPointInRoom 備援**：Solid 交集失敗的候選元素，改用 `Room.IsPointInRoom()` 逐一驗證（測試元素曲線的 25%/50%/75% 三個點），確保 T/L/U 形等複雜輪廓房間不會誤判鄰室材料

> **注意：** 不再有 blanket fallback（不會將所有 BoundingBox 候選全部接受）。每個候選元素必須通過 Solid 交集**或** IsPointInRoom 至少一項檢查。

偵測三個類別：`OST_Walls`、`OST_Floors`、`OST_Ceilings`

### 牆面粉刷的母牆關聯

牆面粉刷層會嘗試關聯到最近的邊界牆：
- 平行度 > 0.95（DotProduct）
- 距離 < 1000mm
- 取最近的邊界牆

關聯成功的粉刷層出現在對應牆面 Breakdown 的 `FinishLayers` 內。
關聯失敗的放入 `UnassociatedFinishLayers`。

### 覆蓋面積計算

| 情境 | 計算方法 | AreaMethod 標記 |
|------|----------|----------------|
| 單一粉刷（一面表面一種類型） | 面積 = 母牆 NetArea / FloorArea / CeilingArea | `SurfaceArea` |
| 多種牆面粉刷 | LocationCurve 投影計算各自長度 × 高度 | `LocationCurve` |
| 多種地板/天花板粉刷 | Solid 布林交集取水平面面積 | `SolidIntersection` |
| 以上失敗時 | 讀取元素 HOST_AREA_COMPUTED | `ElementArea` |
| 使用者指定預設粉刷 | 該表面的總面積（牆面為母牆 NetArea） | `DefaultFill` |

### 自動執行三項操作

1. **寫入房間飾面參數**
   - `ROOM_FINISH_WALL`（牆面塗層）
   - `ROOM_FINISH_FLOOR`（地板塗層）
   - `ROOM_FINISH_CEILING`（天花板塗層）
   - 值為粉刷層的類型標記（Type Mark），多種逗號串接

2. **建立明細表「各空間粉刷表」**
   - 欄位：樓層、房間編號、房間名稱、天花板塗層、地板塗層、牆面塗層
   - 排序：樓層遞增 → 房間編號遞增

3. **匯出 Excel**
   - 檔名：`粉刷面積明細_{日期}.xlsx`
   - 欄位：房間編號、名稱、各表面的粉刷層列表與面積、各表面總面積
   - 多粉刷層每種佔一行，房間資訊欄位合併儲存格

### 輸出新增欄位

| 欄位 | 位置 | 說明 |
|------|------|------|
| `FinishLayers` | Breakdown 牆面項目內 | 該面牆的粉刷層列表 |
| `FloorFinishLayers` | 房間層級 | 地板粉刷層列表 |
| `CeilingFinishLayers` | 房間層級 | 天花板粉刷層列表 |
| `UnassociatedFinishLayers` | 房間層級 | 找不到母牆的牆面粉刷層 |
| `FinishSchedule` | 最外層 | 建立的明細表名稱 |
| `ExcelPath` | 最外層 | Excel 匯出路徑 |

## 除錯指南

### 診斷 Log

C# 端在關鍵節點寫入 `Logger.Info()`，Log 檔位於 `%AppData%\RevitMCP\Logs\RevitMCP_YYYYMMDD.log`，搜尋 `[RoomSurface]`：

| Log 訊息 | 含義 |
|----------|------|
| `includeFinishLayers=False, raw=NULL` | **AI Client 未傳參數** — 檢查 MCP 工具定義是否為 required |
| `includeFinishLayers=True` | 參數已正確傳入 |
| `BoundingBox candidates: walls=0, floors=0, ceilings=0` | 房間附近無候選粉刷層元素 — 檢查粉刷層是否存在且 BB 與房間重疊 |
| `Final finish elements: N (Solid: X, IsPointInRoom: Y)` | 最終通過的粉刷層數量，X 為 Solid 交集通過，Y 為 IsPointInRoom 備援通過 |
| `Element {id} passed IsPointInRoom fallback` | 該元素 Solid 交集失敗，但位置點確實在房間內 — 複雜幾何房間的正常行為 |
| `IsElementInsideRoomSolid geometry error for {id}` | Solid 布林運算失敗 — 會自動嘗試 IsPointInRoom 備援 |
| `Fallback: BB candidates=N, after IsPointInRoom filter=M` | Fallback 計算路徑的過濾結果，M < N 表示有效過濾了鄰室元素 |
| `LookupFinishType: found {category} type '{name}' for mark '{mark}'` | 成功在專案中找到對應的粉刷類型 |
| `LookupFinishType: no {category} type found for mark '{mark}'` | 專案中找不到對應類型，將以字串直接填入 |
| `DefaultFill: floor/wall/ceiling → '{mark}', area=Xm²` | 預設粉刷已填入指定表面 |

### 部署注意

- `dotnet build -c Release.R24` 的輸出在 `MCP/bin/Release.R24/RevitMCP.dll`（**不是** `bin/Release/`）
- 部署時必須從 `bin/Release.R{YY}/` 複製，否則會部署到舊版 DLL

## 典型對話範例

```
使用者：幫我算 3F 所有房間的粉刷面積
AI：
1. 呼叫 get_room_surface_areas(level: "3F", subtractOpenings: true, includeFinishLayers: true)
2. 列出每個房間的三項面積：
   - WallNetArea_m2（牆面淨面積）
   - CeilingArea_m2（天花板面積）
   - FloorArea_m2（地板面積）
3. 列出偵測到的粉刷層與覆蓋面積
4. 以表格呈現各房間明細與總計
5. 回報明細表名稱與 Excel 匯出路徑
```
