---
name: detail-component-sync
description: "2D 詳圖元件同步：將詳圖圖頭與詳圖項目的圖紙號碼、詳圖編號、圖說名稱、詳圖名稱自動同步。觸發條件：使用者提到詳圖同步、圖頭、detail header、AE-numbering、圖紙編號同步、詳圖元件、detail component、AE-圖號詳圖編號標頭-3.5mm、AE-矩形框詳圖元件。工具：get_detail_components、sync_detail_component_numbers、create_detail_component_type、create_detail_component_types_from_sheet_viewports、sync_detail_component_sheet_numbers_by_type_parameters、create_detail_component_types_from_metadata、list_family_symbols。"
---

# 2D 詳圖元件同步

## 核心原則

詳圖元件同步的目標，是讓詳圖項目類型的 `詳圖圖號`、`圖說名稱`、`詳圖編號`、`詳圖名稱` 與可信資料來源一致。可信來源可能是 Revit 圖紙視埠、既有類型參數、PDF/OCR 後設資料，或人工校閱表格。

執行前要先判斷使用者要的是「建立/更新類型」、「修正既有類型參數」、「同步例證所在圖紙」，還是「從 PDF/OCR 建立類型」。大量批次或 OCR 資料一律先 dry run，再依結果執行。

## 可用工具

| 工具 | 用途 |
|------|------|
| `get_detail_components` | 查詢詳圖元件例證與其參數 |
| `sync_detail_component_numbers` | 依所在圖紙同步詳圖元件的圖紙號碼與圖說名稱 |
| `create_detail_component_type` | 建立單一詳圖項目類型並寫入類型參數 |
| `create_detail_component_types_from_sheet_viewports` | 從指定圖紙的視埠資料建立、更新或重新命名詳圖項目類型 |
| `sync_detail_component_sheet_numbers_by_type_parameters` | 依既有類型參數 `圖說名稱` + `詳圖名稱` 反查並修正 `詳圖圖號` |
| `create_detail_component_types_from_metadata` | 從 PDF、OCR、試算表或人工校閱後設資料建立/更新詳圖項目類型 |
| `list_family_symbols` | 列出族群類型，確認精準族群名稱 |

## 模式 A：由圖紙視埠驅動

工具：`create_detail_component_types_from_sheet_viewports`

適用情境：

- 使用者指定一張或多張圖紙號碼，並希望建立、更新或重新命名詳圖項目類型。
- 資料來源以 Revit 的 ViewSheet 與 Viewport 資料為準。
- 需求需要填入 `詳圖圖號`、`圖說名稱`、`詳圖編號`、`詳圖名稱`。
- 使用者希望把過去依 `視圖名稱` 建立的舊類型，更新為優先使用 `圖紙上的標題` 的新命名規則。

參數來源：

- `詳圖圖號` = 圖紙號碼
- `圖說名稱` = 圖紙名稱
- `詳圖編號` = 視埠詳圖編號
- `詳圖名稱` = `圖紙上的標題`；若為空值則改用 `視圖名稱`

## 模式 B：由既有類型參數反查

工具：`sync_detail_component_sheet_numbers_by_type_parameters`

適用情境：

- 使用者要求偵測既有詳圖項目類型參數 `圖說名稱` + `詳圖名稱`。
- 使用者只想修正 `詳圖圖號`。
- 既有類型不應被重建，也不應大範圍重新命名。

規則：

- 大量批次處理時，一律先執行 `dryRun=true`。
- 將 `not_matched` 與 `ambiguous` 視為待人工檢查項目，不視為失敗。
- 使用者指定 `AE-矩形框詳圖元件` 時，不可悄悄改用 `AE-矩形框詳圖元件標籤`；必須優先精準符合族群名稱。
- 若使用者要求依圖紙號碼建立/更新，優先使用模式 A；若使用者要求從既有類型參數反推圖紙號碼，優先使用模式 B。

## 模式 C：`sync_detail_component_numbers` 安全檢查

工具：`sync_detail_component_numbers`

保留兩種安全比對方法：

- 方法 1：類型名稱以所在圖紙號碼開頭。
- 方法 2：所在圖紙號碼以從類型名稱解析出的圖紙號碼前綴開頭。

只有其中一種方法符合時，才更新 `詳圖圖號` 與 `圖說名稱`。若兩種方法都不符合，略過該例證，以保留共用或標準詳圖項目不被誤改。

## 模式 D：由 PDF 或外部後設資料驅動

工具：`create_detail_component_types_from_metadata`

適用情境：

- 來源是 PDF、OCR 結果、試算表，或已人工校閱的後設資料表。
- Revit 內尚未有可對應的 ViewSheet 紀錄，或使用者希望不依賴圖紙資料，直接建立詳圖項目類型。
- 每筆資料已具備 `詳圖圖號`、`圖說名稱`、`詳圖編號`、`詳圖名稱`。

規則：

- 類型名稱 = `詳圖圖號-圖說名稱-詳圖名稱`。
- 寫入類型參數 `詳圖圖號`、`圖說名稱`、`詳圖編號`、`詳圖名稱`。
- 後設資料若來自 OCR，一律先執行 dry run。

## PDF OCR v2/v4：詳圖編號與詳圖名稱配對

PDF 或外部 metadata 建立類型時，優先採用較穩定的視覺規則來降低 OCR 誤判。

