---
name: finish-legend-creation
description: 自動建立粉刷層與油漆材料填滿圖例 Legend 視圖。掃描全專案 Wall/Floor/Ceiling 的 CompoundStructure 粉刷層，以及用「油漆工具」塗色的面，輸出地坪／牆面／天花三張圖例表。觸發條件：粉刷圖例、finish legend、材料圖例、fill pattern legend、油漆材料、paint。
tags: [粉刷圖例, 材料圖例, finish legend, material legend, 填滿樣式, fill pattern legend, 圖例表, surface pattern legend, 油漆, paint, 塗料]
tools: [create_finish_legend, get_room_surface_areas]
revit_versions: [2022, 2023, 2024, 2025, 2026]
version: 1.2
last_updated: 2026-04-22
---

# 粉刷／油漆材料圖例自動建立

在 Revit 中自動建立「粉刷層＋油漆材料填滿圖例」Legend 視圖。本功能同時處理兩種資料來源：

1. **粉刷層**：掃描全專案所有房間 → 偵測 Wall/Floor/Ceiling 類型 CompoundStructure 中 Function=Finish 的層，依類型去重
2. **油漆材料**：掃描全專案 Wall/Floor/Ceiling 元素上用「油漆工具」塗的材料，依面法向量分類為牆/地/天

兩者合併進三張表（地坪／牆面／天花）。每張表三欄：編號 | 圖例 | 說明。粉刷列在上、油漆列在下，中間以「── 油漆材料 ──」分隔列隔開。

## 前提條件

1. **專案中至少存在一個 Legend 視圖**（即使空白也可）。Revit API 不允許直接建立 Legend，本功能會複製既有的 Legend。
   - 若沒有 Legend，請先在 Revit 中手動建立：`檢視 → 圖例 → 圖例`，命名隨意。
2. 建議先執行 `get_room_surface_areas` 並設 `includeFinishLayers: true` 確認粉刷層偵測正常（Excel 明細能看到 Wall/Floor/Ceiling 的粉刷類型）。
3. 專案中至少存在一個 `FilledRegionType`（任一名稱），作為新 FilledRegionType 的複製模板。

## 步驟

### 1. 確認粉刷層偵測已運作

先呼叫 `get_room_surface_areas`：

```json
{ "includeFinishLayers": true, "includeBreakdown": true, "subtractOpenings": true }
```

檢查回傳：確保 Breakdown 中有 `FinishLayers` 資訊、至少有一間房間偵測到粉刷層。若粉刷層為空，代表模型尚未建立粉刷 Wall/Floor/Ceiling，此時本圖例功能會產出空圖例。

### 2. 呼叫 `create_finish_legend`

```json
{ "legendName": "粉刷圖例_20260421" }
```

- `legendName`（選填）：新 Legend 名稱，預設 `粉刷圖例_yyyyMMdd`；若重複會自動加 `_HHmmss` 避免衝突。
- `legendTemplateName`（選填）：指定要複製哪個既有 Legend，預設取專案第一個 Legend。

### 3. 檢視回傳結果

```json
{
  "success": true,
  "legendViewId": 12345,
  "legendViewName": "粉刷圖例_20260421",
  "isNewLegend": true,
  "filledRegionTypes": {
    "created": 8, "reused": 3,
    "paintCreated": 5, "paintReused": 1
  },
  "rows": {
    "floors": 4, "walls": 5, "ceilings": 2,
    "paintFloors": 2, "paintWalls": 3, "paintCeilings": 1
  },
  "warnings": [
    "材料 'RC' 沒有 SurfaceForegroundPattern，已用 Solid Fill + 材料色",
    "粉刷類型 'W2 油漆' 的 CompoundStructure 找不到 Function=Finish 的層，已用第一非結構層的材料"
  ]
}
```

- `filledRegionTypes.created/reused`：粉刷類型建立／複用的 FilledRegionType 數量
- `filledRegionTypes.paintCreated/paintReused`：油漆材料建立／複用的 FilledRegionType 數量
- `rows.floors/walls/ceilings`：各類別的粉刷列數
- `rows.paintFloors/paintWalls/paintCeilings`：各類別的油漆列數

### 4. 切到新 Legend 檢視成果

在 Revit 專案瀏覽器展開 Legends → 雙擊新產出的 Legend。應看到三張表垂直排列：地坪粉刷圖例 → 牆面粉刷圖例 → 天花粉刷圖例，各表三欄：編號（TypeMark）｜圖例（FilledRegion）｜說明（TypeName）。

### 5. 拖到圖紙上

Legend 可被拖到多張圖紙，只占用一份資料。新建一張圖紙或打開既有圖紙，從專案瀏覽器把 Legend 拖入。

## 油漆材料偵測（Paint Tool）

