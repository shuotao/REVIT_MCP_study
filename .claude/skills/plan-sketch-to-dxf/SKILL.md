---
name: plan-sketch-to-dxf
description: convert architectural sketch images into clean dxf centerline drawings with optional interactive web preview. use when the user uploads or references a hand-drawn or color-coded floor plan and asks to convert it to dxf, separate different wall colors into cad layers, treat black marks as columns, make columns square, add column centerlines, make wall centerlines single lines, connect broken wall lines, straighten and align walls, or force orthogonal horizontal/vertical cad geometry. also serves as the underlying tool for `sketch-to-revit` (delegated via codex cli) — provides the python vectorization + web preview + ok/redo decision loop.
---

# Plan Sketch To DXF

## Overview

Convert color-coded architectural sketch images into DXF files with single-line wall centerlines, square columns, column centerlines, and preview images. The default workflow matches the prior conversation: magenta and cyan strokes are wall centerlines on separate layers, black marks are columns, columns are square, and walls should be continuous, aligned, and orthogonalized.

This skill can be used standalone (just produce DXF + PNG preview), or as the interactive front-end of `sketch-to-revit` — in which case the full 4-stage workflow runs through to a `decision.json` handoff file.

## Default Output Contract

Always produce both:

1. A `.dxf` file.
2. A `.png` preview image showing the generated vector geometry.
3. A `.json` geometry file (`output/sketch_geometry.json`) — required when downstream skills (e.g. `sketch-to-revit`) need to read coordinates.

Use these DXF layers unless the user specifies otherwise:

- `WALL_MAGENTA`: magenta or pink wall centerlines.
- `WALL_CYAN`: cyan or blue wall centerlines.
- `COLUMNS_BLACK`: square column outlines derived from black blobs.
- `COLUMN_CENTERLINE`: crosshair centerlines through each column.

Wall geometry must be single-line centerline CAD geometry, not double-line wall thickness geometry. Column geometry must be square by default, not circular.

## Workflow

### 階段 1：Python 向量化

Identify the input image path. Choose the processing mode:
- `basic`: first-pass centerline extraction.
- `aligned` (default): straighter, better-aligned walls with endpoint cleanup.
- `strong-ortho`: aggressive horizontal/vertical orthogonalization and endpoint snapping. Use this when the user asks for stronger alignment, CAD cleanup, or forced orthogonal geometry.

Standard command:

```bash
python .claude/skills/plan-sketch-to-dxf/scripts/vectorize_plan_to_dxf.py <sketch_path> \
  --out output/sketch.dxf \
  --preview output/sketch_preview.png \
  --json output/sketch_geometry.json \
  --mode aligned \
  --column-side 40
```

Use `output/` for generated files (project root relative). Create `output/` first if missing: `mkdir -p output`.

驗證輸出：
- `columns N ≥ 1`，否則回報「未偵測到柱，可能黑色閾值太嚴」
- `magenta_segments + cyan_segments ≥ 4`，否則回報「牆段太少，可能顏色閾值不匹配」

If the standalone use case ends here (user only needs DXF), stop after Stage 1 and return links to the DXF and preview. Otherwise continue to Stage 1.5 onward for interactive decision flow.

### 階段 1.5：尺寸校正（推薦在網頁上做，順序：預覽 → 標尺寸 → OK）

`vectorize_plan_to_dxf.py` writes `refMmDistance = 10000` into `geometry.json` (assumes the two nearest columns are 10 m apart). If the actual building scale differs, downstream coordinate conversion will在 Stage 5 / Revit 建模拿到錯誤的 mm 座標。

**首選做法（網頁互動）**：階段 2 啟動 server 後，使用者在預覽頁按 **「📏 標尺寸」** 即可校正；server 會在背景跑 `rescale_geometry.py` 並 in-place 改寫 `output/sketch_geometry.json` 和 `output/sketch.dxf`。下次 Codex 階段 5 讀 geometry.json 拿到的就是正確 mmPerPx。

