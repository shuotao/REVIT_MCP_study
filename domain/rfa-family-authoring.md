---
name: rfa-family-authoring
description: "用 rfa_* 工具在族群編輯器中建立參數化族群的 SOP：前置條件、建立順序（類型→參數→參考平面→尺寸標籤→形體→對齊鎖定→撓曲測試）、四種形體（擠出/融合/掃掠/參考線掃掠）的座標約定、參數化管件配方，以及實測確認行不通的路線。觸發：族群建模、RFA 建模、參數化族群、族群編輯器、rfa_create_extrusion、參考線掃掠、Rhino 轉族群。"
metadata:
  version: "1.0"
  updated: "2026-11-10"
  created: "2026-11-10"
  references:
    - "Revit API: FamilyItemFactory (NewExtrusion / NewBlend / NewSweep / NewReferencePlane / NewDimension / NewAlignment)"
    - "Revit API: FamilyManager (AddParameter / SetFormula / AssociateElementParameterToFamilyParameter)"
  related:
    - GM_rfa-family-injection.md
    - tool-capability-boundary.md
    - family-inventory-cleanup.md
  referenced_by: []
  tags: [族群, family, rfa, 族群編輯器, 參數化, extrusion, sweep, blend, reference-plane, reference-line]
---

# RFA 族群建模工作流程（rfa_* Family Authoring）

`rfa_*` 系列共 10 支工具，作用對象是 **Revit 目前作用中的族群文件（.rfa）**。用途是讓 AI 在族群編輯器裡建立參考平面、參數、類型與形體，並把形體綁到參數上，做出可撓曲（flex）的參數化族群。

本文件定義「怎麼用、照什麼順序用、哪些路線已確認走不通」。工具的完整參數以 `MCP-Server/src/tools/rfa-family-tools.ts` 為準。

---

## 1. 前置條件（Prerequisites）

| 條件 | 說明 |
|---|---|
| 作用中文件必須是族群檔 | 由使用者在 Revit 手動開啟既有 .rfa，或用族群樣板新建。作用中文件是專案檔時，所有 `rfa_*` 工具都會直接拒絕 |
| 工具不負責開檔、存檔、載入專案 | 沒有開新族群、儲存 .rfa、Load into Project 的工具。這三步由使用者在 Revit 中完成 |
| 材質要先存在於族群檔 | `material` 參數只能填族群檔內已有的材質名稱，工具不會從專案或材質庫帶入 |
| 單位 | 長度 mm、角度 度、面積 m²、體積 m³、是/否 用 true/false、材質用名稱 |
| 實機驗證範圍 | 2026-11 只在 **Revit 2027** 實測通過；R22–R26 可編譯但未實測 |

---

## 2. 工具總覽（Tool Map）

| 工具 | 作用 | 唯讀 |
|---|---|---|
| `rfa_get_document_info` | 讀族群類別、放置型式、類型、參數現值、參考平面、樓層、視圖、既有形體 | ✅ |
| `rfa_set_family_type` | 建立或更新類型，設值／公式，可改名；被操作的類型會成為目前類型 | |
| `rfa_add_parameter` | 新增族群參數（9 種資料型別，可帶公式或初值） | |
| `rfa_create_reference_plane` | 建立具名參考平面（簡式 normal + offsetMm，或進階式三向量） | |
| `rfa_create_dimension` | 在平行參考平面之間標尺寸；兩平面可掛長度參數標籤，三個以上可設 EQ | |
| `rfa_create_extrusion` | 擠出（實體／空心），可多迴圈開孔 | |
| `rfa_create_blend` | 融合：底部一個迴圈、頂部一個迴圈 | |
| `rfa_create_sweep` | 掃掠：沿平面路徑；或沿參考線＋直徑參數做參數化圓管 | |
| `rfa_create_reference_line` | 在參考平面上建立直線或三點圓弧參考線，並約束到其他參考平面 | |
| `rfa_align_to_reference_plane` | 把形體上與參考平面共面的面鎖到該平面 | |

同名的參考平面與參數會**跳過不重建**，所以重跑同一步是安全的。

---

## 3. 標準建立順序（Standard Sequence）

參數化族群的關係鏈是：**參數 → 尺寸標籤 → 參考平面 → 鎖定 → 形體**。任何一環沒接上，形體就不會跟著參數動。

```
1. rfa_get_document_info         先讀現況，確認是族群檔、有沒有類型
2. rfa_set_family_type           樣板常常是 0 個類型 → 先建一個類型
3. rfa_add_parameter             建參數（長度、材質、可見性……）
4. rfa_create_reference_plane    建骨架平面（Left/Right/Front/Back/Top…）
5. rfa_create_dimension          兩平面＋label 綁參數；三平面＋equalize 設 EQ
6. rfa_create_extrusion / blend / sweep   形體畫在「剛好落在平面上」的位置
7. rfa_align_to_reference_plane  把形體的面鎖到平面
8. rfa_set_family_type           換一組值做撓曲測試，再換回來
```

