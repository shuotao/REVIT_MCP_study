---
name: dwg-beam-import
description: "從 CAD/DWG 圖紙自動翻模 Revit 結構樑的 SOP。包含單向/雙向大樑與次樑的處理，以及兩種建置模式：快速模式 (指定統一尺寸) 與 名稱對應模式 (依圖面文字對應)。也記錄了 Y/Z 軸對正與偏移值的關鍵防呆處理。"
metadata:
  version: "1.1"
  updated: "2026-06-25"
  references: []
  related:
    - dwg-column-import.md
    - tool-capability-boundary.md
  referenced_by:
    - dwg-beam-import
  tags: [DWG, DXF, CAD, ImportInstance, 結構樑, 樑翻模, 名稱對應, 快速模式, create_beams_from_dwg, Revit]
---

# DWG/DXF 樑自動翻模 SOP

將 CAD 圖紙中的雙線樑中心線擷取，並自動於 Revit 專案中建立對應之結構樑。支援讀取 DWG/DXF 圖紙上的文字標註（例如 B1、b4），並自動配對至 Revit 內既有之族群類型。

- **相關工具**：`get_dwg_beam_layers`, `preview_dwg_beams`, `create_beams_from_dwg`。
- **涉及核心**：`MCP/Core/DwgBeamExecutor.cs`、`bridge/python/skills/ezdxf_worker.py`。

> **嚴格防呆設計**：樑的翻模不會像柱子一樣「隨意亂猜尺寸並自動建新型別」，因為單看平面圖線條無法得知樑深。因此必須透過文字配對（或人工指定）既有的族群型別。

---

## 1. 兩種翻模模式

大樑（Main Beams）與小樑/次樑（Secondary Beams）皆支援以下兩種模式：

### 模式 A：快速模式 (Quick Mode)
- **適用時機**：初期建置、圖說尚未有完整文字標註，或是想把某個圖層的所有樑統一設定為相同尺寸時。
- **作法**：使用者直接指定一個現有的類型名稱（例如 `50x70` 或 `G1_50x70`）。程式會將圖層中萃取出的所有樑中心線，全部套用此類型建立。

### 模式 B：名稱對應模式 (Label Mode / Mode C)
- **適用時機**：CAD 圖面上已有完整的文字標註（如 `B1`、`b4`），且 Revit 專案內已預先建立好對應名稱的族群類型（如 `B1_50 x 70`、`b4_40 x 65`）。
- **作法**：提供文字所在的圖層名稱（可分別指定 X 向與 Y 向，也可共用）。程式會利用 `ezdxf` 讀取文字位置，藉由空間距離將文字標註與附近的樑中心線配對。
- **配對邏輯 (嚴格區分大小寫)**：
  - 程式會去尋找與文字「完全相同」或「以前綴+底線/橫槓」開頭的型別。
  - 例如 CAD 文字為 `b4`：會精準對應到 `b4_40 x 65`，而**絕對不會**去抓到大樑的 `B4_50 x 70`（`StringComparison.Ordinal`）。
  - 若找不到對應的文字或型別，該線段會被**安全地略過**，不會報錯中斷。

---

## 2. 翻模標準操作流程 (SOP)

### ⛔ 強制斷點：每步取得使用者同意，不一口氣跑完。

**步驟 1 — 掃描圖層 ⛔ 斷點**
呼叫 `get_dwg_beam_layers`，回報視圖名稱、CAD 數量、推薦樑圖層，並依序詢問：
1. 這次先處理哪個批次？（大樑 / 次樑 / 地樑）
2. 翻模模式：
   - **模式 A（快速）**：指定統一型別，全部套用，不需要文字圖層
   - **模式 B（名稱對應）**：依圖面文字自動配對 Revit 型別

**步驟 2 — 依模式收集參數**
- **模式 A**：詢問族群名稱 + 統一型別名稱 → 直接進步驟 3
- **模式 B**：詢問文字圖層（X 向、Y 向可分開，也可共用）→ 詢問族群名稱 → 進步驟 3

