---
name: dwg-column-import
description: "從 CAD/DWG 圖層自動批次建立 Revit 結構柱/建築柱的方法 SOP：圖層掃描、矩形柱識別、預覽確認、批次建模。涵蓋 LAYER 指定、import vs link、柱名稱對應（textLayerName）、DXF 單位自動偵測、基準點座標、族群前置等工程確認點。觸發於 dwg 建柱、cad 匯入建模、圖層建柱、批次建柱。"
metadata:
  version: "1.1"
  updated: "2026-06-16"
  references: []
  related:
    - ifc-structural-sync.md
    - tool-capability-boundary.md
  referenced_by:
    - dwg-column-import
  tags: [DWG, CAD, ImportInstance, 結構柱, 建築柱, 批次建模, create_level, Revit]
---

# DWG 圖層批次建柱 SOP

把 CAD/DWG 圖面中的矩形柱輪廓，批次轉成 Revit 的結構柱或建築柱。來源 fork：`s9101800111-byte`（作者 lt0106）。

對應工具：`get_dwg_column_layers` → `preview_dwg_columns` → `create_columns_from_dwg`，外加 `create_level`。
對應實作：`MCP/Core/DwgColumnExecutor.cs`、`MCP/Core/Commands/CommandExecutor.Level.cs`。

---

## 1. 前置條件（缺一不可）

1. **Revit 開在平面視圖**：三個工具都要求 `doc.ActiveView` 為 `ViewPlan`，否則丟「請在平面視圖中執行」。
2. **CAD 已匯入或連結到該視圖**：用 `get_dwg_column_layers` 確認視圖內至少有一個 `ImportInstance`。
3. **專案已載入矩形柱族**：`create_columns_from_dwg` 會在現有族群中挑選，挑不到會丟「找不到…族群，請先在專案中載入矩形柱族群」。族群評分優先序：含「混凝土／Concrete／RC」+「矩形／Rect」最高。
4. **柱輪廓畫在獨立圖層**：見下方「LAYER 指定」。

---

## 2. 工作流（務必照順序、preview 先行）

| 步驟 | 工具 | 作用 | 是否改模型 |
|---|---|---|---|
| 0 | （對話詢問） | 詢問是否需要對應柱名稱（C1/C2 等），若是則請使用者指定 `familyName` 與 `textLayerName` | 否 |
| 1 | `get_dwg_column_layers` | 列出視圖內所有 CAD 圖層，並用關鍵字（柱/column/col/pillar）推薦柱圖層 | 否 |
| 2 | `preview_dwg_columns(layerName)` | 解析該圖層矩形，回傳每根柱的 x/y(mm)、寬、深、旋轉角，與尺寸分組統計 | 否 |
| 3 | `create_columns_from_dwg(layerName, columnType, familyName?, textLayerName?)` | 批次建柱，回傳 created/failed/typesUsed/unmatchedLabels/errors | **是（不可自動復原）** |

`columnType`：`structural`（預設，`OST_StructuralColumns`）或 `architectural`（`OST_Columns`）。

`familyName`（選填）：指定族群名稱（如 `2_RC柱-矩形`）。指定後建柱時會從該族群的現有類型中依名稱比對，保留 `C1_100 x 60`、`C3a_100 x 60` 等原有柱型別名稱。**不指定則自動選族群，類型名稱為純尺寸格式（如 `100x60`），不會對應柱號。**

`textLayerName`（選填）：CAD 圖面上標注柱名稱（C1、C2 等）文字所在的圖層名稱（如 `S-COLS-LABL`）。指定後會：
1. 呼叫 `ezdxf_worker.py`（需 Python + ezdxf），讀取該圖層的 TEXT/MTEXT
2. **自動偵測 DXF 單位**（試算 mm/cm/m/inch 四種比例，選讓標注最靠近柱輪廓的那個）
3. 依空間距離（容許 2000mm）將最近標注對應到每根柱
4. 以標注代號（如 `C3a` 從 `C3a(100×60)` 萃取）在 `familyName` 族群中尋找前綴符合的類型
5. **找不到對應類型時跳過該柱（不自動建立新類型）**，並回傳 `unmatchedLabels` 告知哪些標注沒有對應
- DXF 格式可直接讀取；DWG 格式需安裝 ODA File Converter，未安裝時回傳 `labelReadStatus=no_oda`，柱仍建立但不對應名稱
- **必須使用「連結 CAD（Link CAD）」**，「匯入（Import CAD）」無法讀取原始檔案路徑

**比對邏輯**（同 BIMAssistant `find_or_create_type`，三段優先序）：
1. 完整名稱完全符合（如 `C3a(100×60)` ＝ 類型名）
2. 代號前綴符合（如 `C3a` 前綴 → 找 `C3a_100 x 60`、`C3a-...`）
3. 代號前綴 + 尺寸雙重符合（防代號相同尺寸不同時誤判）

**鐵則**：執行步驟 3 前一定先看步驟 2 的數量與尺寸是否合理，避免在錯圖層或錯比例下建出大量錯柱。

---

## 3. 關鍵工程確認點（合併與每次使用都要核對）

### 3.1 以 LAYER 指定，不是顏色／線型
識別靠 `GraphicsStyleCategory.Name`（＝DWG 圖層名）。`get_dwg_column_layers` 回傳的就是圖層清單，`create/preview` 用 `layerName` 過濾。
- **做法**：在 CAD 端把柱輪廓放在**獨立、命名清楚的圖層**（名稱含「柱/COL」可被自動推薦）。
- **不支援**：用顏色或線型來篩選柱。同圖層混雜其他圖元會被一起當候選矩形解析。

