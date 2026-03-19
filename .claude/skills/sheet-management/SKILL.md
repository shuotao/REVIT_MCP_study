---
name: sheet-management
description: "圖紙與視圖埠管理：批次建立圖紙、自動修正圖號衝突、語義化重新排序、依網格裁剪建立從屬視圖。觸發條件：使用者提到圖紙、圖號、titleblock、viewport、renumber、重新編號、從屬視圖、dependent view、分區出圖、網格裁剪。工具：get_all_sheets、get_titleblocks、create_sheets、auto_renumber_sheets、get_viewport_map、calculate_grid_bounds、create_dependent_views。"
---

# 圖紙與視圖埠管理

## Available Tools

| 工具 | 用途 |
|------|------|
| `get_all_sheets` | 列出所有圖紙的編號與名稱 |
| `get_titleblocks` | 列出可用的圖框族群類型 |
| `create_sheets` | 批次建立圖紙（指定圖框） |
| `auto_renumber_sheets` | 修正 `-1` 後綴衝突 + 語義化排序 |
| `get_viewport_map` | 取得視圖與圖紙的對應關係 |
| `calculate_grid_bounds` | 依網格交會計算裁剪範圍 |
| `create_dependent_views` | 建立從屬視圖並設定裁剪框 |

## Workflow 1：批次建立圖紙

1. `get_titleblocks` → 記下 `titleBlockId`
2. `get_all_sheets` → 確認沒有圖號衝突
3. `create_sheets` 帶入 `titleBlockId` + 圖紙陣列 `[{number, name}]`

## Workflow 2：修正圖紙編號

1. `get_all_sheets` → 找出有 `-1` 後綴的圖紙
2. `auto_renumber_sheets` → 執行：
   - 第 0 階段：回復 `_MCPFIX` 殘留
   - 連鎖位移：來源 → 目標，被位移的 → 下一個可用號碼
   - 語義排序：依 `(一)/(二)/(三)` 或 `(1/3)/(2/3)/(3/3)` 在連續號碼組內排序
   - 兩段式執行：暫時名稱 → 最終名稱（避免衝突）

## Workflow 3：依網格裁剪建立從屬視圖

1. `calculate_grid_bounds` 指定網格名稱（`xGrids`、`yGrids`）+ `offset_mm`
2. `create_dependent_views` 帶入母視圖 ID + 上一步的裁剪範圍
3. 系統自動命名：`{母視圖名稱}-1`、`{母視圖名稱}-2`...

### 網格裁剪邏輯
- 同軸 2 條網格 → 以其座標為範圍 ± 偏移量
- 同軸 1 條網格 → 中心 ± 偏移量（容差模式）
- Z 軸設定極大值（-100m 到 +100m）確保涵蓋所有範圍

## Naming Convention

```
[專業代碼]-[圖紙類型][流水號]
範例：ARB-D0408（建築-詳圖-0408）
```

## Reference

詳見 `domain/sheet-viewport-management.md` 和 `domain/dependent-view-crop-workflow.md`。
