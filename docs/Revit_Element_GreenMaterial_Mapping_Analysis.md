# 建築 AGENT：Revit 綠建材 TASK-003 全情境對映規範與優化分析報告

## 1. 建築 BIM Agent 核心理念 (Architectural BIM Rationale)

在 Revit Building Information Modeling (BIM) 與台灣綠建材 (TABC) 標章對接體系中，綠建材並非單純的文字註記，而是涵蓋 **系統家族 (Walls/Floors/Ceilings)**、**載入家族 (Windows/Doors)** 以及 **非幾何輔助材料 (Adhesives/Sealants)** 的全方位 BIM 物件。

本報告由 **建築 Agent** 針對各種 Revit 元件對接情境進行深度剖析、提出優化建議，並擴充完整的情境對映規範表。

---

## 2. 7 大既有對映情境之建築 Agent 優化分析

| 情境編號與名稱 | 原始剖析內容 | 建築 Agent 優化分析與建議 | 建議之 Revit 參數/構造層落點 |
| :--- | :--- | :--- | :--- |
| **情境 1**<br>牆體構造與塗料 (`OST_Walls`) | 牆 Type 名稱由牆體+塗料組合；牆體設為 Structure，塗料設為 Finish。 | 1. **塗料厚度**：薄塗材 (`GBM0104204`) 設為 $2\,\text{mm}$，厚度設於 `Finish 1 [4]`。<br>2. **命名規範**：採標準 BIM 命名 `W_[內外牆]_[結構厚度]_[綠建材名稱]` (如 `W_INT_RC15_GBM0104204`)，避免欄位過長。<br>3. 塗料履歷雙向寫入 `OST_Materials` 材質庫與牆 Type Identity Data。 | • `Finish 1 [4]` 構造層<br>• `OST_Materials` 16個共享參數<br>• `Type Identity Data` 欄位 |
| **情境 2**<br>地磚與表面填充 (`OST_Floors`) | 預設 Structure + 地磚 Finish 1 [4] + Surface Hatch 表面填滿圖案。 | 1. **自動 Surface Pattern**：Agent 依 TABC 規格 (如 `60x60cm` 地磚) 自動生成 Revit 幾何對應 Pattern (如 600x600 網格或木紋 Hatch)。<br>2. **厚度層級**：面磚設 $15\,\text{mm}$ `Finish 1 [4]`，下方設定 $20\,\text{mm}$ 泥作打底層 `Substrate [2]`。 | • `Finish 1 [4]` 面冊層<br>• `Substrate [2]` 打底層<br>• `Material Appearance` 填滿圖案 |
| **情境 3**<br>系統家族參數存放位置 (`Walls`/`Floors`) | 綠建材名稱連結 Materials and Finishes，新建 Material 參數由 Agent 判斷，證號履歷入 Identity Data。 | 1. **雙階層寫入規範 (Dual-Level Placement)**：<br>   - **Material 層級 (`OST_Materials`)**：存放完整的 16 個共享參數本體。<br>   - **Type 層級 (`Identity Data`)**：存放綠建材摘要字串 (`GreenMaterial_Summary`)，便於點選元件直接於 Property Palette 檢視。 | • `OST_Materials` (16個共享參數)<br>• `Type Identity Data` (摘要字串)<br>• `Materials and Finishes` |
| **情境 4**<br>非幾何材料附屬 (填縫劑/接著劑) | 於 Set 內包含地板材料與填縫劑時，Floor Type Construction 增設英文欄位 (如 `Sealant`)，資訊入 Identity Data。 | 1. **標準輔助欄位規範**：統一定義 auxiliary 共享參數欄位：<br>   - `GreenMaterial_Adhesive` (接著劑/泥狀膠)<br>   - `GreenMaterial_Sealant` (填縫材/矽利康)<br>   - `GreenMaterial_Waterproofing` (防水膜)<br>2. 參數歸類於 `Construction` 或 `Green Building` 屬性群組。 | • `Floor Type Construction` 群組<br>• `GreenMaterial_Sealant`<br>• `GreenMaterial_Adhesive` |
| **情境 5**<br>單選非模型綠建材 (僅選填縫劑/膠類) | Agent 詢問套用位置 ➔ 使用者選既有 Floor Type ➔ Agent 複製建立新 Type 並增設欄位與使用者討論。 | 1. **互動引導 SOP**：Agent 提供 2 種複製模式：<br>   - **模式 A (新建 Type)**：複製 Type 並命名 `[原Type]_GBM0104192(填縫劑)`，防止影響舊元件。<br>   - **模式 B (既有套用)**：不複製，直接將填縫材參數寫入當前選取 Type。 | • 互動式討論 Prompt<br>• `Type Duplication` 機制<br>• `Construction` 自訂欄位 |
| **情境 6**<br>牆壁/地坪預設厚度判斷 | Agent 自動判斷外牆 (15cm) 或內牆 (12cm, 10cm) 常見厚度。 | 1. **厚度推判矩陣 (Thickness Heuristics)**：<br>   - **外牆 (`OST_Walls`)**：$150\,\text{mm}$ RC + $20\,\text{mm}$ 外飾層。<br>   - **室內輕隔間 (`OST_Walls`)**：$100\,\text{mm}$ / $120\,\text{mm}$ 矽酸鈣/石膏板牆。<br>   - **室內分戶牆 (`OST_Walls`)**：$120\,\text{mm}$ / $150\,\text{mm}$ 磚牆/RC。<br>   - **樓板 (`OST_Floors`)**：$150\,\text{mm}$ 結構板 + $30\,\text{mm}$ 裝修地坪。 | • Agent 自動推判預設值<br>• 提供使用者一鍵微調介面 |
| **情境 7**<br>獨立元件/門窗 (`OST_Windows`/`OST_Doors`) | 方法 7.1 (選模型既有元件另存 .rfa 注入參數) vs 方法 7.2 (Agent 從頭生成 - 太暴力且缺乏型錄尺寸)。 | 1. **採納方法 7.1 為標準 SOP**（方法 7.2 確實不符實務）。<br>2. **圖像預覽匹配機制**：Agent 讀取 TABC 預覽圖與 `subCategory` (如 GBM0103738 雙開橫拉窗)，提示使用者選擇相似基底元件，另存 .rfa 注入 16 個共享參數並導回專案。 | • `Family (.rfa)` 另存備份<br>• `Family Type Parameters`<br>• `Identity Data` / 遮陽 $S_c$ / 隔音 $R_w$ |

