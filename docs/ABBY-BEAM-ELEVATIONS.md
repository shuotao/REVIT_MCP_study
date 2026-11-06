# CAD 梁號、高程與文字編號

`create_beams_from_dwg` 的 `numericSchedule` 模式可同時讀取大小梁文字圖層，以 `scheduleRules` 對應尺寸。明列梁號優先於數字範圍，再優先於萬用前綴；大小寫敏感。

- `defaultLevelId`：無括號梁號的參考樓層，省略採目前視圖樓層，Z 偏移為零。
- `parenthesizedLevelId` 與 `parenthesizedOffsetUnits`（`mm` 或 `cm`）：需一起指定。有括號數值的梁改用指定樓層與 Z 向偏移。
- `writeTextNumber: true`：建立時將純梁號寫入 `GetGroupTypeId() == GroupTypeId.Text` 的「編號」。不寫內建標記。
- `elevationLayerPairs`：獨立高程文字可指定 `[{"numberLayer":"0S-大梁編號","elevationLayer":"0S-大梁高程"},{"numberLayer":"0S-小梁編號","elevationLayer":"0S-小梁高程"}]`。每筆高程先找同組最近梁號，距離上限 2000 mm、與次近差至少 100 mm。超距或歧義時排除最近兩個受影響梁號；同一梁號配到多筆高程、非數字高程均跳過。沒有高程標註的梁才用預設樓層。指定的高程圖層讀不到任何文字時直接拒絕執行，不默認全部為零。

例如預設樓層為 1FL.S.L、括號樓層為 GL、單位為 cm：`B1` 參考 1FL.S.L、偏移 0；`B1(-15)` 參考 GL、Z 偏移 -150 mm；兩者文字編號均為 B1。梁採頂部對齊，起終點高程偏移均為零。

支援半形或全形成對括號、帶正負號小數。不將 `WB1(車道下)` 等文字註記猜成高程，會列為 `InvalidBeamLabel` 跳過。回傳 `LabelInventory` 可核對文字來源。

先以 `dryRun: true` 預覽，正式寫入需 `apply: true`、`dryRun: false` 及最新 `previewToken`。單一 Transaction；高程與文字參數寫入後回讀驗證，缺少、唯讀、歧義或驗證失敗則整批回滾。重複檢查同時比較平面線段與高程。

正式建置前仍需核對目前 CAD、圖層、尺寸表及括號單位；尺寸缺少可先用 `create_structural_beam_types` 建立後重新預覽。名稱與實際尺寸不符的類型不自動修改。

若明確指定 `uncertainElevationLevelId`，上述高程配對歧義、超距、多筆或無效數值改採該樓層、Z 偏移 0；不放寬梁號、尺寸與位置的辨識條件。回傳 `ElevationFallbacks` 逐筆列出純梁號、原因、參考樓層、偏移、狀態及成功建立的元素 ID。未標高的梁仍採 `defaultLevelId`。本次使用者指定高程不明者採 GL。
