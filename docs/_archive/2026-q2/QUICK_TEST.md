# 外牆開口檢討測試 - 快速開始

## ✅ 建置狀態

**最新建置結果**：
```
建置成功
- 警告: 0 個
- 錯誤: 0 個
- 時間: 00:00:00.51
- DLL: D:\David\BIM MCP\REVIT_MCP_study\MCP\bin\Release\RevitMCP.dll
- 大小: 126 KB
- 時間: 2026/1/13 上午 10:25:36
```

---

## 🚀 快速測試步驟

### 步驟 1：部署 DLL 到 Revit（如果尚未部署）

```powershell
# 執行安裝腳本
cd "d:\David\BIM MCP\REVIT_MCP_study"
.\scripts\install-addon.ps1
# 選擇: 2024

# 或手動複製
$target = "$env:APPDATA\Autodesk\Revit\Addins\2024\RevitMCP"
Copy-Item "MCP\bin\Release\RevitMCP.dll" $target -Force
Copy-Item "MCP\RevitMCP.addin" $target -Force
```

### 步驟 2：啟動 Revit 並準備專案

**必要條件**（缺一不可）：
1. ✅ **PropertyLine（地界線）** - 必須先建立！
   - Revit → 場地 → Property Line
   - 繪製基地邊界封閉線
   
2. ✅ **外牆** - WallType.Function = Exterior
3. ✅ **門或窗** - 至少一個開口

### 步驟 3：執行檢討

#### 選項 A：使用測試腳本（推薦）

```powershell
cd "d:\David\BIM MCP\REVIT_MCP_study\MCP-Server"
node scripts\exterior_wall_check.js
```

#### 選項 B：透過 AI 呼叫

對 AI 說：
```
請檢查 Revit 專案中的外牆開口，依據建築技術規則第45條及第110條。
```

---

## 📊 預期結果

### 1. 終端輸出

```
🔌 連接到 Revit MCP Server...
✅ 連接成功

📋 執行外牆開口檢討...

✅ 檢討完成！

📊 統計摘要：
  - 檢查牆壁數: X
  - 檢查開口數: X
  - 🔴 違規: X
  - 🟠 警告: X
  - 🟢 通過: X
  - 地界線數: X
```

### 2. Revit 視覺化

- 🔴 **紅色開口** = 違規（距境界線 < 1.0m）
- 🟠 **橘色開口** = 警告（需防火門窗）
- 🟢 **綠色開口** = 符合規定

### 3. JSON 報表

位置：`D:\Reports\exterior_wall_check.json`

```json
{
  "success": true,
  "summary": { ... },
  "details": [ ... ]
}
```

---

## ⚠️ 常見問題

### ❌ 找不到基地邊界線

**錯誤**：
```
找不到基地邊界線（PropertyLine）。請確認專案中已建立地界線。
```

**解決**：
1. Revit → 場地 → Property Line
2. 繪製基地邊界（必須封閉）
3. 重新執行

### ❌ 無法連接到 Revit

**錯誤**：
```
connect ECONNREFUSED 127.0.0.1:8964
```

**解決**：
1. 確認 Revit 已開啟
2. 點擊 RevitMCP 按鈕（外部工具）
3. 檢查 WebSocket 連接狀態
4. 若顯示 Port 被 System (PID: 4) 佔用，以系統管理員執行 `scripts\release-port.ps1`

---

## 📝 測試清單

- [ ] DLL 已部署到 Revit Addins 目錄
- [ ] Revit 已開啟並載入 RevitMCP
- [ ] 專案包含 PropertyLine
- [ ] 專案包含外牆與開口
- [ ] 執行測試成功
- [ ] Revit 中看到顏色標示
- [ ] JSON 報表已產生

---

**建置時間**: 2026-01-13 10:25:36
**測試狀態**: ⏳ 待執行
**下一步**: 執行測試腳本或透過 AI 呼叫
