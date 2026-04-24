# 開會討論策略文件：PR #30 + PR #32

> **日期**：待定（建議 2026-04-21 ~ 04-25 之間）
> **參與者**：@shuotao（維護者）、@lesleyliuke（PR #30）、@ChimingLu（PR #32）
> **目的**：釐清合併策略、風險評估、責任分工
> **產出本文件的 Agent**：Claude Code (Opus 4.6)，其他 GenAI 可直接接續使用

---

## PR #30 — feat: 大型功能包（@lesleyliuke）

### 基本資訊
- **規模**：+3208/-12 行，24 檔案，7 commits
- **衝突狀態**：MERGEABLE（但需先 rebase，因 PR #34 已合併）
- **實質功能**：捆綁了 4 個獨立功能模組

### 討論課題一覽

#### 課題 1：PR 拆分策略
**現狀**：單一 PR 包含 4 個獨立功能：
1. **view-category-visibility**（3 tools + 1 Skill）— 元素隱藏/顯示控制
2. **excel-to-legend**（3 tools + 1 Skill）— Excel 匯入 Drafting View
3. **text-note-batch + scale-drafting**（2 tools + 2 Skills）— TextNote 批次操作 + 縮放
4. **viewport-arrangement**（0 new tools + 1 Skill）— 視埠排列管理

**需要討論**：
- [ ] 作者是否同意拆分為 3-4 個子 PR？
- [ ] 如果不拆分，是否接受分階段 review（先 C# 核心、後 Skills）？
- [ ] 拆分的優先順序為何？（建議：① view-category-visibility → ② excel-to-legend → ③ 其餘）

#### 課題 2：.claude/settings.json 與 hooks 的影響
**現狀**：PR 新增了：
- `.claude/hooks/preload-revit-tools.sh` — UserPromptSubmit hook，偵測 Revit/Excel 關鍵字提醒 AI 載入 deferred tools
- `.claude/settings.json` 修改 — 註冊上述 hook

**風險**：
- 這些檔案影響所有使用 Claude Code 的開發者
- Hook 腳本的觸發頻率和效能影響未知
- 其他 AI Client（Gemini CLI、VS Code Copilot）不受影響但也無法受益

**需要討論**：
- [ ] 這個 hook 是否應該放在 `.claude/settings.local.json`（個人設定）而非 `.claude/settings.json`（共用設定）？
- [ ] Hook 的觸發邏輯是否過於激進（每次 prompt 都跑 shell 腳本）？
- [ ] 是否有更輕量的替代方案（例如在 Skill frontmatter 中聲明 tool dependencies）？

#### 課題 3：ClosedXML 依賴引入
**現狀**：excel-to-legend 功能在 `RevitMCP.csproj` 中加入了 ClosedXML 0.104.2 NuGet 套件。

**風險**：
- 增加 DLL 大小和部署複雜度
- ClosedXML 的 transitive dependencies 可能與 Revit 的 .NET 環境衝突
- Revit 2022-2024（.NET Framework 4.8）和 2025-2026（.NET 8）的相容性需驗證

**需要討論**：
- [ ] ClosedXML 在 .NET Framework 4.8 上是否正常運作？
- [ ] 是否需要把 Excel 讀取做成可選模組（非核心依賴）？
- [ ] 部署時是否需要額外 copy ClosedXML.dll 到 Addins 資料夾？

#### 課題 4：issues/ 目錄的處理
**現狀**：PR 新增了 `issues/claude-md-wrong-build-output-path.md` 檔案。

**問題**：本專案使用 GitHub Issues 追蹤問題，不應在 repo 中用檔案方式追蹤。

**需要討論**：
- [ ] 是否要求移除 `issues/` 目錄？
- [ ] 檔案內容是否需要轉移到 GitHub Issue？

#### 課題 5：CLAUDE.md 修改的同步問題
**現狀**：PR 修改了 CLAUDE.md，將 Skills 數量從 18 更新為 22（新增 4 個 Skills）、更新工具數量。

**問題**：當前 main 的 CLAUDE.md 記載 18 個 Skills，此 PR 的修改基於較舊的 main。

**需要討論**：
- [ ] Rebase 後需重新同步 CLAUDE.md
- [ ] 合併後需執行 `/claude-md-sync` 驗證

---

## PR #32 — feat: Loader/Core 熱重載架構（@ChimingLu）

### 基本資訊
- **規模**：+779/-63 行，13 檔案，3 commits
- **衝突狀態**：MERGEABLE
- **架構影響**：新增 2 個子專案，修改入口點 Application.cs

### 討論課題一覽

#### 課題 1：Deployment Rules 相容性
**現狀**：CLAUDE.md 明確規定：
> - **ONE** `.csproj`: `MCP/RevitMCP.csproj`
> - **DO NOT** create version-specific `.csproj` files

PR #32 新增了：
- `MCP.Contracts/MCP.Contracts.csproj`
- `MCP.CoreRuntime/MCP.CoreRuntime.csproj`

