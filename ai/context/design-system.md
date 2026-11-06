# 設計系統（Design System）

由 Epic 0「專案設置」的「UI 設計系統」User Story 分五階段（框架 → 風格 → design token → 元件庫 → 版面）逐步填寫。**這份文件是後續所有功能 Epic 做 UI 時的單一事實來源**：任何前端任務開工前都要先讀它，能用既有 token／元件就必須用；缺的元件要照既有風格補做並登記回這裡（見 `ai/skills/project-kickoff.md` 步驟 6 與 `ai/skills/ui-mockup-gate.md`）。

狀態：範本佔位符（尚未展開設計）。

## S1 底層框架

- UI 框架：待補（例如 React / Vue / SvelteKit）
- 元件庫策略：待補（採用現成 / 自建；若採用，列出來源如 shadcn/ui、MUI、Ant Design）
- 樣式方案：待補（例如 Tailwind、CSS-in-JS、CSS Modules）
- 選定理由：待補
- 人工核准：待補（核准人／日期）

## S2 風格方向

- 選定的 style tile：待補（變體名稱）
- 色彩情緒：待補
- 字體個性：待補
- 圓角／陰影傾向：待補
- 密度：待補（緊湊 / 舒適）
- 亮／暗模式：待補
- 參考產品：待補
- 人工核准：待補（核准人／日期）

## S3 Design Token 清單

### Primitive Token

| 類別 | Token | 值 | 備註 |
|---|---|---|---|
| 色彩 | 待補 | 待補 | 完整色票（含各階明度） |
| 字級 | 待補 | 待補 | type scale |
| 字重／行高 | 待補 | 待補 | |
| 間距 | 待補 | 待補 | spacing scale |
| 圓角 | 待補 | 待補 | |
| 陰影 | 待補 | 待補 | |
| z-index | 待補 | 待補 | |
| 動效 | 待補 | 待補 | 時間與曲線 |

### Semantic Token

| Token | 對應 primitive | 用途 |
|---|---|---|
| color.primary | 待補 | 待補 |
| color.surface | 待補 | 待補 |
| color.danger | 待補 | 待補 |
| space.page | 待補 | 待補 |

### 實際 token 檔位置

- 專案內真實 token 檔路徑：待補（例如 `src/styles/tokens.css`、`tailwind.config.ts` 主題、`theme.ts`）。若專案尚無可跑框架，可留空，僅以上表為準。
- 人工核准：待補（核准人／日期）

## S4 元件庫 Inventory

每做一個核心元件就登記一列。後續 Epic 缺元件、照風格補做後也要回來補登。

| 元件 | 狀態 | 涵蓋狀態 | 用到的 token | 檔案位置 | 截圖 | 來源階段 |
|---|---|---|---|---|---|---|
| Button | 待補 | 預設/hover/focus/停用/載入 | 待補 | 待補 | 待補 | S4 |
| Input | 待補 | 預設/focus/停用/錯誤 | 待補 | 待補 | 待補 | S4 |
| Select | 待補 | 待補 | 待補 | 待補 | 待補 | S4 |
| Card | 待補 | 待補 | 待補 | 待補 | 待補 | S4 |
| Nav | 待補 | 待補 | 待補 | 待補 | 待補 | S4 |
| Modal/Dialog | 待補 | 待補 | 待補 | 待補 | 待補 | S4 |
| Table | 待補 | 待補 | 待補 | 待補 | 待補 | S4 |
| Form | 待補 | 待補 | 待補 | 待補 | 待補 | S4 |
| Toast/Alert | 待補 | 待補 | 待補 | 待補 | 待補 | S4 |

（「來源階段」記錄這個元件是 S4 初建，還是後續某個功能 Epic 補做並回登的。）

## S5 各介面版面

| 介面／使用者端 | 選定版型 | Mockup 決策紀錄 | 人工核准 |
|---|---|---|---|
| 待補（例如 管理員後台） | 待補 | 連結到 `ai/artifacts/<Epic>/mockup-decision-<畫面>.md` | 待補 |
| 待補（例如 顧客前台） | 待補 | 待補 | 待補 |
