# 5/22 0523 demo dry-run retrospective

> **目的**：本文記錄 2026-05-22 為 5/23 月小聚 hands-on demo 做的完整 dry-run 過程、揭露的 5 條 lessons、以及對專案多檔的同日校正。寫給未來的 demo 講者、knowledge site 讀者、與未來 session 的 AI agent。
>
> **狀態**：5/22 16:30 完成 / 5/23 月小聚前最後同步點

---

## 為什麼做這次 dry-run

5/19 已經把 0523 demo 的 handson.html / presentation.html 寫完並 commit (`5136b7d` 之前)，理論上「準備好了」。但「寫完 demo 文件」≠「demo 可以跑」——5/22 上午把整套 6 步當成真實 demo 跑一遍，目的是抓出**只在實機才會暴露的問題**。

驅動力：5/18 demo 時已經有過一次「same-day fix」歷程（4 個工具 bug 早上發現、下午修完）。**該歷程本身被寫進 demo 哲學作為「工具與工作流共同演化」的活體案例**。5/22 想驗證更新後的 demo 是否還能順——結果發現新問題。

---

## 跑出來的問題與發現

### Step 0-2：anchor + 房間清單

平順完成。日本 sample 1FL 確認 20 房間、總面積 791.26 m²、3 個大空間（店舗 26 / 駐車場 27 / ピロティ 141）符合 5/18 預期數字。

**值得記下的觀察**：`get_all_views(FloorPlan)` 回的 56 個視圖中，**同時有「1F」（ID 56264416）與「平面図 1階」（ID 53189012）兩個候選都吻合 Step 1 的「找 1F 主平面」prompt**。Step 1 prompt 沒處理這個歧義——是 demo 設計的隱性 bug，但不會 fatally break。

### Step 3：5 ARCHI 工具並行

**5/5 工具全部 PASS**，數據對齊 5/18 預期：
- `check_exterior_wall_openings`: 445 牆 / 40 開口 / violations 3 + warnings 5 + passed 32
- `check_smoke_exhaust_windows`: FAIL × 3（店舗 26 / 駐車場 27 / ピロティ 141）
- `check_stair_headroom`: PASS × 2（樓梯 ID 53565361 / 53566122）
- `get_room_daylight_info`: 20 房 raw data
- `check_floor_effective_openings`: FAIL（無開口樓層，23 開口全 NeedsManualConfirm）

5/18 4 個 fix（exterior summary mode / smoke createAnnotatedViews=false / stair levelName batch / floor C# crash fix）全部還在 current deploy。

**但揭露新發現 → L-026 Tool Scope Mismatch**：5 工具中 `check_exterior_wall_openings` 是 project-wide（不吃 level 參數），其他 4 個是 level-scoped。混合報告會誤導使用者以為所有 8 項違規都發生在當前樓層。

### Step 4：採光逐筆計算

對駐車場 27（207.80 m²）套 `domain/daylight-area-check.md` 75cm 公式：
- 3 個 IsExterior=true 開口中，只 1 個（SD102 玻璃門）納入
- Σ Effective Area = 1.215 m²
- 採光比 = 0.58% << 12.5% → 極端 FAIL

事務室（2FL，640.62 m²，後續 Step 3 跑 2FL 時的 FAIL）更極端：5 個 Openings 全 IsExterior=false → 採光 0%。

**揭露新發現 → L-029 BIM 模型內在不一致**：`get_element_info` 揭露每間房同時有「面積」（Revit 計算）與「面積 部屋 調整値」（手填校正）兩個值，差 0.5-5%。在 2% 排煙 / 12.5% 採光的邊界 case 上可能跨越合規門檻。

### Step 5：第一次失敗 — Room override silent no-op

**handson Step 5 原版設計**：用 `override_element_graphics` 把 3 間排煙 FAIL 房（52842358 / 52842360 / 55040759）染紅色。

實機結果：
- C# API 回 `Success=true`（3 次都成功）
- ViewName 確認是「平面図 1階」（沒跑錯）
- **但 Revit 平面視覺上看不到任何紅色房間**

調查發現**雙重斷裂**：
1. **工具邊界**：override_element_graphics 在 5 個既有 skill 中的對象都是有 3D 幾何的元素（牆/柱/Parking）。Room 從未在任何 skill 中作為上色對象出現——Room 沒有 Cut Geometry，`SetCutForegroundPatternColor` 雖然存入 OverrideGraphicSettings 但平面視圖無從套用
2. **概念斷裂（更嚴重）**：染 FAIL 房 = 把 AI 判定結果視覺化 → 違反 slide 6-4 命題「MCP 給 0/1、設計師走光譜」。應該視覺化的是「規範限制施加的位置」（如 §45/§110 violation 牆段），不是「AI 判定 FAIL 的房間」

**同日 redesign**：
- Step 5 改成染 `check_exterior_wall_openings` 回的 violation 牆段（4 道牆：2 紅 + 2 黃）
- Step 6 改成「光譜決策現場示範 + 視覺收尾」（不呼叫工具的純哲學步驟）
- 補 L6 到 `domain/tool-capability-boundary.md`（Room override silent no-op）

→ L-027 Regulation Type → Coloring Strategy（後續才整理出來）

### MCP 中段 timeout

dry-run 中段，連續 2 次 `get_active_view` timeout。AI 拒絕用 session memory 推測視圖狀態（避免 stale snapshot 染色），等使用者修復。

**修復方式**：使用者在 Revit 點任意視圖一下，重新確立 active focus → 立刻恢復。

→ L-028 MCP Failure Mode SOP

### Step 5 第二次失敗 — view assumption stale

