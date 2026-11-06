---
name: mep-spacing-clearance-check
description: "管線間預留淨距檢討 SOP：檢查管、風管、電纜架、導線管彼此（含保溫、含連結模型）以及與梁柱等結構的外緣淨距是否達到使用者指定的預留值，回報不足與碰撞位置。MEP spacing / clearance between pipes, ducts, cable trays. 觸發：管線間距、管線淨距、管線預留、維修空間、保溫間距、check_mep_spacing。"
metadata:
  version: "1.0"
  updated: "2026-10-21"
  created: "2026-10-21"
  references:
    - "Christer Ericson, Real-Time Collision Detection, §5.1.9 線段最近點"
    - "分隔軸定理（Separating Axis Theorem）"
    - "Revit API: ElementIntersectsSolidFilter、MEPCurve.ConnectorManager"
  related:
    - mep-csa-clash-detection.md
    - mep-opening-candidate-scan.md
    - beam-clear-height-check.md
    - mep-space-demand-matrix.md
  referenced_by: []
  tags: [管線間距, 管線淨距, 預留, 維修空間, 保溫, MEP spacing, clearance, SAT, check_mep_spacing]
---

# 管線間預留淨距檢討（MEP Spacing Clearance Check）

## 1. 目的

`detect_clashes`（見 `mep-csa-clash-detection.md`）回答「有沒有撞到」；本方法回答「**撞不到，但夠不夠寬**」——管與管、管與梁之間是否留了施工、保溫、吊架、維修所需的淨距。

## 2. 淨距值必須由使用者指定（鐵則）

- `clearanceMm` **必填、沒有預設值**，定義為兩構件**外緣到外緣**的最小距離（外緣含保溫）。
- 與 `mep-opening-candidate-scan.md` 的 `clearanceMm` 鐵則一致：各專案預留規範差異大，AI 不得自行代入「業界常用 50 mm」之類的值；使用者沒給就先問。
- 不同系統組合的差異以 `rules` 表達，例如：

```json
{
  "clearanceMm": 50,
  "rules": [
    { "a": "ducts", "b": "cable_trays", "clearanceMm": 150 },
    { "a": "pipes", "b": "cable_trays", "clearanceMm": 100 }
  ],
  "checkAgainst": ["beams"],
  "againstClearanceMm": 50
}
```

## 3. 斷面模型

| 類別 | 斷面 | 尺寸來源 |
|---|---|---|
| 管（Pipe） | 圓 | 外徑參數 + 保溫厚度 |
| 導線管（Conduit） | 圓 | 外徑參數 |
| 圓風管 | 圓 | 直徑 + 保溫厚度 |
| 矩形 / 橢圓風管 | 矩形（橢圓以外接矩形近似） | 端點接頭寬高與方向 + 保溫厚度 |
| 電纜架 | 矩形 | 端點接頭寬高與方向 |

只處理**直管段**；彎頭、三通、閥件等管件本身不參與管對管比較（管件附近的距離會由相鄰直管段反映）。

## 4. 管對管：分隔軸估算

1. 以 2 m 網格做空間索引，只比較外框（外擴「斷面半徑 + 最大要求淨距」）重疊的組合。
2. **同一路由排除**：經由管件、附件或設備在 3 個族群實例以內相連的管段視為同一路由，不互相比較（避免彎頭兩側的管段被誤判為間距不足）。
3. 求兩中心線段的最近點，候選軸取：最近點連線方向、兩管方向外積、矩形管自身的寬向與高向。
4. 在每條軸上把兩構件的「線段 ± 斷面支撐距離」投影成區間，分離量 = 兩區間的間隙；淨距估計 = 所有候選軸中的最大分離量。
5. 淨距 < 要求值 → `INSUFFICIENT_CLEARANCE`；淨距 < 0 → `CLASH`。

**精度**：平行管、交叉管為精確值；斜向錯位的兩支矩形管，真實距離會略大於估計值（保守，寧可多報）。報告中須保留工具回傳的 `accuracyNote`。

## 5. 管對結構（checkAgainst）

以管線斷面**外擴 againstClearanceMm** 後的擠出實體，對指定類別（主模型與連結）做實體相交：

- 外擴實體有交、原斷面實體也有交 → `CLASH`
- 只有外擴實體有交 → `INSUFFICIENT_CLEARANCE`（只知道小於要求值，不給精確淨距）

刻意設計成穿梁的管線也會被列出；若專案已有套管，先以 `domain/sleeve-classification-protocol.md` 判斷是否為合法穿越，再決定是否列為問題。

## 6. 已知限制

- 管件、設備、吊架、支撐不在比較範圍。
- 非直線（彎曲軟管、弧形）管段略過，數量回報於 `skippedNonLinear`。
- 連結模型需已載入；未載入的連結不會被檢查。
- 座標為主模型內部原點（mm）。

## 7. 標準流程

1. `get_model_snapshot` 確認連結已載入、確認要檢討的樓層。
2. 向使用者確認 `clearanceMm`、是否需要 `rules`、是否檢查對結構（`checkAgainst` 與 `againstClearanceMm`）。
3. 執行 `check_mep_spacing`，先回報 `summaryByPair` 與最嚴重的前幾筆。
4. 需要定位時 `createMarkers=true` 或 `highlightInView=true`，搭配 `export_view_image` 的 `focusElementIds` 目視確認。
5. 結束後 `clear_review_markers`（`checkName: "mep_spacing"`）。

## 8. 報告格式

引用本文件：`Per domain/mep-spacing-clearance-check.md, step 4: ...`，至少包含使用的淨距與規則、檢討範圍、各類別組合的問題數、問題清單（兩端元素 ID／來源／系統／尺寸、淨距估計、要求值、位置）與精度說明。
