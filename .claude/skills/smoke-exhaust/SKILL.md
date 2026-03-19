---
name: smoke-exhaust
description: "排煙窗法規檢討：無窗居室判定、無開口樓層判定、排煙有效面積計算、剖面標註、Excel 報告匯出。觸發條件：使用者提到排煙、排煙窗、無窗居室、無開口樓層、建技規§101、消防§188、天花板下80cm、有效開口、煙層。工具：check_smoke_exhaust_windows、check_floor_effective_openings、create_section_view、create_detail_lines、create_filled_region、create_text_note、export_smoke_review_excel。"
---

# 排煙窗法規檢討

## Workflow

### 步驟 1：無開口樓層判定
`check_floor_effective_openings` → 檢查外牆有效開口面積是否 ≥ 樓地板面積 1/30（消防§4 + §28③）

### 步驟 2：排煙窗檢討（主要檢查）
`check_smoke_exhaust_windows` → 逐間檢查天花板下 80cm 內可開啟窗面積是否 ≥ 區劃面積 2%
- 自動上色：綠色 = 全開合格、黃色 = 折減合格、紅色 = 不合格
- 自動建立四方位標註視圖

### 步驟 3：剖面檢視（選用）
`create_section_view` → 建立面向指定牆面的剖面視圖，檢視窗戶與天花板高度關係

### 步驟 4：標註（選用）
- `create_detail_lines` → 繪製天花板線、有效帶範圍線
- `create_filled_region` → 填充排煙有效帶色塊
- `create_text_note` → 加入文字標註

### 步驟 5：匯出報告
`export_smoke_review_excel` → 匯出 .xlsx 報告（樓層總覽、房間明細、窗戶明細、改善建議）

## Regulatory Basis

| 法規 | 內容 |
|------|------|
| 建技規§101① | 排煙口面積 ≥ 防煙區劃面積 2%，設於天花板下 80cm 內 |
| 消防§188③⑦ | 排煙口水平距離 ≤ 30m，每 500m² 以防煙壁區劃 |
| 消防§4 + §28③ | 有效開口 < 1/30 → 無開口樓層，≥ 1000m² 須設排煙設備 |
| 建技規§1 第35款 | > 50m² 居室，天花板下 80cm 通風面積 < 2% → 無窗居室 |

## Reference

詳見 `domain/smoke-exhaust-review.md`。
