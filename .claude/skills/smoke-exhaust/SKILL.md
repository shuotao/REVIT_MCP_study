---
name: smoke-exhaust
description: "排煙窗法規檢討：無窗居室判定、無開口樓層判定、排煙有效面積計算、剖面標註、Excel 報告匯出。觸發條件：使用者提到排煙、排煙窗、無窗居室、無開口樓層、建技規§101、消防§188、天花板下80cm、有效開口、煙層。工具：check_smoke_exhaust_windows、check_floor_effective_openings、create_section_view、set_3d_section_box、create_detail_lines、create_filled_region、create_text_note、export_smoke_review_excel。"
metadata:
  references:
    - domain/smoke-exhaust-review.md
    - domain/references/building-code-tw.md
---

# 排煙窗法規檢討

## 工作流程

### 步驟 1：無開口樓層判定
`check_floor_effective_openings` → 檢查外牆有效開口面積是否 ≥ 樓地板面積 1/30（消防§4 + §28③）

### 步驟 2：排煙窗檢討（主要檢查）
`check_smoke_exhaust_windows` → 逐間檢查天花板下 80cm 內可開啟窗面積是否 ≥ 區劃面積 2%
- 自動上色：綠色 = 全開合格、黃色 = 折減合格、紅色 = 不合格
- 自動建立四方位標註視圖

#### 天花板高度來源路由（執行前必做）

`ceilingHeightSource` 必須依模型成熟度選擇，不可因專案裡「有少數 Ceiling 元素」就一律使用實體天花：

1. **使用者明確指定來源時，以使用者選擇為準。**
2. **天花尚未建模、只建一部分、或無法證明目標房間都有正確 Ceiling 覆蓋時**：使用 `room_parameter`。
3. **目標範圍的天花已完整建模，且每個受檢房間都能對應到正確 Ceiling 時**：使用 `ceiling_element`。
4. **無法確認模型成熟度時**：預設 `room_parameter`，並在結果中揭露這項假設；不得用少數實體天花推定整層完成。

`room_parameter` 讀取 Room 的 Upper Limit + Limit Offset；`ceiling_element` 讀取實體 Ceiling 高度。一次檢討與其後的 `export_smoke_review_excel` 必須使用相同來源，避免畫面與報告的有效帶不一致。

選用 `room_parameter` 不等於固定假設 3000 mm。第一次檢討必須核對工具逐房回傳的 `CeilingHeight`；若與使用者宣告的標準房高不符，先讀取代表 Room 的 Upper Limit、Limit Offset、Base Offset 與 Unbounded Height，回報差異並停止正式判定。不得用口頭假設覆蓋模型實值。使用者確認要統一房高後，才可另行使用房高工具修改模型，再重新檢討。

本案目前天花尚未建模，因此應傳：

```json
{
  "ceilingHeightSource": "room_parameter",
  "projectedOpeningRatio": 1.0,
  "casementOpeningRatio": 1.0,
  "slidingOpeningRatio": 0.5,
  "unknownWindowAssumption": "projected"
}
```

只有在工具讀回 Room 高度確為 3000 mm 時，有效帶才是 2200–3000 mm。

#### 本案窗型名稱與係數

係數基準為 Revit 族群回傳的整樘窗帶內面積。門窗名稱採 CNS／台灣業界常用分類：

| 正式名稱 | 常見別名／關鍵字 | 本案係數 |
|---|---|---:|
| 推射窗 | 外推、上懸、awning、projected | 1.0 |
| 推開窗 | 平開、側開、casement | 1.0 |
| 橫拉窗 | 推拉、sliding、單拉、雙拉 | 0.5 |
| 固定窗 | fixed、picture、固定 | 0 |
| 無法判定 | 其他名稱 | 暫按推射窗 1.0，但必須保留人工確認標記 |

不要把「橫拉窗」寫成外推窗；橫拉窗沿水平軌道移動，典型雙扇最大淨開口約為整樘的一半。檢討與 Excel 匯出必須傳入同一組係數及 `unknownWindowAssumption`。

### 步驟 3：剖面檢視（選用）
`create_section_view` → 建立面向指定牆面的剖面視圖，檢視窗戶與天花板高度關係

逐房 3D 檢視用 `set_3d_section_box`（elementId = Room，`includeSupportingBeams=true`，`padding_bottom_mm=0`）。上方結構是使用者的選擇，未指定時先問，不要自行決定：
- `upperStructureMode="current_level_only"`（預設，`padding_top_mm=0`）：只看當層，上層結構切掉。
- `upperStructureMode="include_upper_structure_without_slab"`：延伸到上層樓板底，看得到上層樑、看不到樓板。

找不到上層樓板時工具會報錯且不改視圖；照實回報，不要用猜的樓高重試。回傳 `UpperStructureBeamCount=0` 或 `TopBelowTargetTop=true` 時要告知使用者。方法細節見 `domain/smoke-exhaust-review.md`「逐房 3D Section Box 檢視」。

### 步驟 4：標註（選用）
- `create_detail_lines` → 繪製天花板線、有效帶範圍線
- `create_filled_region` → 填充排煙有效帶色塊
- `create_text_note` → 加入文字標註

### 步驟 5：匯出報告
`export_smoke_review_excel` → 匯出 .xlsx 報告（5 個工作表：樓層總覽、房間明細、窗戶明細、改善建議、§101 補充檢討）

報告自動包含 §101 補充法規檢討：
- **排風量提醒**：排風機 ≥ 120 m³/min（靜態提醒，需人工確認）
- **中央管理室偵測**：自動判斷建築高度 > 30m 或地下面積 > 1000m² 時，搜尋模型中是否有中央管理室

## 法規依據

| 法規 | 內容 |
|------|------|
| 建技規§101① | 排煙口面積 ≥ 防煙區劃面積 2%，設於天花板下 80cm 內 |
| 建技規§101 | 排風機排風量 ≥ 120 m³/min，隨排煙口自動啟動 |
| 建技規§101 | 高度 > 30m 或地下 > 1000m²，排煙控制設於中央管理室 |
| 消防§188③⑦ | 排煙口水平距離 ≤ 30m，每 500m² 以防煙壁區劃 |
| 消防§4 + §28③ | 有效開口 < 1/30 → 無開口樓層，≥ 1000m² 須設排煙設備 |
| 建技規§1 第35款 | > 50m² 居室，天花板下 80cm 通風面積 < 2% → 無窗居室 |

## 參考文件

詳見 `domain/smoke-exhaust-review.md`。
