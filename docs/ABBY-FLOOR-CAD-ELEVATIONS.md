# CAD 樓板編號與高程

`create_floors_from_framing_regions` 可按厚度及高程分批預覽、建置。

- `levelId` 是目前平面視圖的樓層；`placementLevelId` 是樓板實際樓層，`offsetMm` 相對後者。
- `includeBeamsAcrossLevels=true` 依實際板厚高程取梁柱淨邊界，包含其他參考樓層的梁；不跨越開放缺口，無法解析區域隔離跳過。
- `cadSchedule.elevationLayerName` 指定獨立高程文字圖層。必須明確設定 `elevationUnits`、`defaultLevelId`、`numericLevelId`。
- 數字括號轉為指定單位的偏移，採 numericLevelId；沒有註記採 defaultLevelId／0。
- `(折板)` 僅於使用者同意且 `foldedAsFlat=true` 時建為預設樓層的平板，不產生折線或坡度。
- 高程配對採文字平面距離（預設最大 1800 mm、最近與次近差距至少 100 mm）；歧義、衝突、過遠及未知括號內容會列原因跳過。`skipLabels` 是大小寫敏感的原始文字精確排除。
- 原始板號與解析板號分開回報；文字群組「編號」寫解析板號，「Floor」寫 `floorValue`。
- `newThicknessMm` 與 `newFloorTypeName` 可由單層 `floorTypeId` 模板複製厚度類型。同名厚度不符則拒絕，不修改既有類型。只在有區域正式建置時建立缺少類型。
- 每批先 dryRun，確認 Results、LabelInventory、ExcludedItems，再用該批最新 previewToken 套用。先前批次的樓板會列為重疊跳過。每區獨立交易，驗證樓層、偏移、實體頂底高程、類型及文字欄位。

本次 1F：板號 `0S-樓版編號`，高程 `0S-樓版高程`，CAD 與括號單位均為公分，預設 1FL.S.L／0，數字括號 GL／數值，Floor=1FL。跳過 ST1(上)、ES1(斜版)、CES。ST1=150 mm；S*、CS1=200 mm；ES1～ES4、SS1=250 mm；ES5、ES6=300 mm（展開數字範圍後送 rules）。

## 靠近梁邊的板號

`cadSchedule.labelBoundaryToleranceMm` 預設 0，可明確設定最多 300 mm 的板號定位點補配距離。先判斷包含定位點的區域，再對未落入任何區域的文字找最近板區；最近與次近距離差須達 `labelBoundaryGapMm`（預設 100 mm）。柱洞內、歧義或過遠文字不補配。比較包含已佔用及未選區域，避免搶取隔壁板號。此設定只影響文字歸屬，不改樓板邊界，補配文字與距離回傳於 BoundaryLabelMatches。

## 低梁平面投影

`beamProjectionBelowSlabMm` 預設 0，明確啟用後僅向下延伸梁的實體裁切分析範圍，投影到平面以判斷封閉區域。柱與既有樓板仍使用實際板厚範圍；實際建板樓層、偏移、厚度不变。不補平面缺口，不移動或增高梁。回傳 BeamAnalysisBottomMm、SlabTopMm、SlabBottomMm 及 UsedExtendedBeamBand，設定納入 previewToken。須先檢視區域並用 regionKeys 限定正式建置範圍。

ES1(+25) 圖例：板頂 GL +250 mm、板厚 250 mm、板底 GL 0。周圍梁頂 GL -300 mm，選擇向板底下方延伸 350 mm，讓梁頂以下 50 mm 的實體投影參與分區；實際板高程仍為 GL +250 mm。

### 建板後自動牆板接合
此工具建立每塊樓板後，自動接合與該板相交的牆，並強制由樓板切割牆。僅正式套用時執行，dryRun 不改模型。每組接合採子交易；可恢復失敗回滾該組並記錄在 Results[].WallJoins，整體 WallJoinFailed 統計接合失敗，不混入樓板 Failed。樓板交易或整批回滾時一併移除未提交的接合回報。此功能不監聽 Revit 手動建板事件。

