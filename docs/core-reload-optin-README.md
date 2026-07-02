# Core-Reload Opt-In 分支說明（`feature/core-reload-optin`）

> **分支性質**：opt-in 進階開發分支，**永不併入 main**。
> **知識來源**：啟銘（ChimingLu）的 Loader/Core 熱重載架構（fork `feat/DWSchedule`，2026-07-01）。
> **收編整理**：2026-07-02。
> **對應文件**：`docs/core-reload-architecture.md`、`domain/core-reload-boundary.md`、`/core-reload-dev` skill。

---

## 1. 這個分支是什麼？

這是把 CommandExecutor 核心邏輯拆成可**熱重載（不重啟 Revit）**的 Loader/Core 三層架構分支：

```
Revit 進程（常駐）
├── Loader 層   RevitMCP.dll               ← 隨 Revit 存活（重啟邊界）
├── Contracts 層 MCP.Contracts.dll         ← Loader↔Core 介面契約（netstandard2.0）
└── CoreRuntime 層 RevitMCP.CoreRuntime.dll ← shadow-copy 載入，可熱重載
```

改 `CommandExecutor.*.cs` 後，只要重建 `RevitMCP.CoreRuntime.dll` → 覆蓋 `runtime/` → 在 Revit 按「Core 重載」，新邏輯即生效，單次迭代由 90–180 秒縮短到約 10–25 秒。架構細節見 `docs/core-reload-architecture.md`；邊界與效益見 `domain/core-reload-boundary.md`；操作流程見 `/core-reload-dev` skill。

---

## 2. 對 main 憲法的 opt-in 偏差（預期，不是 bug）

main 維持「單一 `MCP/RevitMCP.csproj`」憲法。本分支**刻意**偏離下列項目，這些偏差是本分支存在的理由，`scripts/verify-qaqc.ps1` 若在此分支執行會對其中部分項目報 flag，屬**預期**：

| 偏差 | 說明 |
|---|---|
| 多專案結構 | 新增 `MCP.Contracts/`、`MCP.CoreRuntime/` 兩個子專案（main 只有一個 csproj）。QA/QC 的「forbidden version-specific csproj / 檔案結構」檢查會標記——這兩個子專案就是熱重載的核心，非違規。 |
| 工具數 105（非 main 的 103） | 本分支多兩支 opt-in 工具：`get_revit_version`、`get_recent_logs`（`get_recent_logs` 供 `core-reload-verify.js --full` 讀取 CoreRuntime 重載日誌）。故 QA/QC 的工具計數對照（103）不適用本分支。 |
| csproj `<Configurations>` 多列 R20/R21 | 啟銘的 csproj 於單一 `MCP/RevitMCP.csproj` 多列 `Debug/Release.R20`、`R21`（main 僅 R22–R26）。仍是**單一** csproj，未新增版本專屬 csproj/`.addin`，不觸犯「禁止版本專屬檔案」規則。 |
| SocketService 用 TcpListener（非 HttpListener） | 見下節。 |
| `.vscode/mcp.json` 用 `mcpServers`（非 main 的 `servers`） | main/CLAUDE.md 採 VS Code 官方 `servers` schema；本分支沿用啟銘的 `mcpServers`。切回 main 時以 main 版為準。 |

**未觸犯的憲法紅線（已核對）**：`MCP/RevitMCP.csproj` 為 `<DeployAddin>false</DeployAddin>`（非 true）；未新增第二份 `.addin`；未變更 `AddInId`；`scripts/install-addon.ps1` 未被本分支改動；`.gitignore` 的 `/vault/`、`/.obsidian/` 保護行完整保留。

---

## 3. TcpListener（取代 HttpListener）

本分支的 `MCP/Core/SocketService.cs` 是**原生 TCP WebSocket** 實作（`TcpListener` + 手動握手 + 手動 frame 解析），取代 main 的 `HttpListener` 版本：

- **Port**：綁 `ServiceSettings.Port`，預設 `8964`（`ServiceSettings.DefaultPort`），可由設定檔調整；與 main 相同的 8964 契約。
- **好處**：TcpListener 由 Revit 進程直接持有，Revit crash 時 OS 自動釋放 port，**根除了 HttpListener 的 HTTP.sys 孤兒 Request Queue 問題**（main 疑難排解表中「Port 8964 監聽=是但連線 timeout」那一列，在本分支不會發生，屬歷史議題）。
- **片段化收框**：`ReadExactAsync` 逐 frame 依 header 的 payloadLen 讀滿，已能處理單一 frame 的 TCP 分段；收編 main 時另補上 **FIN bit + opcode 0x0 續傳 frame 累積**，等同 main `MemoryStream+EndOfMessage` 迴圈的概念，處理 WebSocket 訊息層級的多 frame 片段化。

