# 🌿 分支開發索引 (Branch Development Index)

本文件用於紀錄 `REVIT_MCP_study` 專案中各功能分支的開發背景與任務清單，作為專案回溯之用。

---

## 1. `feat/dependent-view-docs`
*   **關鍵詞**：從屬視圖、自動裁切、視圖自動化
*   **任務描述**：
    *   紀錄如何透過 API 依據指定網格範圍 (Grid Bounds) 自動產生從屬視圖。
    *   定義 `BoundingBox` 與視圖裁切框 (Crop Box) 的轉換邏輯。
*   **目前狀態**：已整合至 `main`，保留分支作為 API 邏輯回溯參考。

## 2. `feat/mep-extension-guide-final`
*   **關鍵詞**：MEP 生態系、雙軌分權、技術對照
*   **任務描述**：
    *   建立專案的「知識庫架構」。
    *   研究全球 MEP 自動化生態（如 pyRevitMEP）。
    *   確立「本倉庫存知識、擴展庫存代碼」的協作原則。
*   **目前狀態**：核心原則已定案，為本專案的基礎憲法。

## 3. `feat/pyrevit-tools`
*   **關鍵詞**：pyRevit 擴充、工具集、Potato Print 前身
*   **任務描述**：
    *   將早期零散的 Python 腳本封裝成 pyRevit PushButton。
    *   測試 `doc.Export` 與傳統印表機模式的穩定性。
*   **目前狀態**：工具已遷移至 `MCP-Tools-extension`，此分支保留原始開發足跡。

---

> [!TIP]
> **維修建議**：
> 雖然這些分支目前都已經合併或完成階段任務，但建議保留，直到專案正式發布 Version 1.0 為止。若想清理，請確保已在 `domain/lessons.md` 中留下關鍵技術筆記。
