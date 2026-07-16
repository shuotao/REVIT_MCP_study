# HANDOFF ← Windows 回報 Mac — salvage/reclaim-contributor-impl 建置+測試結果

> 對應 `HANDOFF_PR86_WINDOWS_BUILD.md` 的回報。Windows(Revit 2024 SDK)這端已完成
> **C# 建置 + TS 建置 + Revit 實跑測試**。以下是結果、重大發現、與交回 Mac 的待辦。
> **本檔與 PR #86 一起 merge 後可刪除。**

## TL;DR

- ✅ **16 支貢獻者工具全數接通、build 全綠(R24 int + R26 long)、Revit 2024 實跑 16/16 通過。**
- ⚠️ **handoff 的兩個假設不成立,已在下方修正**:(1) 缺的 helper **不在任何 fork**;(2) registry 發佈基礎設施 **在此 repo 不存在**。
- 🔢 runtime tools **159 → 166**(補了 7 支漏掉 TS 定義的工具)。計數已同步、QA 工具數檢查 PASS。
- 🔧 需要 **Jacky 複核重建的 helper**、Mac 端 **建立 registry 發佈基礎設施** 並清 **文件債**。

---

## 1. 重大發現:缺的 helper「不在任何 fork」(修正 handoff 假設)

`HANDOFF_PR86_WINDOWS_BUILD.md` 寫「build 報缺方法就從對應 fork 補入 helper」——**此假設不成立**。

