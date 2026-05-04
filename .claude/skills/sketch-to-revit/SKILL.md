---
name: sketch-to-revit
description: "彩色手稿 → DXF 預覽 → Revit 牆/柱（V2 確定性管線）。委派 OpenAI Codex CLI 跑 Python OpenCV+Hough 向量化 + 本地網頁預覽 + OK/Redo 決策循環，主 session 收到 codex 結果後驅動 Revit 建模（階段 4-7）。粉色=RC15 牆、藍色=12cm 磚牆、黑點=柱。觸發條件：使用者上傳彩色手繪平面圖並提到「先轉 DXF」「網頁預覽」「sketch to revit」「確定性建模」「不要 dryrun」「視覺化預覽」等。執行前讀 domain/sketch-color-convention.md。"
---

# Sketch-to-Revit V2 — Codex 委派 + Revit 建模

執行前必讀：`domain/sketch-color-convention.md`（顏色約定、Y 軸翻轉、50mm snap 規則）。

## 適用條件

需要顏色清楚（螢光筆飽和度足夠）、線條乾淨。手稿太潦草時 codex 階段可能反覆 Redo，請使用者先整理圖面再丟進來。

## 架構

| 階段 | 執行者 |
|------|--------|
| 階段 1：圖 → DXF → 使用者決策 | OpenAI Codex CLI（讀 `plan-sketch-to-dxf` SKILL.md） |
| 階段 4-7：Revit 建模 | 本 Skill 主 session |

> 階段編號從 4 開始是有意的：codex 那邊跑完 plan-sketch-to-dxf 的 4 個內部階段（向量化 / rescale / server / Monitor / decision），主 session 從 Revit 建模相關的階段 4 接手，編號連續可讀。

## Workflow

### 階段 1：委派 Codex CLI 跑向量化 + 預覽決策

主 session 不直接跑 Python / node server / Monitor。改呼叫 OpenAI Codex CLI（已 `npm install -g @openai/codex`），由 codex 自行讀 `.claude/skills/plan-sketch-to-dxf/SKILL.md`，跑完整個「圖→DXF→使用者決策」循環。

#### 前置檢查
- `which codex` 確認指令存在；不存在則告知使用者：`npm install -g @openai/codex`
- 確認 `.claude/skills/plan-sketch-to-dxf/SKILL.md` 存在
- `mkdir -p output/`（codex 需要這個目錄）

#### 呼叫指令（用 Bash，**不要 run_in_background**）

```bash
codex exec --full-auto --cd "$(pwd)" --skip-git-repo-check \
  "讀取 .claude/skills/plan-sketch-to-dxf/SKILL.md 並完整執行『圖→DXF→使用者決策』流程。
輸入手稿路徑：<sketch_path>
輸出目錄：output/
要求：
1. 跑 vectorize_plan_to_dxf.py（mode=aligned, column-side=40），產出 output/sketch.dxf、output/sketch_preview.png、output/sketch_geometry.json
2. 計算 bbox，問使用者是否需要 rescale_geometry.py 校正尺寸
3. 啟動 node MCP-Server/scripts/sketch_preview_server.cjs（port 10003，被佔則 fallback 10004→10005），把 stdout 重導 output/sketch_server.log，把 http://localhost:<port> URL 給使用者
4. 自行處理 OK/Redo 循環：tail log 偵測 [sketch_preview] decision written:；redo 時讀 output/decision.json 取 mode/column_side 重跑階段 1
5. 收到 OK 後確認 output/decision.json 內容齊全（action/mode/column_side），kill server，正常結束
失敗回報：vectorize 失敗或 server 啟動失敗時，把錯誤寫入 output/decision.json 的 error 欄位後結束。"
```

> **Flag 不確定性備案**：若 `codex exec --full-auto --cd ... --skip-git-repo-check` 報「unknown flag」，先跑 `codex --help` 確認當前版本支援的 flag，至少要保留 `--full-auto`（自動批准 file edits + shell）。`--cd` 與 `--skip-git-repo-check` 是 codex CLI 0.x 後加入的，舊版可能要靠 `cd "$(pwd)" && codex ...` workaround。

#### Handoff Contract — codex 必須產生

