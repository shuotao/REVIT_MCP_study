---
name: stair-hidden-line
description: "剖面隱藏樓梯可視化：在剖面視圖中，自動為被側板遮擋的樓梯梯級繪製虛線詳圖線。觸發條件：使用者提到樓梯隱藏線、stair hidden line、剖面樓梯、虛線、梯級、stair visualization、組合式樓梯。工具：trace_stair_geometry、create_detail_lines、get_line_styles。"
---

# 剖面隱藏樓梯可視化

在剖面視圖中，為被側板（stringer）遮擋的組合式樓梯梯級自動繪製極密虛線。

## When to Use

- 剖面視圖中，組合式樓梯（含側板）的梯級輪廓被遮擋
- RC 樓梯自動排除（沒有側板遮擋問題）

## Workflow

1. 在 Revit 中開啟目標**剖面視圖**
2. `trace_stair_geometry` → 分析所有 `StairsRun` 元素：
   - 篩選：僅處理「組合式」樓梯（檢查 `FamilyName` 是否含 "assembled" 或 "combined"）
   - 提取 3D 幾何邊緣
   - 計算相對於剖切面的深度
   - 排除前景樓梯（minDepth ≤ 0.05 ft）
   - 僅保留第一排邊緣（depth ≤ minDepth + 2.5 ft）
   - 保留短線段（< 0.65 ft）作為踢腳/輪廓細節
3. `get_line_styles` → 找到「虛線(極密)」的樣式 ID
4. `create_detail_lines` 使用步驟 2 的座標 + 步驟 3 的樣式 ID

## Geometry Logic

```
剖切面
    |
    |  depth=0        depth > 0（後方）
    |  ← 前景          背景 →
    |
    |  跳過            繪製（僅第一排）
```

- **深度計算**：`(viewOrigin - edgeMidpoint) · viewDirection`
- **第一排過濾**：`depth ≤ minDepth + 2.5ft`（約 75cm 容差）
- **輪廓偵測**：水平線段（踏板）、垂直線段（踢面）、短線段 < 20cm（踢腳輪廓）

## Recommended Line Style

`ID: 11911982` —「虛線(極密)」

使用 `get_line_styles` 確認你的專案中的正確 ID。

## Reference

詳見 `domain/stair-hidden-line-workflow.md`。
