---
name: section-datum-adjustment
description: "剖面視圖網格線與樓層線自動調整：根據裁剪框 (Crop Box) 及樓層字串長度自動適應 2D 範圍。"
metadata:
  version: "1.0"
  updated: "2026-05-20"
  created: "2026-05-20"
  tags: [Revit, Automation, Section, Datum, Grid, Level]
---

# 剖面視圖基準線自動調整 (Section Datum Adjustment SOP)

## 1. 目的
自動化地調整 Revit 剖面視圖中的網格線 (Grids) 與樓層線 (Levels) 的 2D 顯示範圍與氣泡顯示，使圖面排版自動達到出圖標準，並可批次套用於多個視圖。

## 2. 核心功能規格

### 2.1 網格線 (Grids / 柱線)
*   **範圍延伸 (Offset)**：上下端點統一超出視圖裁剪框 (Crop Box) **150 mm**。
*   **氣泡顯示 (Bubble)**：僅顯示**上方氣泡**，下方氣泡隱藏。

### 2.2 樓層線 (Levels)
*   **動態範圍延伸 (Dynamic Offset)**：因樓層名稱（如 `4FL-1200cm` 與 `100FL-30000cm`）的字元長度不同，為保持氣泡文字與裁剪框之間的視覺呼吸空間，將採用**字串長度動態加權演算法**。
    *   **計算邏輯**：`Offset = Base_Offset + (字元總數 * 字寬係數 * 視圖比例)`。字數越多，超出裁剪框的線段越長，避免文字擠壓。
*   **氣泡顯示 (Bubble)**：**左右兩側**皆顯示氣泡。

### 2.3 裁剪區域 (Crop Box)
*   若目標剖面視圖未啟用或未設定裁剪區域，系統將自動掃描視圖內的所有可見模型元件，計算其最佳包絡框 (Bounding Box)，並將其設定為該視圖的基準裁剪邊界後，再進行基準線調整。

## 3. 作業流程 (批次處理)
1. 使用者在專案中選取多個「剖面視圖」或「剖面標記」。
2. 啟動功能後，系統利用 Revit API (`SetCurveInView` 並指定 `DatumExtentType.ViewSpecific`) 逐一進入視圖，套用上述 2D 範圍邏輯。
3. 全程不影響 3D 模型的絕對基準位置，亦不干涉其他平立剖面圖的顯示狀態。
