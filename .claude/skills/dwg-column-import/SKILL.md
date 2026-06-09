---
name: dwg-column-import
description: |
  從 CAD/DWG 圖層自動批次建立 Revit 結構柱/建築柱：掃描圖層 → 預覽確認 → 批次建模。
  TRIGGER when: dwg 建柱, cad 建柱, 圖層建柱, 批次建柱, dwg 匯入建模, cad 柱, column from dwg, 從圖面建柱
user-invocable: true
---

依據 `domain/dwg-column-import.md` 執行。把 CAD 圖面的矩形柱輪廓批次轉成 Revit 柱。

## Prerequisites（先確認，缺則停止並告知使用者）

- Revit 開在**平面視圖**（三個工具都要求 `ViewPlan`）。
- 目標 CAD 已**匯入(import)或連結(link)**到該視圖。
- 專案已**載入矩形柱族**（RC/混凝土/矩形優先）。
- 柱輪廓畫在**獨立圖層**（靠圖層名識別，非顏色/線型）。

## 工作流（三段式，preview 必先於 create）

### Phase 1 — 掃描圖層
- 工具：`get_dwg_column_layers`
- 取得視圖內所有 CAD 圖層與「建議柱圖層」。把清單與建議回報給使用者確認要用哪個圖層。

### Phase 2 — 預覽（不改模型）
- 工具：`preview_dwg_columns(layerName)`
- 回傳每根柱 x/y(mm)、寬、深、旋轉角與尺寸分組統計。
- **檢核點**：數量是否合理？尺寸是否落在常見柱斷面（非整片牆/雜訊）？旋轉角是否正常？任何異常先回報使用者，不要貿然建模。

### Phase 3 — 批次建柱（改模型，不可自動復原）
- 工具：`create_columns_from_dwg(layerName, columnType)`
- `columnType`：`structural`（預設）或 `architectural`。
- 執行前向使用者複述「將在圖層 X 建立 N 根 columnType 柱」並取得同意。
- 回傳 created/failed/errors，逐項回報；failed 多時提示檢查族群與尺寸範圍。

### 前置補樓層（視需要）
- 若 Phase 3 報「找不到高於基準層的樓層」，先用 `create_level(elevation, name)` 補上層樓層再重試。

## 關鍵確認（每次都要帶到，理由見 domain）

- **座標對位**：柱位置＝CAD 在模型中的實際座標，**未做 shared coordinates 換算**。建柱後抽查中心對位；import 與 link 兩情境分別驗。
- **import vs link**：兩者都讀，但 link 須已正確載入、路徑有效。
- **圖層而非顏色**：只能用圖層名篩選；同圖層雜物會被當候選矩形。

## Error Handling

| 症狀 | 可能原因 | 處理 |
|---|---|---|
| 「請在平面視圖中執行」 | 當前非 ViewPlan | 切到平面視圖再執行 |
| 圖層清單為空 | CAD 未載入/連結遺失/不可見 | 確認 import/link 狀態與視圖可見性 |
| 「找不到…族群」 | 未載入矩形柱族 | 先載入 RC/矩形柱族再重試 |
| preview 數量為 0 | 圖層錯誤或非矩形/尺寸超範圍 | 換圖層；確認柱為 100–3000mm 矩形 |
| 「找不到高於…的樓層」 | 無上層 Level | 先 `create_level` 補樓層 |
| 柱整批偏移 | CAD 匯入未對位 | 重新以正確原點/比例匯入 |

## Related
- domain：`domain/dwg-column-import.md`
- 互補工具：`create_column`（單根手動建柱，與本批次工作流並存）
