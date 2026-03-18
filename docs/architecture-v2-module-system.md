# Architecture V2: Module System

## 問題陳述

隨著社群貢獻增加，兩個核心檔案持續膨脹：

- `MCP/Core/CommandExecutor.cs` — 3000+ 行，所有 C# 命令集中在一個檔案
- `MCP-Server/src/tools/revit-tools.ts` — 900+ 行，所有 MCP 工具定義集中在一個檔案

造成的問題：

| 問題 | 影響 |
|------|------|
| 多人 PR 同時修改相同檔案 | 合併衝突頻繁 |
| AI Client 每次載入全部工具定義 | Context 肥大（100 工具 ≈ 30K tokens） |
| Domain SOP 未被 LLM 參照 | 幻覺風險，不依標準流程執行 |
| 不同角色載入不相關工具 | 結構工程師不需要 MEP 工具 |

## 解決方案：三層模組架構

```
Layer 1: Skill 觸發層（輕量路由）
  ↓ 命中關鍵字後載入
Layer 2: Domain SOP 層（工作流程指引）
  ↓ 指定呼叫哪些工具
Layer 3: MCP Tools 執行層（預建工具）
  ↓ WebSocket
Revit API
```

### 每個功能模組的完整結構

```
.claude/skills/{module}/skill.md     ← Skill 觸發定義
domain/{module}.md                   ← Domain SOP（法規/流程）
MCP-Server/src/tools/{module}-tools.ts  ← MCP 工具 schema
MCP/Core/Commands/{Module}Commands.cs   ← C# Revit API 實作
```

### Token 效益

| 方案 | 每輪 Context 成本 |
|------|-------------------|
| 現在：全量載入 | ~35K tokens（100 工具 + CLAUDE.md） |
| Module：Profile 篩選 + Skill 觸發 | ~5K tokens（20 工具 + 命中的 domain） |

## 技術實作

### TypeScript 端：模組化工具定義 + Profile 篩選

```
MCP-Server/src/tools/
├── index.ts              ← registerRevitTools() 匯總 + Profile 篩選
├── base-tools.ts         ← 基礎工具（所有角色都需要）
├── wall-tools.ts         ← 牆/門窗/結構
├── room-tools.ts         ← 房間/採光/法規
├── visualization-tools.ts ← 圖形覆寫/視圖樣版
├── schedule-tools.ts     ← 明細表
├── mep-tools.ts          ← MEP 管線工具
├── curtain-wall-tools.ts ← 帷幕牆面板（PR#11）
└── smoke-exhaust-tools.ts ← 排煙窗檢討（PR#12）
```

Profile 透過環境變數 `MCP_PROFILE` 控制：

```json
{
  "mcpServers": {
    "revit-mcp": {
      "command": "node",
      "args": ["path/to/build/index.js"],
      "env": { "MCP_PROFILE": "architect" }
    }
  }
}
```

| Profile | 載入的模組 |
|---------|-----------|
| `full`（預設） | 全部 |
| `architect` | base + wall + room + visualization + schedule |
| `mep` | base + mep + smoke-exhaust + schedule |
| `structural` | base + wall（結構部分）+ visualization |
| `fire-safety` | base + room + smoke-exhaust + visualization |

### C# 端：Partial Class 拆分

```
MCP/Core/
├── CommandExecutor.cs                    ← 路由 switch + 共用 helper
├── Commands/
│   ├── CommandExecutor.CurtainWall.cs    ← partial class: 帷幕牆
│   ├── CommandExecutor.SmokeExhaust.cs   ← partial class: 排煙窗
│   └── （未來新增模組放這裡）
```

選擇 `partial class` 而非獨立類別的原因：
- 共用 `_uiApp`、`FindLevel()` 等成員不用改
- 對外介面（switch 路由）完全不變
- **現有 Fork 使用者的修改仍然有效**

### 遷移策略（對 Fork 的影響最小化）

```
Phase 1（本次）：
  ✅ 拆分 revit-tools.ts → 模組檔案（保留 re-export 相容層）
  ✅ CommandExecutor.cs 加 partial keyword
  ✅ 新工具（PR#11, #12）直接用新結構
  ✅ MCP_PROFILE 環境變數支援

Phase 2（後續）：
  將現有方法逐步搬到 partial class 檔案
  每次搬一個 region，確認功能正常

Phase 3（觀察 MCP 協議演進）：
  動態工具載入（如果 Client 支援）
  Handshake 自動偵測專案類型
```

## 貢獻者指南

### 新增功能模組的 Checklist

1. 建立 Skill 定義：`.claude/skills/{module}/skill.md`
2. 建立 Domain SOP：`domain/{module}.md`
3. 建立 MCP 工具定義：`MCP-Server/src/tools/{module}-tools.ts`
4. 建立 C# 實作：`MCP/Core/Commands/CommandExecutor.{Module}.cs`
5. 在 `CommandExecutor.cs` 的 switch 加入 case
6. 在 `tools/index.ts` 的 profile 對照表加入模組
7. 使用 `IdType` / `GetIdValue()` 確保跨版本相容（Revit 2022-2026）

### 不要修改的檔案

- ❌ 不要修改現有 tools 模組檔案（除非修 bug）
- ❌ 不要修改 `package.json` / `package-lock.json`（SDK 升級由 maintainer 處理）
- ❌ 不要提交 `bin/`、`obj/`、scratch 檔案

### 搭配 Git Worktree 開發

```bash
git worktree add ../wt-新功能 -b feat/新功能
cd ../wt-新功能

# 只需建立自己模組的檔案，不碰共用大檔案
# → PR 零衝突
```

## 相關 PR

- PR#11 帷幕牆面板（@7alexhuang-ux）→ 整合為 curtain-wall 模組
- PR#12 排煙窗檢討（@7alexhuang-ux）→ 整合為 smoke-exhaust 模組
- PR#13 明細表建立（@CyberPotato0416）→ 已整合到 schedule + mep 模組
- PR#14 Domain Workflows（@Jacky820507）→ 已轉換為 Skills