網頁三種校正模式（modal 內以 tab 切換，單位一律 cm）：

| Tab | 操作 | server 內部 |
|------|------|-------------|
| 兩點實際距離 | 在圖上 click 兩點（已知實際距離的基準點）→ 輸入 cm | patch geometry.json 的 `refPxDistance` 為使用者量到的像素距離，再呼叫 `--ref-mm <cm*10>` |
| 整圖寬度 | 直接輸入整圖元素 X 軸實際 cm | `--target-width-mm <cm*10>` |
| 整圖高度 | 直接輸入整圖元素 Y 軸實際 cm | `--target-height-mm <cm*10>` |

校正成功 → toast 顯示新 `mmPerPx` 與新 bbox（cm），畫面 hint 會提醒「按 OK 建模」。`/api/rescale` 不會寫 `decision.json`，與 OK / Redo 完全獨立。

**順序很重要**：
- ✅ 預覽 → 標尺寸 → OK：rescale 改的是 mmPerPx，圖像本身不變，但 OK 後 Codex 階段 5 會用新 mmPerPx 算 mm 座標。
- ❌ 標尺寸 → Redo：Redo 會重跑 vectorize，產生新 geometry.json，剛才的 rescale 會被覆蓋。如果要 Redo 就先 Redo 再標尺寸。

**Fallback：CLI 模式（無瀏覽器環境）**

1. Read `output/sketch_geometry.json`, compute bbox from `columns[].px/py` and `*_segments`, print:
   ```
   bbox: <寬> mm × <高> mm  (refMmDistance=10000, mmPerPx=<...>)
   ```
2. Use `AskUserQuestion`:
   - 「目前 bbox = X m × Y m，正確嗎？」
   - A: 對 → skip rescale
   - B: 實際 = ? m × ? m → use `--target-width-mm` + `--target-height-mm`
   - C: 實際 C1↔C2 = ? m → use `--ref-mm`
3. Run rescale（注意 `--out-dxf` 用同一個檔，避免產生 `sketch_scaled.dxf` 干擾下游）：
   ```bash
   python .claude/skills/plan-sketch-to-dxf/scripts/rescale_geometry.py \
     --geometry output/sketch_geometry.json \
     --dxf output/sketch.dxf \
     --out-dxf output/sketch.dxf \
     --ref-mm <N>          # or --target-width-mm <N> / --target-height-mm <N>
   ```
4. Script updates `geometry.json` in place (adds `scale.mmPerPx`, `scale.rescaled_at`) and prints new bbox.

### 階段 2：啟動 web 預覽 server

```bash
node MCP-Server/scripts/sketch_preview_server.cjs \
  --original <sketch_path> \
  --preview output/sketch_preview.png \
  --geometry output/sketch_geometry.json \
  --decision output/decision.json \
  --port 10003 \
  > output/sketch_server.log 2>&1 &
```

- Port fallback：10003 → 10004 → 10005（被佔則順移）
- **必須**把 stdout 重導 `output/sketch_server.log`，下一階段 Monitor 會 tail 它
- 把 `http://localhost:<port>` 提示給使用者，請其在瀏覽器：
  1. （建議）先按 **「📏 標尺寸」** 校正比例尺（見階段 1.5；server 會 in-place 改 `output/sketch_geometry.json` 與 `output/sketch.dxf`）
  2. 再按 **OK 建模** 或 **不對，重跑**
- `.cjs` 副檔名必要（MCP-Server 是 ESM 專案，不能用 .js）
- 預設 server 會自動定位 `rescale_geometry.py`（`.claude/skills/plan-sketch-to-dxf/scripts/rescale_geometry.py`），如需自訂可用 `--rescale-script` 與 `--python` 旗標

### 階段 3：等待使用者決策（Monitor 串流，**禁止輪詢**）

不能用 `while sleep` 輪詢——那會讓 turn 結束、要等使用者再輸入才會醒。改用 `Monitor` 工具串流 server log：

