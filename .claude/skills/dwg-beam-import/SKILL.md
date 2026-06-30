---
name: dwg-beam-import
description: |
  從 CAD/DWG 圖層批次建立 Revit 結構樑：掃描圖層 → 確認批次與模式 → 放樣健檢 → 批次建樑 → 詢問下一批次。支援全正交與含非正交（斜樑）圖說，分批處理（大樑 → 次樑 → 地樑）。
  TRIGGER when: 結構樑翻模, dwg 建樑, cad 建樑, 圖層建樑, 批次建樑, beam from dwg, 從圖面建樑, 樑翻模, 大樑, 次樑, 地樑, create_beams_from_dwg
user-invocable: true
---

依據 `domain/dwg-beam-import.md` 執行。把 CAD 圖面的雙線樑批次轉成 Revit 結構樑。

**鐵則：建樑不可自動復原，逐步走斷點、每步取得同意，不一口氣跑完。**

## Prerequisites（先確認，缺則停止並告知）
- Revit 開在**平面視圖**（三個工具都要求 `ViewPlan`）。
- 目標 CAD 已**匯入或連結**到該視圖。
- 專案已**載入目標結構樑族群**。
- 樑輪廓與文字標注畫在**獨立圖層**（靠圖層名識別）。

## 工作流（強制斷點）

### 步驟 1 — 掃描圖層 ⛔ 斷點
`get_dwg_beam_layers` → 回報視圖、CAD 數量、推薦樑圖層，並依序詢問：
1. 這次先處理哪個批次？（大樑 / 次樑 / 地樑）
2. 翻模模式：
   - **模式 A（快速）**：指定統一型別，全部套用，**不需要文字圖層**
   - **模式 B（名稱對應）**：依圖面文字自動配對 Revit 型別

### 步驟 2 — 依模式收集參數
- **模式 A**：詢問族群名稱 + 統一型別名稱（如 `50x70`）→ 直接進步驟 3
- **模式 B**：詢問文字圖層（X 向、Y 向可分開，也可共用）→ 詢問族群名稱 → 進步驟 3

### 步驟 3 — 放樣健檢（僅供參考，不等確認）
`preview_dwg_beams(layerName)` → 回報偵測樑數、X / Y / 斜向各幾根、樑寬範圍，直接繼續下一步。

### 步驟 4 — 批次建樑（改模型，不可復原）⛔ 執行前複述參數取得同意
`create_beams_from_dwg(layerName, familyName, textLayerNameX?, textLayerNameY?, typeName?, beamRole?)`

**內部兩波機制（斜樑防呆）：**
- 第一波：正交樑優先配對，消耗標籤進 `usedLabelKeys`
- 第二波：斜樑用剩餘標籤，點到線段距離配對（閾值 = 樑寬/2 + 500mm）

**回傳檢查：**
| 現象 | 處理 |
|---|---|
| `unmatchedLabels` 不為空 | 停下協作：族群缺對應型別？ |
| `failed > 0` 且 `unmatchedLabels` 為空 | 樑端點問題（太短/無支撐），手動補 |
| `labelReadStatus = no_oda` | 安裝 ODA File Converter 或改用 DXF |

### 步驟 5 — 詢問下一批次
完成後詢問：是否繼續處理下一批次？（大樑 → 次樑 → 地樑）

## Error Handling
| 症狀 | 處理 |
|---|---|
| 「請在平面視圖中執行」 | 切換至平面視圖 |
| 圖層清單為空 | 確認 CAD 已匯入/連結且可見 |
| preview 數量為 0 | 換圖層或確認樑為雙線且寬度在 100–2500mm |
| 「找不到族群」 | 先載入結構樑族群 |

## Related
- domain：`domain/dwg-beam-import.md`
- 互補 skill：`dwg-column-import`（柱翻模）
- 核心實作：`MCP/Core/DwgBeamExecutor.cs`
