# 第二堂：MCP 架構深度解析

## 用建築設計視角理解系統架構

---

> **本堂目標**：讓你完全理解這套系統如何運作，
> 就像理解一棟建築的結構系統一樣。

---

## 2.1 建築師視角的系統架構

### 用蓋房子來理解

想像你是一個建築案的業主兼建築師，想要蓋一棟房子：

```
你（業主/建築師）                    ← 這是 AI Client（Claude/Gemini）
     │
     │ 「我要一面 5 米長的紅磚牆」
     ▼
總工程師辦公室                       ← 這是 MCP Server（Node.js）
     │
     │ 翻譯成施工指令
     ▼
工地現場                             ← 這是 Revit Add-in（C#）
     │
     │ 執行施工
     ▼
實際建築物                           ← 這是 Revit 模型
```

**關鍵重點**：
- 你不需要自己拿磚頭砌牆
- 你只需要說清楚你要什麼
- 中間有專業的人幫你翻譯和執行

---

## 2.2 各系統元件的角色

用建築專案的角色來對應：

| 系統元件 | 建築角色 | 職責說明 |
|:--------|:--------|:--------|
| **AI Client** | 業主 + 建築師 | 發出需求：「我要這樣的空間」 |
| **MCP Server** | 總工程師 | 把設計圖翻譯成施工計畫 |
| **Revit Add-in** | 工地主任 | 在現場指揮實際施工 |
| **Revit** | 建築工地 | 實際執行並呈現結果 |
| **WebSocket** | 對講機 | 讓各單位即時溝通 |
| **Tools** | 施工工具 | 鑽頭、起重機、鷹架... |
| **Domain Files** | 施工規範書 | SOP、工法說明、安全守則 |

---

## 2.3 檔案類型深度解析

### 每種檔案就像建築專案中的不同文件

讓我們來了解這套系統中每種檔案的意義：

---

### 📋 `.json` - 設定檔

**建築類比**：專案聯絡人清單、工程設定表

```json
{
  "mcpServers": {
    "revit-mcp": {
      "command": "node",
      "args": ["路徑/index.js"]
    }
  }
}
```

**白話解釋**：
> 「這是一份名片，告訴 AI 助手：  
> 如果你要找 Revit 的工具，去這個地址找 Node.js 那位。」

**常見檔案**：
- `mcp.json` - 告訴 VS Code 去哪找 MCP Server
- `settings.json` - Gemini CLI 的設定
- `package.json` - Node.js 專案的「材料清單」

---

### 📜 `.ts` / `.js` - TypeScript / JavaScript

**建築類比**：總工程師的工作手冊

```typescript
{
    name: "create_wall",
    description: "建立一面牆",
    inputSchema: {
        properties: {
            length: { type: "number", description: "牆長度（公尺）" }
        }
    }
}
```

**白話解釋**：
> 「這是總工程師的手冊，寫著：  
> 有一個叫 create_wall 的服務，需要知道牆的長度，  
> 然後我會幫你傳達給工地。」

**常見檔案**：
- `revit-tools.ts` - 定義有哪些工具可用
- `index.ts` - MCP Server 的總指揮
- `socket.ts` - 管理和工地的通訊

---

### 🔧 `.cs` - C# 程式碼

**建築類比**：工地師傅的施工手冊

```csharp
case "create_wall":
    // 取得參數
    double length = parameters["length"].Value<double>();
    // 調用 Revit API 建立牆
    Wall.Create(doc, curve, levelId, false);
    break;
```

**白話解釋**：
> 「這是工地師傅的手冊，寫著：  
> 如果接到 create_wall 的指令，  
> 就拿起工具，照著這個步驟砌一面牆。」

**常見檔案**：
- `CommandExecutor.cs` - 核心執行邏輯
- `SocketService.cs` - 接聽對講機
- `Application.cs` - 開工準備

**版本差異注意**：
- Revit 2022/2023：使用 `int` 型別的 ElementId
- Revit 2024：使用 `long` 型別的 ElementId
- 這就像不同年份的建築規範有微小差異

---

### 📦 `.csproj` - C# 專案檔

**建築類比**：施工合約書

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="RevitAPI">
      <HintPath>...\RevitAPI.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

**白話解釋**：
> 「這是合約書，寫著：  
> 這個工程要用 .NET 4.8 規範，  
> 需要 RevitAPI 這個材料。」

**版本對應**：
| Revit 版本 | 專案檔 | 為什麼不同？ |
|:----------|:------|:-----------|
| 2022/2023 | `RevitMCP.csproj` | 使用舊版 API |
| 2024 | `RevitMCP.2024.csproj` | 使用新版 API |

---

### 🧱 `.dll` - 編譯後的程式庫

**建築類比**：預鑄構件

**白話解釋**：
> 「這是工廠做好的預鑄件，  
> 直接運到工地就可以安裝使用，  
> 不需要在現場從零開始做。」

**重要觀念**：
- `.cs` 是設計圖（原始碼）
- `.dll` 是成品（可執行）
- 修改 `.cs` 後要「重新生產」才會更新 `.dll`

---

### 🎫 `.addin` - Revit 外掛設定

**建築類比**：進場許可證

```xml
<AddIn Type="Application">
  <Assembly>RevitMCP.dll</Assembly>
  <FullClassName>RevitMCP.Application</FullClassName>
  <AddInId>12345678-1234-1234-1234-123456789012</AddInId>
</AddIn>
```

