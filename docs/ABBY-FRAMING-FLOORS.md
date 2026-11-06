# 依梁柱封閉區域建立樓板

工具：list_floor_types、create_floors_from_framing_regions。

1. 在目標樓層平面視圖執行 list_floor_types，讓使用者選擇現有樓板類型 ID。不能以清單第一項代替使用者選擇。
2. 指定 levelId、floorTypeId。includeColumns=true 使用梁柱，false 僅使用梁。可用 beamIds 限定來源梁；未指定時取本文件、此樓層、目前視圖階段的梁。來源不受暫時隔離影響。
3. dryRun=true、apply=false 預覽。回傳區域邊界（mm）、柱洞、淨面積（m²）、跳過原因及 previewToken。預設板頂與樓層同高、offsetMm=0，結構板=true，minimumAreaM2=0.1。
4. 核對全部區域，包括不應建板的樓梯間、挑空或管道間。可用 regionKeys 選取要建立的區域，再以相同設定重跑預覽。
5. 使用該預覽的 previewToken，apply=true、dryRun=false 正式建立。逐區獨立交易；單區建立失敗只回滾該區，繼續其他區。致命文件錯誤才回滾整批；回傳成功元素 ID 與逐區失敗原因。
6. 重新執行會辨識既有樓板重疊並跳過；模型或設定變更後舊 previewToken 失效。

## 幾何方法與限制

- 使用 Revit 實際 Solid，與所選樓板板頂至板底高度帶做交集，再投影為平面輪廓。
- 合併梁柱輪廓，取其封閉內部淨空；區內獨立柱保留為板洞。採 NetTopologySuite 2.6.0，固定運算精度 0.001 mm。
- 不用梁中心線、軸向包圍盒代替實體邊界，不進行毫米級以上缺口補線。精度網格內的數值誤差會被合併。
- 包含柱時，取與樓板高度帶相交的結構柱與建築柱；省略柱的模式不扣除柱。
- 第一版僅平面面 Solid；曲面梁柱／圓柱、曲面既有樓板、可變厚板與基礎板不支援。拒絕設計選項成員，不讀連結模型。
- 不自動推斷 CAD 編號、樓板厚度、樓梯間或未建模的洞口；需使用者選區。
- 偵測到已有樓板與區域面積重疊超過 1 mm² 時整區跳過，不覆寫、不裁切既有樓板。
- 面積低於門檻、短邊不符合 Revit 容差或無效輪廓會跳過並回報；幾何擷取失敗時保留保守包圍範圍，排除相鄰整區並繼續其他區；沒有有效包圍範圍時才停止，避免無法定位的錯誤影響其他板區。
- 幾何演算法與 MCP 輸入／傳遞有離線測試；Revit 實際交易回滾及真實模型幾何須部署後驗證。

## 來源

- https://nettopologysuite.github.io/NetTopologySuite/api/NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.html
- https://www.nuget.org/packages/NetTopologySuite/2.6.0

## 通用部分成功模式
不硬編碼任何族群名稱或元件 ID。解析失敗的梁柱與既有樓板回傳 ExcludedItems（元素 ID、名稱、類別、原因、包圍範圍），並將相接或距離其保守包圍範圍 1 mm 內的板區標為 NearUnsupportedGeometry。梁柱包圍範圍仍作阻擋物，不會因忽略成員而合併附近空間。可建區域逐一交易，失敗區標為 RegionNotCommitted，其他成功板保留；TransactionGroup 合併為一次復原。Success=false 且 PartialSuccess=true 代表有樓板成功提交但另有區域建立失敗。無可定位範圍、全域拓撲失效、文件再生失敗等致命狀況不能安全繼續，會停止或整批回滾。預覽依然唯讀；正式執行仍需 apply=true、dryRun=false 與有效 previewToken。

## CAD slab numbering and thickness
Optional `cadSchedule` on `create_floors_from_framing_regions` specifies a visible linked CAD UniqueId, exact text layer, source units, Python executable, `floorValue` and case-sensitive pattern/thickness rules. Exact labels override prefix-plus-digits wildcards. Each valid clear region must contain exactly one distinct label. Missing/conflicting labels, unsupported codes, and labels for another thickness skip with reasons. Run separately for each selected actual floor thickness, refreshing previews between batches.

The new floor's Text-group (`Definition.GetGroupTypeId() == GroupTypeId.Text`) string parameters `編號` and `Floor` are written and read back within its creation transaction. Missing, ambiguous or read-only fields roll back that floor. CAD labels/settings participate in the preview token. Existing no-CAD calls retain their behavior. Nested text/leader interpretation and unlabeled slab inference are not supported.