---

## 3. 新增與擴充之 4 大工程實務情境 (Additional Scenarios)

| 情境編號與名稱 | 工程應用背景 | 建築 Agent 規範與處理邏輯 | 落點與參數設計 |
| :--- | :--- | :--- | :--- |
| **情境 8**<br>天花板系統 (`OST_Ceilings`) | 吸音天花板、矽酸鈣天花板、礦纖板 (`GBM0103919`) | 天花板面層設為 `Finish 1 [4]`，厚度 $9\sim 15\,\text{mm}$。吸音數據 (`GreenMaterial_AcousticNRC`) 自動導出至天花板 Type 與材質屬性。 | • `OST_Ceilings` 構造層<br>• `GreenMaterial_AcousticNRC`<br>• `Finish 1 [4]` |
| **情境 9**<br>隔熱與防音底層 (`OST_Floors` / `OST_Walls` 夾層) | 樓板防音墊、牆體保溫 XPS 板、岩棉吸音毯 | 層位置設為 `Substrate [2]` (底層) 或 `Thermal/Air Layer [3]` (隔熱層)。衝擊音降低量 $\Delta L_w$ 與導熱率寫入 Physical Assets。 | • `Substrate [2]` 底層<br>• `Thermal/Air Layer [3]`<br>• `GreenMaterial_TestItems` |
| **情境 10**<br>單一元件包含多重綠建材 (Multi-Material) | 單一牆體同時包含綠建材隔間板 (構造層) 與綠建材粉刷 (飾面層) | 各構造層引用各自獨立的 Revit 綠建材 Material。牆 Type 的 Identity Data 自動產生 `GreenMaterial_CertNo_List` 清單 (如: `GBM0103919; GBM0104204`)。 | • 多重 `Finish` 構造層<br>• `GreenMaterial_CertNo_List`<br>• `OST_Materials` 分開寫入 |
| **情境 11**<br>既有模型材質覆蓋 vs 新建元件 Type | 施工階段更新既有專案模型材料 | Agent 擬訂計畫時提供 2 種替換路徑：<br>• **路徑 A (新建 Type)**：不影響模型中其他既有牆面。<br>• **路徑 B (覆蓋既有材質)**：直接批次更新模型內既有 Material 參數。 | • 互動式確認 Prompt<br>• `Material Injection` 策略 |

