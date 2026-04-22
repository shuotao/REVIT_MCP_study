---
name: mep-extension-guide
description: 全球 pyRevit 大神擴展資源索引與機電自動化研究指南
tags: [MEP, pyRevit, Research, Guide]
version: 1.0
---

# 📚 全球 pyRevit 大神擴展資源索引 (機電自動化專題)

為了在 Revit 機電自動化道路上走得更遠，我們深度研究了全球頂尖開發者的源代碼。本文件將這些「大神級」的邏輯進行了提煉，作為我們開發時的底層參考手冊。

---

## 📐 1. [幾何之神] pyRevitMEP (Cyril Waechter)
> 🔗 **Source**: [CyrilWaechter/pyRevitMEP](https://github.com/CyrilWaechter/pyRevitMEP)
> **核心價值**：機電幾何精準度與複雜剖面管理的標竿。

*   **🌟 必用神技**：
    *   **自動化剖面 (Section Views)**：可依照管線走向自動建立「正交」或「平行」的剖面。
    *   **機電連接搜尋 (MEP Connectors)**：全網最穩定的 Fitting 搜尋與替換邏輯。
*   **💡 研究重點**：管線與空間關係的幾何算法。

---

## 📑 2. [出圖之神] pyrevitplus (Gui Talarico)
> 🔗 **Source**: [gtalarico/pyrevitplus](https://github.com/gtalarico/pyrevitplus)
> **核心價值**：極致的出圖美學與標註自動化排列。

*   **🌟 必用神技**：
    *   **標籤對齊 (Tag Alignment)**：全網最強的標籤排列工具。
    *   **標註管理 (Dimension Mgr)**：批次檢查並修正標註，確保竣工圖品質。
*   **💡 研究重點**：標註標記與 UI 介面交互封裝 (Forms)。

---

## 🧬 3. [計算之神] OpenMEP (Chuong Mep)
> 🔗 **Source**: [chuongmep/OpenMEP](https://github.com/chuongmep/OpenMEP)
> **核心價值**：機電工程計算與跨平台數據整合。

*   **🌟 必用神技**：
    *   **水力計算 (Hydraulic Calc)**：包含完整的壓降、流速計算邏輯。
    *   **參數映射 (Param Mapping)**：處理大規模 MEP 元件數據的標準化機制。
*   **💡 研究重點**：大數據處理與 Dynamo 節點橋接。

---

## 🚀 4. [效率之神] EF-Tools (Erik Frits)
> 🔗 **Source**: [ErikFrits/EF-Tools](https://github.com/ErikFrits/EF-Tools)
> **核心價值**：消滅所有重複性點擊，工程師日常的「瑞士軍刀」。

*   **🌟 必用神技**：
    *   **智能圖紙生成 (Sheet Creator)**：快速生成圖紙與視埠排列。
    *   **房間管理 (Room Mgmt)**：自動偵測邊界並生成填充區域。
*   **💡 研究重點**：高質感 UI 選單設計與工作流優化。

---

## 📊 5. [交付之神] guRoo (Gavin Crump)
> 🔗 **Source**: [aussieBIMguru/guRoo](https://github.com/aussieBIMguru/guRoo)
> **核心價值**：大廠級 BIM 交付標準與數據審計。

*   **🌟 必用神技**：
    *   **模型審核 (Audit Tools)**：自動檢查未連接的管線或失效實例。
    *   **數據同步 (Data Sync)**：Revit 與外部 Excel/資料庫的深度同步。
*   **💡 研究重點**：大規模專案的效能優化與交付標準建立。

---
**維護者**：CYBERPOTATO0416
**最後更新**：2026-04-23
