# MCP 架構詳細說明

## MCP 是什麼

**MCP（Model Context Protocol）** 是一個開放標準協議，讓 AI 應用程式（Claude、Gemini 等）能夠存取外部工具和資源。

在本專案中，MCP 的作用是：
- 定義 Revit 可以執行的操作（建立牆、查詢元素等）
- 讓 AI 能夠理解和調用這些操作
- 實現 AI 與 Revit 之間的雙向通訊

---

## MCP Client 是什麼

**MCP Client（客戶端）** 是指內建 MCP 支援的應用程式。

在本專案中有 5 種 MCP Client：

1. **Claude Desktop** - Anthropic 的官方應用程式
2. **Gemini CLI** - Google 的命令列工具
3. **VS Code Copilot** - 整合在 VS Code 中的 AI
4. **Google Antigravity** - Google 的雲端開發環境
5. **Revit Add-in Chat** - 本專案開發的 Revit 內嵌客戶端

---

## 4+1 方案架構

### 前 4 種：外部 MCP Client

所有外部客戶端都遵循相同的架構流程：

```
使用者輸入
   │
   ▼
┌──────────────────────┐
│  MCP Client          │  1. 解析使用者的自然語言
│ (Claude/Gemini/等)   │  2. 讀取 MCP Server 的工具定義
└──────────┬───────────┘
           │
           │ 3. 決定要調用哪個工具
           │    (例如: create_wall)
           │
           ▼
┌──────────────────────┐
│  MCP Server          │  4. 接收工具調用請求
│ (Node.js)            │  5. 驗證參數
└──────────┬───────────┘
           │
           │ 6. 透過 WebSocket 發送給 Revit
           │
           ▼
┌──────────────────────┐
│  Revit Add-in        │  7. 在 Revit 中執行操作
│ (C# WebSocket)       │  8. 返回結果
└──────────┬───────────┘
           │
           │ 9. WebSocket 回傳結果給 MCP Server
           │
           ▼
┌──────────────────────┐
│  MCP Server          │  10. 格式化結果
└──────────┬───────────┘
           │
           │ 11. 發送給 MCP Client
           │
           ▼
┌──────────────────────┐
│  MCP Client          │  12. 向使用者呈現結果
└──────────────────────┘
```

**關鍵特點：**
- AI 應用程式已經內建 MCP 支援
- 使用者無需額外設定 API Key
- 每個應用程式使用自己的認證方式

---

### 第 5 種：Revit 內嵌客戶端（直接 API 整合）

```
使用者在 Revit 內開啟 Chat 視窗
   │
   ▼
┌────────────────────────────────┐
│  GeminiChatService (C#)        │  1. 接收使用者訊息
│  - 使用 API Key 認證           │  2. 直接調用 Gemini API
└────────────┬───────────────────┘
             │
             │ 3. HTTP 請求到 Gemini API
             │    (包含 API Key)
             │
             ▼
         ┌────────────────┐
         │  Gemini API    │  4. AI 處理請求
         │  (雲端)        │  5. 返回回應
         └────────┬───────┘
                  │
                  │ 6. 回傳給 GeminiChatService
                  │
                  ▼
         ┌────────────────────────┐
         │  CommandExecutor       │  7. 解析 AI 回應
         │  - 執行 Revit 操作     │  8. 調用 Revit API
         └────────┬───────────────┘
                  │
                  │ 9. 在 Revit 中執行
                  │
                  ▼
         ┌────────────────────────┐
         │  Revit 應用程式        │  10. 操作完成
         └────────────────────────┘
```

**關鍵特點：**
- 不經過 MCP Server
- 直接調用 Gemini API
- 需要 API Key 認證
- 完全在 Revit 內運行

---

## 為什麼外部方案不需要 API Key

外部方案（Claude Desktop、Gemini CLI 等）不需要 API Key 的原因：

1. **應用程式已經認證**
   ```
   Claude Desktop
   └─ 已綁定您的 Anthropic 帳戶
      └─ Anthropic 知道您的身份和配額
   
   Gemini CLI
   └─ 已綁定您的 Google 帳戶
      └─ Google 知道您的身份和配額
   ```

2. **工作流程**
   ```
   使用者告訴 Claude: "請建立一面牆"
   │
   Claude (已認證的客戶)
   └─ 我知道這是誰在用我，用他的帳戶額度
   
   Claude 調用 MCP Server 的工具
   └─ 不需要額外的 API Key
   
   MCP Server 只負責翻譯
   └─ 不需要認證
   ```

---

## 為什麼內嵌方案需要 API Key

內嵌方案（Revit 內的 Chat）需要 API Key 的原因：

1. **直接客戶關係**
   ```
   您 (使用者)
   └─ 是 Gemini API 的直接客戶
   
   您需要告訴 Google:
   └─ "我是誰，請根據我的 API Key 計費"
   ```

2. **認證方式**
   ```
   GeminiChatService (在 Revit 內)
   │
   └─ 需要 API Key 才能驗證身份
   
   API Key 告訴 Gemini API:
   └─ "這個請求來自正當的使用者"
   ```

3. **計費模式**
   ```
   外部方案: Claude Desktop 計費 → 您
   內嵌方案: Gemini API 計費 → 您（直接）
   ```

---

## 關鍵差異總結

| 項目 | 外部方案 (4 種) | 內嵌方案 (1 種) |
|------|-----------------|-----------------|
| **API Key** | 不需要 | 需要 |
| **應用程式** | 獨立運行 | 在 Revit 內執行 |
| **MCP Server** | 必需 | 不需要 |
| **認證方式** | 應用程式內建 | API Key |
| **使用者體驗** | 切換應用程式 | 在 Revit 內對話 |
| **網路需求** | 需要應用程式有網路 | 需要 Revit 有網路 |
| **計費** | 應用程式計費 | 直接 API 計費 |
| **部署難度** | 簡單（現成應用） | 複雜（需開發） |

---

## MCP Server 在不同方案中的角色

### 外部方案中的 MCP Server

```
作用：
- 定義 Revit 工具列表
- 實現 AI 客戶端與 Revit 的通訊橋樑
- 將 MCP 工具調用轉換為 Revit API 調用

地位：
- 必需元件
- 在本機運行
- 監聽 WebSocket 連接 (localhost:8964)
```

### 內嵌方案中的情況

```
MCP Server 不參與
- 內嵌客戶端直接調用 Revit API
- 無需 MCP 協議轉換
- Gemini API 直接向 CommandExecutor 發送請求
```

---

## 選擇方案的建議

### 使用外部方案如果：
- 您已經在使用 Claude / Gemini 等應用程式
- 您想要最簡單的設定
- 您不想額外承擔 API 費用

### 使用內嵌方案如果：
- 您想要最流暢的使用體驗
- 您願意為 Gemini API 付費
- 您需要在 Revit 內直接對話
- 您的團隊需要統一的工作流程

---

## 常見問題

### Q: 我可以同時使用多個方案嗎？
A: 可以。您可以同時設定 Claude Desktop 和內嵌 Chat，根據需求選擇使用。

### Q: MCP Server 會做什麼？
A: 它只負責「翻譯」。AI 說「建立牆」，MCP Server 把它翻譯成 Revit API 調用。

### Q: 內嵌方案的 API Key 會暴露嗎？
A: 不會。API Key 存在環境變數中，不會提交到 GitHub，也不會被外部應用程式看到。

### Q: 外部方案為什麼需要 MCP Server？
A: 因為 Claude、Gemini 等應用程式不了解 Revit。MCP Server 告訴它們「你可以做這些 Revit 操作」。
