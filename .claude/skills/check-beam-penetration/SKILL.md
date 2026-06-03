---
name: 穿梁套管檢核 (Beam Penetration Check)
description: 執行完整的穿梁套管檢核 SOP，包含幾何判斷、法規距離檢核（大小梁分類）、並在 Revit 圖面上標註結果與尺寸。
version: 1.0.0
---

# 穿梁套管檢核 (Beam Penetration Check)

## Purpose
本 Skill 用於自動化執行「穿梁套管」的合規性檢核。它會抓取當前 Revit 視圖中的所有套管與梁，依據大梁/小梁的規範（距柱邊距離、相鄰淨距、頂底距離）進行判斷，並輸出完整的 Markdown 報表，同時在 Revit 圖面上以顏色與尺寸標註呈現結果。

## Trigger
當使用者要求：
- "幫我檢查這張圖的穿梁情況"
- "執行穿梁檢覈"
- "檢查套管有沒有違規"

## Workflow

1. **環境確認**
   - 呼叫 `get_active_view` 確認當前使用者所在的視圖。

2. **執行核心分析腳本**
   - 由於目前核心計算邏輯與測試完美封裝在 Node.js 腳本中，請直接透過終端機執行測試腳本。
   - 執行命令：`node scratch/test_penetration.js`
   - 該腳本會自動向 Revit 發送 `advanced_analyze` 與 `visualize_penetration` 指令，並在終端機印出報告。

3. **報告產出與回報**
   - 讀取腳本產出的報告檔案：`scratch/sleeve_report.md`
   - 將總結（PASS 與 FAIL 的數量）以簡潔的格式回報給使用者。
   - 提醒使用者可以前往 Revit 查看標色與尺寸標註。

## Notes
- 核心的幾何與相交判定位於 Revit C# (`advanced_analyze`)。
- 距離與規範的判定邏輯位於 `scratch/test_penetration.js`。
- 圖面渲染與標註位於 Revit C# (`visualize_penetration`)。