| 檔案 | 必要 | 用途 |
|------|------|------|
| `output/sketch_geometry.json` | ✅ | 階段 5 座標換算讀此檔 |
| `output/decision.json` | ✅ | 主 session 驗證 action == "ok" |
| `output/sketch.dxf` | 選用 | 給使用者下載 |
| `output/sketch_preview.png` | 選用 | codex 已用過 |

#### 主 session 驗證（codex 返回後立刻跑）

```bash
test -f output/decision.json     || abort "codex 沒寫 decision.json"
test -f output/sketch_geometry.json || abort "幾何檔遺失"
jq -e '.action == "ok"' output/decision.json \
  || abort "decision.action != ok，codex 沒處理完循環"
```

#### 錯誤處理

| 失敗情境 | 對策 |
|---------|------|
| `codex` 不存在 | 提示 `npm install -g @openai/codex`，中止 Skill |
| codex 非 0 退出 | 讀 `output/decision.json` 的 `.error` 欄位回報；若連檔案都沒有，告知使用者 codex 自身崩潰 |
| `decision.json` 缺 `action` 或 `action == "redo"` | 表示 codex 沒處理完循環，視為失敗，建議使用者重跑 |
| `sketch_geometry.json` 缺 | 向量化失敗，建議使用者檢查手稿顏色 |

### 階段 4：建模參數詢問

1. `mcp__revit-mcp__get_selected_elements` → 錨點
2. 並行：
   - `mcp__revit-mcp__get_active_view` → 取 `LevelName`（active view 的關聯樓層）
   - `mcp__revit-mcp__get_all_levels`
   - `mcp__revit-mcp__get_wall_types`
   - `mcp__revit-mcp__get_column_types`
3. **由 active view 推導 bottom/top Level**：
   - 從 `get_active_view` 拿 `LevelName`，那就是 `bottomLevel`
   - 把 `get_all_levels` 結果依 `Elevation` 升冪排序，找到 `bottomLevel` 的下一個 → `topLevel`
   - 若是頂樓（沒有下一個 level）→ fallback 到 4-題版（向使用者確認）
   - 若 active view 沒有關聯 level（例如 3D / Drafting / Sheet view）→ `LevelName` 為空字串，fallback 到 4-題版
4. **正常情況只問 3 題**（用 `AskUserQuestion`）：RC15 牆型、磚12 牆型、柱型
5. **fallback 4-題版**：RC15 牆型、磚12 牆型、柱型、bottom/top Level

> 為什麼這樣改：使用者通常已經把 active view 切到要建模的樓層，再問一次 level 是多餘的。從 active view 推導同時也避免「使用者選錯 level → 牆建錯地方」。

### 階段 5：座標換算 + 50mm snap
讀 `output/sketch_geometry.json`：
1. 比例尺：`scale.refMmDistance / scale.refPxDistance`，或直接讀 `scale.mmPerPx`（若階段 1.5 已校正則存在）
2. `mmPerPx = refMmDistance / refPxDistance`
3. 幾何中心 `(cx_px, cy_px)` = 所有柱平均
4. 對每個 px 座標：
   - `mm_x = (px - cx_px) * mmPerPx + Cx`
   - `mm_y = -(py - cy_px) * mmPerPx + Cy`（**Y 翻轉**）
5. 50mm snap（順序:錨點→柱心→牆端點共線分組→自由端點），詳見 domain doc §7

### 階段 6：Revit 批次建立

```
for col in columns:
  create_column(x, y, columnType, bottomLevel, topLevel)

for wall in walls:
  create_wall(
    startX, startY, endX, endY,
    height,
    wallType=<RC15 或 BRICK12 名稱>,
    bottomLevel=<bottomLevelName>,
    topLevel=<topLevelName>,
  )

# C# CreateWall 已支援 wallType / bottomLevel / topLevel，並把 Base/Top Offset 預設 0
# （見 domain/sketch-color-convention.md §11）。
# 萬一 wallType 名稱查找失敗（例：拼字錯）會 fallback 到 active type，
# 此時可用 change_element_type 補救：
# change_element_type(elementIds=[<出錯的 wall ids>], typeId=<目標 typeId>)

unjoin_element_joins(sourceCategory="Walls", elementIds=[...])
```

