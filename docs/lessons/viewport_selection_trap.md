# 🎓 課程筆記：視埠選取陷阱與 MEP 選取策略

## 🎯 問題背景 (Scenario)
在開發標註 (Marking) 工具時，使用者反應點選 MEP 元件卻報錯「沒有可寫入標記參數」。

## 🔍 問題核心：圖紙上的「分層選取」
當使用者在 **圖紙 (Sheet)** 視圖下執行點選功能時：
1. **未活化視圖 (None-Activated)**：Revit API 會優先點選到 **視埠 (Viewport)** 框，而不是視窗內部的管線、設備。
2. **已活化視圖 (Activated)**：Revit API 才能穿透視埠，點選到真正的模型元件 (Model Elements)。

## ⚠️ 陷阱特徵 (Symptoms)
- 常見報錯元件：`Viewport (視埠)` (ID: 25xxxx)。
- 可用參數差異：
  - **模型元件**：具備 `Mark (標記)` 參數。
  - **視埠**：不具備 `Mark`，其對應的邏輯編號參數為 `Detail Number (詳細編號)`。

## 💡 開發指引 (Best Practices)
1. **主動警告**：若偵測到選取元件為 `Viewport` 類別，應主動提示使用者「請進入活化視圖 (Activate View) 以選取 MEP 元件」。
2. **自動導引**：在 pyRevit 腳本中，可以透過 `doc.ActiveView` 判斷當前視圖類型。如果在 Sheet 上，應先提示切換模式。
3. **區分目標**：
   - 模型標註 $\rightarrow$ 必須在模型模式下執行。
   - 圖紙標記 (Renumbering Details) $\rightarrow$ 才在圖紙模式對視埠操作。

---
*這份文件記錄於 2026-03-25，針對 MEP 標註工具選取失效之修正筆記。*
