# Handoff — PR #42 / #43 合併與驗證（核心熱重載框架）

> **狀態**：Mac 端已完成研究與準備，等 Windows 端接手驗證 + 合併。
> **接手對象**：開 Revit 的 Windows 環境（人類或 AI session 皆可）。
> **預估時間**：建置 5 min + Revit 驗證 5 min + 合併與留言 5 min ≈ 15 min。

## 1. PR 概覽

| | PR #42 | PR #43 |
|---|---|---|
| 標題 | feat(core): implement core-reload framework with cross-version compatibility 熱重載核心邏輯 | feat(infra): update deployment scripts, documentation and vscode config 熱重載基礎設施與文件 |
| 作者 | @ChimingLu | @ChimingLu |
| 分支 | `feat/core-reload-logic` → `main` | `feat/infra-and-docs` → `main`（依賴 #42） |
| 變更量 | 10 files, +711 / −55 | 21 files, +906 / −573 |
| mergeable | true（無檔案衝突） | true |
| CI | ❌ check-pr fail（治理問題，見 §3） | ❌ 同上 |

**作者已自行測試 R20–R26 全版本通過**（提供完整 log 與 Revit 2025 截圖佐證），並在發送 PR 前先諮詢資深維護者，依其建議將原本一個大 PR 拆成 #42（核心程式）與 #43（基礎設施/文件）兩個獨立主題。

## 2. 架構摘要

### #42 — 三層解耦的熱重載核心
- **MCP.Contracts**（`netstandard2.0`，新 csproj）：介面層，定義 `IRevitMcpRuntime`
- **MCP.CoreRuntime**（新 csproj）：業務邏輯隔離，可被卸載/重載
- **MCP**（既有 Loader）：仍是 Revit Add-in 入口，新增 `CoreRuntimeManager` 統一生命週期

雙軌熱重載：
- Revit 2025+（.NET 8）：`CoreLoadContext : AssemblyLoadContext`，`isCollectible: true` → 真正卸載
- Revit 2020–2024（.NET FX 4.8）：Shadow-Copy 把 DLL 複製到 `%TEMP%` 載入 → 解決檔案鎖定

### #43 — 部署與文件補完
- `setup.ps1` / `install-addon.ps1` 支援三層 DLL 部署
- `smoke-test-version.ps1` 多版本驗證
- `.gitignore` 清理 bin/obj 誤追蹤
- `docs/core-reload-architecture.md`（341 行設計文件）
- `domain/core-reload-boundary.md`（熱重載邊界 SOP，符合 frontmatter 標準）
- `.vscode/mcp.json` `"servers"` → `"mcpServers"` 修正

## 3. CLAUDE.md 憲章合規檢查（PASS）

| 規則 | 結果 | 證據 |
|---|---|---|
| Deployment Rules — 不可新增 version-specific `.csproj`/`.addin` | ✅ | 新增的是 `MCP.Contracts.csproj` / `MCP.CoreRuntime.csproj`，非版本特異；`RevitMCP.addin` 維持唯一 |
| Deployment Rules — `<DeployAddin>false</DeployAddin>` | ✅ | `MCP.CoreRuntime.csproj` 設定正確 |
| Deployment Rules — 不可巢狀 `MCP/MCP/` | ✅ | 新專案在 sibling 目錄 |
| Tool 鏈 Guard Rails — 不可自寫 WebSocket | ✅ | `CoreRuntimeManager` 走 `ExternalEvent` |
| Skill/Domain 規範 | ✅ | `domain/core-reload-boundary.md` 含 frontmatter |
| Tool 雙端一致 | ✅ N/A | 無新增 MCP tool；`reload_core` 由 ExternalEvent 觸發 |
| Commit message conventional 格式 | ✅ | `feat(core): ...` `feat(infra): ...` `chore: ...` |

**技術層面所有憲章規則都通過。** 唯一阻礙是治理層：

`.github/workflows/check-pr.yml` 對外部 PR 套用嚴格白名單（`allowedPaths = ['domain/', 'GEMINI.md']`），ChimingLu 的 PR 觸碰了 `MCP/`、`MCP.Contracts/`、`MCP.CoreRuntime/`、`scripts/`、`docs/`、`.vscode/`、`.gitignore` → 被擋下。這是**設計**好的學員貢獻護欄，不是 bug。`CODEOWNERS` 也對應該政策（`MCP/`、`scripts/` 為 owner-reviewed only）。

