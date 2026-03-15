---
name: connection-diagnosis
description: MCP 連線診斷與驗證流程。當遇到連線失敗或需要確認 Revit 專案狀態時使用。
tags: [debug, connection, port, 11111, verification, diagnostic]
---

# MCP 連線診斷與驗證流程

本文件定義當 AI 無法透過標準 MCP 工具連接 Revit 時的診斷與修復步驟，以及如何使用腳本直接驗證連線。

## 🔍 症狀

1.  呼叫 MCP 工具 (如 `get_project_info`) 失敗，錯誤訊息包含 `server name not found` 或 `Connection refused`。
2.  `read_resource` 失敗。
3.  需要確認目前連線的是哪個 Revit 專案。

## 🛠️ 診斷步驟

### 1. 確認 Revit Add-in 狀態

檢查 Revit 內的 MCP 服務是否已啟動，並確認監聽埠號 (預設 **11111**)。

**執行命令**：
```powershell
# 檢查 Port 11111 是否處於 LISTENING 狀態
netstat -ano | findstr :11111
```

### 2. 檢查 MCP Server 設定

確認專案設定檔中的 `REVIT_PORT` 是否與 Revit 實際開啟的埠號一致。

**設定檔位置**：
- `MCP-Server/gemini_mcp_config.json`
- `~/.gemini/settings.json` (如果使用 Gemini CLI)

**修正檢查點**：
```json
"env": {
    "REVIT_PORT": "11111", 
    "REVIT_VERSION": "2024"
}
```

### 3. 使用診斷腳本驗證 (Bypass MCP)

若懷疑是 AI Client 設定問題，可使用 Node.js 腳本直接測試 Socket 連線，繞過 MCP 協定層。

**建立診斷腳本** (`MCP-Server/scratch/get_project_info_manual.js`)：

```javascript
import { RevitSocketClient } from "../build/socket.js";
import fs from 'fs';
import path from 'path';

const client = new RevitSocketClient('localhost', 11111);

async function main() {
    try {
        console.error("Connecting to Revit at port 11111...");
        await client.connect();
        
        // 發送查詢指令
        const response = await client.sendCommand('get_project_info', {});
        
        if (response.success) {
            console.log("連線成功！專案資訊如下：");
            console.log(JSON.stringify(response.data, null, 2));
            
            // 選用：寫入檔案以便 AI 讀取
            // fs.writeFileSync('project_info.json', JSON.stringify(response.data, null, 2));
        } else {
            console.error("Command failed:", response.error);
        }
    } catch (err) {
        console.error("Execution failed:", err);
    } finally {
        client.disconnect();
        process.exit(0);
    }
}

main();
```

**執行診斷**：
```bash
node scratch/get_project_info_manual.js
```

### 4. 重啟 AI Client

修正設定檔後，通常需要**重啟 AI Client** (或 Reload Window) 才能讓新設定生效。

---

## ✅ 驗證成功標準

1.  `netstat` 顯示 Port 11111 為 `LISTENING`。
2.  診斷腳本成功回傳 `ProjectName`、`ProjectNumber` 等 JSON 資訊。
3.  確認回傳的專案名稱與使用者當前開啟的一致。
