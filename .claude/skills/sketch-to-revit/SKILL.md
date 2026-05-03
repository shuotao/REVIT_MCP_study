---
name: sketch-to-revit
description: "彩色手稿 → DXF 預覽 → Revit 牆/柱（V2 確定性管線）。用 Python OpenCV+Hough 把手繪平面圖向量化成 DXF + PNG，啟動 localhost 網頁讓使用者視覺化檢查（取代文字版 dryrun），確認後再驅動 Revit 建模。粉色=RC15 牆、藍色=12cm 磚牆、黑點=柱。觸發條件：使用者上傳彩色手繪平面圖並提到「先轉 DXF」「網頁預覽」「sketch to revit」「確定性建模」「不要 dryrun」「視覺化預覽」等。若顏色不純或線條斷裂建議改用 /sketch-to-walls (V1 Vision 版本)。執行前讀 domain/sketch-color-convention.md。"
---

# Sketch-to-Revit V2 — DXF 預覽取代 dryrun

執行前必讀：`domain/sketch-color-convention.md`（顏色約定、Y 軸翻轉、50mm snap 規則）。

## 適用條件

| 情境 | 適用版本 |
|------|---------|
| 顏色清楚（螢光筆飽和度足夠）、線條乾淨 | **V2（本 Skill）** |
| 顏色不純、手繪潦草、線條斷裂多 | V1 `/sketch-to-walls` |

## V2 vs V1 差異

| 項目 | V2（本 Skill） | V1 `/sketch-to-walls` |
|------|----------------|----------------------|
| 圖形辨識 | Python OpenCV+Hough（確定性） | Claude Vision（多模態） |
| 預覽形式 | localhost 網頁 PNG（圖形） | Markdown mm 座標表（文字） |
| 中間產物 | DXF + PNG + JSON | 無 |
| Revit dryrun | 不需要（網頁已視覺確認） | 需要 |

## 七階段 Workflow

### 階段 0：環境檢查
- `python --version` 確認 ≥ 3.8（fallback `python3`）
- 確認 `cv2`、`numpy`、`skimage` 可 import
- 確認 port 10003 未佔用（fallback 10004 / 10005）
- 建立 `output/` 暫存目錄

### 階段 1：Python 向量化
```bash
python .claude/skills/plan-sketch-to-dxf/scripts/vectorize_plan_to_dxf.py <sketch_path> \
  --out output/sketch.dxf \
  --preview output/sketch_preview.png \
  --json output/sketch_geometry.json \
  --mode aligned \
  --column-side 40
```

預設 mode = `aligned`（中等清理：直線校正 + 端點 snap，不強制全部正交）。
- 顏色純、要求乾淨 CAD：改 `--mode strong-ortho`
- 想保留原始斜率：改 `--mode basic`

驗證輸出：
- `columns N` ≥ 1，否則回報「未偵測到柱，可能黑色閾值太嚴」
- `magenta_segments + cyan_segments` ≥ 4，否則回報「牆段太少，可能顏色閾值不匹配」

### 階段 2：啟動預覽 server
```bash
node MCP-Server/scripts/sketch_preview_server.cjs \
  --original <sketch_path> \
  --preview output/sketch_preview.png \
  --geometry output/sketch_geometry.json \
  --decision output/decision.json \
  --port 10003
```

用 Bash 工具帶 `run_in_background: true` 啟動，**記住回傳的 task_id**（後續 Monitor 不需要它，但若要 KillBash 用得到）。把 `http://localhost:10003` 貼給使用者，請對方按 OK 或 Redo。

### 階段 3：等待使用者決策（Monitor 串流，**不要輪詢**）

> 不能用 `while`/`Bash sleep` 輪詢 — 那會讓 turn 結束、要等使用者再輸入才會醒。改用 `Monitor` 工具串流 server stdout：server 寫入 decision.json 時會印 `[sketch_preview] decision written: ok` 或 `... redo`（見 `MCP-Server/scripts/sketch_preview_server.cjs` 第 137 行）。

呼叫 Monitor，例如：
```
Monitor(
  description: "sketch preview decision",
  command: "tail -f output/sketch_server.log 2>/dev/null | grep --line-buffered -E '\\[sketch_preview\\] decision written:|listen failed|EADDRINUSE'",
  timeout_ms: 300000,
  persistent: false
)
```

