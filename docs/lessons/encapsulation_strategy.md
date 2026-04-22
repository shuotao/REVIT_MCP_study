# 🎓 課程筆記：一魚兩吃 — Revit 功能的多重封裝策略

## 🎯 核心概念：邏輯與介面分離
為了同時滿足 **人類使用者 (GUI)** 與 **AI (API)** 的需求，功能開發應遵循以下分層：

1. **人類介面 (pyRevit)**：
   - 專注於開發「互動手感」。
   - 提供配置視窗 (Settings) 與視覺反饋 (Success Windows)。
   - 適合手動選取少數元素的情況。

2. **AI 接口 (MCP Server)**：
   - 專注於開發「語意化操作」。
   - 定義清晰的輸入結構 (Schema)，如 `prefix`, `startNumber`。
   - 適合大範圍、邏輯遞增的自動化任務。

3. **核心引擎 (C# Backend)**：
   - 提供強健的底層 API，處理 Revit 參數的各種邊緣情況。
   - 實作「參數回退機制」(Fallback)，例如找不到 `ALL_MODEL_MARK` 時自動查找 `Mark`。

## 🛠️ 本次實作案例：標註工具 (Marking Tool)
- **pyRevit**: 實現了點選賦值。
- **MCP**: 實現了 `batch_set_marks`，讓 AI 能一次標註整個樓層的牆體。
- **PR 標註**: 在 Commit 訊息關聯 `PR#14`。

---
*這份文件記錄於 2026-03-24，作為 Revit MCP 雙向封裝計畫的開發標準。*