### 步驟要點

- **第 2 步不可省略**：族群目前沒有任何類型時，`rfa_add_parameter` 的 `value` 不會寫入，回傳會帶 note 提醒。
- **公式驅動的參數不能直接給值**：有 `formula` 的參數，`rfa_set_family_type` 的 `values` 會被拒絕；要改值就先在 `formulas` 給空字串清掉公式。
- **第 5 步之前，平面位置要等於參數現值**：例如「寬度」= 600，Left/Right 就要先建在 ±300。尺寸綁參數後，平面會跟著參數移動。
- **第 6 步形體要畫在骨架上**：形體的面必須已經和參考平面共面，第 7 步才找得到那個面。
- **第 8 步撓曲測試**：至少換一組明顯不同的值（例如 600×400 → 800×500），確認形體跟著變，再換回原值。

---

## 4. 座標與平面約定（Coordinate Conventions）

### 4.1 參考平面（簡式）

| normal | 平面方向 | 自動放置的視圖 |
|---|---|---|
| `X` | 垂直，位於 x = offsetMm | 平面圖 |
| `Y` | 垂直，位於 y = offsetMm | 平面圖 |
| `Z` | 水平，位於 z = offsetMm | 前／後立面 |

### 4.2 草圖平面與擠出方向

| plane | 2D 座標 [u, v] | 擠出／融合方向 |
|---|---|---|
| `XY` | u = X, v = Y | +Z |
| `XZ` | u = X, v = Z | **−Y** |
| `YZ` | u = Y, v = Z | +X |
| `referencePlane` | 沿該平面自己的 X/Y 向量，原點為平面原點 | 平面法向量 |

`endOffsetMm` 給負值會往反方向擠出。

### 4.3 「XY/XZ/YZ」與「referencePlane」的差別

用 `plane: "XY"` 等方式建立的形體，工作平面**沒有關聯到任何參考平面**；參考平面移動時形體不會跟著走。要讓形體的草圖平面跟著參數移動，請改用 `referencePlane: "<平面名稱>"`。

### 4.4 輪廓（profiles）

每個迴圈只能給 `points`、`rectangle`、`circle` 其中一種：

- `points`：至少 3 點，自動封閉；任一段短於 0.5 mm 會被拒絕（請先合併重複點）。
- 第一個迴圈是外框，後續落在外框內的迴圈變成開孔（擠出、掃掠適用；融合底部與頂部各只能一個迴圈）。
- 從 Rhino 等外部模型轉入時，**曲線邊要離散成 points，只有完整圓可以直接用 circle**。

---

## 5. 四種形體的用法

### 5.1 擠出（Extrusion）

最常用。`associate` 可綁：`extrusionEnd`、`extrusionStart`（長度參數）、`material`（材質參數）、`visible`（是/否參數）。

> 例：板厚由參數「厚度」控制 → `associate: {"extrusionEnd": "厚度"}`。

### 5.2 融合（Blend）

`baseProfile` → `topProfile`，相距 `heightMm`。`associate` 可綁 `blendTop`、`blendBase`、`material`、`visible`。

- Revit 2027 的 `NewBlend` 實際上把**第一個**輪廓引數當成頂部，與 API 文件寫法相反。工具會比對回傳的頂部／底部迴圈組成（曲線種類＋總長），順序反了就刪掉重建，並在 warnings 註明。
- 回傳的 TopProfile / BottomProfile 曲線組成就是自檢結果：方底圓頂應看到 bottom「4 Line」、top「2 Arc」。

### 5.3 掃掠（Sweep，固定路徑）

用於扶手、線腳、框料。路徑必須**在同一平面**上：

- `pathPoints`（多段線，可 `closed`）或 `pathSegments`（line / 三點 arc）。
- 斷面以 [u, v] mm 繪製，Revit 會把它放在路徑起點、垂直於路徑。
- 3D 空間曲線請拆成多段平面路徑，各自建一個掃掠。
- 斷面太大會在路徑轉角自我交錯而失敗，先縮小斷面或放大轉角半徑。

固定路徑的掃掠**不會跟著參數移動**。要參數化請用 5.4。

### 5.4 參數化圓管（Reference Line + Sweep）

這是參數化管件、桿件、曬衣架、欄杆的標準配方：**位置由參考線控制，管徑由斷面直徑標籤控制**。