**白話解釋**：
> 「這是給 Revit 看的許可證：  
> 有一個叫 RevitMCP 的團隊要進場，  
> 他們的工具在 RevitMCP.dll 裡面，  
> 這是他們的識別證號碼。」

---

### 📝 `.md` - Markdown 文件

**建築類比**：施工規範書、SOP 手冊

```markdown
# 防火等級檢查流程

## 步驟 1：取得樓層清單
使用工具：get_all_levels

## 步驟 2：查詢外牆
使用工具：query_elements
```

**白話解釋**：
> 「這是一本 SOP 手冊，  
> 寫給 AI 看的，告訴它：  
> 如果有人說要『檢查防火』，  
> 就照著這些步驟做。」

**重要觀念**：
- `domain/*.md` = 已驗證的工作流程
- AI 會讀這些文件來知道怎麼做

---

## 2.4 系統架構總圖

### 完整的建築專案組織圖

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│    👤 業主/建築師（你 + AI Client）                              │
│    ├── Claude Desktop / Gemini CLI / VS Code                   │
│    └── 說：「幫我檢查所有牆的防火時效」                           │
│                                                                 │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│    🏢 總工程師辦公室（MCP Server）                               │
│    ├── 位置：MCP-Server/                                        │
│    ├── 工作手冊：.ts / .js 檔案                                 │
│    ├── 設定表：.json 檔案                                       │
│    └── 職責：                                                   │
│        1. 接收業主指令                                          │
│        2. 查找合適的工具                                        │
│        3. 翻譯成工地聽得懂的話                                   │
│        4. 回報執行結果                                          │
│                                                                 │
└───────────────────────────┬─────────────────────────────────────┘
                            │ WebSocket（對講機）
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│    🏗️ 工地現場（Revit Add-in）                                  │
│    ├── 位置：MCP/                                               │
│    ├── 施工手冊：.cs 檔案                                       │
│    ├── 預鑄構件：.dll 檔案                                      │
│    ├── 進場許可：.addin 檔案                                    │
│    └── 職責：                                                   │
│        1. 接收總工程師指令                                       │
│        2. 調用 Revit API 執行                                   │
│        3. 回報施工結果                                          │
│                                                                 │
└───────────────────────────┬─────────────────────────────────────┘
                            │ Revit API
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│    🏛️ 建築成品（Revit 模型）                                    │
│    └── 你的 .rvt 專案檔案                                       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2.5 檔案位置總覽

### 專案資料夾結構

```
REVIT_MCP_study/
│
├── MCP/                          🏗️ 工地現場
│   ├── Core/
│   │   ├── CommandExecutor.cs        ← 施工手冊（核心）
│   │   └── SocketService.cs          ← 對講機接聽
│   ├── RevitMCP.csproj               ← 合約書（2022/2023）
│   ├── RevitMCP.2024.csproj          ← 合約書（2024）
│   ├── RevitMCP.addin                ← 進場許可證
│   └── bin/Release/
│       └── RevitMCP.dll              ← 預鑄構件
│
├── MCP-Server/                   🏢 總工程師辦公室
│   ├── src/
│   │   ├── index.ts                  ← 總指揮
│   │   ├── socket.ts                 ← 通訊管理
│   │   └── tools/
│   │       └── revit-tools.ts        ← 工具清單
│   ├── build/                        ← 編譯後的成品
│   └── package.json                  ← 材料清單
│
├── domain/                       📚 施工規範書庫
│   ├── fire-rating-check.md          ← 防火檢查 SOP
│   ├── floor-area-review.md          ← 容積檢討 SOP
│   └── ...
│
└── .vscode/
    └── mcp.json                  📋 設定表
```

---

## 2.6 通訊流程實例

### 當你說「幫我檢查防火」時發生什麼事？

```
時間軸
──────────────────────────────────────────────────────────────────

T+0s    你：「幫我檢查這個案子的防火等級」
        │
        ▼
T+0.1s  AI Client 收到訊息
        ├── 讀取 domain/fire-rating-check.md
        └── 知道要執行哪些步驟
        │
        ▼
T+0.2s  AI Client 發送工具請求
        └── 調用 get_all_levels
        │
        ▼
T+0.3s  MCP Server 收到請求
        ├── 驗證參數
        └── 轉發給 Revit Add-in
        │
        ▼
T+0.5s  Revit Add-in 收到請求
        ├── 調用 Revit API
        └── 取得樓層清單
        │
        ▼
T+0.6s  結果回傳
        │
        ▼
T+0.7s  AI：「這個專案有 1F, 2F, 3F 三個樓層，
              接下來我會逐一檢查每層的牆體...」
```

---

## 📝 本堂重點

1. **系統就像建築專案**：業主 → 總工程師 → 工地 → 成品
2. **每種檔案都有意義**：
   - `.json` = 設定/聯絡簿
   - `.ts/.js` = 總工程師手冊
   - `.cs` = 工地施工手冊
   - `.dll` = 預鑄構件
   - `.addin` = 進場許可
   - `.md` = SOP 規範書
3. **版本差異**：2024 和 2022/2023 的 API 有差異，對應不同 .csproj
4. **通訊靠 WebSocket**：像對講機一樣即時雙向

---

## ▶️ 下一步

理解了系統架構，讓我們進入 [第三堂：安裝與環境設定](./03-安裝篇.md)！
