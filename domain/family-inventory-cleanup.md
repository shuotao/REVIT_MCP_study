---
name: family-inventory-cleanup
description: "族群盤點與清整 SOP / Family inventory & cleanup：查指定 category → 雙軸盤點（載入 vs 放置）→ 參數簽章比對 → 提案 → 使用者裁決 → 執行（purge 未使用 / merge 重複型 / rename）→ 驗證的作業閉環。每個破壞性動作前執行 7 項強制前置檢查。觸發關鍵字：族群整理、類型盤點、未使用類型、purge、重複類型、合併類型、family cleanup、type inventory、unused type、duplicate type。"
metadata:
  version: "1.1"
  updated: "2026-06-17"
  created: "2026-06-17"
  references:
    - "MCP/Core/CommandExecutor.cs DeleteElement（doc.Delete 連帶刪除）"
    - "MCP/Core/ExternalEventManager.cs（單一 action，禁止並行）"
  related:
    - element-query-workflow.md
    - tool-capability-boundary.md
    - qa-checklist.md
    - session-context-guard.md
  referenced_by:
    - family-inventory-cleanup
  tags: [family, type, inventory, cleanup, purge, merge, duplicate, 族群, 類型, 盤點, 清整]
---

# 族群盤點與清整（Family Inventory & Cleanup）

## Purpose

對指定 category（門/窗/任何族群）做**可重複、使用者把關**的盤點與清整:找出未使用類型、重複類型、命名/參數不一致,提案後由使用者裁決,再以 MCP 執行 purge / merge / rename,全程逐步驗證。`category` 為變數,跨族群通用。

## 核心原則:雙軸盤點（Two-Axis Inventory）

兩個視角缺一不可,差集才是真相:

| 軸 | 看到什麼 | 工具 |
|---|---|---|
| **載入（Project Browser）** | 該族群「存在哪些類型」(含未放置) | `list_family_symbols`(**逐族群過濾**) |
| **放置（模型中）** | 哪些類型「真的有被用」+ 實例數 | `query_elements_with_filter`(高 maxCount,無 viewId) |

**載入 − 放置 = 未使用類型(Purge 候選)**。只看放置會漏掉未使用型;只看載入不知道誰在用。

## 閉環流程（Closed Loop）

```
查指定 category → 盤點(雙軸) → 比對 → 提案【STOP 等裁決】
→ 執行(purge/merge/rename) → 驗證 → SELF-AUDIT【STOP 回報】
```

## 偵測啟發法（Detection Heuristics）

**鐵則:判定「重複 / 名實不符」一律以參數簽章為準,嚴禁用名稱或 `get_category_fields` 捷徑。** 柱實測佐證:`get_category_fields(Columns)` 只抽一個樣本(抽到 Corinthian 柱)根本沒回 Depth/Width;主力型 `30"D x 30"W` 名字寫 30×30、實際 Depth/Width 是 24×24,名稱完全反指,純名稱比對會把 111 根判錯。

- **參數簽章(MANDATORY)**:`get_element_info` 逐型讀實際幾何參數(門:Width/Height;柱:Depth/Width…);兩型除 Type Mark / IfcGUID(自動唯一)外全同 = 真重複。
- **名實比對**:Type 名稱宣稱尺寸 vs 實際參數,不符即標記——但**僅作提示,不作判定依據**。
- **` N` 尾碼(僅線索,非證據)**:`36"x84" 2` 之類是 Revit 去重痕跡,**必須用簽章確認**:(a) 有同簽章他型→真重複;(b) 簽章唯一→只是醜名→rename;(c) 注意被比的「無尾碼」型才可能是名字錯的那個(柱案例)。
- **群聚(類別相依)**:同 Level + 連號 Mark 僅在有 Mark 的類別(門/窗)有效;柱等無 Mark 類別失效,改靠簽章。Mark 為空在柱屬正常,不算缺陷。

## ⚠️ 強制前置檢查（MANDATORY PRE-CHECKS）— 破壞性動作前逐項過

這是本 SOP 的核心,任一項不過 → 停。皆由實戰盲點固化而來:

1. **截斷防護**:`list_family_symbols` 上限 100 筆且按名稱比對會跨類別污染(「Door」會中櫥櫃/電梯門)。→ 盤點類型一律**逐族群精確過濾**,或以 `query_elements`(高 maxCount)為基準。絕不以廣詞清單當完整。回報前比對 `Count` vs `maxCount` 防截斷。
2. **0 實例驗證**:刪任何**類型**前,必以查詢確認該 TypeId 的實例數 = 0。非 0 → 禁止直接刪。
3. **連帶刪除揭露**:`delete_element` 對「有實例的類型」會 **cascade 連帶刪掉實例**,但工具只回「成功刪除 <id>」、**不報連帶**。→ 動作前必須自己列出「會連帶刪什麼」給使用者確認。
4. **簽章等價(合併前)**:merge 前以 `get_element_info` 比對來源/目標類型。注意 `get_element_info` 對類型可能**只回部分參數**(非全部 type 欄位)→ 等價結論須**註明涵蓋範圍**,有疑慮逐欄補查,勿宣稱「完全相同」。
5. **不可並行**:`ExternalEventManager` 只有單一 action 欄位,並行送指令會互相覆寫遺失。→ 破壞性動作**逐一執行、逐筆驗證**。
6. **可逆性告知**:破壞前說明可在 Revit 按 Ctrl+Z 復原,並提醒「**接了其他動作後復原會變難**」。
7. **基準時點**:模型可能在會談中被編輯(曾見同一查詢 142→145 漂移)→ 每個破壞批次前**重抓基準**,不沿用上一回合計數。
8. **族群發現**:對未知 category,先 `query_elements(category)` 取得 family 名稱 + 放置,**再**逐族群做載入軸;不可假設已知 family 名稱(Phase 1 不能從載入軸起手)。
9. **簽章強制**:判重複/名實一律 `get_element_info` 逐型讀簽章;**嚴禁名稱或 `get_category_fields` 捷徑**(後者在異質類別會抽錯樣本,回到錯族群的欄位)。
10. **名≠參數須人工裁決**:簽章只揭露不一致,**不能決定哪個是設計本意**。尺寸不符可能是**結構錯誤(斷面不足)而非命名問題** → 停,交使用者/結構技師,並標註結構影響。
11. **類別覆蓋**:`query_elements` 只認 6 個硬編碼類別(Walls/Rooms/Doors/Windows/Floors/Columns,且 Columns=建築柱);結構柱(OST_StructuralColumns)等到不了。開頭先釐清目標類別,並聲明工具觸及邊界。
12. **Filter 字串精確**:錯字會回**假 0**(實測 `24" x 24"W 2` 漏一個 D → 假 0,差點誤判)。0 實例驗證優先用**完整列舉**;若用 filter,型名須從前一筆工具回應**原樣複製**,並加正控(查一個應為非 0 的型確認 filter 機制正常)。

## 範式 A:Purge 未使用類型(0 實例,零模型衝擊)

1. 雙軸盤點求未使用集(載入 − 放置)。
2. 逐一刪前驗 0 實例(前置 2)。
3. `delete_element` 逐一刪(前置 5,不並行)。
4. 重查驗證:**類型數下降、實例數不變**。

## 範式 B:Merge 重複型(有實例,正規流程)

1. `get_element_info` ×2 簽章比對(前置 4)→ 確認等價。
2. `change_element_type` 把實例轉移到正規型(實例 Mark 保留,Type Mark 會改成目標型的)。
3. 查詢確認來源型已 **0 實例**。
4. `delete_element` 刪空出來的重複型。
5. 重查驗證:**類型數 −1、實例數不變**。

## 閘點（Gates）

- **G2 提案後**:STOP,使用者選要做的項目與值。
- **G3 每個破壞批次前**:跑前置檢查 1–7,把影響範圍明列。
- **G4 執行後**:重查驗證 + 輸出 SELF-AUDIT。

## QA/QC

對映 `qa-checklist.md`:資料誠實(數據溯源本回合工具)、無臨時碼(不繞 MCP)、迴圈逐筆回報、步驟完整。**注意:SELF-AUDIT 是自評,非獨立檢核**——破壞性動作的真正防線是前置檢查 1–7,屬硬性閘,不可略過。

## 工具與能力缺口

工具:`get_active_view`、`list_family_symbols`、`query_elements` / `query_elements_with_filter`、`get_element_info`、`change_element_type`、`delete_element`。

缺口(現況):
- **單型改名「現在就能做」**:`modify_element_parameter(elementId=<型 id>, parameterName="Name", value=<新名>)` 會走 `element.Name=` 特例(`CommandExecutor.cs:723-728`),在 Transaction 內改類型/族群名,可 Ctrl+Z。專用 `rename_family_or_type`(批次 + 重名擋下)仍屬 Wave 2,但**單型改名不卡**。改名前確認無重名。
- `delete_element` **不回報連帶刪除**(靠前置 3 補)。
- `get_element_info` 類型參數**可能不齊全**(靠前置 4 補)。
- 改參數**資料格式**(Text↔Number)、改族群**內部定義** = Family Editor,**MCP 範圍外**。

## Reference

詳見 `domain/element-query-workflow.md`(三階段查詢)、`domain/tool-capability-boundary.md`(能力邊界)、`domain/qa-checklist.md`(QA 判準)。