實作要點：
1. 階段 2 啟動 server 時把 stdout 重導到 `output/sketch_server.log`（例：`node ... > output/sketch_server.log 2>&1`），這樣 Monitor 才有檔案可 tail
2. Monitor 也要監聽 `listen failed` / `EADDRINUSE`，避免 port 被佔卻沒人通知
3. 收到 `decision written: ok` → Read `output/decision.json` 確認 action，進階段 4
4. 收到 `decision written: redo` → Read `output/decision.json` 取 `mode` / `column_side`，回階段 1 用新參數重跑
5. 超時（300 秒無事件）→ 提示「網頁預覽逾時，請重新觸發 Skill 或在網頁按 OK / Redo」並結束

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
1. 比例尺：用 `scale.refPxDistance`（最近兩柱），對應 10000mm（或使用者指定）
2. `mmPerPx = refMmDistance / refPxDistance`
3. 幾何中心 `(cx_px, cy_px)` = 所有柱平均
4. 對每個 px 座標：
   - `mm_x = (px - cx_px) * mmPerPx + Cx`
   - `mm_y = -(py - cy_px) * mmPerPx + Cy`（**Y 翻轉**）
5. 50mm snap（順序：錨點→柱心→牆端點共線分組→自由端點），詳見 domain doc §7

### 階段 6：Revit 批次建立 + 牆參數修正

```
for col in columns:
  create_column(x, y, columnType, bottomLevel, topLevel)

for wall in walls:
  create_wall(startX, startY, endX, endY, height, wallType)

# C# CreateWall 仍忽略 wallType — 用 change_element_type 修正：
change_element_type(elementIds=[<RC15 wall ids>], typeId=<RC15 typeId>)
change_element_type(elementIds=[<BRICK12 wall ids>], typeId=<BRICK12 typeId>)

# 設定每道牆的 Base/Top Constraint 為從 active view 推導的 level，
# Base Offset / Top Offset 歸零 — 讓牆精確介於兩個樓層之間。
for wall_id in all_walls:
  modify_element_parameter(wall_id, "Base Constraint", "<bottomLevelName>")
  modify_element_parameter(wall_id, "Top Constraint",  "<topLevelName>")
  modify_element_parameter(wall_id, "Base Offset", "0")
  modify_element_parameter(wall_id, "Top Offset",  "0")

unjoin_element_joins(sourceCategory="Walls", elementIds=[...])
```

> 不再用「Base Offset = bottomLevel高度mm」的 hack。`modify_element_parameter` 已修正單位 bug（用 `SetValueString` 解析顯示單位），且新增 ElementId 型別支援（透過 level 名稱查 ElementId 設定 Constraint）。

### 階段 7：報告
- 建立 N 柱、M 牆
- 修正 N 個牆型、M 個 Base Offset
- 解除接合 K 對

## 執行範圍模式

支援只跑部分階段（使用者明確要求）：
- **只到階段 1**：「先看 DXF 偵測對不對」
- **只到階段 3**：「等我看完網頁再決定要不要建」
- **跳過階段 1-3，直接從階段 4 開始**：當 `output/sketch_geometry.json` 已存在且使用者確認過，直接進入 Revit 建模

## Edge Cases

| 風險 | 對策 |
|------|------|
| 顏色閾值不匹配 | preview.png 漏線使用者一眼看出，重跑換 mode 或調 mask |
| port 被佔 | fallback 10003→10004→10005 |
| 使用者忘了點 OK | 5 分鐘 timeout |
| Python 命令不存在 | fallback `python` → `python3` |
| C# CreateWall 忽略 wallType | 階段 6 用 `change_element_type` 修正（spawned task 追蹤底層 fix） |
| Active view 沒關聯 level（3D / Drafting / Sheet） | 階段 4 fallback 4-題版，向使用者確認 bottom/top Level |
| Active view 是頂樓（沒有更高 level） | 階段 4 fallback 4-題版 |
| DXF 寫成 AC1009 舊格式 | AutoCAD/Revit 都支援，無需修改 |

## 實作參考
- Python 管線：[plan-sketch-to-dxf/scripts/vectorize_plan_to_dxf.py](../plan-sketch-to-dxf/scripts/vectorize_plan_to_dxf.py)
- 預覽 server：[MCP-Server/scripts/sketch_preview_server.cjs](../../../MCP-Server/scripts/sketch_preview_server.cjs)（注意：MCP-Server 為 ESM 專案，必須用 `.cjs` 副檔名）
- 預覽頁：[MCP-Server/scripts/sketch_preview.html](../../../MCP-Server/scripts/sketch_preview.html)
- V1 fallback：[sketch-to-walls/SKILL.md](../sketch-to-walls/SKILL.md)
- C# CreateWall bug 紀錄：[domain/sketch-color-convention.md](../../../domain/sketch-color-convention.md) §11