```
1. rfa_add_parameter          建「管徑」與各位置參數（長度）
2. rfa_create_reference_plane 建好約束用的參考平面
3. rfa_create_reference_line  hostPlane = 參考線所在平面
                              constraints:
                                - 對齊鎖定：省略 distanceParameter（該部位必須已在平面上）
                                - 以參數標距：給 distanceParameter（目前距離必須等於參數值）
                              圓弧可加 radiusParameter
4. rfa_create_sweep           pathReferenceLineIds = 第 3 步回傳的 id（首尾相接、同一平面）
                              diameterParameter = "管徑"，profiles 可省略
5. 檢查回傳的 flexCheck       工具會暫時把管徑 +10 mm，確認斷面圓弧半徑等於參數一半再復原
```

約束 target 說明：

| target | 適用 | 說明 |
|---|---|---|
| `curve` | 直線 | 線本身平行於某參考平面時，鎖到該平面或與它標距 |
| `start` / `end` | 直線、圓弧 | 端點 |
| `center` | 圓弧 | 圓心 |

- 參考線會隨 `hostPlane` 沿其法向移動；平面內的兩個方向要靠 constraints 控制。
- 參考線若高於平面圖的剖切面，工具會自動提高該平面圖的視圖範圍，並在 warnings 註明。
- Revit 拒絕約束（多半是過度約束）時，整個交易會被復原，不會留下半成品。

---

## 6. 對齊與尺寸（Constraints）

### 6.1 `rfa_create_dimension`

- 參考平面必須互相平行；順序不重要，工具會沿法向排序。
- `label`：只能用在**剛好兩個**平面，且參數必須是長度型別。
- `equalize`：需要**三個以上**平面。
- 視圖自動挑選：垂直平面用平面圖，水平平面用立面；也可用 `viewName` 指定。

常見骨架：Left / 中心 / Right 三平面設 EQ，Left / Right 再標一道「寬度」。這樣改寬度時形體會對稱縮放。

### 6.2 `rfa_align_to_reference_plane`

- 對每個參考平面，找出形體上**已經與它共面**的平面面，建立鎖定對齊。
- 面沒有落在平面上就找不到，不會自動移動形體。

---

## 7. 確認行不通的路線（Dead Ends）

以下是 2026-11 實測確認走不通的作法，**不要再嘗試**：

| 作法 | 結果 | 改用 |
|---|---|---|
| 用 `SketchEditScope` 編輯族群內的草圖 | 拋出「document is not a project document」，此 API 只能在專案檔使用 | 參考線＋掃掠（5.4） |
| 在草圖模式外，對草圖曲線（例如圓心）建立對齊 | 「Failed to create the alignment」 | 參考線＋掃掠（5.4） |
| 在草圖模式外，對草圖圓弧的參考標尺寸並掛參數 | Revit 接受，但約束錯誤：圓跳到平面上、管徑變成兩倍，還改寫了其他參數值 | 位置走參考線，尺寸只用斷面直徑標籤 |

結論：族群文件沒有可用的草圖編輯 API。**唯一可靠的草圖約束是斷面圓弧上的直徑標籤**；位置類約束一律走參考平面或參考線。

---

## 8. 常見錯誤訊息對照（Troubleshooting）

| 訊息重點 | 原因 | 處理 |
|---|---|---|
| 作用中的文件是專案檔 | 目前視窗不是 .rfa | 請使用者切換到族群編輯器視窗 |
| 族群目前沒有任何類型，未寫入 value | 樣板沒有類型 | 先 `rfa_set_family_type` 建類型，再補值 |
| 找不到材質 | 材質不在族群檔中 | 請使用者先在族群檔建立或傳遞材質；或省略 material |
| 綁定失敗（兩邊型別需相同） | 例如把長度屬性綁到數字參數 | 參數型別改成相符的型別 |
| 建立擠出失敗 | 輪廓不封閉、自我交錯，或樣板不支援（概念量體／自適應族群） | 檢查輪廓；換一般族群樣板 |
| 掃掠路徑必須位於同一個平面上 | 3D 路徑 | 拆成多個平面掃掠 |
| 距離是 X mm，但參數是 Y mm | 約束前幾何位置與參數現值不一致 | 先把參數值設成目前距離，或把幾何畫到正確位置 |
| 交易被復原，可能是過度約束 | 同一方向被約束兩次 | 拿掉重複的約束 |

---

## 9. 回報要求（Reporting）

依 `CLAUDE.md` Tool Call Data Honesty：回報給使用者的元素 ID、參數值、平面名稱、類型名稱，都必須來自本輪工具回傳。完成後至少回報：

1. 建立了哪些參數、參考平面、形體（附 id）。
2. 哪些形體綁了哪些參數。
3. 撓曲測試用了哪兩組值、結果是否跟著變。
4. 所有 warnings（不要省略）。
