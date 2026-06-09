# 更新拉取指南：如何 pull 上游新功能並重新部署

> 給每一位 fork 了 `shuotao/REVIT_MCP_study` 的夥伴。照本頁做，就能把上游的新功能（例如「DWG 圖層批次建柱」）更新到自己的 Revit。
> 本頁不另造工具，只把既有的 `/build-revit`、`/deploy-addon`、`scripts/setup.ps1` 串成一條更新動線。

---

## 0. 一次性設定（只需做一次）

確認本機有指向上游的 `upstream` 遠端：

```powershell
git remote -v
# 若沒有 upstream，新增它：
git remote add upstream https://github.com/shuotao/REVIT_MCP_study.git
```

---

## 1. 拉取上游更新

```powershell
git fetch upstream
git checkout main
git merge upstream/main      # 或 git rebase upstream/main
```

> 若你平常在自己的功能分支工作，先把 main 更新好，再把 main 併回你的分支。
> 遇到衝突：通常集中在 `MCP-Server/src/tools/index.ts`、`MCP/Core/CommandExecutor.cs`、`CLAUDE.md`；以上游版本為基礎、保留自己新增的列即可。

---

## 2. 重新編譯

### 2a. MCP Server（Node/TypeScript）

```powershell
cd MCP-Server
npm install
npm run build
cd ..
```

完成後可確認工具數（目前應為 **96**）：

```powershell
node -e "const {registerRevitTools}=require('./MCP-Server/build/tools/index.js'); console.log(registerRevitTools().length)"
```

### 2b. Revit Add-in（C#）— 用你的 Revit 版本

```powershell
dotnet build -c Release.R24 MCP/RevitMCP.csproj   # R22/R23/R24/R25/R26 擇一
```

> 或直接用技能：`/build-revit`（可一次建多版本）。

---

## 3. 部署到 Revit

```powershell
.\scripts\install-addon.ps1
```

> 或用技能：`/deploy-addon`。
> 也可用一鍵腳本 `.\scripts\setup.ps1` 一次跑完「安裝相依 → 編譯 → 部署 → 設定 AI client」。

部署前請**先關閉 Revit**（DLL 被占用會複製失敗）。

---

## 4. 啟動與驗證

1. 開啟 Revit → ribbon 找到 **Revit MCP** → 開啟 MCP 服務（監聽 `localhost:8964`）。
2. 在 AI client（Claude Code / Claude Desktop 等）確認看得到工具，且總數為 **96**。
3. 試新功能（DWG 建柱）：開一張**含 CAD 的平面視圖**，請 AI 跑 `get_dwg_column_layers`，能列出圖層即代表更新成功。

---

## 5. 體驗新功能：DWG 圖層批次建柱（自動建模紅利）

> 完整方法見 `domain/dwg-column-import.md`；工作流編排見技能 `/dwg-column-import`。

前置：① Revit 開在平面視圖、② 已 import 或 link CAD、③ 專案已載入矩形柱族（RC/混凝土優先）、④ 柱輪廓在獨立圖層。

三步驟（**preview 一定先於 create**）：

1. `get_dwg_column_layers` — 列出 CAD 圖層、推薦柱圖層。
2. `preview_dwg_columns(layerName)` — 預覽柱數量/尺寸/旋轉角（不改模型）。
3. `create_columns_from_dwg(layerName, columnType)` — 批次建柱（`structural` 或 `architectural`）。

> 若提示「找不到高於基準層的樓層」，先用 `create_level(elevation, name)` 補上層樓層。
> 重要：柱位置採 CAD 在模型中的實際座標，**未做共用座標換算**——CAD 匯入時務必先對位正確，建完抽查幾根柱中心是否對齊。

---

## 常見問題

| 症狀 | 處理 |
|---|---|
| AI 看不到新工具 | 重跑 `npm run build`、重啟 AI client |
| 部署時 DLL 複製失敗 | 先關閉 Revit 再 `install-addon.ps1` |
| 工具數不是 96 | 確認 `git merge upstream/main` 成功、`npm run build` 無錯 |
| 圖層清單為空 | 確認 CAD 已載入、連結未遺失、在該視圖可見 |
| 柱整批偏移 | CAD 匯入未對位，重新以正確原點/比例匯入 |