> **驗證提醒**：建完任意一道牆後，用 `get_element_info` 確認 `Base Constraint` 等於指定的 `bottomLevelName`。若仍顯示 GL（或 Level 1 等第一個 Level），代表 Revit 載入的還是舊版 DLL — 提醒使用者關掉 Revit、跑 `/deploy-addon` 或 `scripts/install-addon.ps1`、再重新啟動 Revit。

### 階段 6.5：自動標尺寸（預設執行，可關閉）

蓋完牆後呼叫一次 `auto_dimension_walls` 補上總尺寸：

```
auto_dimension_walls(
  viewId=<active view ID>,
  wallIds=[<剛建立的 wall IDs>],
  mode="overall_bbox",   # 預設：top 邊沿 X + right 邊沿 Y 兩條總長串
  offsetMm=1500
)
```

模式選擇（使用者可指定）：
- **`overall_bbox`（預設）** — 兩條外圍總長串。一眼看到建築總尺寸。
- **`chained`** — 同列／同排牆段串接。要做正式平面圖時用。
- **`per_wall`** — 每道牆獨立標長度。debug 用。

> **行為提醒**：dimension 直接綁牆面 reference，牆**移動時會跟著走、刪除時會自動消失**。對「先建出來看，不對就刪掉重來」的 sketch iteration 流程是正確的（不會留下孤兒標註）。代價是牆移動後標註值會更新 — 若需要凍結值，用 hard-coded text 註解而不是 dimension。

使用者明確說「不要標尺寸」時跳過階段 6.5。

### 階段 7：報告
- 建立 N 柱、M 牆
- 修正 N 個牆型、M 個 Base Offset
- 解除接合 K 對
- 自動標註 K 條尺寸（mode、總寬 mm × 總高 mm）

## 執行範圍模式

支援只跑部分階段（使用者明確要求）：
- **只到階段 1**：「先看 DXF 偵測對不對」（codex 跑完即停，不進建模）
- **跳過階段 1，直接從階段 4 開始**：當 `output/sketch_geometry.json` 與 `output/decision.json (action=ok)` 已存在且使用者確認過，直接進入 Revit 建模

## Edge Cases

| 風險 | 對策 |
|------|------|
| codex 不存在或版本不符 | `npm install -g @openai/codex`；檢查 `codex --version` |
| codex flag 不被識別 | `codex --help` 對照當前版本，至少保留 `--full-auto` |
| codex 跑太久（使用者開著網頁不按 OK） | server 端不設 timeout；codex 自身 timeout 由 OpenAI Codex CLI 預設處理 |
| port 被佔 | codex 內 fallback 10003→10004→10005 |
| 使用者忘了點 OK | codex 會持續等待；使用者可關掉 codex CLI process 中止 |
| C# `CreateWall` 收到 wallType 但找不到（拼字錯 / 未載入） | 自動 fallback 到 active type 並 log warning，階段 6 註解中的 `change_element_type` 可手動補救 |
| 牆建在 GL 而非 active view level | 通常代表 Revit 還在用舊版 DLL（修正前的版本）— 關掉 Revit、redeploy、重啟 |
| Active view 沒關聯 level（3D / Drafting / Sheet） | 階段 4 fallback 4-題版，向使用者確認 bottom/top Level |
| Active view 是頂樓（沒有更高 level） | 階段 4 fallback 4-題版 |
| DXF 寫成 AC1009 舊格式 | AutoCAD/Revit 都支援，無需修改 |

## 實作參考
- 委派工具（codex 學習這份）：[plan-sketch-to-dxf/SKILL.md](../plan-sketch-to-dxf/SKILL.md)
- Python 向量化管線：[plan-sketch-to-dxf/scripts/vectorize_plan_to_dxf.py](../plan-sketch-to-dxf/scripts/vectorize_plan_to_dxf.py)
- 預覽 server：[MCP-Server/scripts/sketch_preview_server.cjs](../../../MCP-Server/scripts/sketch_preview_server.cjs)（注意：MCP-Server 為 ESM 專案，必須用 `.cjs` 副檔名）
- 預覽頁：[MCP-Server/scripts/sketch_preview.html](../../../MCP-Server/scripts/sketch_preview.html)
- C# CreateWall bug 紀錄：[domain/sketch-color-convention.md](../../../domain/sketch-color-convention.md) §11