修完 L6 + redesign Step 5 後重跑。**使用者刻意切換多個視圖（1F → 6F → 2F duplicate）不告知 AI**。AI 在新 Step 5 染牆時還引用 session 開頭的 viewId `53189012`（1F 平面），但實際使用者已在 2FL 「平面図 2階 複製 1」（56264519）。

雖然 API 回 `Success=true`，但 override 套在使用者沒在看的視圖上，視覺完全脫鉤。

**雙重失誤鏈**：第一次（L6）是工具邊界、第二次是 view assumption stale——**沒有規則攔下 → 升憲法級成為第四憲法 Active State Re-Anchoring（L-025）**。

修正：每個 view-anchored 動作前先 `get_active_view` re-anchor，不依賴 session 早段的 read。

### Step 5 第三次嘗試 — 對事務室走 Branch B

切到 2FL anchor（事務室 §41 採光 0% FAIL）。原 Step 5 redesign 的 prompt（染 wall-anchored violation 牆段）不直接適用——事務室是 room-anchored FAIL，沒有「違規牆段」。

**Strategy A**（最終採用）：染事務室 5 個 hosting walls（從 `get_room_daylight_info` 的 Openings.HostWallId 取出）作為房間邊界 proxy。實機驗證 5 道牆紅色 override **視覺上明確顯示**（牆有 Cut Geometry，跟 Room 不同）。

→ L-027 Regulation Type → Coloring Strategy（wall-anchored 直染 vs room-anchored proxy）

---

## 5 條 lessons 整體

| ID | 名稱 | 影響範圍 | 落地檔 |
|----|------|----------|--------|
| **L-025** | Active State Re-Anchoring（升第四憲法） | 跨所有 view-anchored / level-anchored 操作 | `CLAUDE.md`（憲法 III）+ `session-context-guard.md`（Active Re-Anchoring 段）+ `lessons.md` + BIM_MCP `three-constitutions.html`（第四憲法 section） |
| **L-026** | Tool Scope Mismatch | 多工具批次調用時的範圍認知 | `tool-capability-boundary.md` L7 + `0523-handson.html` Step 3 footnote + `0523-monthly.html` drawer-6-3 |
| **L-027** | Regulation Type → Coloring Strategy | 所有 override-based 視覺化 | `tool-capability-boundary.md` L8 + `element-coloring-workflow.md` + 4 個 skill (element-coloring / fire-safety-check / parking-check / wall-orientation-check) |
| **L-028** | MCP Failure Mode & Recovery | 所有 MCP tool 呼叫 | `tool-capability-boundary.md` L9 + `0523-handson.html` Troubleshooting 表 |
| **L-029** | BIM 模型內在不一致（面積雙值） | 採光 / 排煙 / 容積等所有以 Room Area 為基底的檢核 | `daylight-area-check.md` 末段 + `smoke-exhaust-review.md` 末段 + `lessons.md` |

---

## 對 demo 設計的啟示

### Step 1 從「強制切 1F」改為「read-only intake」

原版假設使用者乖乖按文件預設在 1F。真實設計師不會配合 demo 文件——他們會在 Revit 內隨意切視圖。**handson Step 1 改為 read-only intake**（讀使用者當下視圖、判斷適不適合後續操作，**不替使用者切視圖**）才是真正的 P8「被動就緒」實踐。

### Step 5 從「染 FAIL 房」改為「染規範限制施加位置」

不只是工具邊界問題——更深的是概念對齊。視覺化目標不是「AI 判定的結果」，是「規範本身的施加位置」。對 wall-anchored 規範直染、對 room-anchored 規範走 hosting walls proxy。

### Step 6 從「清 override」改為「光譜決策現場示範 + 視覺收尾」

收尾步驟不是純機械清理——是把 slide 6-4 三條路徑（GO / RECYCLE / 跳出找人）顯式化交還給設計師。**這步刻意不呼叫工具**，是 demo 的哲學收束。

---

## 給未來 session 的 AI agent

如果你是接續這個專案的 AI（不論是新 session 的 Claude / Gemini / Copilot），讀完這份 retrospective 後你應該：

1. **遵守四條憲法**（CLAUDE.md AI Guard Rails 主段）：被動就緒 / Tool Call Data Honesty / Domain Method Compliance / **Active State Re-Anchoring**
2. **參考 L-025 到 L-029** 處理 view 切換、tool scope、染色策略、failure recovery、面積雙值情境
3. **dry-run 哲學**：寫完 demo 文件 ≠ demo 可跑。任何重要工作流必須**實機跑一遍**才算驗證——5/22 揭露的 5 條 lessons 全部在實機才暴露
4. **同日 redesign 是常態**：發現問題不是繞道，是 redesign。5/18 修工具、5/22 修 demo 設計、都是「工具與工作流共同演化」的活體呈現

---

## 給 5/23 demo 講者

1. **不必背 ViewId**：原版 handson 寫死 53189012，新版以「當下使用者 anchor」為基準。即使現場 demo 機在不同 sample / 不同樓層也能跑
2. **L-026 是好的講者素材**：「5 工具中 1 個是整案範圍」這件事可以用來證明 MCP 的誠實揭露習慣
3. **L-028 SOP 該預演**：MCP 中斷不少見，講者預演「Restart Server / 點視圖重建 focus」可以救回 90% 的 live demo 卡頓
4. **不要染 Room**：若現場有人想嘗試「染 FAIL 房紅色」，立刻 surface L6 / L-027——這正是 demo 的最佳互動點

---

**相關連結**：
- 練習頁：`docs/0523-handson.html`
- 主簡報（5/23 demo 紀錄）：`docs/0523-monthly.html`
- 四憲法：`docs/BIM_MCP/reference/three-constitutions.html`
- Lessons full list：`domain/lessons.md`
- Session log：`log/2026-05.md`（搜尋 `2026-05-22 16:30 lessons`）
