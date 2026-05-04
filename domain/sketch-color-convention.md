# Sketch Color Convention — 彩色手稿建模約定

> 用於 `/sketch-to-revit` Skill 與其底層的 `/plan-sketch-to-dxf` 工具。Python 向量化管線依顏色約定做色相分類，後續座標換算（§6 Y 翻轉、§7 50mm snap）由 sketch-to-revit 階段 5 套用。

## 1. 顏色約定

| 顏色 | 色相範圍 (HSL Hue) | 元素 | 預設類型 | 預設厚度 |
|------|--------------------|------|----------|----------|
| 粉色 / 洋紅 (Pink / Magenta) | 290°–330° | 牆 | RC15 | 150 mm |
| 藍色 / 青色 (Blue / Cyan) | 180°–250° | 牆 | BRICK12 | 120 mm |
| 黑色 (Black) | 任何 hue，亮度 < 20% | 柱 | 預設 RC 柱 | 由 Type 決定 |
| 紅色 (Red, 250°–290°) | 保留 | （未定義，目前忽略） | — | — |
| 綠色 (Green, 90°–150°) | 保留 | （未定義，目前忽略） | — | — |

**色相辨識原則**：以「色相 (hue)」為準，不糾結色彩飽和度。手繪用螢光筆顏色不純常見：
- 粉色偏紅 → 仍歸粉
- 藍色偏綠 → 仍歸藍

## 2. 形狀約定

| 形狀 | 語義 | 處理 |
|------|------|------|
| 黑色實心橢圓/圓 | 柱中心 | 取質心當作柱座標 |
| 帶 X 的方框 | 樓梯間 / 管道間 / 電梯間 | **不建模**，預覽中註記「X-marked shaft, ignored」 |
| 雙線（寬度大、含內部白邊） | 牆（語義同單線） | 取中心線 |
| 短粗黑線（位於牆中段） | 門 | 本版本暫不處理，預覽中註記「possible door, ignored」 |

## 3. 比例尺推斷

### 3.1 預設方法
1. 找出所有「黑點」配對距離
2. 取距離**最短**的兩個黑點 → 假設為「相鄰柱」
3. 該距離 = 10000 mm（使用者預設值）
4. `mmPerPx = 10000 / 該像素距離`

### 3.2 例外處理
- 若使用者於對話中明確給定不同距離（例：「柱距 8m」），改用該值
- 若所有柱兩兩距離差異 < 10%（規則網格），用平均值
- 若柱距變化大（不規則布局），用**中位數**作為基準距離

## 4. 共線合併規則

### 4.1 為何要合併
手繪時一面長牆常被分成多段繪製（中間因為畫筆抬起或牆穿過柱）。建模時應視為**一面連續牆**（除非中間有開口）。

### 4.2 合併條件（需全部滿足）
- 同色（粉粉合併、藍藍合併，**粉藍不合併**）
- 在同一條直線上（夾角 < 5°）
- 端點重疊或間距 < 牆厚 1.5 倍

### 4.3 合併後的端點
取所有原始端點的**最遠兩端**。

## 5. 牆端點 snap 到柱

### 5.1 規則
若牆的某端點 P 距離某柱中心 C 的距離 ≤ 牆厚 × 1.5，則：
- P 的座標被替換為 C
- 牆會「**穿過**」柱（不切齊柱面，因為要不 join）

### 5.2 為何不切齊柱面
- 使用者要求「不要 join」 → 牆與柱獨立
- 若切齊柱面，建完後仍會在柱角處留縫
- 穿過柱中心 → unjoin_element_joins 後視覺上柱與牆共存無縫

## 6. Y 軸翻轉

- 影像座標系：左上角為原點，**Y 軸向下**
- Revit 座標系：**Y 軸向上**
- 換算：`mm_y = -(py - cy_px) * mmPerPx + Cy`

## 7. 50mm 網格 snap

### 7.1 為何要 snap
使用者要求「牆心、柱心距離為 5cm 倍數」。這讓建模後的尺寸標註乾淨（不會出現 4987mm 這種破數字）。

### 7.2 snap 順序
1. **錨點先 round**：`Cx = round(Cx / 50) * 50`、`Cy = round(Cy / 50) * 50`
2. **柱心 round**：每根柱獨立 `round(.../50)*50`
3. **牆端點共線分組**：
   - 水平牆（|Δy| < 牆厚）：所有端點 Y 取群組平均 → round 50 → 共用
   - 垂直牆（|Δx| < 牆厚）：所有端點 X 取群組平均 → round 50 → 共用
   - 群組容差 = 牆厚 × 1.5
4. **柱端點吸附**：牆端點若靠柱中心 ≤ 牆厚 1.5 倍，直接吸附到柱（柱已 rounded）
5. **自由端點 round**：剩餘端點各自 round 50

### 7.3 斜牆（非正交）
- 不做共線校正
- 兩端點各自 round 50
- 註：斜牆的「牆心 50mm 整」較難保證；可接受的是端點 50mm 整

