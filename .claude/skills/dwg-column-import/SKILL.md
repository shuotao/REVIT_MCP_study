---
name: dwg-column-import
description: |
  從 CAD/DWG 圖層批次建立 Revit 結構柱/建築柱：單位/放樣健檢 → 選模式 → 批次建模。三模式（自動建型別 / 既有族群尺寸對應 / 既有族群柱號對應）依情境選用，分步協作不一口氣。
  TRIGGER when: dwg 建柱, cad 建柱, 圖層建柱, 批次建柱, dwg 匯入建模, cad 柱, column from dwg, 從圖面建柱, 柱號對應, 柱名稱對應, C1 C2 柱
user-invocable: true
---

依據 `domain/dwg-column-import.md` 執行。把 CAD 圖面的矩形柱輪廓批次轉成 Revit 柱。

**鐵則：建柱不可自動復原，逐步走斷點、每步取得同意，不要一口氣跑完。**

## Prerequisites（先確認，缺則停止並告知）
- Revit 開在**平面視圖**（三個工具都要求 `ViewPlan`）。
- 目標 CAD 已**匯入(import)或連結(link)**到該視圖（模式 C 須 link）。
- （模式 B/C）專案已**載入目標矩形柱族**。
- 柱輪廓畫在**獨立圖層**（靠圖層名識別，非顏色/線型）。

## 模式路由（依使用者語意選一，這是斷點 2 的核心）
| 關鍵字訊號 | 模式 | 帶入參數 |
|---|---|---|
| 「柱號 / 對應名稱 / C1 C2 / 沿用圖面命名」 | **C** 既有族群·柱號對應 | `familyName` + `textLayerName` |
| 指定族群但只談尺寸沿用 | **B** 既有族群·尺寸對應 | `familyName` |
| 「快速建柱 / 不在意命名」 | **A** 自動建型別 | 都不給 |

不確定就**列出族群問使用者**，不要替他預設。

## 工作流（強制斷點）

### 步驟 1 — 掃描圖層
- `get_dwg_column_layers` → 回報 CAD 圖層清單、建議柱圖層、Link/Import 狀態。請使用者確認用哪個圖層。

### 步驟 2 — 放樣/單位健檢 ⛔ 斷點 1（第一要務）
- `preview_dwg_columns(layerName)` → 看 `count`、`sizeSummary`、`columns`，**重點看 `preflight`**：
  - `preflight.unitSanity`：若為 `check`，**停下**，把 `preflight.warnings` 回報使用者（多半是 CAD 連結單位選錯，如 `$INSUNITS=0` 的 DXF 連成 mm），請回連結對話框改正單位後重做。**不要硬建。**
  - `preflight.sizeRangeMm` / `extentMm`：複述斷面尺寸跨度與放樣範圍，確認落在合理柱斷面（100–3000mm）與正確圖面位置。
- 向使用者複述「N 根柱、尺寸分布、放樣範圍、單位健檢結果」，取得「定位放樣正確」的確認才往下。

### 步驟 3 — 選 family 模式 ⛔ 斷點 2（協作選擇，不替使用者預設）
- 模式 B/C：先 `get_column_types` 或 `list_family_symbols` 列出可用族群/類型給使用者挑。
- 與使用者敲定：模式（A/B/C）、`familyName`、`textLayerName`、`columnType`（structural 預設 / architectural）。
- **缺對應柱號類型時的協作路徑**（不一口氣）：可先模式 A/B 建尺寸型別 → `modify_element_parameter` 改名+改參數成 C1/C2… → 再模式 C 重跑。
  - ⚠️ `modify_element_parameter` 尺寸值是 **Revit 內部 feet 不是 mm**：600mm 要傳 `1.9685`（600/304.8）；改名用 `parameterName="Name"`。

### 步驟 4 — 批次建柱（改模型，不可復原）
- 執行前複述「將在圖層 X 以模式 ? 建 N 根 columnType 柱，族群 Y、柱號圖層 Z」並取得同意。
- `create_columns_from_dwg(layerName, columnType, familyName?, textLayerName?)`。
- 看回傳：`created`/`failed`、`typesUsed`（含 C1/C2 表柱號對應成功）、`unmatchedLabels`、`labelReadStatus`、`labelsPreview`、`matchDebug`（`dist≈0` 表單位/對位成功）。
- **`unmatchedLabels` 不為空 → 停下協作**：是圖說錯誤？還是族群缺類型（走上面的改名到位協作）？不要自動亂建。

### 前置補樓層（視需要）
- 報「找不到高於基準層的樓層」→ 先 `create_level(elevation, name)` 補上層再重試。

## 關鍵確認（每次帶到，理由見 domain）
- **單位/放樣是第一步且最重要**：靠步驟 2 的 `preflight` 把關。
- **座標對位**：柱位置＝CAD 在模型中的實際座標，**未做 shared coordinates 換算**；建柱後抽查中心對位，import 與 link 各驗。
- **DXF/DWG 通用**：模式 C 讀柱號 DXF 直讀、DWG 需 ODA（無則 `no_oda` 優雅降級、柱照建不對名）。
- **圖層而非顏色**：只能用圖層名篩選。

## Error Handling
| 症狀 | 可能原因 | 處理 |
|---|---|---|
| 「請在平面視圖中執行」 | 當前非 ViewPlan | 切平面視圖 |
| 圖層清單為空 | CAD 未載入/連結遺失/不可見 | 確認 import/link 與可見性 |
| `preflight.unitSanity=check` | 連結單位選錯（尺寸全 10x 偏差） | 回連結對話框改正單位重做，勿建 |
| 「找不到…族群」 | 未載入矩形柱族 | 先載入 RC/矩形柱族 |
| preview 數量為 0 | 圖層錯誤或非矩形/尺寸超範圍 | 換圖層；確認柱為 100–3000mm 矩形 |
| 「找不到高於…的樓層」 | 無上層 Level | 先 `create_level` 補樓層 |
| `labelReadStatus=no_worker` | 找不到 `ezdxf_worker.py` | 跑 `install-addon.ps1` 或複製 worker 到 `%APPDATA%\RevitMCP\` |
| `labelReadStatus=no_oda` | DWG 需 ODA File Converter | 安裝 ODA 或改用 DXF |
| `labelReadStatus=error` 含「Import」 | CAD 用匯入非連結 | 改用「插入→連結 CAD」 |
| `unmatchedLabels` 不為空 | 族群缺對應類型 | 協作：圖說錯誤？或改名到位後重跑 |

## Related
- domain：`domain/dwg-column-import.md`
- 互補工具：`create_column`（單根手動建柱，與本批次工作流並存）
- 來源 fork：幾何 `s9101800111-byte`；柱號對應 `Roy-y111`