### 3.2 import 與 link 都讀幾何，但讀文字標注只能用 link
工具用 `OfClass(typeof(ImportInstance))` 收集，**匯入(import)與連結(link)在 Revit 都是 `ImportInstance`，幾何識別兩者皆支援**。
- **讀柱輪廓（幾何）**：import 與 link 都可以，`get_dwg_column_layers`、`preview_dwg_columns`、`create_columns_from_dwg` 三工具皆適用。
- **讀文字標注（`textLayerName`）**：**只能用「連結 CAD（Link CAD）」**。「匯入 CAD（Import CAD）」在 Revit API 中無法取得原始檔案路徑（`GetExternalFileReference()` 會丟例外），工具會回傳錯誤並提示改用 Link CAD。
- **link 注意**：連結檔若未載入、路徑遺失或在該視圖不可見，幾何讀不到 → 圖層清單或矩形數會是空。
- **建議**：需要柱名稱對應時用 link；只需建柱位置與尺寸時 import 也可。

### 3.3 基準點／座標（最重要的驗收項）
柱位置 = CAD 幾何經 `GeometryInstance.GetInstanceGeometry()`（已套用 ImportInstance 的 transform）後的**模型內部座標**；Z 取目前視圖樓層 `ViewPlan.GenLevel`，頂部自動抓「高於基準樓層的最近一層 Level」。
- **未處理** shared coordinates／survey point／專案基準點的額外換算——柱會落在「CAD 匯入後在 Revit 模型中的實際位置」。
- **因此**：CAD 匯入/連結時就要**對位正確**（正確的原點、比例、單位）。匯入設定不對，柱位置就整批偏移。
- **驗收**：建柱後抽查幾根柱中心是否落在 CAD 柱輪廓中心；跨 import 與 link 兩情境各驗一次。
- 頂樓層找不到（基準層已是最高層）會丟「找不到高於…的樓層」→ 需先用 `create_level` 補上層樓層。

### 3.4 族群偵測與尺寸規則
- 自動偵測寬/深參數名（多語言別名：b/B/寬度/寬/柱寬/Width/w…；h/H/深度/深/Depth/d…），偵測不到時改用「50–5000mm 範圍內的 Double 參數」由小到大當寬、深。
- 尺寸 **5mm 量化**（`Round(v/5)*5`）；**100–3000mm 範圍外的矩形視為非柱被濾掉**；近正方形旋轉角歸零。
- 缺對應尺寸的族類型時自動 `Duplicate` 新類型，命名 `寬x深`（cm），衝突加尾號。（使用 `textLayerName` 時不自動建立新類型——找不到對應就跳過並回傳 `unmatchedLabels`）

---

## 4. 已知限制
- 斷面須為**矩形**，但**不挑畫法**：可解析封閉 PolyLine（含倒角／多頂點，採「最長邊主方向 + 去旋轉外接矩形」）、四條 Line 接合的迴圈、以及圖塊 block(INSERT)（圖塊一律收集、以點群重心+transform 轉角解析）；圓柱、L 形、異形不支援。
- **實機驗證案例 1（2026-06-09，FL1 一樓）**：CAD 柱為 **13 頂點倒角 polyline**（早期只認 4 點矩形的版本完全抓不到），改通用外接矩形後 `preview` 正確識別 30 根（430×430 × 27、915×305 × 3），`create` **30/30 建立成功、failed 0、對位正確**，自動選用「混凝土柱-矩形」族、FL1→FL2。
- **實機驗證案例 2（2026-06-16，2FL，汀洲路案）**：CAD 為 DXF（`$INSUNITS=0` 無單位實為 cm），柱輪廓在 `S-COLS-CONC`，柱號標注在 `S-COLS-LABL`（格式 `C3a(100×60)`）。族群 `2_RC柱-矩形` 含 C1/C1a/C2/C3/C3a/C4/C5/C5a/C6/C6a/EC1-EC4 等類型。工具**自動試算 DXF 單位為 cm**（1/30.48 比例），標注萃取代號（`C3a`）後前綴比對找到 `C3a_100 x 60`，**12/12 建立成功、failed 0、typesUsed 涵蓋 10 種柱型、unmatchedLabels 空**。
- 同位置 50mm 內視為重複柱，自動去重（可能漏掉刻意極近的雙柱）。
- 一次只處理一個圖層。
- `create_columns_from_dwg` 不可自動復原；誤建需手動刪除或 Ctrl+Z。

## 5. 附：create_level
`create_level(elevation, name?)`：以公釐標高建立樓層（自動 /304.8 轉 feet）。標高或名稱重複會在回傳的 `Warning` 提示但仍建立（Revit 自動附加尾號）。常用於建柱前補齊頂部樓層。

## 6. QA／驗收清單
- [ ] 平面視圖 + CAD 已載入，`get_dwg_column_layers` 有回傳圖層
- [ ] `preview_dwg_columns` 數量、尺寸分組、旋轉角合理
- [ ] 專案已載入矩形（RC/混凝土）柱族
- [ ] 建柱後抽查中心對位（import 與 link 各驗）
- [ ] 頂部樓層存在（否則先 `create_level`）
- [ ] created/failed 比例正常，errors 已檢視
