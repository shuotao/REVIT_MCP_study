# Quick Access MEP Tools V.0

這是「快速管材工具」的初始版本架構，包含 5 個可客製化按鈕與動態名稱更新功能。

## 架構說明 (Architecture)

### 1. 核心邏輯 (`lib/quick_access.py`)
- **配置管理**：處理 `config.json` 的讀寫，儲存 5 種常用的管材名稱。
- **UI 更新**：提供 `update_ribbon_titles` 方法，可遍歷 pyRevit 面板並根據配置動態修改按鈕標題。

### 2. 啟動掛鉤 (`hooks/app-init.py`)
- **自動同步**：在 Revit 啟動或 pyRevit Reload 時，自動載入配置並更新面板按鈕名稱。

### 3. 面板組件 (`panel/QuickAccess.panel`)
- **設定按鈕 (`Settings.pushbutton`)**：
  - 過濾當前模型所有 `PipeType`。
  - 提供多步選取 UI (5 個 Slot)，並自動剔除已選項目。
  - 儲存配置並觸發 UI 刷新。
- **管材按鈕 (`FavoritePipes.pulldown`)**：
  - 動態獲取 Index (1-5)。
  - 使用 `doc.SetDefaultElementTypeId` 切換系統族群預設類型。
  - 透過 `PostCommand` 進入原生繪圖模式。

## 安裝方式
將此目錄下的組件整合至您的 pyRevit 擴展中適當的位置即可使用。