```
Monitor(
  description: "sketch preview decision",
  command: "tail -f output/sketch_server.log 2>/dev/null | grep --line-buffered -E '\\[sketch_preview\\] decision written:|listen failed|EADDRINUSE'",
  timeout_ms: 300000,
  persistent: false
)
```

- 收到 `decision written: ok` → 進階段 4 的 OK 分支
- 收到 `decision written: redo` → 進階段 4 的 Redo 分支
- 收到 `listen failed` / `EADDRINUSE` → 換 port 重試階段 2
- 超時（5 分鐘無事件）→ 提示使用者「網頁預覽逾時」並結束

### 階段 4：處理決策

Read `output/decision.json`：

```json
{
  "action": "ok" | "redo",
  "mode": "basic" | "aligned" | "strong-ortho",
  "column_side": 40,
  "timestamp": "2026-05-03T12:34:56.789Z"
}
```

- `action == "ok"`：KillBash 終結 server process，流程正常結束
- `action == "redo"`：用 decision 內的新 `mode` / `column_side` 回階段 1 重跑
  （server **不重啟**，下一輪向量化結束後 server 自動讀新 PNG，使用者可再次決策；下一輪 decision.json 會覆寫舊的）

## decision.json 契約

| 欄位 | 型別 | 必要 | 說明 |
|------|------|------|------|
| `action` | string | ✅ | `"ok"` 或 `"redo"` |
| `mode` | string | ✅ | `"basic"` / `"aligned"` / `"strong-ortho"` |
| `column_side` | number | ✅ | 柱檢測寬度（預設 40） |
| `timestamp` | ISO8601 | ✅ | server 寫入時間 |
| `error` | string | 失敗時 | vectorize / server 失敗訊息 |

## Parameter Guidance

- Use `--mode strong-ortho` when the user says "make it more orthogonal", "straighten further", "align walls", "walls should be continuous", or similar.
- Use `--mode aligned` when the user wants cleanup but not aggressive endpoint snapping.
- Increase `--column-side` when the black column marks are large or the user requests larger column blocks.
- Decrease `--column-side` when the output columns obscure nearby walls.
- Use `--scale` only when the user specifies a drawing scale or CAD unit conversion.

## Quality Rules

- Do not output black column blobs as circles.
- Do not output wall strokes as filled shapes or thick polylines; use single centerlines.
- Preserve color separation into different layers.
- Add column centerlines unless the user explicitly asks not to.
- Prefer a DXF with clean, editable `LINE` and `LWPOLYLINE` entities over visually exact but messy traced contours.
- Be transparent that image-to-DXF conversion from sketches is approximate and may need manual CAD review.

## Iteration Patterns

When the user asks for revision:

- "columns must be square" → rerun with square column output; never use circles.
- "walls are broken" → use `aligned` or `strong-ortho`; bridge through columns and merge gaps.
- "add column centerlines" → ensure `COLUMN_CENTERLINE` layer is present with crosshair lines.
- "straighten and align walls" → use `aligned`.
- "more forced orthogonalization" → use `strong-ortho`.
- "use mm" or "set units" → 跑 Stage 1.5 rescale，or apply `--scale` if scale is known.

## 實作參考

- 向量化腳本：`.claude/skills/plan-sketch-to-dxf/scripts/vectorize_plan_to_dxf.py`
- 尺寸校正腳本：`.claude/skills/plan-sketch-to-dxf/scripts/rescale_geometry.py`
- 預覽 server：[MCP-Server/scripts/sketch_preview_server.cjs](../../../MCP-Server/scripts/sketch_preview_server.cjs)（log 訊號格式：第 137 行）
- 預覽頁：[MCP-Server/scripts/sketch_preview.html](../../../MCP-Server/scripts/sketch_preview.html)
- 顏色約定：[domain/sketch-color-convention.md](../../../domain/sketch-color-convention.md)