## 4. 採用策略：admin merge + 最小驗證 + 規則明文化

不選「owner 自己 squash 重做」的原因：
1. PR 拆分結構是資深 review 後的成果，squash 會破壞它
2. 作者已實機驗證 R20–R26，風險已大幅下降
3. 強制 squash 會給未來貢獻者「即使做對也會被改寫」的負面訊號

不選「永久放寬 check-pr.yml」的原因：
1. 護欄存在的價值（讓多數學員集中在 `domain/`）仍應保留
2. 把破例條件明文化，比放寬規則更精準

**採用：一次性 admin override + 同步 commit 破例條款進憲章**。

## 5. Mac 端已完成（截至本檔案 commit）

- ✅ 在本地 git 加 `chiminlu` remote 並 fetch 兩個分支
- ✅ 建立本地 `verify/pr42`、`verify/pr43` 分支
- ✅ 寫好 Windows 端驗證腳本：`scripts/scratch/verify-pr42-pr43.ps1`
- ✅ 本檔案 push 到 main，Windows 端 `git pull` 即可讀

## 6. Windows 端接手步驟

### Step 1 — 同步本地與遠端

```powershell
cd Y:\0-GitHub\RevitMCP\REVIT_MCP_study
git fetch origin
git pull origin main
git fetch chiminlu feat/core-reload-logic feat/infra-and-docs
# 若沒有 chiminlu remote，先加：
# git remote add chiminlu https://github.com/ChimingLu/REVIT_MCP_study.git
git checkout verify/pr42 2>$null; if ($LASTEXITCODE -ne 0) { git checkout -b verify/pr42 chiminlu/feat/core-reload-logic }
git checkout verify/pr43 2>$null; if ($LASTEXITCODE -ne 0) { git checkout -b verify/pr43 chiminlu/feat/infra-and-docs }
```

### Step 2 — 跑驗證腳本

```powershell
.\scripts\scratch\verify-pr42-pr43.ps1
```

腳本會：建置 R24 → 部署 → 提示你開 Revit 2024 → 確認「Core 重載」按鈕功能 → 合併 #43 → 跑 setup/preflight。

- **PASS** → 繼續 Step 3
- **FAIL** → 在 PR 留言告知 ChimingLu 具體錯誤，**不要合併**

### Step 3 — Admin merge #42

```powershell
gh pr merge 42 --repo shuotao/REVIT_MCP_study --admin --merge `
  --subject "feat(core): implement core-reload framework with cross-version compatibility 熱重載核心邏輯 (#42)" `
  --body "Approved by owner after local verification on Revit 2024 (.NET FX) and reviewed against CLAUDE.md governance rules.`n`nOriginal work by @ChimingLu, PR split per senior maintainer's review guidance.`nBypassing check-pr.yml policy (external core-code contribution exception) because:`n1. Author consulted senior maintainer before submission`n2. Author tested R20-R26 with documented evidence (log + screenshot)`n3. PR split structure preserved per senior review`n`nLocal verification: dotnet build Release.R24 + Revit 2024 + Core 重載 button works."
```

### Step 4 — 等 #43 base 自動更新後合併

```powershell
# 等 30 秒讓 GitHub 偵測 #43 base 更新
Start-Sleep -Seconds 30
gh pr view 43 --repo shuotao/REVIT_MCP_study --json mergeable,mergeable_state
# 確認 mergeable=true 後
gh pr merge 43 --repo shuotao/REVIT_MCP_study --admin --merge `
  --subject "feat(infra): update deployment scripts, documentation and vscode config 熱重載基礎設施與文件 (#43)" `
  --body "Follow-up to #42 with deployment scripts, docs and SOP. Same exception rationale applies."
```

### Step 5 — 留致謝 comment（兩個 PR 都做）