- 先偵測圖面中最大字級的數字，作為候選 `詳圖編號`。
- 在候選數字附近尋找同樣為大字級、最長的繁體中文文字，作為候選 `詳圖名稱`。
- 忽略小字註記、材料說明、標註文字與尺寸文字，避免誤當成詳圖名稱。
- 同一基準線附近的英數前綴需合併進標題，例如 `3F,5F`、`C3,C9`。
- 若相同 `詳圖名稱` 對應多個 `詳圖編號`，應合併為同一個類型，詳圖編號以範圍或列表表示，例如 `1-5` 或 `1,3,7`。
- OCR 結果必須先輸出 preview 或 dry run，讓使用者檢查高風險項目後再寫入 Revit。
- 使用者已手動修正的類型應視為高可信參考，不應被 OCR 批次覆寫。

## PDF OCR v5：紅框優先、圓圈備援

使用者提供新版 PDF 並希望從圖面建立 `AE-圖號詳圖編號標頭-3.5mm` 類型時，優先使用 V5 preview 流程：

工具腳本：`tmp/pdf_ocr_300/build_v5_preview.py`

V5 判斷優先序：

- 若 PDF 頁面有紅色 Square 註解框，視為使用者人工指定的 `詳圖名稱` 範圍；直接讀取紅框座標與框內 OCR 文字。
- 紅框模式下，`詳圖編號` 以紅框同一基準線右側附近的圓圈數字為優先；若 OCR 漏讀，僅在 preview 階段使用版面順序補判，並標記 `sequence_fallback`。
- 若頁面沒有紅框，改用圓圈圖頭模式：先偵測圖頭底線右側圓圈，再讀圈內數字，最後往左抓同一基準線的大字繁體中文標題。
- 圓圈模式必須過濾圖框座標、尺寸、施工說明、表格文字與材料註記；標題需包含常見詳圖關鍵字，例如 `詳圖`、`立面圖`、`剖面圖`、`平面圖`、`操作圖`、`標示`。
- V5 只產生 preview JSON、Markdown、CSV 與定位疊圖；未經使用者確認前不可寫入 Revit。
- 任何 `sequence_fallback`、紅框內空值、缺少詳圖關鍵字、或 OCR 修正過的文字，都應列入人工複核清單。

V5 輸出檔名慣例：

- `detail_metadata_v5_preview.json`
- `detail_metadata_v5_review_report.md`
- `detail_metadata_v5_all_types.csv`
- `detail_metadata_v5_review_only.csv`
- `detail_metadata_v5_pageXX_overlay.png`

## PDF OCR v5 inclusive：V5 主判斷、V4 補漏

若使用者明確表示「可以不用那麼保守」、「先建出來，後續人工查核」，不要只採用純 V5 高信心結果。此時應使用 V5 inclusive 流程：以 V5 作為主要來源，再用 V4/OCR 補回 V5 漏掉的候選，並把補漏與低信心項目列入人工複核。

工具腳本：

- `tmp/pdf_ocr_300/build_v5_inclusive_preview.mjs`
- `tmp/pdf_ocr_300/apply_v5_preview_to_revit.mjs`

V5 inclusive 規則：

- 純 V5 與 V4 數量不必相同。V5 較精準但可能漏項；V4 較寬鬆但錯字與誤抓較多。
- V5 項目優先；若同一圖紙與詳圖編號已被 V5 覆蓋，不再採用 V4。
- V4 補漏項目一律標記為 review，作為人工查核清單。
- 送入 Revit 前，必須依 `詳圖圖號 + 正規化後詳圖名稱` 合併。若同名對應多個詳圖編號，只建立或更新一個類型，`詳圖編號` 寫成 `1-5` 或 `1,3,7`。
- 合併後的類型名稱仍為 `詳圖圖號-圖說名稱-詳圖名稱`，不可把詳圖編號放入類型名稱。
- 若 OCR 辨識結果有疑慮，仍可先建立類型，但必須輸出 review report 與 review-only CSV 供使用者人工修正。

執行經驗：

- 大量寫入 Revit 時，使用批次送入 `create_detail_component_types_from_metadata`，每批約 20 筆，並序列化等待每批回應。
- Node/WebSocket 腳本在 Revit 已完成後可能因連線收尾而讓 Codex 看似仍在運行；套用腳本應在完成或失敗後明確結束程序，並寫出 progress/result JSON。
- 最後確認應讀取 progress JSON 的 `status=completed`、`totalInput` 與 `counts`，不要只依 Codex 畫面是否還在轉圈判斷 Revit 是否完成。

V5 inclusive 輸出檔名慣例：

- `detail_metadata_v5_inclusive_preview.json`
- `detail_metadata_v5_inclusive_review_report.md`
- `detail_metadata_v5_inclusive_all_types.csv`
- `detail_metadata_v5_inclusive_review_only.csv`
- `v5_inclusive_preview_apply_progress.json`
- `v5_inclusive_preview_apply_result.json`

## 常用流程

1. 先確認使用者指定的族群名稱，必要時用 `list_family_symbols` 避免選到標籤族群。
2. 根據資料來源選擇模式 A、B、C 或 D。
3. 大量批次、跨圖紙、OCR 來源一律先 dry run。
4. 檢查 `not_matched`、`ambiguous`、空值與重名項目。
5. 確認後再執行正式建立或同步。

## 參考文件

詳見 `domain/detail-component-sync.md`，包含 v1.0 到 v5 inclusive 的演進歷程、FAQ 與 PDF OCR 經驗整理。
