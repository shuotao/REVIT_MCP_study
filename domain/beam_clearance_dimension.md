---
description: 標註梁下淨高 (Beam Clearance Dimensioning)
---

# 標註梁下淨高工作流程 (Beam Clearance Dimensioning Workflow)

當使用者請求「在剖面視圖建立最低處的梁下淨高尺寸標註」時，AI 代理必須遵循以下知識與步驟，禁止擅自省略：

## 1. 確認標註目標樓層 (詢問使用者)
剖面視圖中通常會同時出現多個樓層的梁。**除非使用者已經在提示詞中明確指定了樓層**，否則 AI **必須先詢問使用者**：「請問您想要標註哪一個樓層的梁？」(例如：1F, GL, B1F 等)。不可預設為 GL。

## 2. 搜尋與定位最低梁
取得使用者指定的樓層（目標樓層）後，透過 MCP Server 查詢該視圖中所有 `Structural Framing`：
- 篩選 `Reference Level` 為使用者目標樓層的梁。
- 比較 `Elevation at Bottom` (底部高程)，找出數值最小（最低）的那一根梁。

## 3. 尋找「下層樓層線」(Level Below)
**關鍵知識**：梁下淨高是指「該梁底面」與「其**下方**樓層」之間的距離。
- 例如：若梁屬於 `GL`，則淨高標註的底端應該對齊 `B1F` 樓層線。
- 做法：透過 MCP Server 取得專案中所有的樓層 (`Levels`)，依據 `Elevation` (高程) 由低到高排序。
- 找出高程**緊鄰於目標樓層下方**的那一個樓層，作為我們的標註底線 (Level Below)。

## 4. 建立尺寸標註
呼叫 MCP 工具 `mcp_revit-mcp-2025_create_beam_clearance_dimension`：
- `viewId`: 目前剖面視圖的 ID
- `beamId`: 步驟 2 找出的那根最低梁的 ID
- `levelId`: 步驟 3 找出的「下層樓層」的 ID (注意：不可傳入梁所在的樓層 ID！)