**需要討論**：
- [ ] 這是否違反 Deployment Rules？還是屬於「合理例外」（這些是子專案，不是版本特定的 csproj）？
- [ ] 如果接受，Deployment Rules 需要如何修改？
  - 建議措辭：「ONE 主專案 .csproj + 允許 Contracts/Runtime 子專案」
- [ ] Solution 檔案（.sln）是否需要更新？

#### 課題 2：多版本相容性
**現狀**：
- `CoreLoadContext` 繼承自 `AssemblyLoadContext`（.NET Core/.NET 5+ API）
- Revit 2022-2024 使用 .NET Framework 4.8，**不支援 AssemblyLoadContext**
- 目前僅驗證 R26（.NET 8）

**需要討論**：
- [ ] 熱重載是否僅限 R25/R26（.NET 8）？
- [ ] R22-R24 的 fallback 策略為何？
  - 選項 A：R22-R24 完全不載入 Loader/Core，維持傳統模式
  - 選項 B：R22-R24 使用 AppDomain 替代方案（複雜度高）
  - 選項 C：明確標示為 R26-only 功能，不做跨版本支援
- [ ] `#if NET8_0_OR_GREATER` 條件編譯是否已正確使用？

#### 課題 3：Application.cs 的改動範圍
**現狀**：PR 對 `Application.cs`（Revit 入口點）做了 +68/-52 的修改。

**風險**：
- Application.cs 是 `IExternalApplication` 的實作，是整個 Add-in 的啟動點
- 錯誤的修改會導致 Add-in 完全無法載入
- 影響所有使用者，不僅僅是開發者

**需要討論**：
- [ ] 改動是否都在條件編譯區塊內（僅 R26 生效）？
- [ ] 是否有 fallback 機制（Core 載入失敗時是否能退回傳統模式）？
- [ ] OnStartup/OnShutdown 的改動是否影響現有 Ribbon 按鈕？

#### 課題 4：install-addon.ps1 部署流程
**現狀**：PR 修改了 `scripts/install-addon.ps1`（+55/-9），調整部署邏輯。

**需要討論**：
- [ ] 新的部署流程是否向後相容？
- [ ] 是否需要額外 copy MCP.Contracts.dll 和 MCP.CoreRuntime.dll？
- [ ] 現有的 `/deploy-addon` Skill 是否需要同步更新？

#### 課題 5：合併路徑與驗證計劃
**已在 Issue #33 提出的條件**：

**需要討論**：
- [ ] 是否接受先合併至 `develop` 分支？
- [ ] 多版本驗證的責任人和時程：
  - R24 驗證：由誰負責？預計何時完成？
  - R25 驗證：由誰負責？預計何時完成？
- [ ] 從 develop 合併至 main 的 gate 條件為何？
- [ ] 驗證通過前，其他 PR 是否基於 main 而非 develop 開發？

---

## 會議執行建議

### 議程安排（建議 60 分鐘）

| 時間 | 議題 | 參與者 |
|------|------|--------|
| 0-5 min | 開場：目前 main 分支狀態、已合併 PR 回顧 | All |
| 5-25 min | PR #30 五大課題逐一討論 | @shuotao + @lesleyliuke |
| 25-45 min | PR #32 五大課題逐一討論 | @shuotao + @ChimingLu |
| 45-55 min | 跨 PR 依賴：PR #30 的 csproj 改動 vs PR #32 的子專案結構 | All |
| 55-60 min | Action items 確認、下次 check-in 時間 | All |

### 會前準備（請作者先完成）

**@lesleyliuke（PR #30）**：
1. Rebase onto latest main（PR #34 已合併，有 4 個檔案衝突需解決）
2. 準備說明 ClosedXML 在 .NET Framework 4.8 的測試結果
3. 考慮 PR 拆分方案

**@ChimingLu（PR #32）**：
1. 準備 R24 或 R25 的測試報告（即使是失敗報告也有價值）
2. 確認 Application.cs 改動是否在條件編譯區塊內
3. 準備 Core 載入失敗的 fallback 機制說明

### 會後 Action Items 模板

| # | Action | Owner | Deadline | Status |
|---|--------|-------|----------|--------|
| 1 | PR #30 rebase + 解衝突 | @lesleyliuke | | ⬜ |
| 2 | PR #30 拆分（若同意） | @lesleyliuke | | ⬜ |
| 3 | 移除 issues/ 目錄 | @lesleyliuke | | ⬜ |
| 4 | PR #32 多版本驗證 | @ChimingLu | | ⬜ |
| 5 | 更新 Deployment Rules | @shuotao | | ⬜ |
| 6 | 建立 develop 分支 | @shuotao | | ⬜ |

---

## 跨 GenAI 接續指引

本文件可供任何 AI Agent 讀取並協助後續作業：

1. **會前**：AI 可協助生成會議邀請、整理 PR diff 摘要
2. **會中**：AI 可即時查詢 PR 細節、產生程式碼比較
3. **會後**：AI 可依據 Action Items 執行技術任務（rebase、code review、CLAUDE.md sync）

### 接續 Checkpoint
- 若會議已結束，讀取本文件的「會後 Action Items」欄位
- 用 `gh pr view {N}` 確認 PR 最新狀態
- 按 Action Items 優先順序執行