```powershell
gh pr comment 42 --repo shuotao/REVIT_MCP_study --body "感謝 @ChimingLu 的高品質貢獻：諮詢資深維護者 → 拆分主題 → R20-R26 全版本實測 → 提交 PR，這是專案標準流程的示範。本次 admin override 是基於你提供的測試證據（log + screenshot），未來想貢獻 core 區的學員可以參考這個流程。"

gh pr comment 43 --repo shuotao/REVIT_MCP_study --body "感謝 @ChimingLu 補上完整的部署腳本與 SOP 文件。core-reload-architecture.md 的 341 行設計記錄對未來維護者很有價值。"
```

### Step 6 — Commit CLAUDE.md 破例條款

打開 `CLAUDE.md`，找到 `## CODEOWNERS` 章節（約 399 行），在現有兩條 bullet 之後追加：

```markdown

### Core 區的破例條款（Exception Clause）

外部貢獻者觸碰 core 區（`MCP/`、`scripts/`、`MCP-Server/src/`）時，check-pr CI 會擋下。
Owner 可以走 `gh pr merge --admin` 破例合併，但 PR 必須**同時**滿足以下三項：

1. **諮詢過資深維護者**（在 PR 描述或 commit message 引用對話紀錄）
2. **提供多版本實測證據**（log + screenshot，至少涵蓋一個 .NET FX 與一個 .NET 8 版本）
3. **拆分為獨立主題的 PR**（避免單個 PR 同時動 core code 與 infrastructure）

不滿足三項的 core 區 PR 仍會依政策由 owner 自行重做或請貢獻者改投 `domain/`。
首次援引此條款：PR #42 / #43（ChimingLu 熱重載框架，2026-04-25）。
```

```powershell
git checkout main
git pull origin main  # 拉回剛 admin merge 的兩個 PR
git add CLAUDE.md
git commit -m "docs(constitution): 新增 core 區破例條款（基於 PR #42/#43 經驗）"
git push origin main
```

### Step 7 — 收尾

```powershell
# 清掉一次性檔案
git rm scripts/scratch/verify-pr42-pr43.ps1 docs/handoff-pr-chiminlu.md
git commit -m "chore: cleanup PR #42/#43 handoff artifacts"
git push origin main

# 清本地驗證分支
git checkout main
git branch -D verify/pr42 verify/pr43
git remote remove chiminlu  # optional
```

## 7. 驗證清單

- [ ] Step 2 驗證腳本完整通過（含 Revit 2024 點擊「Core 重載」成功）
- [ ] PR #42 在 GitHub 顯示 merged，`git log` 看得到 ChimingLu 為 commit author
- [ ] PR #43 同上
- [ ] 兩個 PR 都有致謝 comment
- [ ] CLAUDE.md 「Core 區破例條款」已 commit + push
- [ ] 一次性 handoff 檔案已清掉
- [ ] `/qaqc` 跑一次確認 `domain/core-reload-boundary.md` frontmatter 通過

## 8. 風險與後備

- **驗證腳本失敗**：在 PR 留言告知 ChimingLu 具體錯誤訊息，請其修正；**不要 admin merge 一個沒驗證過的 PR**
- **#43 在 #42 合併後出現衝突**：先 `gh pr comment 43 "請 rebase 到最新 main"` 等作者處理，再合
- **破例條款被認為太寬鬆**：可加上「需 owner 在 issue 中明確核准」這條前置，更嚴格
- **若想完全回滾**：`git revert` 兩個 merge commit；admin merge 不會自動 close PR 之外的副作用

## 9. 不要做的事

- ❌ 在 Mac 端執行 `gh pr merge --admin`（沒驗證過不能合）
- ❌ 在 Mac 端執行 `gh pr review --approve`
- ❌ 在 Mac 端 commit CLAUDE.md 破例條款（PR 還沒合就改規則邏輯倒置）
- ❌ 用 `--squash` 合 #42 / #43（會吞掉 senior review 的拆分意圖）
- ❌ Close PR 後另行 push owner 重做版（會丟掉作者的 GitHub merged 紀錄）

---

**完成所有步驟後，本檔案 (`docs/handoff-pr-chiminlu.md`) 與 `scripts/scratch/verify-pr42-pr43.ps1` 即可刪除——它們是一次性接力檔案。**
