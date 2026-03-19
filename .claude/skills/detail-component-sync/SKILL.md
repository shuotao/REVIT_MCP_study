---
name: detail-component-sync
description: "2D 詳圖元件同步：將詳圖圖頭（AE-numbering）的編號與圖紙號碼自動同步。觸發條件：使用者提到詳圖同步、圖頭、detail header、AE-numbering、圖紙編號同步、詳圖元件、detail component。工具：get_detail_components、sync_detail_component_numbers、create_detail_component_type、list_family_symbols。"
---

# 2D 詳圖元件同步（v3.5 安全模式）

## Core Principle

確保詳圖圖頭的類型參數（`detail number`、`sheet name`）與其所在圖紙一致。**v3.5 只更新類型名稱已匹配圖紙號碼的元素**——共用/標準詳圖受到保護。

## Available Tools

| 工具 | 用途 |
|------|------|
| `get_detail_components` | 依族群名稱篩選查詢詳圖元件 |
| `sync_detail_component_numbers` | 批次同步參數（安全模式） |
| `create_detail_component_type` | 為特定圖紙建立新類型 |
| `list_family_symbols` | 瀏覽可用的族群符號 |

## Workflow 1：建立新詳圖圖頭類型

1. 確認圖紙號碼（如 `ARB-D0921`）
2. 確認詳圖名稱（如 `淺溝`）
3. `create_detail_component_type` → 建立類型命名為 `ARB-D0921-{圖紙名稱}-{詳圖名稱}`
4. 設定參數：`detail number` = 圖紙號碼、`sheet name` = 圖紙名稱

## Workflow 2：批次同步參數

1. `get_detail_components` 以 `familyName: "AE-numbering"` 查詢 → 稽核
2. `sync_detail_component_numbers` → 對每個元素：
   - 找出它在哪張圖紙上（透過 viewport 對應）
   - 檢查：類型名稱是否以該圖紙號碼開頭？
   - 是 → 更新 `detail number` 和 `sheet name` 參數
   - 否 → **跳過**（保護跨圖紙共用的標準詳圖）

## Safeguard Logic

```
類型: ARB-D0921-...-淺溝
所在圖紙: ARB-D0921  → 匹配 → 更新參數 ✅
所在圖紙: AR-B-D02X2 → 不匹配 → 跳過 ⏭️（標準詳圖被引用到其他圖紙）
```

## After Sheet Renumbering

v3.5 不會自動重新命名類型。需要手動：
1. 在 Revit 中重新命名類型（修改圖紙號碼前綴）
2. 執行 `sync_detail_component_numbers` 更新參數

## Reference

詳見 `domain/detail-component-sync.md`（含 v1.0→v3.5 演進歷程與 FAQ）。