**步驟 3 — 放樣健檢（僅供參考，不等確認）**
呼叫 `preview_dwg_beams(layerName)`，回報偵測樑數、X / Y / 斜向各幾根、樑寬範圍，直接繼續。

**步驟 4 — 執行建樑（不可復原）**
呼叫 `create_beams_from_dwg`，傳入：
- `layerName`：樑輪廓線圖層
- `familyName`：族群名稱
- `typeName`：（模式 A 專用）
- `textLayerNameX` / `textLayerNameY`：（模式 B 專用）
- `beamRole`：`大樑` / `次樑` / `地樑`

回報 `created` / `failed` / `typesUsed` / `unmatchedLabels`。

**失敗根因判斷：**

| 現象 | 可能原因 |
|---|---|
| `unmatchedLabels` 不為空 | 族群缺對應型別，需協作補齊 |
| `failed > 0` 且 `unmatchedLabels` 為空 | Revit 端點問題（樑太短、無支撐點），手動補 |
| `labelReadStatus = no_oda` | DWG 需安裝 ODA File Converter |

**步驟 5 — 下一批次**
詢問是否繼續處理下一個批次（次樑 / 地樑）。分批原則：**大樑 → 次樑 → 地樑**。

---

## 3. 關鍵機制與歷史防呆 (Important Fixes)

### 3.1 獨立線段與雜訊過濾
`ExtractBeamCenterLines` 採用了嚴格的雙線配對邏輯：
- 必須找到兩條平行的線段（角度差異小於一定閾值）。
- 兩線間距必須在合理樑寬範圍內（10cm ~ 2.5m）。
- 兩線在縱向上必須有足夠的重疊長度（大於 30cm）。
- **獨立線段會被直接當作雜訊無視**，不會誤建樑。重疊的兩張 CAD（導致線段雙倍）也會因為配對距離的篩選，自然忽略重複的線條。

### 3.2 樑偏移與對正問題 (Y/Z Justification & Offset)
在 Revit API 建立 `FamilyInstance` 時，為避免新建立的樑繼承到專案中「上一次繪製殘留的記憶偏移值」（例如 Z 向偏移 -2.40），程式在建置後會強制重置內建參數：
```csharp
// 確保樑位在中心與頂部，且沒有偏移
var yJustParam = inst.get_Parameter(BuiltInParameter.Y_JUSTIFICATION);
if (yJustParam != null) yJustParam.Set(1); // 1 = Center (注意: 2 為 Origin)

var zJustParam = inst.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
if (zJustParam != null) zJustParam.Set(0); // 0 = Top

var zOffsetParam = inst.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE);
if (zOffsetParam != null) zOffsetParam.Set(0.0); // 強制歸零
```

### 3.3 斜樑兩波處理（2026-06-25）
`create_beams_from_dwg` 內部將樑分兩波建立，同一 Transaction：
- **第一波**：正交樑（`isX || isY`），中心點距離配對，配對成功的標籤加入 `usedLabelKeys`（HashSet）。
- **第二波**：斜樑（`!isX && !isY`），過濾已用標籤後，改用**點到線段距離**（`PointToLineDistanceMm`）配對，閾值 = 樑寬/2 + 500mm。

優點：避免轉角處標籤被正交樑誤搶、提升非正交配對精準度、兩波邏輯解耦。
`IsUsed` 狀態由 C# 維護（HashSet），Python worker 無狀態不動。

### 3.4 DWG 與 ODA File Converter
因為 Python 的 `ezdxf` 原生只支援 DXF 格式，若圖紙為 DWG 格式，底層工具 (`ezdxf_worker.py`) 會自動呼叫 `ODAFileConverter.exe` 進行背景轉檔再讀取文字。因此不論 DWG 或 DWF/DXF，只要安裝了 ODA 即可無縫支援「名稱對應模式」。
