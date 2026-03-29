# 📦 一般使用者：pyRevit 掛載指南 (無需 Git 指引)

如果您只想將標註工具 (Marking tool) 掛載到您的 Revit 中使用，且不需要進行代碼開發或使用 MCP Server，請遵循以下步驟：

## 第一步：取得代碼
將您的 **`pyRevit_Tools`** 資料夾 (內含 `MCP_Tools.extension`) 下載並解壓縮到您的本地路徑（例如：`D:\Revit_Scripts\pyRevit_Tools`）。

## 第二步：在 pyRevit 中「掛載 (Add Extensions)」
1. 打開 **Autodesk Revit**。
2. 在上方功能表 (Ribbon) 找到 **pyRevit 頁籤** $\rightarrow$ 點擊 **Settings (設定)**。
3. 在彈出的視窗中，找到 **Extensions (擴展)** 側標籤。
4. 在上方分欄中點擊 **Add Folder (新增資料夾)**。
5. 瀏覽並選擇您剛才下載的 **`pyRevit_Tools`** 資料夾路徑，按下確定。

## 第三步：重新載入 (Reload)
1. 回到 Revit 的 pyRevit 頁籤。
2. 找到右側的 **Reload (重新載入)** 按鈕並點擊。
3. 等待數秒後，您就會在 Revit 上方看到一個名為 **「MCP_Schedules」** 的分頁抽屜，裡面已經包含最新版標註工具了！

---
### 💡 給開發者的備註：
*   **Bundle 架構**：pyRevit 基於文件夾後綴（`.extension`, `.tab`, `.panel`）進行動態渲染。
*   **即時生效**：更新代碼後只需重新載入 (Reload)，不需重啟 Revit。
*   **一魚兩吃**：本 Extension 同時相容於 MCP AI 代理人的程式化調用。