使用者常用 Revit 的「油漆」工具（修改 → 幾何圖形 → 上漆）把材料直接塗在元素的某個面上，這些材料不在 CompoundStructure 裡。本功能自動偵測這類材料並列入圖例：

- **偵測範圍**：只掃 Wall / Floor / Ceiling 三個類別的元素（不含柱、梁、家具族內的面）
- **快速預篩**：先以 `Element.GetMaterialIds(true)` 判定元素是否有 Paint 塗層，無則跳過 geometry 遍歷
- **面分類**：讀取每個塗色面的法向量 → 依 Z 分量分類
  - `normal.Z > 0.2` → 地板
  - `normal.Z < -0.2` → 天花
  - 其他 → 牆面
  - 斜面（屋頂、斜天花）依 Z 正負歸入最近的類別
- **去重**：以 `(Category, MaterialId)` 為 key。同款材料塗在牆和天花 → 牆表與天花表各一筆
- **編號欄 = Material.Mark，說明欄 = Material.Description**；空值顯示字串 `(未填)`
- **FilledRegionType 命名**：`Paint {MaterialName}`（避免與粉刷類型同名衝突）
- **同一份 Legend 內表格版面**：粉刷列在上、油漆列在下，中間插入「── 油漆材料 ──」分隔列。若某表僅有粉刷（或僅有油漆），不出現分隔列

## 版面規格（固定）

- Legend 視圖比例：**1:100**
- 欄寬：**130 / 120 / 650 cm**（總寬 900 cm）
- 列高：**50 cm** 一律相同
- 三張表之間留 **100 cm** 空白
- 標題列：跨 3 欄合併，文字「{類別}粉刷圖例」置中
- 表頭列：固定「編號 / 圖例 / 說明」置中
- 資料列：TypeMark 置中、FilledRegion 填滿中間欄、TypeName 靠左對齊
- 文字：Microsoft JhengHei UI、3.0 mm、寬度係數 0.7、黑色（若不存在會建立 TextNoteType「粉刷圖例 3mm」）

## 挑層規則（CompoundStructure）

從 WallType/FloorType/CeilingType 的 CompoundStructure 中，依以下順序挑一層作為圖例展示：

1. `Function = Finish1`（首選）
2. `Function = Finish2`
3. 多層 Finish 時取最外側（Exterior 一側）
4. 全找不到：取第一個非 Structure 層的材料（warnings 會註記）

## 填滿樣式規則

讀取挑中層的 Material：

- 有 `SurfaceForegroundPatternId` + Color → 套用為 FilledRegionType 前景
- 有 `SurfaceBackgroundPatternId` + Color → 套用為背景
- 兩者皆空 → 用 `<Solid fill>` + 材料 Color，且在 TypeName 後加「(僅顏色)」標記

FilledRegionType 命名：`TypeMark + 空格 + TypeName`（如 `F1 整體粉光+彈泥`）。同名已存在則複用（warnings 中 `reused` 計數）。

## 失敗模式與錯誤訊息

| 情境 | 行為 |
|------|------|
| 專案無任何 Legend 視圖 | 拋錯並引導：「請先在 Revit 建立一個空白 Legend 再重新呼叫」 |
| 專案無任何粉刷層也無油漆材料 | 回傳 `success: true`、rows 全為 0、warnings 註記「無偵測到粉刷層或油漆材料」 |
| 同名 FilledRegionType 已存在 | 複用（不覆蓋設定） |
| 同名 Legend 已存在 | 自動加 `_HHmmss` 時間後綴 |
| 找不到 FilledRegionType template | 拋錯：「請先在 Revit 建立任一填滿區域類型」 |
| 元素 Geometry 讀取失敗 | warnings 註記該元素 Id，跳過繼續處理 |
| 油漆材料 Mark 或 Description 為空 | 顯示字串 `(未填)` |

## 不會做的事（負面清單）

- 不修改既有 `get_room_surface_areas` 的回傳 schema 或行為
- 不建立新的房間或修改既有房間
- 不覆蓋既有同名 FilledRegionType 的設定（只複用或新建）
- 不修改既有 Legend 視圖（只複製一份新的）
- 不涉及視圖模板、圖紙、標題欄

## 效能注意

- 粉刷層掃描與油漆材料掃描是兩段獨立流程，大型專案首次呼叫可能 30–60 秒
- 油漆偵測以 `Element.GetMaterialIds(true)` 預篩，無塗層元素會被快速跳過
- 低頻操作（一個專案做一次圖例就好），不引入額外 cache

## 相關檔案

- C# 實作：`MCP/Core/Commands/CommandExecutor.FinishLegend.cs`
- 底層粉刷偵測：`MCP/Core/Commands/CommandExecutor.RoomSurface.cs`
- MCP Tool 定義：`MCP-Server/src/tools/room-tools.ts`
- 前置 Domain：`domain/room-surface-area-review.md`（粉刷層偵測說明）
