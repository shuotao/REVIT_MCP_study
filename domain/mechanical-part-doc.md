# 機械組件自動化出圖 (Mechanical Assembly Documentation)

本文件定義了自動生成「機械零件圖/組件圖」的核心邏輯與技術標準，涵蓋從視圖生成到最終圖面後處理的完整生命週期。

## 1. 核心邏輯架構

自動化出圖流程分為三個階段：
1. **視圖生成 (Generation)**：建立三視圖與 3D 視圖。
2. **自適應比例計算 (Adaptive Scaling)**：決定最佳工程比例。
3. **佈置與後處理 (Layout & Post-Processing)**：排版並套用視覺標準。

---

## 2. 自適應比例演算法 (Scaling Concept)

為確保零件在不同尺寸的圖框（如 A3 或 E1）中都能獲得最佳表現，比例計算遵循以下原則：

### A. 幾何邊界判定
- **禁止使用**：`View.get_BoundingBox()`，因為其結果會被未收縮的「裁切框 (Crop Region)」干擾。
- **正確做法**：遍歷組件內所有成員 (Members)，計算其幾何聯集 (Union) 的最大維度 ($L_{max}$)。

### B. 目標比例計算
1. **計算原始比例 ($S_{raw}$)**：
   $$S_{raw} = L_{max} / (D_{effective} \times 0.8)$$
   - $D_{effective}$：圖紙的有效象限長度（例如 E1 圖紙的 1/2 寬度）。
2. **級距化 (Snapping)**：
   - 比例應符合標準工程級距：`1:1, 1:2, 1:5, 1:10, 1:20...`。
   - 避免出現 `1:3` 或 `1:7` 這種非標準比例。

---

## 3. 視覺表現標準 (Visual Standards)

所有零件圖視圖必須透過 **BuiltInParameter (BIP)** 強制執行以下設定，以確保一致性並避開 API Enum 解析錯誤：

| 參數 (BIP) | 設定值 (Value) | 說明 |
| :--- | :--- | :--- |
| `VIEW_DETAIL_LEVEL` | `3` (Fine) | 展現螺絲、管件與細部構造。 |
| `MODEL_GRAPHICS_STYLE` | `2` (Hidden Line) | 2D 三視圖標準：隱藏線模式。 |
| `MODEL_GRAPHICS_STYLE` | `3` (Shaded) | 3D 等角圖標準：描影(材質)模式。 |

---

## 4. 佈置與排版 (Layout Logic)

### A. 四象限座標映射
在一張標準圖紙中，四個視圖應映射至以下相對位置（以圖紙中心為原點）：
- **左下**: Plan (平面圖)
- **右下**: Front (正視圖)
- **左上**: Side (側視圖)
- **右上**: 3D (等角圖)

### B. 視圖標題線 (Viewport Title)
- **陷阱**：調整比例後，標題線長度不會自動重置。
- **概念方案**：需透過 API 重設 `Viewport.LabelOffset` 與長度參數，或在零件圖中統一使用「無標題 (No Title)」類型。

## PDF 輸出策略：原生引擎轉換 (Revit 2022+)

本工具鏈採用 Revit 2024 原生 PDF 導出 API (`doc.Export`)，與傳統虛擬印表機列印有本質區別：

*   **零依賴 (Zero Dependency)**：無需在 Windows 系統安裝任何 PDF 印表機驅動程式（如 Bluebeam, Adobe PDF）。即使是全新的電腦環境，只要有 Revit 2022+ 即可運行。
*   **非虛擬列印，而是「轉換」**：直接將 Revit 向量幾何轉換為 PDF 格式，而非模擬列印。這消除了跳轉印表機視窗的步驟，實現真正的「背景導出」，速度極快。
*   **精確 API 控制**：透過 `PDFExportOptions` 直接控制檔名、品質、超連結（View Links）等細節。
    *   *技術細節*：設置 `ViewLinksInBlue = False` 可消除 PDF 中常見的藍色點擊框。
    *   *技術細節*：隱藏參考平面需使用單數屬性 `HideReferencePlane` (Revit API 的命名不對稱陷阱)。

---

## 5. 環境清理與自動化收尾

1. **裁切框管理 (Crop Management)**：
   - 必須將 `Crop Box` 強制收縮至零件幾何邊界外加 **20mm - 50mm** 的偏移量。
   - 完成後將 `CropBoxVisible` 設為 `False`。
2. **分類可見性 (Category Visibility)**：
   - 自動隱藏：Levels (樓層)、Grids (軸網)、Sections (剖面符號)。
   - 確保圖面僅保留組件成員與必要的標註。

---

## 關聯資源
- **C# 實作核心**: [CommandExecutor.Assembly.cs](file:///h:/0_REVIT MCP/REVIT_MCP_study-main/MCP/Core/Commands/CommandExecutor.Assembly.cs)
- **開發教訓總結**: [lessons.md](file:///h:/0_REVIT MCP/REVIT_MCP_study-main/domain/lessons.md)