實查(窮舉 origin 全分支 + 4 個 preview 快照 + Jacky fork 全部 14 分支 + PR #79/#81/#82/#85 head + tags):
- Jacky 的 `add/partition-opening-takeoff-workflows`、`add/ifc-structural-native-sync` **只有 domain `.md`,零 C#**;連 `AnalyzeTallPartitionRooms` 方法本體都不在 fork 上。
- 這 9 個缺失 helper 的**定義文字,在任何 git ref 都不存在**,只以「被呼叫」形式出現在 salvage commit `604cafe`。
- 即「**沉默貢獻者**」:貢獻者本機實作、只推 docs;salvage 帶進了 `.cs` 卻沒帶 helper。

### 我的處理:照「呼叫點合約 + repo 既有同族程式 + 貢獻者 domain 規格」重建

全部集中在新檔 **`MCP/Core/Commands/CommandExecutor.ReclaimedHelpers.cs`**,清楚標註來源。

| Helper | 風險 | 重建依據 |
|---|:--:|---|
| `DismissWarningsPreprocessor` | 🟢 | 比照 `DetailCopy.cs` 既有 `SuppressWarningsPreprocessor` |
| `GetLevelName` | 🟢 | 標準 `Level.Name` |
| `GetDetector3DView` | 🟢 | 比照 `StairCompliance.cs:135` 取非樣板 View3D |
| `TrySetStringParameter` | 🟢 | 比照同檔 `TrySetDoubleParameterFeet` first-match-return |
| `TrySetDoubleParameterMm` | 🟢 | 比照同檔 `TrySetAllDoubleParametersMm`(mm→feet 用 `IfcSyncMmToFeet`) |
| `TrySetColumnTopAttachment` | 🟡 | **參數名用推測**;柱頂實際對齊已由 TopLevel+TopOffset 完成,此為附帶旗標 |
| `FloorHitInfo`(型別) | 🟢 | 由呼叫點欄位存取逆推:`{HasHit, BottomZFeet, FloorId(IdType), FloorName, LevelName, Message}` |
| `CollectFloorBottomHitsAtPoint`(射線) | 🟡 | domain `tall-partition-index-workflow.md` 第 73 行規格 + repo `ReferenceIntersector` 樣式 |
| `CollectGeometryFloorBottomHitsAtPoint`(幾何) | 🟠 | 同上,幾何 `Solid.IntersectWithCurve` 版 |

> **⚠️ 請 Jacky 對其原始碼複核這 9 個 helper**,尤其 🟡🟠 三個幾何/型別的,以及 `TrySetColumnTopAttachment` 的目標參數名。

---

## 2. Build 結果

- `dotnet build -c Release.R24`:**0 error** → `bin/Release.R24/RevitMCP.dll`
- `dotnet build -c Release.R26`:**0 error**(驗證 IdType int↔long 跨版:`FloorHitInfo.FloorId` 用 `IdType` + `.GetIdValue()`,兩版皆過)
- 16 條 dispatcher case 已加入 `CommandExecutor.cs`(switch 151→167),含 Jacky #79/#81/#82 的 13 條 + 林孟毅 #85 帷幕 3 條。
- MCP-Server:`npm run build` 綠。**補了 7 支 salvage 漏掉 TS 定義的工具**(view-duplicate / room-filled / 4×fill-pattern / ifc-sync),新增 3 個 TS module。執行橋接是泛型 passthrough,無需逐工具映射。
- `registerRevitTools()` full = **166**、無重複、16 支全在冊。
- 6 處空 `catch{}` 已依規範處理(5 加意圖註解 + `FillPatterns.cs:588` 升級 `Logger.Info`)。

---

## 3. Revit 2024 實跑測試:16/16 通過

模型:Autodesk 官方 **Snowdon Towers Sample Architectural**(含連結結構模型)。測試產生物已全數清除。

- **`analyze_tall_partition_rooms` — 重建射線 helper 經真實資料驗證正確**:多樣本 100% 命中,`HeightMm = FloorBottomZ − BaseZ` 全部吻合,`FloorName`/`FloorLevel`/`Message` 皆正確。
- **`sync_ifc_structural_to_native`(dryRun)**:讀連結結構模型、5 樑、`CB24x24→609.6mm` 換算正確。
- 其餘 13 支(scaffold 3 / 帷幕 3 / 門窗計數 2 / 填充 3 / 視圖複製 1 / 選取周長 1):dispatch + 執行全數正常;無選取/無型別皆為乾淨錯誤而非崩潰。
- `create_room_filled_regions`(建 2)、`batch_create_rc_filled_region`(偵測 20 RC 建 20)實際觸發了空 catch 修正路徑。

### ⚠️ 唯一缺口
`CollectGeometryFloorBottomHitsAtPoint`(幾何版)因**連結結構模型偵測不到柱**(ColumnPlans:0)而**未能 runtime 觸發**。其**射線姊妹版已驗證正確**、程式為標準 `Solid.IntersectWithCurve`,但仍需 **有柱的模型 + Jacky 複核** 才算完整驗證。
> 附帶:框架偵測到 5 樑卻 0 柱——貢獻者的**柱偵測邏輯**也請 Jacky 一併看。

---

## 4. 變更檔案清單

**新增**
- `MCP/Core/Commands/CommandExecutor.ReclaimedHelpers.cs`(9 helper)
- `MCP-Server/src/tools/{view-duplicate,fill-region,ifc-structural-sync}-tools.ts`

**修改**
- `MCP/Core/CommandExecutor.cs`(+16 case)
- `MCP/Core/Commands/CommandExecutor.{FillPatterns,RoomDoorCounts,RoomFilledRegions,RoomWindowCounts}.cs`(空 catch)
- `MCP-Server/src/tools/index.ts`(掛 3 module 進 full/architect/structural)
- `CLAUDE.md` `README.md` `README.zh-TW.md` `docs/DOCUMENT_AUDIENCE_INVENTORY.md`(159→166)
- `docs/BIM_MCP/reference/*.html` ×5(工具數 146→166,10 處)

---

## 5. 交回 Mac 的待辦

### 5-1. Merge PR #86(Windows 交付已完成,可 merge)
build 全綠 + 166 tools + Revit 16/16 → PR #86 draft 轉正 → merge main → 關閉被取代的 **#79 / #81 / #82 / #85**。

### 5-2. ⚠️ Registry 發佈基礎設施「在此 repo 不存在」(修正 handoff 假設)
handoff 寫「server.json / npm token / GitHub Actions 均已就緒」——**在此 repo 查無實據**:
- **無 `server.json`**(MCP registry 清單)——任何分支皆無。
- **無 publish / registry workflow**——`.github/workflows/` 只有 `check-pr.yml`。
- **`MCP-Server/package.json` 無 `publishConfig`**、version 仍 `1.0.0`。

→ registry 發佈前,Mac 端需先**建立 `server.json` + publish workflow + 設 npm token**。若那套設定在 Mac 本機(非版控),請一併入庫或確認位置。

### 5-3. 文件債(QA/QC 尚有 5 紅,皆 salvage 既有、非本次工具工作造成)
`scripts/verify-qaqc.ps1 -SkipBuild -SkipDeploy`:工具數檢查已 PASS,剩餘 5 項:
1. domain-count 宣稱不一致(truth 72)
2. skill-count:CLAUDE.md 寫 **42** 但實際 **50**
3. BIM_MCP `domain-index` 缺 11 張 reclaimed domain 卡(scaffold-takeoff / tall-partition-index / threshold-opening-takeoff / rc-filled-region-workflow / ifc-structural-native-sync …)
4. BIM_MCP `skills-index` 缺 8 張卡
5. `domain/room-numbering-workflow.md` 斷鏈 → `user-specified-runtime-parameters.md`

### 5-4. Jacky 複核清單
- 9 個重建 helper(§1),尤其 `FloorHitInfo` / 2×`Collect...` / `TrySetColumnTopAttachment`。
- IFC 同步的**柱偵測**為何在標準結構模型回傳 0 columns。