---

## 4. 主 Agent 綜合全情境對映 Master 表格 (TASK-003 最新規範)

| Revit 元件品類 (Category) | 綠建材種類/情境 | 建議構造層 (Layer) | 上層/材質寫入 (`OST_Materials`) | 下層/元件屬性寫入 (`Identity Data` / `Construction`) | 預設厚度推判 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`OST_Walls` (牆面)** | 塗料、壁紙、漆面 (情境 1) | `Finish 1 [4]` / `Finish 2 [5]` | 全量 16 個共享參數 (`GBM0104204`) | `GreenMaterial_Summary`, `Wall_GreenPaint_TVOC` | $2\,\text{mm}$ (塗料) |
| **`OST_Walls` (牆面)** | 板材、輕隔間、石膏磚 (情境 1, 6) | `Structure [1]` / `Finish 1 [4]` | 全量 16 個共享參數 (`GBM0103919`) | `W_INT_RC15_GBM0103919`, `GreenMaterial_CertNo` | $100\sim 150\,\text{mm}$ |
| **`OST_Floors` (樓板)** | 複合木地板、陶瓷地磚 (情境 2) | `Finish 1 [4]` | 全量 16 個共享參數 (`GBM0104194`) | `Surface Pattern` (Hatch), `Floor_QualifyArea` | $15\,\text{mm}$ (地磚) + $20\,\text{mm}$ 打底 |
| **`OST_Floors` (樓板)** | 隔音墊、防水膜 (情境 9) | `Substrate [2]` | 衝擊音 $\Delta L_w$, TVOC, 試驗數據 | `GreenMaterial_TestItems`, `Floor_ImpactSound_DeltaLw` | $5\sim 10\,\text{mm}$ |
| **`OST_Ceilings` (天花)** | 吸音天花板、矽酸鈣板 (情境 8) | `Finish 1 [4]` | 全量 16 個共享參數 (`GBM0103919`) | `GreenMaterial_AcousticNRC`, `Ceiling_QualifyArea` | $9\sim 15\,\text{mm}$ |
| **`OST_Windows` / `OST_Doors`** | 防音門窗、Low-E 玻璃 (情境 7) | 載入家族 `.rfa` (方法 7.1) | 玻璃/門窗材質屬性 | `Family Type Identity Data`, 遮陽 $S_c$, 隔音 $R_w$ | 依原 Family 尺寸 |
| **非幾何輔助材料** | 接著劑、填縫劑、膠類 (情境 4, 5) | 附屬於 `Walls` / `Floors` Type | 寫入對應 Parent Material 屬性 | `GreenMaterial_Adhesive`, `GreenMaterial_Sealant` (Construction) | $0\,\text{mm}$ (屬性寫入) |

---

## 5. 📘 Material 獨立命名與生成 Domain 規範 (Material Creation Domain Rules)

所有導入 Revit 材質庫 (`OST_Materials`) 的綠建材獨立材質，必須嚴格遵循以下語法公式：

$$\text{MaterialName} = \text{GBM標章編號} + \text{"\_"} + \text{材料名稱/簡稱}$$

### 🟢 命名標準對照
- **板材獨立材質**: **`GBM0103810_NICHIAS矽酸鈣板材`**
- **塗料獨立材質**: **`GBM0104106_水性漆Finish`**

### ⛔ 嚴格防呆門檻
1. 嚴禁包含 `預設牆_` 或 `TABC_` 前綴。
2. 嚴禁將塗料與板材標章串接在同一個 Material 名稱。

---

## 6. 檔案與工具鏈對接
- **Domain 規範檔**：[.agents/skills/combined-wall-set-import/domain.md](file:///c:/Users/User/Desktop/REVIT_MCP_study/.agents/skills/combined-wall-set-import/domain.md)
- **Revit 共享參數檔**：[GreenMaterial_SharedParams.txt](file:///c:/Users/User/Desktop/REVIT_MCP_study/GreenMaterial_SharedParams.txt)
- **計畫擬訂引擎**：[generate_revit_injection_plan.py](file:///c:/Users/User/Desktop/REVIT_MCP_study/generate_revit_injection_plan.py)
- **Showcase 展示網頁**：[assets/green-material-showcase.html](file:///c:/Users/User/Desktop/REVIT_MCP_study/assets/green-material-showcase.html)