### 7.4 驗證
所有輸出座標 `% 50 == 0`，否則拒絕進入建立階段。

## 8. 不要 Join 的處理

### 8.1 Revit 端預設
`Wall.Create()` 第四參數 hardcoded `false`，**不會自動 join**。但仍可能因為共端點被 Revit auto-join 邏輯介入。

### 8.2 保險措施
建立完所有牆與柱後，呼叫一次：
```
unjoin_element_joins(sourceCategory="Walls")
```
涵蓋預設 8 類 target，session 結束前可用 `rejoin_wall_joins` 還原。

### 8.3 何時跑 unjoin（重要時序）
若 `create_wall` 之後立刻 unjoin，當時牆都還在 Level GL（見 §11 bug），**不會與 2FL 的柱重疊**，因此 unjoin 結果通常為 0 對。
正確時序：
1. `create_wall` 全部建好
2. `change_element_type` 修正類型
3. `modify_element_parameter("Base Offset", "<2FL高度mm>")` 抬升牆到正確 Level
4. **再跑** `unjoin_element_joins` — 此時牆與柱才在 3D 空間真正重疊，join 風險才存在

## 11. C# `CreateWall` 已知 bug（歷史紀錄，已修正）

> **狀態：已修正。** `CreateWall` 現在正確套用 `wallType` / `bottomLevel` / `topLevel`，並把 Base Offset / Top Offset 預設為 0。本節保留作回溯參考。
> 修正後若要驗證：建牆後 Base Constraint 應該等於 active view level，**不再是 GL**；如果仍是 GL，代表 Add-in DLL 還沒重新部署，請跑 `/deploy-addon` 並重啟 Revit。

[CommandExecutor.cs `CreateWall`](../../MCP/Core/CommandExecutor.cs) 過去**完全忽略**以下 MCP 參數：

| 參數 | 預期行為 | 修正前實際行為 | 修正後 |
|------|---------|---------|------|
| `wallType` | 用指定的牆型建牆 | 用 Revit 當下 active wall type | 接受名稱字串或 ElementId 數字；找不到才 fallback active type 並 log warning |
| `bottomLevel` | 牆 Base Constraint 設為指定 Level | `FilteredElementCollector` 第一個 Level（通常 GL） | 接受名稱或 ElementId；找不到才 fallback 第一個 Level 並 log warning |
| `topLevel` | Top Constraint = Up to level | （未實作，永遠 Unconnected） | 接受名稱或 ElementId；指定後 `WALL_HEIGHT_TYPE` set 為 topLevel.Id |
| Base/Top Offset | 預設 0 | 沿用 Revit 預設 | 建立時主動寫 0 |

### 歷史 workaround（修正前使用）

當時無法在 `create_wall` 直接傳 level，需要事後修：
- ❌ `modify_element_parameter("Base Constraint", "2FL")` 早期會回 `"不支援的參數類型: ElementId"`，後來 commit `e188037` 已支援 ElementId 型別 + level 名稱查找
- ✅ `modify_element_parameter("Base Offset", "3800")` 把牆從 GL 往上抬 3800mm，視覺上等效座落在 2FL（會在 Schedule / Plan 顯示錯誤的 Base Constraint）

### 修正後（目前正確流程）

`create_wall` 直接帶 `wallType`、`bottomLevel`、`topLevel` 參數即可，不需要事後 `modify_element_parameter` 修 Base/Top Constraint。Skill `sketch-to-revit` 階段 6 已同步移除事後修正步驟。

修正後仍然要在 `unjoin_element_joins` **之前**確認牆已落在正確 Level（現在會正確就位），避免 join 風險時序問題。

## 9. AI 視覺辨識的容錯

### 9.1 模糊邊界
手繪線常常起點不清楚，端點 ±5px 的不確定性是正常的。50mm snap 會吸收這些誤差。

### 9.2 圖層分離不完美
粉藍交界處可能出現「混色像素」。判斷規則：
- 看每段線的**主色** (majority hue)
- 若一條線粉藍各半，視為分段：粉色一段、藍色一段

### 9.3 不確定性回報
階段 1 的 JSON 輸出，每個牆/柱可附 `confidence` 欄位（0–1），低於 0.7 的請在預覽中標示讓使用者重點檢查。

## 10. 與既有 Skill 的關係

| Skill | 對比 |
|-------|------|
| `/facade-generation` | 同樣 AI 看圖→JSON→批次建立元素，差別是 facade 處理立面面板，本 Skill 處理平面圖 |
| `/curtain-wall` | curtain-wall 是設計面板矩陣，本 Skill 是辨識手繪 |
| `/element-query` | element-query 是查詢已建模元素，本 Skill 是建立新元素 |
| `/unjoin-geometry` | 本 Skill 階段 6 直接呼叫 `unjoin_element_joins`，與該 Skill 共用工具 |