---

## 4. 如何建置（本機驗證）

### R26（.NET 8，Revit 2026）— 標準路徑

`MCP/RevitMCP.csproj` 內含 `BuildCoreRuntime` target，會在建置主專案**前**自動 `Restore;Build` `MCP.CoreRuntime.csproj`，並在建置後把 `RevitMCP.CoreRuntime.dll` 複製到 `bin/Release.R26/runtime/`：

```powershell
dotnet build -c Release.R26 MCP/RevitMCP.csproj
# 產物：MCP/bin/Release.R26/RevitMCP.dll
#       MCP/bin/Release.R26/runtime/RevitMCP.CoreRuntime.dll
```

2026-07-02 於本機驗證 **0 errors**（warnings 皆為 nullable 註記雜訊）。

> **收編修正**：`MCP.CoreRuntime.csproj` 以明確 `<Compile Include>` 白名單編入核心邏輯。收編 main 後，`CommandExecutor.cs` 新參照到的分部類與 helper（BeamPenetration、DoorWindowLegend、ElementOps、FinishLegend、Level、RoomSurface、ViewOps；ClashDetector、LinkedModelHelper、DwgColumnExecutor、DwgBeamExecutor、FloorSlopeAnalyzer）原本不在清單中，R26 會以 CS0103/CS0246 失敗——已於本分支補齊。日後在 main 新增 CommandExecutor 分部類/helper 時，記得同步加進本 csproj 的清單。

### R22–R24（.NET Framework 4.8）— 需 net48 targeting pack

Loader 設計為**跨 runtime**：`CoreLoadContext`/真正 `AssemblyLoadContext.Unload()` 走 `#if NET8_0_OR_GREATER`；net48（Revit 2020–2024）走 `Assembly.Load(byte[])` + `AppDomain.AssemblyResolve` 的 shadow-copy 後備路徑（可載入新版邏輯，但舊 Assembly 無法真正卸載，長時間反覆重載會緩慢累積記憶體——見 `docs/core-reload-architecture.md` §4.3）。

net48 建置需安裝 **.NET Framework 4.8 Developer Pack（targeting pack）**。若機器未安裝其參考組件，`dotnet build -c Release.R24` 會以 `MSB3644: 找不到 .NETFramework,Version=v4.8 的參考組件` 失敗——這是**環境缺件，非程式碼問題**（此收編機器即無 net48 targeting pack，故 R24 未能於本機驗證；R22/R23 同理）。安裝 targeting pack 後即可建置。

---

## 5. 驗證腳本（dev-only）

| 腳本 | 用途 |
|---|---|
| `MCP-Server/scripts/core-reload-verify.js` | 端到端熱重載驗收：`--label before/after` 手動、`--auto` 全自動（before → `reload_core` → after 比對 `CoreVersion`）、`--full`（`--auto` + 以 `get_recent_logs` 確認 CoreRuntime 重載日誌）。 |
| `MCP-Server/scripts/smoke-test.js` | 連 `ws://localhost:8964/` 對 Revit 做基本命令 smoke test。 |

> 這些是**開發驗證腳本**，直連 WebSocket（繞過 MCP 工具鏈），僅供本 opt-in 分支開發迭代使用，**不是 MCP 工具**、不隨 main 發佈。若未來要把任何驗證能力帶進 main，須改寫為正規 MCP 工具編排。

---

## 6. 如何乾淨切回 main

本分支只在自己的線上存在，不影響 main。切回：

```powershell
git checkout main
# 子專案 bin/obj 已被 .gitignore 忽略；若殘留產物想清掉：
git status               # 確認乾淨（切勿使用 git clean -x，會刪個人 vault/）
```

回到 main 後即是單一 `MCP/RevitMCP.csproj`（無 `MCP.Contracts`/`MCP.CoreRuntime`）。此時 `/core-reload-dev` skill 的 Pre-flight 會偵測到不存在 `MCP.CoreRuntime.csproj` 而要求改用 `/build-revit` + `/deploy-addon`——這是預期行為。

---

## 7. 致謝

Loader/Core 熱重載架構、TcpListener 重寫、shadow-copy 載入、`core-reload-verify.js` / `smoke-test.js` 驗證腳本、`get_revit_version` / `get_recent_logs` 工具，均由 **啟銘（ChimingLu）** 設計與實作。本分支為其 `feat/DWSchedule` 的收編延伸。
