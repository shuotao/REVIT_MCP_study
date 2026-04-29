---
name: mep-extension-guide
description: 全球 pyRevit 大神擴展資源索引與機電自動化研究指南 (完整版)
tags: [MEP, pyRevit, Research, Guide]
version: 1.2
---

# 📚 全球 pyRevit 大神擴展資源索引 (機電自動化專題)

為了在 Revit 機電自動化道路上走得更遠，我們深度研究了全球頂尖開發者的源代碼。本文件將這些「大神級」的邏輯進行了提煉，作為我們開發時的底層參考手冊。

---

## 📐 1. [幾何之神] pyRevitMEP (Cyril Waechter)
> 🔗 **Source**: [CyrilWaechter/pyRevitMEP](https://github.com/CyrilWaechter/pyRevitMEP)
> **核心價值**：機電幾何精準度與複雜剖面管理的標竿。作為機電工程師，這是您研究「管線與空間關係」的最佳教科書。

### 🌟 必用三大神技 (Must-use)
*   **自動化剖面 (Section Views)**：能夠依照管線走向自動建立「正交」或「平行」的剖面。這對於理解自動化建模時的幾何檢查非常有幫助。
*   **機電連接搜尋 (MEP Radios)**：其底層對於管件 (Fittings) 的搜尋與替換邏輯是全網最穩定的。
*   **幾何轉換 (Geometry Conversion)**：提供了極佳的 Revit Geometry 轉換到 Python 列表的封裝，適合進行複雜碰撞檢查演算。

### 🛠️ 推薦研究路徑
*   觀摩其 `mepputils` 模組，學習如何處理 MEP 系統的連通性（Connectors）與系統分類邏輯。

---

## 📑 2. [出圖之神] pyrevitplus (Gui Talarico)
> 🔗 **Source**: [gtalarico/pyrevitplus](https://github.com/gtalarico/pyrevitplus)
> **核心價值**：極致的出圖美學與標註自動化排列。這套工具證明了透過腳本可以讓 Revit 的圖面變得像 CAD 一樣整齊。

### 🌟 必用三大神技 (Must-use)
*   **標籤對齊 (Tag Alignment)**：全網最強的標籤對齊工具，支援水平、垂直與等距排列。
*   **標註管理 (Dimension Mgr)**：可以批次檢查並修正標註的文字覆蓋，是確保竣工圖品質的神器。
*   **介面封裝 (Quick UI Templates)**：他的 `forms` 介面封裝非常優雅，適合用來做個人工具的 UI 範本，提升工具質感。

### 🛠️ 推薦研究路徑
*   重點研究其對「標註 (Dimension)」的座標偏移算法，這對我們開發「自動標註流程」有直接的數學邏輯幫助。

---

## 🧬 3. [計算之神] OpenMEP (Chuong Mep)
> 🔗 **Source**: [chuongmep/OpenMEP](https://github.com/chuongmep/OpenMEP)
> **核心價值**：機電工程計算與跨平台數據整合。如果您需要處理「大量數據」或「水力計算」，一定要參考這裡。

### 🌟 必用三大神技 (Must-use)
*   **水力計算 (Hydraulic Calc)**：包含完整的管徑選擇、壓降與流速計算邏輯，是自動化設計的核心。
*   **Dynamo 整合 (Dynamo Integration)**：庫結構嚴謹，完美橋接了 Python 腳本與 Dynamo 節點，適合跨平台調用。
*   **參數映射 (Parameter Mapping)**：針對機電元件大量參數（如壓力、流量、系統名稱）有一套標準化的映射機制。

### 🛠️ 推薦研究路徑
*   研究其 `Core` API 的組織方式，這是處理大規模 MEP 模型數據流的全球標準範本。

---

## 🚀 4. [效率之神] EF-Tools (Erik Frits)
> 🔗 **Source**: [ErikFrits/EF-Tools](https://github.com/ErikFrits/EF-Tools)
> **核心價值**：消滅所有重複性點擊，BIM 工程師日常使用的「瑞士軍刀」。

### 🌟 必用三大神技 (Must-use)
*   **智能圖紙生成 (Sheet Creator)**：快速批量生成圖紙與視埠排列。
*   **房間管理 (Room Mgmt)**：自動偵測房間邊界並生成填充區域，對於容積檢討非常管用。
*   **極速操作 (Custom Shortcuts)**：強調人因工程的導航介面設計，對於提高日常工作流效率極具參考潛力。

### 🛠️ 推薦研究路徑
*   觀摩其 UI 排版 (`.tab` 與 `.panel` 的組織方式)，這能讓您的工具選單在 Revit 介面中更具專業質感。

---

## 📊 5. [交付之神] guRoo (Gavin Crump)
> 🔗 **Source**: [aussieBIMguru/guRoo](https://github.com/aussieBIMguru/guRoo)
> **核心價值**：大廠級 BIM 交付標準與數據審計。適合在專案後期交付階段，用來確保模型質量。

### 🌟 必用三大神技 (Must-use)
*   **數據審計 (Audit Tools)**：自動檢查未連接的管線或失效的族群實例，適合交付前的 QA/QC。
*   **深度數據同步 (Data Sync)**：將 Revit 數據與外部 Excel 或 SQL 資料庫進行雙向連動。
*   **視圖樣板控制 (View Control)**：針對大規模專案的視圖樣板 (View Templates) 大批量管理。

### 🛠️ 推薦研究路徑
*   研究其在大規模模型中的效能優化 (Performance Optimization)，確保腳本運行不會導致 Revit 閃退。

---
**維護者**：CYBERPOTATO0416
**最後更新**：2026-04-23
